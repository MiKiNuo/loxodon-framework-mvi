using Loxodon.Framework.Examples.Components.UserCard.State;
using MVI;

namespace Loxodon.Framework.Examples.Components.UserCard.Store
{
    // 用户卡片 Store：把 Result 归约为 State。
    public sealed class UserCardStore : MVI.Store
    {
        protected override IState Reducer(IMviResult result)
        {
            if (result is not UserCardResult userResult)
            {
                return default;
            }

            var current = CurrentState as UserCardState ?? new UserCardState("Guest", 1);
            var newName = userResult.UserName ?? current.UserName;
            var newLevel = userResult.Level ?? current.Level;

            return new UserCardState(newName, newLevel)
            {
                IsUpdateNewState = userResult.IsUpdateNewState
            };
        }
    }
}
