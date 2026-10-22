using Loxodon.Framework.Observables;

namespace Loxodon.Framework.Examples
{
    public class LoginResult
    {
        public LoginResult(Account account, ObservableDictionary<string, string> errors)
        {
            Account = account;
            Errors = errors;
        }

        public Account Account { get; }

        public ObservableDictionary<string, string> Errors { get; }
    }
}
