using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginState : IState
    {
        public string Username { set; get; }
        public string Password { set; get; }
    }
}