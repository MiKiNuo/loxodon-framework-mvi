namespace MVI.Examples.FairyGUI.Counter
{
    // Store：处理 Intent -> Result -> State/Effect。
    internal sealed class CounterStore : Store<CounterState, IFairyCounterIntent, CounterResultBase, CounterEffect>
    {
        protected override CounterState InitialState => new CounterState(0);

        protected override CounterState Reduce(CounterResultBase result)
        {
            if (result is CounterDeltaResult delta)
            {
                // 核心逻辑：根据增量计算新值，并发出提示 Effect。
                var current = CurrentState?.Value ?? 0;
                var next = current + delta.Delta;
                EmitEffect(new CounterMessageEffect($"Counter: {next}"));
                return new CounterState(next);
            }

            if (result is CounterValidationResult validation)
            {
                // 校验输入：为空则发出提示，不改变计数状态。
                if (string.IsNullOrWhiteSpace(validation.InputText))
                {
                    EmitEffect(new CounterValidationEffect("请输入内容后再提交。"));
                }
                else
                {
                    EmitEffect(new CounterMessageEffect($"提交内容：{validation.InputText}"));
                }

                return CurrentState ?? InitialState;
            }

            return CurrentState ?? InitialState;
        }
    }
}
