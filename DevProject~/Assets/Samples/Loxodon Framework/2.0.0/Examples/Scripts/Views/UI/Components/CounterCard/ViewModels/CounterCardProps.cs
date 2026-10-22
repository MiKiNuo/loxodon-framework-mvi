using System;

namespace Loxodon.Framework.Examples.Components.CounterCard.ViewModels
{
    // 计数卡片 props。
    public sealed class CounterCardProps : IEquatable<CounterCardProps>
    {
        public CounterCardProps(string label, int count)
        {
            Label = label;
            Count = count;
        }

        // 标题文本。
        public string Label { get; }

        // 计数值。
        public int Count { get; }

        public bool Equals(CounterCardProps other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Count == other.Count && string.Equals(Label, other.Label);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CounterCardProps);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + Count.GetHashCode();
                hash = (hash * 31) + (Label?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
