using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MVI;
using UnityEngine;

namespace Loxodon.Framework.Examples
{
    /// <summary>
    /// 业务侧 MVI 集成参数（可由 Launcher Inspector 配置）。
    /// </summary>
    public readonly struct BusinessMviIntegrationOptions
    {
        public BusinessMviIntegrationOptions(
            bool enableDiagnostics,
            bool enableDevTools,
            int devToolsMaxEvents,
            bool enableGlobalErrorStrategy,
            int maxRetryCount,
            int retryDelayMs,
            bool enableLoginAuditMiddleware,
            bool autoDumpLoginStoreTimeline,
            bool enableBuiltinLoginMiddlewares,
            bool enableLoginLoggingMiddleware,
            int loginDebounceMs,
            int loginTimeoutMs,
            bool enableLoginMetricsMiddleware,
            bool autoDumpLoginMiddlewareMetrics)
        {
            EnableDiagnostics = enableDiagnostics;
            EnableDevTools = enableDevTools;
            DevToolsMaxEvents = devToolsMaxEvents;
            EnableGlobalErrorStrategy = enableGlobalErrorStrategy;
            MaxRetryCount = maxRetryCount;
            RetryDelayMs = retryDelayMs;
            EnableLoginAuditMiddleware = enableLoginAuditMiddleware;
            AutoDumpLoginStoreTimeline = autoDumpLoginStoreTimeline;
            EnableBuiltinLoginMiddlewares = enableBuiltinLoginMiddlewares;
            EnableLoginLoggingMiddleware = enableLoginLoggingMiddleware;
            LoginDebounceMs = loginDebounceMs;
            LoginTimeoutMs = loginTimeoutMs;
            EnableLoginMetricsMiddleware = enableLoginMetricsMiddleware;
            AutoDumpLoginMiddlewareMetrics = autoDumpLoginMiddlewareMetrics;
        }

        public bool EnableDiagnostics { get; }

        public bool EnableDevTools { get; }

        public int DevToolsMaxEvents { get; }

        public bool EnableGlobalErrorStrategy { get; }

        public int MaxRetryCount { get; }

        public int RetryDelayMs { get; }

        public bool EnableLoginAuditMiddleware { get; }

        public bool AutoDumpLoginStoreTimeline { get; }

        public bool EnableBuiltinLoginMiddlewares { get; }

        public bool EnableLoginLoggingMiddleware { get; }

        public int LoginDebounceMs { get; }

        public int LoginTimeoutMs { get; }

        public bool EnableLoginMetricsMiddleware { get; }

        public bool AutoDumpLoginMiddlewareMetrics { get; }
    }

    /// <summary>
    /// 业务运行时开关（供 ViewModel / Store 读取）。
    /// </summary>
    public static class BusinessMviIntegrationRuntime
    {
        public static bool EnableLoginAuditMiddleware { get; internal set; }

        public static bool AutoDumpLoginStoreTimeline { get; internal set; }

        // 登录链路内置中间件（日志/防抖/超时）开关。
        public static bool EnableBuiltinLoginMiddlewares { get; internal set; }

        public static bool EnableLoginLoggingMiddleware { get; internal set; }

        public static int LoginDebounceMs { get; internal set; } = 250;

        public static int LoginTimeoutMs { get; internal set; } = 5000;

        // 登录链路指标中间件开关。
        public static bool EnableLoginMetricsMiddleware { get; internal set; }

        public static bool AutoDumpLoginMiddlewareMetrics { get; internal set; }

        public static void DumpTimeline(Store store, string tag)
        {
            if (store == null || !MviDevTools.Enabled)
            {
                return;
            }

            var snapshot = store.GetTimelineSnapshot();
            if (snapshot == null || snapshot.Count == 0)
            {
                Debug.Log($"[MVI-DevTools] {tag} timeline is empty.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[MVI-DevTools] {tag} timeline events: {snapshot.Count}");
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                var payloadType = entry.Payload?.GetType().Name ?? "null";
                sb.AppendLine($"#{entry.Sequence} {entry.Kind} payload={payloadType} note={entry.Note}");
            }

            Debug.Log(sb.ToString());
        }
    }

