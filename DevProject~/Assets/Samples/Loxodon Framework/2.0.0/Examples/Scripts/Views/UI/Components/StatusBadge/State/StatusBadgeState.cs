using MVI;

namespace Loxodon.Framework.Examples.Components.StatusBadge.State
{
    // 状态徽标状态。
    public sealed class StatusBadgeState : IState
    {
        // message: 状态文本。
        public StatusBadgeState(string message)
        {
            Message = message;
        }

        // 状态文本。
        public string Message { get; }

        // 是否强制刷新状态。
        public bool IsUpdateNewState { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is not StatusBadgeState other)
            {
                return false;
            }

            return string.Equals(Message, other.Message);
        }

        public override int GetHashCode()
        {
            return Message?.GetHashCode() ?? 0;
        }
    }
}
