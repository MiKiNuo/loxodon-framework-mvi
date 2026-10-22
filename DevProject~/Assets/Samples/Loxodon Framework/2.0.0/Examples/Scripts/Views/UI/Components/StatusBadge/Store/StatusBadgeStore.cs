using Loxodon.Framework.Examples.Components.StatusBadge.Intent;
using Loxodon.Framework.Examples.Components.StatusBadge.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.StatusBadge.Store
{
    // 状态徽标 Store：把 Result 归约为 State。
    public sealed class StatusBadgeStore : Store<StatusBadgeState, IStatusBadgeIntent, StatusBadgeResult>
    {
        protected override StatusBadgeState Reduce(StatusBadgeResult result)
        {
            if (result == null)
            {
                return default;
            }

            var current = CurrentState ?? new StatusBadgeState(string.Empty);
            var message = result.Message ?? current.Message;

            return new StatusBadgeState(message)
            {
                IsUpdateNewState = result.IsUpdateNewState
            };
        }
    }
}