    /// <summary>
    /// MVI 业务接入安装器：集中配置中间件开关、错误策略、DevTools。
    /// </summary>
    public static class MviBusinessIntegrationInstaller
    {
        public static void Install(BusinessMviIntegrationOptions options)
        {
            // 1) 诊断日志开关（轻量级链路追踪）。
            MviDiagnostics.Enabled = options.EnableDiagnostics;
            MviDiagnostics.Log = options.EnableDiagnostics
                ? (message => Debug.Log($"[MVI-Diagnostics] {message}"))
                : null;

            // 2) DevTools 时间线开关（Intent/Result/State/Effect/Error）。
            MviDevTools.Enabled = options.EnableDevTools;
            MviDevTools.MaxEventsPerStore = options.DevToolsMaxEvents > 0 ? options.DevToolsMaxEvents : 256;

            // 3) 全局错误策略开关（重试/降级/抑制）。
            MviStoreOptions.DefaultErrorStrategy = options.EnableGlobalErrorStrategy
                ? new BusinessMviErrorStrategy(options.MaxRetryCount, options.RetryDelayMs)
                : null;

            // 4) 业务中间件开关（这里演示登录意图审计）。
            BusinessMviIntegrationRuntime.EnableLoginAuditMiddleware = options.EnableLoginAuditMiddleware;
            BusinessMviIntegrationRuntime.AutoDumpLoginStoreTimeline = options.AutoDumpLoginStoreTimeline;

            // 5) 内置中间件开关（用于演示通用库在业务链路中的接入方式）。
            BusinessMviIntegrationRuntime.EnableBuiltinLoginMiddlewares = options.EnableBuiltinLoginMiddlewares;
            BusinessMviIntegrationRuntime.EnableLoginLoggingMiddleware = options.EnableLoginLoggingMiddleware;
            BusinessMviIntegrationRuntime.LoginDebounceMs = Mathf.Clamp(options.LoginDebounceMs, 0, 10_000);
            BusinessMviIntegrationRuntime.LoginTimeoutMs = Mathf.Clamp(options.LoginTimeoutMs, 100, 60_000);
            BusinessMviIntegrationRuntime.EnableLoginMetricsMiddleware = options.EnableLoginMetricsMiddleware;
            BusinessMviIntegrationRuntime.AutoDumpLoginMiddlewareMetrics = options.AutoDumpLoginMiddlewareMetrics;
        }
    }

    /// <summary>
    /// 示例全局错误策略：
    /// - Intent 处理阶段按配置重试（默认不立即抛错，避免错误提示抖动）。
    /// - 非 Intent 阶段默认上抛到错误通道（Error/Effect）。
    /// </summary>
    public sealed class BusinessMviErrorStrategy : IMviErrorStrategy
    {
        private readonly TemplateMviErrorStrategy _template;

        public BusinessMviErrorStrategy(int maxRetryCount, int retryDelayMs)
        {
            var safeRetryCount = Mathf.Clamp(maxRetryCount, 0, 5);
            var safeRetryDelayMs = Mathf.Clamp(retryDelayMs, 0, 5000);
            _template = new TemplateMviErrorStrategyBuilder()
                // 业务码示例：未授权直接发错误，不做重试。
                .ForBusinessCode(401, _ => MviErrorDecision.Emit())
                // 超时异常示例：按指数退避重试。
                .UseExponentialBackoffForException<TimeoutException>(
                    maxRetryCount: safeRetryCount,
                    baseDelayMs: safeRetryDelayMs,
                    maxDelayMs: Math.Max(safeRetryDelayMs * 4, safeRetryDelayMs),
                    emitErrorOnRetry: false)
                // 兜底：普通异常也允许重试（可按业务移除）。
                .UseExponentialBackoffForException<Exception>(
                    maxRetryCount: safeRetryCount,
                    baseDelayMs: safeRetryDelayMs,
                    maxDelayMs: Math.Max(safeRetryDelayMs * 4, safeRetryDelayMs),
                    emitErrorOnRetry: false,
                    exhaustedDecision: MviErrorDecision.Emit())
                .Build();
        }

        public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
        {
            if (context == null || context.Exception == null)
            {
                return new ValueTask<MviErrorDecision>(MviErrorDecision.Emit());
            }

            if (context.Exception is OperationCanceledException)
            {
                // 取消不视为业务错误，直接忽略。
                return new ValueTask<MviErrorDecision>(MviErrorDecision.Ignore());
            }

            return _template.DecideAsync(context, cancellationToken);
        }
    }
}
