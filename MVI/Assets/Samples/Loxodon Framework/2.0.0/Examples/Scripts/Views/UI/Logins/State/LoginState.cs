using Loxodon.Framework.Observables;
using MVI;

namespace Loxodon.Framework.Examples
{
    public record LoginState : IState
    {
        public bool LoginCommandEnable { set; get; } = true;
        public ObservableDictionary<string, string> Errors { set; get; } = new();

        public bool IsUpdateNewState { get; set; } = true;
    }
}
