using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginSuccessState : LoginState
    {
 
        public bool IsInteractionFinished { set; get; }
        public Account Account { set; get; }
    }
}