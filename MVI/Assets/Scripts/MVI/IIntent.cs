using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    /// <summary>
    /// 处理意图并返回结果，允许异步执行。
    /// </summary>
    public interface IIntent
    {
        ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default);
    }
}
