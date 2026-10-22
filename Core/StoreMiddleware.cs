using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    // 中间件下一个处理器。
    public delegate ValueTask<IMviResult> StoreMiddlewareNext(StoreMiddlewareContext context);

    public enum StoreMiddlewareStage
    {
        Unknown = 0,
        BeforeIntent = 1,
        InvokeCore = 2,
        AfterResult = 3,
        OnError = 4
    }

    // Intent 中间件上下文。
    public sealed class StoreMiddlewareContext
    {
        private Dictionary<string, object> _items;

        public StoreMiddlewareContext(Store store, IIntent intent, CancellationToken cancellationToken)
        {
            Reset(store, intent, cancellationToken, attempt: 0, correlationId: null);
        }

        public Store Store { get; private set; }

        public IIntent Intent { get; set; }

        /// <summary>
        /// 当前意图处理链使用的取消令牌。中间件可在必要时替换（如超时中间件）。
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// 当前执行阶段（Before/After/Error）。
        /// </summary>
        public StoreMiddlewareStage Stage { get; set; }

        /// <summary>
        /// 当前意图执行尝试次数（重试场景下递增）。
        /// </summary>
        public int Attempt { get; set; }

        /// <summary>
        /// 当前意图处理链的关联 ID（便于日志串联）。
        /// </summary>
        public string CorrelationId { get; set; }

        public IDictionary<string, object> Items => _items ??= new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// 设置上下文数据项（用于中间件链路间传递临时状态）。
        /// </summary>
        public void SetItem<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            Items[key] = value;
        }

        /// <summary>
        /// 尝试读取上下文数据项。
        /// </summary>
        public bool TryGetItem<T>(string key, out T value)
        {
            value = default(T);
            if (string.IsNullOrWhiteSpace(key) || _items == null || !_items.TryGetValue(key, out var raw))
            {
                return false;
            }

            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 按 key 获取上下文数据项，缺失时返回默认值。
        /// </summary>
        public T GetItemOrDefault<T>(string key)
        {
            return GetItemOrDefault(key, default(T));
        }

        /// <summary>
        /// 按 key 获取上下文数据项，缺失时返回指定默认值。
        /// </summary>
        public T GetItemOrDefault<T>(string key, T defaultValue)
        {
            return TryGetItem<T>(key, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// 删除上下文数据项。
        /// </summary>
        public bool RemoveItem(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _items == null)
            {
                return false;
            }

            return _items.Remove(key);
        }

        public StoreMiddlewareContext Clone(CancellationToken cancellationToken)
        {
            var cloned = new StoreMiddlewareContext(Store, Intent, cancellationToken)
            {
                Stage = Stage,
                Attempt = Attempt,
                CorrelationId = CorrelationId
            };

            if (_items == null || _items.Count == 0)
            {
                return cloned;
            }

            cloned._items = new Dictionary<string, object>(_items, StringComparer.Ordinal);
            return cloned;
        }

        internal void Reset(Store store, IIntent intent, CancellationToken cancellationToken, int attempt, string correlationId)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            CancellationToken = cancellationToken;
            Stage = StoreMiddlewareStage.Unknown;
            Attempt = attempt < 0 ? 0 : attempt;
            CorrelationId = correlationId;

            if (_items != null)
            {
                _items.Clear();
            }
        }

        internal void ClearForPool()
        {
            Store = null;
            Intent = null;
            CancellationToken = CancellationToken.None;
            Stage = StoreMiddlewareStage.Unknown;
            Attempt = 0;
            CorrelationId = null;
            _items?.Clear();
        }
    }

    internal static class StoreMiddlewareContextPool
    {
        private const int MaxPoolSize = 512;
        private static readonly Stack<StoreMiddlewareContext> Pool = new();
        private static readonly object SyncRoot = new();

        public static StoreMiddlewareContext Rent(Store store, IIntent intent, CancellationToken cancellationToken, int attempt, string correlationId)
        {
            lock (SyncRoot)
            {
                if (Pool.Count > 0)
                {
                    var context = Pool.Pop();
                    context.Reset(store, intent, cancellationToken, attempt, correlationId);
                    return context;
                }
            }

            return new StoreMiddlewareContext(store, intent, cancellationToken)
            {
                Attempt = attempt < 0 ? 0 : attempt,
                CorrelationId = correlationId
            };
        }

        public static void Return(StoreMiddlewareContext context)
        {
            if (context == null)
            {
                return;
            }

            context.ClearForPool();
            lock (SyncRoot)
            {
                if (Pool.Count < MaxPoolSize)
                {
                    Pool.Push(context);
                }
            }
        }
    }

    // Store 中间件接口：可用于日志、鉴权、限流、重试等横切能力。
    public interface IStoreMiddleware
    {
        ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next);
    }

    // V2 中间件接口：支持标准生命周期钩子。
    public interface IStoreMiddlewareV2 : IStoreMiddleware
    {
        ValueTask OnBeforeIntentAsync(StoreMiddlewareContext context);

        ValueTask OnAfterResultAsync(StoreMiddlewareContext context, IMviResult result);

        ValueTask OnErrorAsync(StoreMiddlewareContext context, Exception exception);
    }

    public abstract class StoreMiddlewareV2Base : IStoreMiddlewareV2
    {
        public virtual ValueTask OnBeforeIntentAsync(StoreMiddlewareContext context)
        {
            return default;
        }

        public virtual ValueTask OnAfterResultAsync(StoreMiddlewareContext context, IMviResult result)
        {
            return default;
        }

        public virtual ValueTask OnErrorAsync(StoreMiddlewareContext context, Exception exception)
        {
            return default;
        }

        public virtual ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            return next != null ? next(context) : default;
        }
    }

    // 便捷的委托中间件。
    public sealed class DelegateStoreMiddleware : IStoreMiddleware
    {
        private readonly Func<StoreMiddlewareContext, StoreMiddlewareNext, ValueTask<IMviResult>> _handler;

        public DelegateStoreMiddleware(Func<StoreMiddlewareContext, StoreMiddlewareNext, ValueTask<IMviResult>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
        {
            return _handler(context, next);
        }
    }
}
