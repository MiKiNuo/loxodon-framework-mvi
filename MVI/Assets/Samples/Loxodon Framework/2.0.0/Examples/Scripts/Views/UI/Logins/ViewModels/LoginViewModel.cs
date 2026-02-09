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

using System;
using Loxodon.Framework.Commands;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Examples.Scripts.Views.UI.Logins.Const;
using Loxodon.Framework.Interactivity;
using Loxodon.Framework.Observables;
using MVI;

namespace Loxodon.Framework.Examples
{
    public class LoginViewModel : MviViewModel<LoginState, ILoginIntent, MviResult<LoginResult>, LoginEffect>
    {
        // 业务 Store 引用（用于挂载中间件与可选的 DevTools 时间线输出）。
        private readonly LoginStore loginStore;
        private readonly StoreMiddlewareMetricsCollector loginMiddlewareMetrics;
        private string username;
        private string password;

        private Account account;
        private bool loginCommandEnable;

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

            // 业务侧接入示例：在 ViewModel 构造阶段给 Store 挂中间件。
            this.loginStore = new LoginStore();
            this.loginMiddlewareMetrics = ConfigureStoreMiddlewares(this.loginStore);

            BindStore(this.loginStore);
            
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

        protected override void OnEffect(LoginEffect effect)
        {
            switch (effect)
            {
                case ShowToastEffect toast:
                    this.toastRequest.Raise(new ToastNotification(toast.Message, toast.Duration));
                    break;
                case FinishLoginEffect:
                    if (BusinessMviIntegrationRuntime.AutoDumpLoginStoreTimeline)
                    {
                        // DevTools 示例：登录完成后打印当前 Store 的时间线快照。
                        BusinessMviIntegrationRuntime.DumpTimeline(this.loginStore, nameof(LoginStore));
                    }

                    if (BusinessMviIntegrationRuntime.AutoDumpLoginMiddlewareMetrics && this.loginMiddlewareMetrics != null)
                    {
                        var metrics = this.loginMiddlewareMetrics.CaptureSnapshot();
                        UnityEngine.Debug.Log(
                            $"[MVI-Middleware] Login metrics total={metrics.TotalCount}, success={metrics.SuccessCount}, failure={metrics.FailureCount}, avgMs={metrics.AverageElapsedMs:F1}");
                    }
                    this.interactionFinished.Raise();
                    break;
            }
        }

        protected override void OnError(MviErrorEffect error)
        {
            if (string.IsNullOrWhiteSpace(error?.Message))
            {
                return;
            }

            this.toastRequest.Raise(new ToastNotification(error.Message, 2f));
        }


        private void Login()
        {
            this.loginCommand.Enabled = false; /*by databinding, auto set button.interactable = false. */
            EmitIntent(new LoginIntent(this.Username, this.password));
        }

        private static StoreMiddlewareMetricsCollector ConfigureStoreMiddlewares(LoginStore store)
        {
            if (store == null)
            {
                return null;
            }

            // 示例 1：业务自定义中间件（审计日志 + 用户名脱敏）。
            if (BusinessMviIntegrationRuntime.EnableLoginAuditMiddleware)
            {
                store.UseMiddleware(new LoginIntentAuditMiddleware());
            }

            // 示例 2：框架内置中间件组合（日志/防抖/超时）。
            if (BusinessMviIntegrationRuntime.EnableBuiltinLoginMiddlewares)
            {
                if (BusinessMviIntegrationRuntime.EnableLoginLoggingMiddleware)
                {
                    store.UseMiddleware(new LoggingStoreMiddleware());
                }

                var debounceMs = Math.Max(0, BusinessMviIntegrationRuntime.LoginDebounceMs);
                if (debounceMs > 0)
                {
                    // 防抖：短时间内重复点击登录，仅保留一次。
                    store.UseMiddleware(new DebounceIntentMiddleware(
                        window: TimeSpan.FromMilliseconds(debounceMs),
                        keyResolver: _ => nameof(LoginIntent)));
                }

                var timeoutMs = Math.Max(100, BusinessMviIntegrationRuntime.LoginTimeoutMs);
                // 超时：防止网络长时间阻塞导致登录交互无响应。
                store.UseMiddleware(new TimeoutIntentMiddleware(TimeSpan.FromMilliseconds(timeoutMs)));
            }

            // 指标中间件：用于统计登录链路成功率和平均耗时。
            if (!BusinessMviIntegrationRuntime.EnableLoginMetricsMiddleware)
            {
                return null;
            }

            var collector = new StoreMiddlewareMetricsCollector();
            store.UseMiddleware(new MetricsStoreMiddleware(collector));
            return collector;
        }
    }
}
