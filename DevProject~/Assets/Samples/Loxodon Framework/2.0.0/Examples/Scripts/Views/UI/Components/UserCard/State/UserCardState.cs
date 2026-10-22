using MVI;

namespace Loxodon.Framework.Examples.Components.UserCard.State
{
    // 用户卡片状态。
    public sealed class UserCardState : IState
    {
        // userName: 用户名；level: 等级。
        public UserCardState(string userName, int level)
        {
            UserName = userName;
            Level = level;
        }

        // 用户名。
        public string UserName { get; }

        // 等级。
        public int Level { get; }

        // 是否强制刷新状态。
        public bool IsUpdateNewState { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is not UserCardState other)
            {
                return false;
            }

            return Level == other.Level && string.Equals(UserName, other.UserName);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (UserName?.GetHashCode() ?? 0);
                hash = (hash * 31) + Level.GetHashCode();
                return hash;
            }
        }
    }
}
