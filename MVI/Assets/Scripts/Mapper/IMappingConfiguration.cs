using System;
using System.Linq.Expressions;

namespace Mapper
{
    /// <summary>
    /// 映射配置接口
    /// </summary>
    public interface IMappingConfiguration
    {
        Action<object, object, bool> CreateMappingDelegate();
    }
    
    /// <summary>
    /// 类型化映射配置接口
    /// </summary>
    public interface IMappingConfiguration<TSource, TTarget> : IMappingConfiguration
    {
        /// <summary>
        /// 配置自定义属性映射
        /// </summary>
        IMappingConfiguration<TSource, TTarget> MapProperty<TValue>(
            Expression<Func<TTarget, TValue>> targetProperty,
            Expression<Func<TSource, TValue>> sourceExpression);
        
        /// <summary>
        /// 忽略目标属性
        /// </summary>
        IMappingConfiguration<TSource, TTarget> IgnoreProperty<TValue>(
            Expression<Func<TTarget, TValue>> targetProperty);
    }
}