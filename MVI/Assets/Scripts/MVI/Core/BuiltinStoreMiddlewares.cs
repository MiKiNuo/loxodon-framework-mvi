using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    public readonly struct StoreMiddlewareMetricsSnapshot
    {
        public StoreMiddlewareMetricsSnapshot(long totalCount, long successCount, long failureCount, long totalElapsedMs)
        {
            TotalCount = totalCount;
            SuccessCount = successCount;
            FailureCount = failureCount;
            TotalElapsedMs = totalElapsedMs;
        }

        public long TotalCount { get; }

        public long SuccessCount { get; }

        public long FailureCount { get; }

        public long TotalElapsedMs { get; }

        public double AverageElapsedMs => TotalCount <= 0 ? 0d : (double)TotalElapsedMs / TotalCount;
    }

    /// <summary>
    /// Store 中间件链路统计器（请求总量/成功失败/平均耗时）。
    /// </summary>
    public sealed class StoreMiddlewareMetricsCollector
    {
        private long _totalCount;
        private long _successCount;
        private long _failureCount;
        private long _totalElapsedMs;

        internal void RecordSuccess(long elapsedMs)
        {
            Interlocked.Increment(ref _totalCount);
            Interlocked.Increment(ref _successCount);
            Interlocked.Add(ref _totalElapsedMs, Math.Max(0, elapsedMs));
        }

        internal void RecordFailure(long elapsedMs)
        {
            Interlocked.Increment(ref _totalCount);
            Interlocked.Increment(ref _failureCount);
            Interlocked.Add(ref _totalElapsedMs, Math.Max(0, elapsedMs));
        }

        public StoreMiddlewareMetricsSnapshot CaptureSnapshot()
        {
            return new StoreMiddlewareMetricsSnapshot(
                Interlocked.Read(ref _totalCount),
                Interlocked.Read(ref _successCount),
                Interlocked.Read(ref _failureCount),
                Interlocked.Read(ref _totalElapsedMs));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalCount, 0);
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _failureCount, 0);
            Interlocked.Exchange(ref _totalElapsedMs, 0);
        }
    }

    /// <summary>
    /// 指标中间件：统计中间件链路成功率与耗时。
    /// </summary>
    public sealed class MetricsStoreMiddleware : IStoreMiddleware
    {
        public MetricsStoreMiddleware(StoreMiddlewareMetricsCollector collector = null)
        {
            Collector = collector ?? new StoreMiddlewareMetricsCollector();
        }

        public StoreMiddlewareMetricsCollector Collector { get; }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            var startedAt = DateTime.UtcNow;
            try
            {
                var result = await next(context);
                var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                Collector.RecordSuccess(elapsedMs);
                return result;
            }
            catch
            {
                var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                Collector.RecordFailure(elapsedMs);
                throw;
            }
        }
    }

    /// <summary>
    /// 内置意图键解析器。
    /// </summary>
    public static class StoreMiddlewareIntentKeyResolvers
    {
        public static string ByIntentType(IIntent intent)
        {
            return intent?.GetType().FullName ?? string.Empty;
        }

        public static string ByIntentToString(IIntent intent)
        {
            if (intent == null)
            {
                return string.Empty;
            }

            return $"{intent.GetType().FullName}:{intent}";
        }
    }

    /// <summary>
    /// 日志中间件：记录 Intent 的开始、结束、耗时与异常。
    /// </summary>
    public sealed class LoggingStoreMiddleware : IStoreMiddleware
    {
        private readonly Action<string> _logger;

        public LoggingStoreMiddleware(Action<string> logger = null)
        {
            _logger = logger ?? MviDiagnostics.Trace;
        }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            var intentName = context.Intent?.GetType().Name ?? "UnknownIntent";
            var startedAt = DateTime.UtcNow;
            _logger?.Invoke($"[MVI-Middleware] {intentName} start.");
            try
            {
                var result = await next(context);
                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                _logger?.Invoke($"[MVI-Middleware] {intentName} done ({elapsedMs}ms), result={(result == null ? "null" : result.GetType().Name)}.");
                return result;
            }
            catch (Exception ex)
            {
                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                _logger?.Invoke($"[MVI-Middleware] {intentName} failed ({elapsedMs}ms): {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// 防抖中间件：同一 key 在窗口期内仅放行一次。
    /// </summary>
    public sealed class DebounceIntentMiddleware : IStoreMiddleware
    {
        private readonly TimeSpan _window;
        private readonly Func<IIntent, string> _keyResolver;
        private readonly Action<StoreMiddlewareContext, string> _onDropped;
        private readonly Dictionary<string, DateTime> _latest = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

        public DebounceIntentMiddleware(
            TimeSpan window,
            Func<IIntent, string> keyResolver = null,
            Action<StoreMiddlewareContext, string> onDropped = null)
        {
            _window = window < TimeSpan.Zero ? TimeSpan.Zero : window;
            _keyResolver = keyResolver ?? StoreMiddlewareIntentKeyResolvers.ByIntentType;
            _onDropped = onDropped;
        }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            if (_window <= TimeSpan.Zero)
            {
                return await next(context);
            }

            var key = _keyResolver(context.Intent) ?? string.Empty;
            var now = DateTime.UtcNow;
            lock (_syncRoot)
            {
                if (_latest.TryGetValue(key, out var last) && now - last < _window)
                {
                    _onDropped?.Invoke(context, key);
                    return default;
                }

                _latest[key] = now;
            }

            return await next(context);
        }
    }

    /// <summary>
    /// 去重中间件：同一 key 的意图在执行中时，后续请求直接丢弃。
    /// </summary>
    public sealed class DeduplicateIntentMiddleware : IStoreMiddleware
    {
        private readonly Func<IIntent, string> _keyResolver;
        private readonly Action<StoreMiddlewareContext, string> _onSkipped;
        private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

        public DeduplicateIntentMiddleware(
            Func<IIntent, string> keyResolver = null,
            Action<StoreMiddlewareContext, string> onSkipped = null)
        {
            _keyResolver = keyResolver ?? StoreMiddlewareIntentKeyResolvers.ByIntentType;
            _onSkipped = onSkipped;
        }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            var key = _keyResolver(context.Intent) ?? string.Empty;
            lock (_syncRoot)
            {
                if (_inFlight.Contains(key))
                {
                    _onSkipped?.Invoke(context, key);
                    return default;
                }

                _inFlight.Add(key);
            }

            try
            {
                return await next(context);
            }
            finally
            {
                lock (_syncRoot)
                {
                    _inFlight.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// 结果缓存中间件：同一 key 命中缓存时直接返回缓存结果。
    /// </summary>
    public sealed class CacheResultMiddleware : IStoreMiddleware
    {
        private sealed class CacheEntry
        {
            public IMviResult Result;
            public DateTime ExpireAtUtc;
            public long LastAccessOrder;
        }

        private readonly Func<IIntent, string> _keyResolver;
        private readonly TimeSpan _ttl;
        private readonly int _maxEntryCount;
        private readonly Action<StoreMiddlewareContext, string> _onCacheHit;
        private readonly Action<StoreMiddlewareContext, string, IMviResult> _onCacheStore;
        private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();
        private long _accessOrder;

        public CacheResultMiddleware(
            TimeSpan ttl,
            Func<IIntent, string> keyResolver = null,
            int maxEntryCount = 256,
            Action<StoreMiddlewareContext, string> onCacheHit = null,
            Action<StoreMiddlewareContext, string, IMviResult> onCacheStore = null)
        {
            _ttl = ttl;
            _keyResolver = keyResolver ?? StoreMiddlewareIntentKeyResolvers.ByIntentToString;
            _maxEntryCount = maxEntryCount <= 0 ? 0 : maxEntryCount;
            _onCacheHit = onCacheHit;
            _onCacheStore = onCacheStore;
        }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            var key = _keyResolver(context.Intent) ?? string.Empty;
            var now = DateTime.UtcNow;
            lock (_syncRoot)
            {
                PruneExpiredLocked(now);
                if (_cache.TryGetValue(key, out var entry) && entry != null)
                {
                    if (_ttl <= TimeSpan.Zero || entry.ExpireAtUtc >= now)
                    {
                        entry.LastAccessOrder = NextAccessOrderLocked();
                        _onCacheHit?.Invoke(context, key);
                        return entry.Result;
                    }

                    _cache.Remove(key);
                }
            }

            var result = await next(context);
            if (result == null)
            {
                return null;
            }

            var expireAt = _ttl <= TimeSpan.Zero ? DateTime.MaxValue : now.Add(_ttl);
            lock (_syncRoot)
            {
                _cache[key] = new CacheEntry
                {
                    Result = result,
                    ExpireAtUtc = expireAt,
                    LastAccessOrder = NextAccessOrderLocked()
                };
                TrimToCapacityLocked();
            }

            _onCacheStore?.Invoke(context, key, result);
            return result;
        }

        private void PruneExpiredLocked(DateTime now)
        {
            if (_ttl <= TimeSpan.Zero || _cache.Count == 0)
            {
                return;
            }

            var expiredKeys = new List<string>();
            foreach (var pair in _cache)
            {
                var entry = pair.Value;
                if (entry == null || entry.ExpireAtUtc < now)
                {
                    expiredKeys.Add(pair.Key);
                }
            }

            for (var i = 0; i < expiredKeys.Count; i++)
            {
                _cache.Remove(expiredKeys[i]);
            }
        }

        private void TrimToCapacityLocked()
        {
            if (_maxEntryCount <= 0 || _cache.Count <= _maxEntryCount)
            {
                return;
            }

            while (_cache.Count > _maxEntryCount)
            {
                string oldestKey = null;
                var oldestOrder = long.MaxValue;
                foreach (var pair in _cache)
                {
                    var order = pair.Value?.LastAccessOrder ?? long.MaxValue;
                    if (order < oldestOrder)
                    {
                        oldestOrder = order;
                        oldestKey = pair.Key;
                    }
                }

                if (oldestKey == null)
                {
                    break;
                }

                _cache.Remove(oldestKey);
            }
        }

        private long NextAccessOrderLocked()
        {
            if (_accessOrder == long.MaxValue)
            {
                _accessOrder = 0;
            }

            _accessOrder++;
            return _accessOrder;
        }
    }

    /// <summary>
    /// 超时中间件：超过指定时间仍未完成则抛出超时异常，并触发取消。
    /// </summary>
    public sealed class TimeoutIntentMiddleware : IStoreMiddleware
    {
        private readonly TimeSpan _timeout;

        public TimeoutIntentMiddleware(TimeSpan timeout)
        {
            _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeout;
        }

        public async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            if (context == null || next == null)
            {
                return default;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var middlewareContext = context.Clone(linkedCts.Token);
            var runTask = next(middlewareContext).AsTask();
            var timeoutTask = Task.Delay(_timeout, linkedCts.Token);
            var completedTask = await Task.WhenAny(runTask, timeoutTask);
            if (completedTask == runTask)
            {
                linkedCts.Cancel();
                return await runTask;
            }

            if (context.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(context.CancellationToken);
            }

            linkedCts.Cancel();
            throw new TimeoutException($"Intent execution timed out after {_timeout.TotalMilliseconds}ms.");
        }
    }
}
