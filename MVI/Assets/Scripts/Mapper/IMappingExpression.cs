using System;
using System.Linq.Expressions;

namespace Mapper
{
    public interface IMappingExpression<TSource, TTarget>
    {
        /// <summary>
        /// 配置自定义属性映射
        /// </summary>
        IMappingExpression<TSource, TTarget> ForMember<TMember>(
            Expression<Func<TTarget, TMember>> destinationMember,
            Expression<Func<TSource, TMember>> sourceExpression);
    
        /// <summary>
        /// 忽略目标类型的属性
        /// </summary>
        IMappingExpression<TSource, TTarget> Ignore<TMember>(Expression<Func<TTarget, TMember>> destinationMember);
    }
}