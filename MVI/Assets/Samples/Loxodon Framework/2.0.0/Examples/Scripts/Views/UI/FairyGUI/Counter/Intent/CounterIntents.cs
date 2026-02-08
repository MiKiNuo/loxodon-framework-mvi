using System.Threading;
using System.Threading.Tasks;

namespace MVI.Examples.FairyGUI.Counter
{
    // 计数器 Intent 接口：统一约束本示例的意图类型。
    internal interface IFairyCounterIntent : IIntent
    {
    }

    // 增加计数的 Intent。
    internal sealed class IncrementIntent : IFairyCounterIntent
    {
        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            // 生成 Result，交给 Store 的 Reducer 处理。
            IMviResult result = new CounterDeltaResult(1);
            return new ValueTask<IMviResult>(result);
        }
    }

    // 减少计数的 Intent。
    internal sealed class DecrementIntent : IFairyCounterIntent
    {
        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            // 生成 Result，交给 Store 的 Reducer 处理。
            IMviResult result = new CounterDeltaResult(-1);
            return new ValueTask<IMviResult>(result);
        }
    }

    // 提交输入的 Intent（演示双向绑定与校验）。
    internal sealed class SubmitInputIntent : IFairyCounterIntent
    {
        public SubmitInputIntent(string inputText)
        {
            InputText = inputText;
        }

        public string InputText { get; }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new CounterValidationResult(InputText);
            return new ValueTask<IMviResult>(result);
        }
    }
}
