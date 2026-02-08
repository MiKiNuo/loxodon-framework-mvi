using ComposedDashboardWindow;
using Loxodon.Framework.Examples.Components.StatusBadge.ViewModels;
using MVI.FairyGUI.Composed;

namespace MVI.Examples.FairyGUI.Composed.Components.StatusBadge.Views
{
    // FairyGUI 状态徽标组件 View：绑定 StatusBadgeViewModel。
    internal sealed class FairyStatusBadgeView : FairyViewBase<StatusBadgeViewModel>
    {
        private readonly UIStatusBadge view;

        public FairyStatusBadgeView(UIStatusBadge view)
        {
            this.view = view;
        }

        public override void Bind()
        {
            if (view == null)
            {
                return;
            }

            var bindingSet = CreateBindingSet(view);

            // 状态文本绑定。
            bindingSet.Bind(view.MessageText)
                .For(v => v.text)
                .To(vm => vm.Message)
                .OneWay();

            bindingSet.Build();
        }
    }
}
