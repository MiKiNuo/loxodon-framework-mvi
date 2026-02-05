using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Examples.Scripts.Views.UI.Logins.Const;
using Loxodon.Framework.Localizations;
using Loxodon.Framework.Observables;
using Loxodon.Framework.Prefs;
using Loxodon.Log;
using MVI;

namespace Loxodon.Framework.Examples
{
    public interface ILoginIntent : IIntent
    {
    }

    public class LoginIntent : ILoginIntent
    {
        public string UserName { get; set; }
        public string Password { get; set; }

        private readonly ILog log;
        private readonly Localization localization;
        private readonly Preferences globalPreferences;
        private readonly IAccountService accountService;


        public LoginIntent(string username, string password)
        {
            this.UserName = username;
            this.Password = password;

            this.log = LogManager.GetLogger(typeof(LoginIntent));
            var context = Context.GetApplicationContext();

            this.localization = context.GetService<Localization>();
            this.accountService = context.GetService<IAccountService>();
            this.globalPreferences = context.GetGlobalPreferences();
        }

        private (bool, string) ValidateUsername()
        {
            var error = localization.GetText("login.validation.username.error", "Please enter a valid username.");
            return (!string.IsNullOrEmpty(this.UserName) && Regex.IsMatch(this.UserName, "^[a-zA-Z0-9_-]{4,12}$"),
                error);
        }

        private (bool, string) ValidatePassword()
        {
            var error = localization.GetText("login.validation.password.error", "Please enter a valid password.");
            return (!string.IsNullOrEmpty(this.Password) && Regex.IsMatch(this.Password, "^[a-zA-Z0-9_-]{4,12}$"),
                error);
        }

        public async ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            var result = new MviResult<LoginResult>();
            if (log.IsDebugEnabled)
                log.DebugFormat("login start. username:{0} password:{1}", this.UserName, this.Password);
            var validateUsername = this.ValidateUsername();
            if (!validateUsername.Item1)
            {
                result.Code = -1;
                result.Msg = validateUsername.Item2;
                result.Data = new LoginResult(null, new ObservableDictionary<string, string> { { "username", validateUsername.Item2 } });
                return result;
            }

            var validatePassword = this.ValidatePassword();
            if (!validatePassword.Item1)
            {
                result.Code = -1;
                result.Msg = validatePassword.Item2;
                result.Data = new LoginResult(null, new ObservableDictionary<string, string> { { "password", validatePassword.Item2 } });
                return result;
            }

            try
            {
                var account = await this.accountService.Login(this.UserName, this.Password);
                if (account != null)
                {
                    result.Data = new LoginResult(account, null);
                    result.Code = 0;
                    /* login success */
                    globalPreferences.SetString(LoginConst.LAST_USERNAME_KEY, this.UserName);
                    globalPreferences.Save();
                }
                else
                {
                    /* Login failure */
                    var tipContent = this.localization.GetText("login.failure.tip", "Login failure.");
                    result.Code = -1;
                    result.Msg = tipContent;
                    result.Data = new LoginResult(null, null);
                }
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.ErrorFormat("Exception:{0}", e);
                var tipContent = this.localization.GetText("login.exception.tip", "Login exception.");
                result.Code = -1;
                result.Msg = tipContent;
                result.Data = new LoginResult(null, new ObservableDictionary<string, string> { { "exception", e.Message } });
            }

            return result;
        }
    }
}
