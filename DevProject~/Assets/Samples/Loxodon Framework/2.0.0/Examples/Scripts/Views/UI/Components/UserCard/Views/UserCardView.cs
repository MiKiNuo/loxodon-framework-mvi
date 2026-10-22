using Loxodon.Framework.Binding;
using Loxodon.Framework.Binding.Builder;
using Loxodon.Framework.Views;
using UnityEngine.UI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.UserCard.ViewModels;

namespace Loxodon.Framework.Examples.Components.UserCard.Views
{
    // 用户卡片 View：仅负责 UI 绑定。
    public class UserCardView : UIView, IViewBinder
    {
        public Text userNameText;
        public Text levelText;
        public Button selectButton;
        private bool isBound;

        // 建立数据绑定（调用一次即可）。
        public void Bind()
        {
            if (isBound)
            {
                return;
            }

            isBound = true;
            BindingSet<UserCardView, UserCardViewModel> bindingSet =
                this.CreateBindingSet<UserCardView, UserCardViewModel>();

            bindingSet.Bind(this.userNameText).For(v => v.text).To(vm => vm.UserName).OneWay();
            bindingSet.Bind(this.levelText).For(v => v.text).ToExpression(vm => vm.Level.ToString()).OneWay();
            bindingSet.Bind(this.selectButton).For(v => v.onClick).To(vm => vm.SelectCommand);
            bindingSet.Build();
        }
    }
}
