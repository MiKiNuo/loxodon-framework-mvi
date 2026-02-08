namespace MVI.Examples.FairyGUI.Counter
{
    // Effect：一次性事件，避免污染 State。
    internal abstract class CounterEffect : IMviEffect
    {
    }

    // Effect 的具体实现：输出文本提示。
    internal sealed class CounterMessageEffect : CounterEffect
    {
        public CounterMessageEffect(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    // 校验失败提示。
    internal sealed class CounterValidationEffect : CounterEffect
    {
        public CounterValidationEffect(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }
}
