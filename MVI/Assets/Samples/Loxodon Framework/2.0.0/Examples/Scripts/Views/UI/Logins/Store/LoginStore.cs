using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginStore : Store
    {
        protected override IState Reducer(IMviResult result)
        {
            var mviResult = result as MviResult;
            return mviResult.Code switch
            {
                -1 => new LoginFailureState() { ToastContent = mviResult.Msg },
                0 => new LoginSuccessState() { Account = mviResult.Data as Account },
                _ => null
            };
        }
    }
}