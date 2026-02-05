using MVI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.StatusBadge.Intent;
using Loxodon.Framework.Examples.Components.StatusBadge.State;
using Loxodon.Framework.Examples.Components.StatusBadge.Store;

namespace Loxodon.Framework.Examples.Components.StatusBadge.ViewModels
{
    // 状态徽标 ViewModel：接收 props 并更新文本。
    public sealed class StatusBadgeViewModel : MviViewModel<StatusBadgeState, IStatusBadgeIntent, StatusBadgeResult>, IPropsReceiver<StatusBadgeProps>
    {
        private string message;

        public StatusBadgeViewModel()
        {
            BindStore(new StatusBadgeStore());
            EmitIntent(new StatusSetIntent("Ready", true));
        }

        // 状态文本。
        public string Message
        {
            get => message;
            set => Set(ref message, value);
        }

        // 直接设置状态文本。
        public void SetMessage(string newMessage, bool forceUpdate = false)
        {
            EmitIntent(new StatusSetIntent(newMessage, forceUpdate));
        }

        // 统一 props 入口。
        public void SetProps(StatusBadgeProps props)
        {
            if (props == null)
            {
                return;
            }

            EmitIntent(new StatusSetIntent(props.Message, props.ForceUpdate));
        }
    }
}
