using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapper
{
    public class MappingConfiguration<TSource, TTarget> : IMappingConfiguration<TSource, TTarget>
    {
        private readonly Dictionary<string, CustomMapping> _customMappings = new();
        private readonly HashSet<string> _ignoredProperties = new();

        public Action<object, object, bool> CreateMappingDelegate()
        {
            var sourceParam = Expression.Parameter(typeof(object), "source");
            var targetParam = Expression.Parameter(typeof(object), "target");
            var onlyIfChangedParam = Expression.Parameter(typeof(bool), "onlyIfChanged");

            var sourceConverted = Expression.Convert(sourceParam, typeof(TSource));
            var targetConverted = Expression.Convert(targetParam, typeof(TTarget));

            var expressions = new List<Expression>();

            foreach (var mapping in _customMappings.Values)
            {
                var valueExpression = mapping.SourceExpression.Body;
                var replacer = new ParameterReplacer(mapping.SourceExpression.Parameters[0], sourceConverted);
                var newValue = replacer.Visit(valueExpression);

                Expression convertedValue = newValue.Type != mapping.TargetProperty.PropertyType
                    ? Expression.Convert(newValue, mapping.TargetProperty.PropertyType)
                    : newValue;
                
                var mappingExpr = CreatePropertyMappingExpression(
                    targetConverted,
                    mapping.TargetProperty,
                    convertedValue,
                    onlyIfChangedParam);

                expressions.Add(mappingExpr);
            }

            var targetProperties = typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var targetProp in targetProperties)
            {
                if (!targetProp.CanWrite) continue;
                if (_ignoredProperties.Contains(targetProp.Name)) continue;
                if (_customMappings.ContainsKey(targetProp.Name)) continue;

                var sourceProp = typeof(TSource).GetProperty(targetProp.Name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (sourceProp == null || !sourceProp.CanRead) continue;

                var sourceValue = Expression.Property(sourceConverted, sourceProp);
                Expression convertedValue = sourceValue.Type != targetProp.PropertyType
                    ? Expression.Convert(sourceValue, targetProp.PropertyType)
                    : sourceValue;

                var mappingExpr = CreatePropertyMappingExpression(
                    targetConverted,
                    targetProp,
                    convertedValue,
                    onlyIfChangedParam);

                expressions.Add(mappingExpr);
            }

            Expression body = expressions.Count > 0
                ? Expression.Block(expressions)
                : Expression.Empty();

            return Expression.Lambda<Action<object, object, bool>>(
                body, sourceParam, targetParam, onlyIfChangedParam).Compile();
        }

        public IMappingConfiguration<TSource, TTarget> MapProperty<TValue>(
            Expression<Func<TTarget, TValue>> targetProperty,
            Expression<Func<TSource, TValue>> sourceExpression)
        {
            if (targetProperty.Body is MemberExpression memberExpr &&
                memberExpr.Member is PropertyInfo propInfo)
            {
                if (!propInfo.CanWrite)
                {
                    throw new ArgumentException("目标属性必须是可写的", nameof(targetProperty));
                }

                _customMappings[propInfo.Name] = new CustomMapping
                {
                    TargetProperty = propInfo,
                    SourceExpression = sourceExpression
                };

                return this;
            }

            throw new ArgumentException("必须提供有效的属性表达式", nameof(targetProperty));
        }

        public IMappingConfiguration<TSource, TTarget> IgnoreProperty<TValue>(
            Expression<Func<TTarget, TValue>> targetProperty)
        {
            if (targetProperty.Body is MemberExpression memberExpr &&
                memberExpr.Member is PropertyInfo propInfo)
            {
                _ignoredProperties.Add(propInfo.Name);
                return this;
            }

            throw new ArgumentException("必须提供有效的属性表达式", nameof(targetProperty));
        }
        
        private static Expression CreatePropertyMappingExpression(
            Expression target,
            PropertyInfo targetProperty,
            Expression valueExpression,
            ParameterExpression onlyIfChangedParam)
        {
            // 目标属性访问
            var targetProp = Expression.Property(target, targetProperty);

            // 赋值表达式
            var assignExpr = Expression.Assign(targetProp, valueExpression);

            // 无条件更新表达式
            var unconditionalUpdate = Expression.IfThen(
                Expression.Not(onlyIfChangedParam),
                assignExpr);

            // 条件更新表达式
            var valuesNotEqual = Expression.NotEqual(targetProp, valueExpression);
            var conditionalUpdate = Expression.IfThen(
                onlyIfChangedParam,
                Expression.IfThen(valuesNotEqual, assignExpr));

            return Expression.Block(unconditionalUpdate, conditionalUpdate);
        }
    }
}