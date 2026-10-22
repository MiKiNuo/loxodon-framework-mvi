using System;

namespace Loxodon.Framework.Examples.Components.UserCard.ViewModels
{
    // 用户卡片 props。
    public sealed class UserCardProps : IEquatable<UserCardProps>
    {
        public UserCardProps(string userName, int level)
        {
            UserName = userName;
            Level = level;
        }

        // 用户名。
        public string UserName { get; }

        // 等级。
        public int Level { get; }

        public bool Equals(UserCardProps other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Level == other.Level && string.Equals(UserName, other.UserName);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UserCardProps);
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
