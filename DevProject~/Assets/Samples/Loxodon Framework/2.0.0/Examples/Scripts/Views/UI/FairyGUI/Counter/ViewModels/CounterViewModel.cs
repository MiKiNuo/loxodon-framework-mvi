using Loxodon.Framework.Commands;

namespace MVI.Examples.FairyGUI.Counter
{
    // 计数器 ViewModel：基于 MviViewModel，统一走 MVI 流程。
    internal sealed class CounterViewModel : MviViewModel<CounterState, IFairyCounterIntent, CounterResultBase, CounterEffect>
    {
        private int value;
        private string inputText;
        private readonly SimpleCommand incrementCommand;
        private readonly SimpleCommand decrementCommand;
        private readonly SimpleCommand submitCommand;

        public CounterViewModel()
        {
            incrementCommand = new SimpleCommand(OnIncrement);
            decrementCommand = new SimpleCommand(OnDecrement);
            submitCommand = new SimpleCommand(OnSubmit);
            BindStore(new CounterStore());
        }

        // 当前计数值（由 State 映射而来）。
        public int Value
        {
            get => value;
            set => Set(ref this.value, value);
        }

        // 输入内容（用于演示双向绑定）。
        public string InputText
        {
            get => inputText;
            set => Set(ref inputText, value);
        }

        // 增加计数命令（用于 FairyGUI 双向绑定）。
        public ICommand IncrementCommand => incrementCommand;

        // 减少计数命令（用于 FairyGUI 双向绑定）。
        public ICommand DecrementCommand => decrementCommand;

        // 提交输入命令（用于演示校验）。
        public ICommand SubmitCommand => submitCommand;

        // 增加计数。
        public void Increment()
        {
            EmitIntent(new IncrementIntent());
        }

        // 减少计数。
        public void Decrement()
        {
            EmitIntent(new DecrementIntent());
        }

        // 提交输入内容（触发校验）。
        public void SubmitInput()
        {
            EmitIntent(new SubmitInputIntent(InputText));
        }

        // 命令回调：转发为 Intent。
        private void OnIncrement()
        {
            Increment();
        }

        // 命令回调：转发为 Intent。
        private void OnDecrement()
        {
            Decrement();
        }

        // 命令回调：转发为 Intent。
        private void OnSubmit()
        {
            SubmitInput();
        }
    }
}
