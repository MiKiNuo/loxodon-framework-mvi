using System.Threading;
using System.Threading.Tasks;
using MVI;

namespace Loxodon.Framework.Examples
{
    public record LoginIntent : IIntent
    {
        public string UserName { get; set; }
        public string Password { get; set; }

        public async ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            await Task.CompletedTask;
            var result = new MviResult();
            

            return result;
        }
    }
}