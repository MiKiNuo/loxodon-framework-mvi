using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginStore : Store
    {
        protected override IState Reducer(IMviResult result)
        {
            var mviResult = result as MviResult;
            return new LoginState();
        }
    }
}