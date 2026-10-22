namespace MVI.Examples.FairyGUI.Counter
{
    // Result 基类：统一约束 Counter 示例的结果类型。
    internal abstract class CounterResultBase : IMviResult
    {
    }

    // 增量结果：承载 Intent 处理后的计数变化。
    internal sealed class CounterDeltaResult : CounterResultBase
    {
        public CounterDeltaResult(int delta)
        {
            Delta = delta;
        }

        public int Delta { get; }
    }

    // 表单提交结果：用于校验输入。
    internal sealed class CounterValidationResult : CounterResultBase
    {
        public CounterValidationResult(string inputText)
        {
            InputText = inputText;
        }

        public string InputText { get; }
    }
}
