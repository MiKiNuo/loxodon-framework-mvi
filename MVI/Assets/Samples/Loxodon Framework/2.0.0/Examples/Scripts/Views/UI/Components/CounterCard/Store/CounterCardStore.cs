using Loxodon.Framework.Examples.Components.CounterCard.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.CounterCard.Store
{
    // 计数卡片 Store：把 Result 归约成新的 State。
    public sealed class CounterCardStore : MVI.Store
    {
        protected override IState Reducer(IMviResult result)
        {
            if (result is not CounterCardResult counterResult)
            {
                return default;
            }

            var current = CurrentState as CounterCardState ?? new CounterCardState(0, "Counter");
            var newCount = counterResult.Count ?? (current.Count + (counterResult.Delta ?? 0));
            var newLabel = counterResult.Label ?? current.Label;

            return new CounterCardState(newCount, newLabel)
            {
                IsUpdateNewState = counterResult.IsUpdateNewState
            };
        }
    }
}
