using Loxodon.Framework.Observables;
using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginState : IState
    {
        public bool LoginCommandEnable { set; get; } = true;
        public ObservableDictionary<string, string> Errors { set; get; } = new();
        public string ToastContent { set; get; }

        public bool IsUpdateNewState { get; set; } = true;
    }
}