using Loxodon.Framework.Examples.Components.UserCard.Intent;
using Loxodon.Framework.Examples.Components.UserCard.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.UserCard.Store
{
    // 用户卡片 Store：把 Result 归约为 State。
    public sealed class UserCardStore : Store<UserCardState, IUserCardIntent, UserCardResult>
    {
        protected override UserCardState Reduce(UserCardResult result)
        {
            if (result == null)
            {
                return default;
            }

            var current = CurrentState ?? new UserCardState("Guest", 1);
            var newName = result.UserName ?? current.UserName;
            var newLevel = result.Level ?? current.Level;

            return new UserCardState(newName, newLevel)
            {
                IsUpdateNewState = result.IsUpdateNewState
            };
        }
    }
}
