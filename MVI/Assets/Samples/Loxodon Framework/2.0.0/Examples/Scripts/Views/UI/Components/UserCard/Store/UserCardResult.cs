using MVI;

namespace Loxodon.Framework.Examples.Components.UserCard.Store
{
    // 用户卡片意图结果。
    public sealed class UserCardResult : IMviResult
    {
        public UserCardResult(string userName, int? level, bool isUpdateNewState)
        {
            UserName = userName;
            Level = level;
            IsUpdateNewState = isUpdateNewState;
        }

        public string UserName { get; }
        public int? Level { get; }
        public bool IsUpdateNewState { get; }
    }
}
