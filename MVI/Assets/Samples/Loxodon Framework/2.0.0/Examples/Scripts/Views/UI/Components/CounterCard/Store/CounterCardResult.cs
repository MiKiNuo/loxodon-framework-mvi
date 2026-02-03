using MVI;

namespace Loxodon.Framework.Examples.Components.CounterCard.Store
{
    // 计数卡片意图结果。
    public sealed class CounterCardResult : IMviResult
    {
        public CounterCardResult(int? count, int? delta, string label, bool isUpdateNewState)
        {
            Count = count;
            Delta = delta;
            Label = label;
            IsUpdateNewState = isUpdateNewState;
        }

        public int? Count { get; }
        public int? Delta { get; }
        public string Label { get; }
        public bool IsUpdateNewState { get; }
    }
}
