namespace MVI.Examples.FairyGUI.Counter
{
    // State：UI 显示用的状态。
    internal sealed class CounterState : IState
    {
        public CounterState(int value)
        {
            Value = value;
        }

        public int Value { get; }
        public bool IsUpdateNewState { get; set; } = true;
    }
}
