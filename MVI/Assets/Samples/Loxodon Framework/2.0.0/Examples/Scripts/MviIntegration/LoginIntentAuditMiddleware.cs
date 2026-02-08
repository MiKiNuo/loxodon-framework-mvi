using System;
using System.Threading.Tasks;
using MVI;
using UnityEngine;

namespace Loxodon.Framework.Examples
{
    /// <summary>
    /// 登录意图审计中间件（业务侧示例）：
    /// - 记录意图开始/结束
    /// - 对用户名做脱敏，避免明文日志泄露
    /// </summary>
    public sealed class LoginIntentAuditMiddleware : IStoreMiddleware
    {
        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            var startedAt = DateTime.UtcNow;
            if (context.Intent is LoginIntent loginIntent)
            {
                Debug.Log($"[MVI-Middleware] LoginIntent start, user={MaskUserName(loginIntent.UserName)}");
            }

            var result = await next(context);

            var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            if (context.Intent is LoginIntent)
            {
                Debug.Log($"[MVI-Middleware] LoginIntent done, elapsed={elapsedMs}ms, result={ResolveResultCode(result)}");
            }

            return result;
        }

        private static string MaskUserName(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return "<empty>";
            }

            if (userName.Length <= 2)
            {
                return "**";
            }

            return $"{userName.Substring(0, 2)}***";
        }

        private static string ResolveResultCode(IMviResult result)
        {
            if (result == null)
            {
                return "null";
            }

            var codeProperty = result.GetType().GetProperty("Code");
            if (codeProperty != null && codeProperty.PropertyType == typeof(int))
            {
                var code = codeProperty.GetValue(result);
                if (code is int typedCode)
                {
                    return typedCode.ToString();
                }
            }

            return result.GetType().Name;
        }
    }
}
