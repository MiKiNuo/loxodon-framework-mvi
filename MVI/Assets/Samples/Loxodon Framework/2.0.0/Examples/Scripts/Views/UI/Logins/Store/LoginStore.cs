using Loxodon.Framework.Observables;
using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginStore : Store<LoginState, ILoginIntent, MviResult<LoginResult>, LoginEffect>
    {
        protected override LoginState Reduce(MviResult<LoginResult> result)
        {
            if (result == null)
            {
                return null;
            }

            var data = result.Data;
            return result.Code switch
            {
                -1 => OnLoginFailed(result.Msg, data),
                0 => OnLoginSucceeded(data),
                _ => null
            };
        }

        private LoginState OnLoginFailed(string message, LoginResult data)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                EmitEffect(new ShowToastEffect(message));
            }

            return new LoginFailureState()
            {
                Errors = data?.Errors ?? new ObservableDictionary<string, string>()
            };
        }

        private LoginState OnLoginSucceeded(LoginResult data)
        {
            EmitEffect(new FinishLoginEffect());
            return new LoginSuccessState() { Account = data?.Account };
        }
    }
}
