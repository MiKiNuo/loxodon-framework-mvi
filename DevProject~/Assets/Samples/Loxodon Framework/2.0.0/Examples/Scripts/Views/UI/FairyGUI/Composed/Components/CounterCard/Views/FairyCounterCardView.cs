using ComposedDashboardWindow;
using Loxodon.Framework.Examples.Components.CounterCard.ViewModels;
using MVI.FairyGUI.Composed;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Components.CounterCard.Views
{
    // FairyGUI 计数卡片组件 View：绑定 CounterCardViewModel。
    internal sealed class FairyCounterCardView : FairyViewBase<CounterCardViewModel>
    {
        private readonly UICounterCard view;

        public FairyCounterCardView(UICounterCard view)
        {
            this.view = view;
        }

        public override void Bind()
        {
            if (view == null)
            {
                Debug.LogWarning("FairyCounterCardView 绑定失败：UICounterCard 为空。");
                return;
            }

            var bindingSet = CreateBindingSet(view);

            // 标题文本绑定。
            bindingSet.Bind(view.LabelText)
                .For(v => v.text)
                .To(vm => vm.Label)
                .OneWay();

            // 计数值绑定。
            bindingSet.Bind(view.CountText)
                .For(v => v.text)
                .ToExpression(vm => vm.Count.ToString())
                .OneWay();

            // 点击事件绑定命令。
            if (view.IncrementButton != null)
            {
                bindingSet.Bind(view.IncrementButton)
                    .For(v => v.onClick)
                    .To(vm => vm.IncrementCommand);
            }
            else
            {
                Debug.LogWarning("FairyCounterCardView 绑定失败：IncrementButton 为空。");
            }

            bindingSet.Build();
        }
    }
}
