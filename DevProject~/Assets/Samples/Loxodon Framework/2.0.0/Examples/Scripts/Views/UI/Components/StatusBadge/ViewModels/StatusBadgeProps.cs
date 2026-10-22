using System;
using MVI.Components;

namespace Loxodon.Framework.Examples.Components.StatusBadge.ViewModels
{
    // 状态徽标 props。
    public sealed class StatusBadgeProps : IEquatable<StatusBadgeProps>, IForceUpdateProps
    {
        public StatusBadgeProps(string message, bool forceUpdate = false)
        {
            Message = message;
            ForceUpdate = forceUpdate;
        }

        // 状态文本。
        public string Message { get; }

        // 是否强制更新（跳过 props diff）。
        public bool ForceUpdate { get; }

        public bool Equals(StatusBadgeProps other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return ForceUpdate == other.ForceUpdate && string.Equals(Message, other.Message);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StatusBadgeProps);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (Message?.GetHashCode() ?? 0);
                hash = (hash * 31) + ForceUpdate.GetHashCode();
                return hash;
            }
        }
    }
}
