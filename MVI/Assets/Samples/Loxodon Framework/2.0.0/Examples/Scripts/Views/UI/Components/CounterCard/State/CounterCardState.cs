using MVI;

namespace Loxodon.Framework.Examples.Components.CounterCard.State
{
    // 计数卡片的状态。
    public sealed class CounterCardState : IState
    {
        // count: 当前计数；label: 标题文本。
        public CounterCardState(int count, string label)
        {
            Count = count;
            Label = label;
        }

        // 计数值。
        public int Count { get; }

        // 标题文本。
        public string Label { get; }

        // 是否强制刷新状态。
        public bool IsUpdateNewState { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is not CounterCardState other)
            {
                return false;
            }

            return Count == other.Count && string.Equals(Label, other.Label);
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
