using System.Linq.Expressions;
using System.Reflection;

namespace Mapper
{
    public class CustomMapping
    {
        public PropertyInfo TargetProperty { get; set; }
        public LambdaExpression SourceExpression { get; set; }
    }
}