using System;
using System.Collections.Generic;
using R3;

namespace MVI
{
    /// <summary>
    /// Store 统一配置模型（支持全局默认 + 局部覆写）。
    /// </summary>
    public sealed class StoreProfile
    {
        private readonly List<IStoreMiddleware> _middlewares = new();
        private readonly Dictionary<Type, IntentProcessingPolicy> _intentPolicies = new();

        public AwaitOperation? ProcessingMode { get; set; }

        public int? MaxConcurrent { get; set; }

        public int? StateHistoryCapacity { get; set; }

        public IStoreStatePersistence StatePersistence { get; set; }

        public IMviErrorStrategy ErrorStrategy { get; set; }

        public bool? DevToolsEnabled { get; set; }

        public int? DevToolsMaxEventsPerStore { get; set; }

        public MviDevToolsSamplingOptions DevToolsSamplingOptions { get; set; }

        public IList<IStoreMiddleware> Middlewares => _middlewares;

        public IDictionary<Type, IntentProcessingPolicy> IntentPolicies => _intentPolicies;

        public StoreProfile AddMiddleware(IStoreMiddleware middleware)
        {
            if (middleware != null)
            {
                _middlewares.Add(middleware);
            }

            return this;
        }

        public StoreProfile AddIntentPolicy<TIntent>(IntentProcessingPolicy policy)
            where TIntent : IIntent
        {
            _intentPolicies[typeof(TIntent)] = policy;
            return this;
        }

        /// <summary>
        /// 一键启用固定窗口限流中间件。
        /// </summary>
        /// <param name="limit">窗口内允许通过的最大次数（最小为 1）。</param>
        /// <param name="window">限流窗口时长。</param>
        /// <param name="keyResolver">限流 key 解析器；为空则按 Intent 类型限流。</param>
        /// <param name="throwOnRejected">超限时是否抛出异常；否则直接丢弃。</param>
        /// <param name="onRejected">超限回调。</param>
        public StoreProfile UseRateLimit(
            int limit,
            TimeSpan window,
            Func<IIntent, string> keyResolver = null,
            bool throwOnRejected = false,
            Action<StoreMiddlewareContext, string> onRejected = null)
        {
            return AddMiddleware(new RateLimitIntentMiddleware(
                limit: limit,
                window: window,
                keyResolver: keyResolver,
                throwOnRejected: throwOnRejected,
                onRejected: onRejected));
        }

        /// <summary>
        /// 一键启用熔断中间件（支持打开/半开探测/恢复）。
        /// </summary>
        /// <param name="failureThreshold">连续失败阈值。</param>
        /// <param name="openDuration">熔断打开时长。</param>
        /// <param name="keyResolver">熔断 key 解析器；为空则按 Intent 类型隔离熔断。</param>
        /// <param name="throwOnOpen">熔断打开时是否抛出异常；否则直接丢弃。</param>
        /// <param name="onOpened">熔断打开回调。</param>
        /// <param name="onClosed">半开探测成功后关闭熔断回调。</param>
        /// <param name="onRejected">熔断打开时请求被拒绝回调。</param>
        public StoreProfile UseCircuitBreaker(
            int failureThreshold,
            TimeSpan openDuration,
            Func<IIntent, string> keyResolver = null,
            bool throwOnOpen = true,
            Action<StoreMiddlewareContext, string, Exception> onOpened = null,
            Action<StoreMiddlewareContext, string> onClosed = null,
            Action<StoreMiddlewareContext, string> onRejected = null)
        {
            return AddMiddleware(new CircuitBreakerIntentMiddleware(
                failureThreshold: failureThreshold,
                openDuration: openDuration,
                keyResolver: keyResolver,
                throwOnOpen: throwOnOpen,
                onOpened: onOpened,
                onClosed: onClosed,
                onRejected: onRejected));
        }
    }
}
