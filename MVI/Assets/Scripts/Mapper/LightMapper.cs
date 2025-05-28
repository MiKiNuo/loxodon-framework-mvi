using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapper
{
    public static class LightMapper
{
    private static readonly ConcurrentDictionary<TypePair, MappingConfiguration> _configurationCache = 
        new ConcurrentDictionary<TypePair, MappingConfiguration>();
    
    private static readonly ConcurrentDictionary<TypePair, Action<object, object, bool>> _mappingDelegateCache = 
        new ConcurrentDictionary<TypePair, Action<object, object, bool>>();

    public static IMappingExpression<TSource, TTarget> CreateMap<TSource, TTarget>()
    {
        var key = new TypePair(typeof(TSource), typeof(TTarget));
        var config = new MappingConfiguration<TSource, TTarget>();
        _configurationCache[key] = config;
        return config;
    }

    public static void Map(object source, object target, bool onlyUpdateIfChanged = false)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));
        
        var key = new TypePair(source.GetType(), target.GetType());
        var mappingDelegate = GetOrCreateMappingDelegate(key);
        mappingDelegate(source, target, onlyUpdateIfChanged);
    }

    public static void Map<TSource, TTarget>(TSource source, TTarget target, bool onlyUpdateIfChanged = false)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));
        
        var key = new TypePair(typeof(TSource), typeof(TTarget));
        var mappingDelegate = GetOrCreateMappingDelegate(key);
        mappingDelegate(source, target, onlyUpdateIfChanged);
    }

    private static Action<object, object, bool> GetOrCreateMappingDelegate(TypePair key)
    {
        return _mappingDelegateCache.GetOrAdd(key, k =>
        {
            if (_configurationCache.TryGetValue(k, out var config))
            {
                return config.CreateMappingDelegate();
            }
            
            return CreateDefaultMappingDelegate(k.SourceType, k.TargetType);
        });
    }

    private static Action<object, object, bool> CreateDefaultMappingDelegate(Type sourceType, Type targetType)
    {
        var sourceParam = Expression.Parameter(typeof(object), "source");
        var targetParam = Expression.Parameter(typeof(object), "target");
        var onlyIfChangedParam = Expression.Parameter(typeof(bool), "onlyIfChanged");
        
        var sourceConverted = Expression.Convert(sourceParam, sourceType);
        var targetConverted = Expression.Convert(targetParam, targetType);
        
        var expressions = new List<Expression>();
        
        // 只处理可写属性
        var targetProperties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite);
        
        foreach (var targetProp in targetProperties)
        {
            var sourceProp = sourceType.GetProperty(targetProp.Name, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            if (sourceProp == null || !sourceProp.CanRead) continue;
            
            var sourceValue = Expression.Property(sourceConverted, sourceProp);
            var valueToSet = ConvertExpressionType(sourceValue, targetProp.PropertyType);
            
            expressions.Add(CreateConditionalUpdateBlock(
                targetConverted, targetProp, valueToSet, onlyIfChangedParam));
        }
        
        if (expressions.Count == 0)
        {
            expressions.Add(Expression.Empty());
        }
        
        var block = Expression.Block(expressions);
        var lambda = Expression.Lambda<Action<object, object, bool>>(
            block, sourceParam, targetParam, onlyIfChangedParam);
        
        return lambda.Compile();
    }

    private static Expression ConvertExpressionType(Expression expression, Type targetType)
    {
        if (expression.Type == targetType)
            return expression;
        
        // 处理可空类型
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (expression.Type == underlyingType)
            {
                return Expression.Convert(expression, targetType);
            }
        }
        
        // 处理从可空类型到非可空类型的转换
        if (expression.Type.IsGenericType && expression.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(expression.Type);
            if (underlyingType == targetType)
            {
                return Expression.Convert(expression, targetType);
            }
        }
        
        return Expression.Convert(expression, targetType);
    }

    private static Expression CreateConditionalUpdateBlock(
        Expression target, 
        PropertyInfo targetProp, 
        Expression valueToSet,
        ParameterExpression onlyIfChangedParam)
    {
        var property = Expression.Property(target, targetProp);
        
        // 无条件更新路径
        var directAssignment = Expression.Assign(property, valueToSet);
        var directUpdateBlock = Expression.IfThen(
            Expression.Not(onlyIfChangedParam),
            directAssignment);
        
        // 条件更新路径
        var notEqual = Expression.NotEqual(property, valueToSet);
        var conditionalAssignment = Expression.IfThen(
            Expression.AndAlso(onlyIfChangedParam, notEqual),
            directAssignment);
        
        return Expression.Block(directUpdateBlock, conditionalAssignment);
    }

    #region Helper Classes

    private readonly struct TypePair : IEquatable<TypePair>
    {
        public Type SourceType { get; }
        public Type TargetType { get; }

        public TypePair(Type sourceType, Type targetType)
        {
            SourceType = sourceType;
            TargetType = targetType;
        }

        public bool Equals(TypePair other) => 
            SourceType == other.SourceType && TargetType == other.TargetType;

        public override bool Equals(object obj) => obj is TypePair other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceType, TargetType);
    }

    private abstract class MappingConfiguration
    {
        public abstract Action<object, object, bool> CreateMappingDelegate();
    }

    private class MappingConfiguration<TSource, TTarget> : MappingConfiguration, IMappingExpression<TSource, TTarget>
    {
        private readonly Dictionary<string, CustomPropertyMapping> _customMappings = 
            new Dictionary<string, CustomPropertyMapping>();
        private readonly HashSet<string> _ignoredProperties = new HashSet<string>();

        public override Action<object, object, bool> CreateMappingDelegate()
        {
            var sourceParam = Expression.Parameter(typeof(object), "source");
            var targetParam = Expression.Parameter(typeof(object), "target");
            var onlyIfChangedParam = Expression.Parameter(typeof(bool), "onlyIfChanged");
            
            var sourceConverted = Expression.Convert(sourceParam, typeof(TSource));
            var targetConverted = Expression.Convert(targetParam, typeof(TTarget));
            
            var expressions = new List<Expression>();
            
            // 处理自定义映射
            foreach (var mapping in _customMappings.Values)
            {
                var valueExpression = mapping.ValueExpression;
                var replacer = new ParameterReplacer(valueExpression.Parameters[0], sourceConverted);
                var newValue = replacer.Visit(valueExpression.Body);
                
                var convertedValue = ConvertExpressionType(newValue, mapping.TargetProperty.PropertyType);
                
                expressions.Add(CreateConditionalUpdateBlock(
                    targetConverted, mapping.TargetProperty, convertedValue, onlyIfChangedParam));
            }
            
            // 处理自动映射（忽略已配置和忽略的属性）
            var targetProperties = typeof(TTarget)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && 
                           !_ignoredProperties.Contains(p.Name) && 
                           !_customMappings.ContainsKey(p.Name));
            
            foreach (var targetProp in targetProperties)
            {
                var sourceProp = typeof(TSource).GetProperty(targetProp.Name, 
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                
                if (sourceProp == null || !sourceProp.CanRead) continue;
                
                var sourceValue = Expression.Property(sourceConverted, sourceProp);
                var valueToSet = ConvertExpressionType(sourceValue, targetProp.PropertyType);
                
                expressions.Add(CreateConditionalUpdateBlock(
                    targetConverted, targetProp, valueToSet, onlyIfChangedParam));
            }
            
            var block = Expression.Block(expressions);
            var lambda = Expression.Lambda<Action<object, object, bool>>(
                block, sourceParam, targetParam, onlyIfChangedParam);
            
            return lambda.Compile();
        }

        public IMappingExpression<TSource, TTarget> ForMember<TMember>(
            Expression<Func<TTarget, TMember>> destinationMember,
            Expression<Func<TSource, TMember>> sourceExpression)
        {
            var memberExpr = GetMemberExpression(destinationMember);
            var propertyInfo = memberExpr.Member as PropertyInfo;
            
            if (propertyInfo == null)
            {
                throw new ArgumentException("目标成员必须是属性", nameof(destinationMember));
            }
            
            if (!propertyInfo.CanWrite)
            {
                throw new ArgumentException("目标属性必须是可写的", nameof(destinationMember));
            }
            
            _customMappings[propertyInfo.Name] = new CustomPropertyMapping
            {
                TargetProperty = propertyInfo,
                ValueExpression = sourceExpression
            };
            
            return this;
        }

        public IMappingExpression<TSource, TTarget> Ignore<TMember>(Expression<Func<TTarget, TMember>> destinationMember)
        {
            var memberExpr = GetMemberExpression(destinationMember);
            var propertyInfo = memberExpr.Member as PropertyInfo;
            
            if (propertyInfo == null)
            {
                throw new ArgumentException("目标成员必须是属性", nameof(destinationMember));
            }
            
            _ignoredProperties.Add(propertyInfo.Name);
            return this;
        }

        private static MemberExpression GetMemberExpression<TMember>(Expression<Func<TTarget, TMember>> expression)
        {
            if (expression.Body is MemberExpression memberExpr)
            {
                return memberExpr;
            }
            
            if (expression.Body is UnaryExpression unaryExpr && 
                unaryExpr.Operand is MemberExpression unaryMemberExpr)
            {
                return unaryMemberExpr;
            }
            
            throw new ArgumentException("无效的成员表达式", nameof(expression));
        }

        private class CustomPropertyMapping
        {
            public PropertyInfo TargetProperty { get; set; }
            public LambdaExpression ValueExpression { get; set; }
        }
    }

    private class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly Expression _replacement;

        public ParameterReplacer(ParameterExpression oldParameter, Expression replacement)
        {
            _oldParameter = oldParameter;
            _replacement = replacement;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParameter ? _replacement : node;
        }
    }

    #endregion
}
}