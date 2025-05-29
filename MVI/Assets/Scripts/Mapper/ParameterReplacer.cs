using System.Linq.Expressions;

namespace Mapper
{
    // 参数替换器
    public class ParameterReplacer : ExpressionVisitor
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
}