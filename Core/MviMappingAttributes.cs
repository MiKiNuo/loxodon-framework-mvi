using System;

namespace MVI
{
    /// <summary>
    /// 标记属性参与映射时忽略该字段。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class MviIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// 指定属性映射的目标/来源名称。
    /// 状态属性上使用：映射到指定 ViewModel 属性。
    /// ViewModel 属性上使用：从指定 State 属性映射。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class MviMapAttribute : Attribute
    {
        public MviMapAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
