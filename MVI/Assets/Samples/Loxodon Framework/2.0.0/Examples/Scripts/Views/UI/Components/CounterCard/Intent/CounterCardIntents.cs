using System.Threading;
using System.Threading.Tasks;
using MVI;
using Loxodon.Framework.Examples.Components.CounterCard.Store;

namespace Loxodon.Framework.Examples.Components.CounterCard.Intent
{
    // 初始化计数与标题。
    public sealed class CounterInitIntent : IIntent
    {
        private readonly string label;
        private readonly int count;

        public CounterInitIntent(string label, int count)
        {
            this.label = label;
            this.count = count;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new CounterCardResult(count, null, label, true);
            return new ValueTask<IMviResult>(result);
        }
    }

    // 计数增量意图。
    public sealed class CounterIncrementIntent : IIntent
    {
        private readonly int delta;

        public CounterIncrementIntent(int delta)
        {
            this.delta = delta;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new CounterCardResult(null, delta, null, false);
            return new ValueTask<IMviResult>(result);
        }
    }

    // 设置标题意图。
    public sealed class CounterSetLabelIntent : IIntent
    {
        private readonly string label;

        public CounterSetLabelIntent(string label)
        {
            this.label = label;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new CounterCardResult(null, null, label, false);
            return new ValueTask<IMviResult>(result);
        }
    }
}
