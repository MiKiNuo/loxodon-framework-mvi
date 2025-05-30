/*
 * MIT License
 *
 * Copyright (c) 2018 Clark Yang
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using Loxodon.Framework.Commands;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Examples.Scripts.Views.UI.Logins.Const;
using Loxodon.Framework.Interactivity;
using Loxodon.Framework.Observables;
using Mapper;
using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginViewModel : MviViewModel
    {
        private string username;
        private string password;

        private Account account;
        private string toastContent;
        
        private bool loginCommandEnable;
        private bool isInteractionFinished;

        private SimpleCommand loginCommand;
        private SimpleCommand cancelCommand;

        private InteractionRequest interactionFinished;
        private InteractionRequest<ToastNotification> toastRequest;
        private ObservableDictionary<string, string> errors = new();


        public LoginViewModel()
        {
            var context = Context.GetApplicationContext();
            var globalPreferences = context.GetGlobalPreferences();

            this.interactionFinished = new InteractionRequest(this);
            this.toastRequest = new InteractionRequest<ToastNotification>(this);
            this.username ??= globalPreferences.GetString(LoginConst.LAST_USERNAME_KEY, "");

            this.loginCommand = new SimpleCommand(this.Login);
            this.cancelCommand = new SimpleCommand(() =>
            {
                this.interactionFinished.Raise(); /* Request to close the login window */
            });

            BindStore(new LoginStore());
            
        }
        
        public IInteractionRequest InteractionFinished => this.interactionFinished;

        public IInteractionRequest ToastRequest => this.toastRequest;

        public ObservableDictionary<string, string> Errors
        {
            get=> this.errors;
            set => this.Set(ref this.errors, value);
        }

        public ICommand LoginCommand => this.loginCommand;

        public ICommand CancelCommand => this.cancelCommand;


        public string Username
        {
            get => this.username;
            set => this.Set(ref this.username, value);
        }

        public string Password
        {
            get => this.password;
            set => this.Set(ref this.password, value);
        }

        public Account Account
        {
            get => this.account;
            set => this.Set(ref this.account, value);
        }

        public bool LoginCommandEnable
        {
            get => this.loginCommandEnable;
            set
            {
                if (Set(ref this.loginCommandEnable, value))
                {
                }

                this.loginCommand.Enabled = this.loginCommandEnable;
            }
        }

        public bool IsInteractionFinished
        {
            get => this.isInteractionFinished;
            set
            {
                if (Set(ref this.isInteractionFinished, value))
                {
                }

                /* Interaction completed, request to close the login window */
                this.interactionFinished.Raise();
            }
        }

        public string ToastContent
        {
            get => this.toastContent;
            set
            {
                if (Set(ref this.toastContent, value))
                {
                }

                this.toastRequest.Raise(new ToastNotification(this.toastContent, 2f));
            }
        }


        private void Login()
        {
            this.loginCommand.Enabled = false; /*by databinding, auto set button.interactable = false. */
            EmitIntent(new LoginIntent(this.Username, this.password));
        }
    }
}