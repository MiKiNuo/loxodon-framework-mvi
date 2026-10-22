using Loxodon.Framework.Binding;
using Loxodon.Framework.Binding.Builder;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Views;
using UnityEngine.UI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.CounterCard.ViewModels;

namespace Loxodon.Framework.Examples.Components.CounterCard.Views
{
    // 计数卡片 View：仅负责 UI 绑定。
    public class CounterCardView : UIView, IViewBinder
    {
        public Text labelText;
        public Text countText;
        public Button incrementButton;
        private bool isBound;

        // 建立数据绑定（调用一次即可）。
        public void Bind()
        {
            if (isBound)
            {
                return;
            }
            
            isBound = true;
            BindingSet<CounterCardView, CounterCardViewModel> bindingSet =
                this.CreateBindingSet<CounterCardView, CounterCardViewModel>();

            bindingSet.Bind(this.labelText).For(v => v.text).To(vm => vm.Label).OneWay();
            bindingSet.Bind(this.countText).For(v => v.text).ToExpression(vm => vm.Count.ToString()).OneWay();
            bindingSet.Bind(this.incrementButton).For(v => v.onClick).To(vm => vm.IncrementCommand);
            bindingSet.Build();
        }
    }
}
