using System.Threading;
using System.Threading.Tasks;
using MVI;
using Loxodon.Framework.Examples.Components.StatusBadge.Store;

namespace Loxodon.Framework.Examples.Components.StatusBadge.Intent
{
    public interface IStatusBadgeIntent : IIntent
    {
    }

    // 设置状态文本的意图。
    public sealed class StatusSetIntent : IStatusBadgeIntent
    {
        private readonly string message;
        private readonly bool forceUpdate;

        public StatusSetIntent(string message, bool forceUpdate = false)
        {
            this.message = message;
            this.forceUpdate = forceUpdate;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new StatusBadgeResult(message, forceUpdate);
            return new ValueTask<IMviResult>(result);
        }
    }
}
