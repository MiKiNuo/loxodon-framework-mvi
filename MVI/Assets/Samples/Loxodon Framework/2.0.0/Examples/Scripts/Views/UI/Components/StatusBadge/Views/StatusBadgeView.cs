using Loxodon.Framework.Binding;
using Loxodon.Framework.Binding.Builder;
using Loxodon.Framework.Views;
using UnityEngine.UI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.StatusBadge.ViewModels;

namespace Loxodon.Framework.Examples.Components.StatusBadge.Views
{
    // 状态徽标 View：仅负责 UI 绑定。
    public class StatusBadgeView : UIView, IViewBinder
    {
        public Text messageText;
        private bool isBound;

        // 建立数据绑定（调用一次即可）。
        public void Bind()
        {
            if (isBound)
            {
                return;
            }

            isBound = true;
            BindingSet<StatusBadgeView, StatusBadgeViewModel> bindingSet =
                this.CreateBindingSet<StatusBadgeView, StatusBadgeViewModel>();

            bindingSet.Bind(this.messageText).For(v => v.text).To(vm => vm.Message).OneWay();
            bindingSet.Build();
        }
    }
}
