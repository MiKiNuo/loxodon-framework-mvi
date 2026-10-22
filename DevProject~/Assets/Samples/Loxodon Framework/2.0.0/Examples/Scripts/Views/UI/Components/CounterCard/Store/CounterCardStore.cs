using Loxodon.Framework.Examples.Components.CounterCard.Intent;
using Loxodon.Framework.Examples.Components.CounterCard.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.CounterCard.Store
{
    // 计数卡片 Store：把 Result 归约成新的 State。
    public sealed class CounterCardStore : Store<CounterCardState, ICounterCardIntent, CounterCardResult>
    {
        protected override CounterCardState Reduce(CounterCardResult result)
        {
            if (result == null)
            {
                return default;
            }

            var current = CurrentState ?? new CounterCardState(0, "Counter");
            var newCount = result.Count ?? (current.Count + (result.Delta ?? 0));
            var newLabel = result.Label ?? current.Label;

            return new CounterCardState(newCount, newLabel)
            {
                IsUpdateNewState = result.IsUpdateNewState
            };
        }
    }
}
