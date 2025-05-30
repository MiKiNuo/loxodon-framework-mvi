using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapper
{
    /// <summary>
    /// 轻量级高性能对象映射器
    /// </summary>
    public static class LightMapper
    {
        // 映射委托缓存
        private static readonly ConcurrentDictionary<TypePair, Action<object, object, bool>> _mappingCache = new();

        // 自定义映射配置
        private static readonly ConcurrentDictionary<TypePair, IMappingConfiguration> _configurations = new();

        /// <summary>
        /// 创建自定义映射配置
        /// </summary>
        /// <typeparam name="TSource">源类型</typeparam>
        /// <typeparam name="TTarget">目标类型</typeparam>
        public static IMappingConfiguration<TSource, TTarget> CreateMap<TSource, TTarget>()
        {
            var key = new TypePair(typeof(TSource), typeof(TTarget));
            var config = new MappingConfiguration<TSource, TTarget>();
            _configurations[key] = config;
            return config;
        }

        /// <summary>
        /// 执行对象映射
        /// </summary>
        /// <param name="source">源对象</param>
        /// <param name="target">目标对象</param>
        /// <param name="onlyIfChanged">是否仅在值变化时更新</param>
        public static void Map(object source, object target, bool onlyIfChanged = false)
        {
            if (source == null || target == null) return;
        
            var sourceType = source.GetType();
            var targetType = target.GetType();
            var key = new TypePair(sourceType, targetType);
        
            var mappingDelegate = GetOrCreateMappingDelegate(key);
            mappingDelegate(source, target, onlyIfChanged);
        }

        /// <summary>
        /// 执行对象映射（泛型版本）
        /// </summary>
        public static void Map<TSource, TTarget>(TSource source, TTarget target, bool onlyIfChanged = false)
        {
            if (source == null || target == null) return;
            var sourceType = source.GetType();
            var targetType = target.GetType();
            var key = new TypePair(sourceType, targetType);
        
            var mappingDelegate = GetOrCreateMappingDelegate(key);
            mappingDelegate(source, target, onlyIfChanged);
        }

        private static Action<object, object, bool> GetOrCreateMappingDelegate(TypePair key)
        {
            return _mappingCache.GetOrAdd(key, k =>
            {
                // 检查是否有自定义配置
                if (_configurations.TryGetValue(k, out var config))
                {
                    return config.CreateMappingDelegate();
                }

                // 默认基于属性的映射
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

            var targetProperties = GetPublicProperties(targetType);
        
            foreach (var targetProp in targetProperties)
            {
                if (!targetProp.CanWrite) continue;
            
                var sourceProp = GetPublicProperties(sourceType)
                    .FirstOrDefault(p => string.Equals(p.Name, targetProp.Name, StringComparison.OrdinalIgnoreCase));
            
                if (sourceProp == null || !sourceProp.CanRead) continue;
            
                var mappingExpr = CreatePropertyMappingExpression(
                    sourceConverted, sourceProp, 
                    targetConverted, targetProp, 
                    onlyIfChangedParam);
            
                expressions.Add(mappingExpr);
            }
        
            Expression body = expressions.Count > 0 
                ? Expression.Block(expressions) 
                : Expression.Empty();
        
            return Expression.Lambda<Action<object, object, bool>>(
                body, sourceParam, targetParam, onlyIfChangedParam).Compile();
        }

        // 添加专用方法获取所有公共属性
        private static IEnumerable<PropertyInfo> GetPublicProperties(Type type)
        {
            if (type.IsInterface)
            {
                // 处理接口的所有属性
                var propertyInfos = new List<PropertyInfo>();
                var considered = new HashSet<Type>();
                var queue = new Queue<Type>();
                considered.Add(type);
                queue.Enqueue(type);

                while (queue.Count > 0)
                {
                    var subType = queue.Dequeue();
                    foreach (var subInterface in subType.GetInterfaces())
                    {
                        if (considered.Contains(subInterface)) continue;
                        considered.Add(subInterface);
                        queue.Enqueue(subInterface);
                    }

                    var typeProperties = subType.GetProperties(
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.DeclaredOnly);

                    propertyInfos.AddRange(typeProperties);
                }

                return propertyInfos.ToArray();
            }

            // 处理普通类的所有公共属性
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }

        private static Expression CreatePropertyMappingExpression(
            Expression source,
            PropertyInfo sourceProp,
            Expression target,
            PropertyInfo targetProp,
            ParameterExpression onlyIfChangedParam)
        {
            // 获取源属性值
            var sourceValue = Expression.Property(source, sourceProp);

            // 类型转换
            Expression convertedValue = sourceValue.Type != targetProp.PropertyType
                ? Expression.Convert(sourceValue, targetProp.PropertyType)
                : sourceValue;

            // 目标属性访问
            var targetProperty = Expression.Property(target, targetProp);

            // 赋值表达式
            var assignExpr = Expression.Assign(targetProperty, convertedValue);

            // 无条件更新表达式
            var unconditionalUpdate = Expression.IfThen(
                Expression.Not(onlyIfChangedParam),
                assignExpr);

            // 条件更新表达式
            var valuesNotEqual = Expression.NotEqual(targetProperty, convertedValue);
            var conditionalUpdate = Expression.IfThen(
                onlyIfChangedParam,
                Expression.IfThen(valuesNotEqual, assignExpr));

            return Expression.Block(unconditionalUpdate, conditionalUpdate);
        }
    }
}