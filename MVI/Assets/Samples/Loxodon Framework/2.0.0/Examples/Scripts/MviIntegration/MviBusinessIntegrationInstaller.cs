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
            bool autoDumpLoginStoreTimeline)
        {
            EnableDiagnostics = enableDiagnostics;
            EnableDevTools = enableDevTools;
            DevToolsMaxEvents = devToolsMaxEvents;
            EnableGlobalErrorStrategy = enableGlobalErrorStrategy;
            MaxRetryCount = maxRetryCount;
            RetryDelayMs = retryDelayMs;
            EnableLoginAuditMiddleware = enableLoginAuditMiddleware;
            AutoDumpLoginStoreTimeline = autoDumpLoginStoreTimeline;
        }

        public bool EnableDiagnostics { get; }

        public bool EnableDevTools { get; }

        public int DevToolsMaxEvents { get; }

        public bool EnableGlobalErrorStrategy { get; }

        public int MaxRetryCount { get; }

        public int RetryDelayMs { get; }

        public bool EnableLoginAuditMiddleware { get; }

        public bool AutoDumpLoginStoreTimeline { get; }
    }

    /// <summary>
    /// 业务运行时开关（供 ViewModel / Store 读取）。
    /// </summary>
    public static class BusinessMviIntegrationRuntime
    {
        public static bool EnableLoginAuditMiddleware { get; internal set; }

        public static bool AutoDumpLoginStoreTimeline { get; internal set; }

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
        }
    }

    /// <summary>
    /// 示例全局错误策略：
    /// - Intent 处理阶段按配置重试（默认不立即抛错，避免错误提示抖动）。
    /// - 非 Intent 阶段默认上抛到错误通道（Error/Effect）。
    /// </summary>
    public sealed class BusinessMviErrorStrategy : IMviErrorStrategy
    {
        private readonly int _maxRetryCount;
        private readonly int _retryDelayMs;

        public BusinessMviErrorStrategy(int maxRetryCount, int retryDelayMs)
        {
            _maxRetryCount = Mathf.Clamp(maxRetryCount, 0, 5);
            _retryDelayMs = Mathf.Clamp(retryDelayMs, 0, 5000);
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

            if (context.Phase == MviErrorPhase.IntentProcessing && context.Attempt < _maxRetryCount)
            {
                // 重试阶段先不发 Error，避免同一错误弹多次。
                return new ValueTask<MviErrorDecision>(
                    MviErrorDecision.Retry(
                        retryCount: _maxRetryCount,
                        retryDelay: TimeSpan.FromMilliseconds(_retryDelayMs),
                        emitError: false));
            }

            return new ValueTask<MviErrorDecision>(MviErrorDecision.Emit());
        }
    }
}
