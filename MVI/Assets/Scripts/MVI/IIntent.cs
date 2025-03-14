using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    public interface IIntent
    {
        ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default);
    }
}