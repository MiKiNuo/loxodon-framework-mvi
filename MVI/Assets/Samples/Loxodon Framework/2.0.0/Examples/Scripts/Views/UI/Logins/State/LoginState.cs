using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginState : IState
    {

        public bool LoginCommandEnable { set; get; } = true;
        public string ToastContent { set; get; }

        public bool IsUpdateNewState { get; set; } = true;
    }
}