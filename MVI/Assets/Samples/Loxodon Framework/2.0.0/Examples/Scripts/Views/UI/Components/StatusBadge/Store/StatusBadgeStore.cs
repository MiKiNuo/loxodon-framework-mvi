using Loxodon.Framework.Examples.Components.StatusBadge.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.StatusBadge.Store
{
    // 状态徽标 Store：把 Result 归约为 State。
    public sealed class StatusBadgeStore : MVI.Store
    {
        protected override IState Reducer(IMviResult result)
        {
            if (result is not StatusBadgeResult statusResult)
            {
                return default;
            }

            var current = CurrentState as StatusBadgeState ?? new StatusBadgeState(string.Empty);
            var message = statusResult.Message ?? current.Message;

            return new StatusBadgeState(message)
            {
                IsUpdateNewState = statusResult.IsUpdateNewState
            };
        }
    }
}
