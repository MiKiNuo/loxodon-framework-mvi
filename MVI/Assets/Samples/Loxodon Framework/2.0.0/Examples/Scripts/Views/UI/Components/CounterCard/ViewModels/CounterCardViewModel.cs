using System;
using Loxodon.Framework.Commands;
using MVI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.CounterCard.Intent;
using Loxodon.Framework.Examples.Components.CounterCard.State;
using Loxodon.Framework.Examples.Components.CounterCard.Store;

namespace Loxodon.Framework.Examples.Components.CounterCard.ViewModels
{
    // 计数卡片 ViewModel：接收 props / 处理点击 / 发出 CountChanged 事件。
    public sealed class CounterCardViewModel : MviViewModel<CounterCardState, ICounterCardIntent, CounterCardResult>, IPropsReceiver<CounterCardProps>
    {
        private int count;
        private string label;
        private readonly SimpleCommand incrementCommand;
        private int? lastNotifiedCount;

        public CounterCardViewModel()
        {
            incrementCommand = new SimpleCommand(OnIncrement);
            BindStore(new CounterCardStore());
            EmitIntent(new CounterInitIntent("Counter", 0));
        }

        // 当前计数。
        public int Count
        {
            get => count;
            set => Set(ref count, value);
        }

        // 标题文本。
        public string Label
        {
            get => label;
            set => Set(ref label, value);
        }

        // 点击事件绑定命令。
        public ICommand IncrementCommand => incrementCommand;

        // 计数变化事件（用于父组件联动）。
        public event Action<int> CountChanged;

        public void SetLabel(string newLabel)
        {
            EmitIntent(new CounterSetLabelIntent(newLabel));
        }

        public void SetCount(int newCount)
        {
            EmitIntent(new CounterInitIntent(Label ?? "Counter", newCount));
        }

        // 统一 props 入口。
        public void SetProps(CounterCardProps props)
        {
            if (props == null)
            {
                return;
            }

            EmitIntent(new CounterInitIntent(props.Label, props.Count));
        }

        protected override void OnStateChanged(CounterCardState state)
        {
            if (lastNotifiedCount != state.Count)
            {
                lastNotifiedCount = state.Count;
                CountChanged?.Invoke(state.Count);
            }
        }

        private void OnIncrement()
        {
            EmitIntent(new CounterIncrementIntent(1));
        }
    }
}
