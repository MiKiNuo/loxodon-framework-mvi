using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    // 中间件下一个处理器。
    public delegate ValueTask<IMviResult> StoreMiddlewareNext(StoreMiddlewareContext context);

    // Intent 中间件上下文。
    public sealed class StoreMiddlewareContext
    {
        private Dictionary<string, object> _items;

        public StoreMiddlewareContext(Store store, IIntent intent, CancellationToken cancellationToken)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            CancellationToken = cancellationToken;
        }

        public Store Store { get; }

        public IIntent Intent { get; set; }

        public CancellationToken CancellationToken { get; }

        public IDictionary<string, object> Items => _items ??= new Dictionary<string, object>(StringComparer.Ordinal);
    }

    // Store 中间件接口：可用于日志、鉴权、限流、重试等横切能力。
    public interface IStoreMiddleware
    {
        ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next);
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
