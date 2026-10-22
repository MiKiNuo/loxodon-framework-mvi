using ComposedDashboardWindow;
using Loxodon.Framework.Examples.Components.UserCard.ViewModels;
using MVI.FairyGUI.Composed;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Components.UserCard.Views
{
    // FairyGUI 用户卡片组件 View：绑定 UserCardViewModel。
    internal sealed class FairyUserCardView : FairyViewBase<UserCardViewModel>
    {
        private readonly UIUserCard view;

        public FairyUserCardView(UIUserCard view)
        {
            this.view = view;
        }

        public override void Bind()
        {
            if (view == null)
            {
                Debug.LogWarning("FairyUserCardView 绑定失败：UIUserCard 为空。");
                return;
            }

            var bindingSet = CreateBindingSet(view);

            // 用户名绑定。
            bindingSet.Bind(view.UserNameText)
                .For(v => v.text)
                .To(vm => vm.UserName)
                .OneWay();

            // 等级绑定。
            bindingSet.Bind(view.LevelText)
                .For(v => v.text)
                .ToExpression(vm => $"Lv.{vm.Level}")
                .OneWay();

            // 选中事件绑定命令。
            if (view.SelectButton != null)
            {
                bindingSet.Bind(view.SelectButton)
                    .For(v => v.onClick)
                    .To(vm => vm.SelectCommand);
            }
            else
            {
                Debug.LogWarning("FairyUserCardView 绑定失败：SelectButton 为空。");
            }

            bindingSet.Build();
        }
    }
}
