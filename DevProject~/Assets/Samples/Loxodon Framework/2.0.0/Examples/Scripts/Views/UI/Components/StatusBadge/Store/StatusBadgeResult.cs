using MVI;

namespace Loxodon.Framework.Examples.Components.StatusBadge.Store
{
    // 状态徽标意图结果。
    public sealed class StatusBadgeResult : IMviResult
    {
        public StatusBadgeResult(string message, bool isUpdateNewState)
        {
            Message = message;
            IsUpdateNewState = isUpdateNewState;
        }

        public string Message { get; }
        public bool IsUpdateNewState { get; }
    }
}
