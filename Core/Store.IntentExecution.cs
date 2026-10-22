using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    public abstract partial class Store
    {
        // 单个 Intent 的执行协议：集中中间件生命周期、上下文归还、错误重试与追踪。
        // 保留为 Store 的私有实现，使已有 protected 扩展点无需增加转发契约。
        private sealed class IntentExecutor
        {
            private readonly Store _store;
            private readonly List<IStoreMiddleware> _middlewares = new();
            private readonly object _middlewareSyncRoot = new();
            private long _middlewareCorrelationSequence;

            public IntentExecutor(Store store)
            {
                _store = store;
            }

            public IList<IStoreMiddleware> Middlewares => _middlewares;

            public void UseMiddleware(IStoreMiddleware middleware)
            {
                if (middleware == null)
                {
                    return;
                }

                lock (_middlewareSyncRoot)
                {
                    _middlewares.Add(middleware);
                }
            }

            public async ValueTask<IMviResult> ExecuteAsync(IIntent intent, CancellationToken cancellationToken, MviErrorPhase phase)
            {
                if (intent == null)
                {
                    return null;
                }
    
                var attempt = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var result = await InvokeMiddlewarePipelineAsync(intent, cancellationToken, attempt);
                        if (result != null)
                        {
                            _store.DevToolsHost.Track(_store, MviTimelineEventKind.Result, result);
                        }
    
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (Exception ex)
                    {
                        var decision = await _store.ResolveErrorDecisionAsync(ex, intent, attempt, phase, cancellationToken);
                        if (decision.EmitError)
                        {
                            _store.OnProcessError(ex, decision);
                        }
    
                        if (decision.FallbackResult != null)
                        {
                            _store.DevToolsHost.Track(_store, MviTimelineEventKind.Result, decision.FallbackResult, "fallback");
                            return decision.FallbackResult;
                        }
    
                        if (attempt < decision.RetryCount)
                        {
                            attempt++;
                            if (decision.RetryDelay > TimeSpan.Zero)
                            {
                                await Task.Delay(decision.RetryDelay, cancellationToken);
                            }
    
                            continue;
                        }
    
                        if (decision.Rethrow)
                        {
                            throw;
                        }
    
                        return null;
                    }
                }
            }

            private async ValueTask<IMviResult> InvokeMiddlewarePipelineAsync(IIntent intent, CancellationToken cancellationToken, int attempt)
            {
                if (intent == null)
                {
                    return default;
                }
    
                IStoreMiddleware[] middlewares = null;
                var hasMiddlewares = false;
                lock (_middlewareSyncRoot)
                {
                    if (_middlewares.Count > 0)
                    {
                        hasMiddlewares = true;
                        middlewares = _middlewares.ToArray();
                    }
                }
    
                if (!hasMiddlewares)
                {
                    return await _store.ProcessIntentAsync(intent, cancellationToken);
                }
    
                var correlationId = $"{_store.GetType().Name}:{Interlocked.Increment(ref _middlewareCorrelationSequence)}";
                var context = StoreMiddlewareContextPool.Rent(_store, intent, cancellationToken, attempt, correlationId);
    
                try
                {
                    context.Stage = StoreMiddlewareStage.BeforeIntent;
                    TrackMiddlewareTrace(correlationId, attempt, context.Stage, message: "pipeline-start");
                    for (var i = 0; i < middlewares.Length; i++)
                    {
                        if (middlewares[i] is IStoreMiddlewareV2 middlewareV2)
                        {
                            TrackMiddlewareTrace(correlationId, attempt, context.Stage, middlewares[i], "before-hook");
                            await middlewareV2.OnBeforeIntentAsync(context);
                        }
                    }
    
                    var index = -1;
                    context.Stage = StoreMiddlewareStage.InvokeCore;
                    TrackMiddlewareTrace(correlationId, attempt, context.Stage, message: "core-start");
    
                    ValueTask<IMviResult> Next(StoreMiddlewareContext current)
                    {
                        index++;
                        if (index >= middlewares.Length)
                        {
                            if (current.Intent == null)
                            {
                                return default;
                            }
    
                            return _store.ProcessIntentAsync(current.Intent, current.CancellationToken);
                        }
    
                        var middleware = middlewares[index];
                        if (middleware == null)
                        {
                            return Next(current);
                        }
    
                        return middleware.InvokeAsync(current, Next);
                    }
    
                    var result = await Next(context);
                    TrackMiddlewareTrace(correlationId, attempt, context.Stage, message: "core-complete");
    
                    context.Stage = StoreMiddlewareStage.AfterResult;
                    for (var i = 0; i < middlewares.Length; i++)
                    {
                        if (middlewares[i] is IStoreMiddlewareV2 middlewareV2)
                        {
                            TrackMiddlewareTrace(correlationId, attempt, context.Stage, middlewares[i], "after-hook");
                            await middlewareV2.OnAfterResultAsync(context, result);
                        }
                    }
    
                    TrackMiddlewareTrace(correlationId, attempt, context.Stage, message: "pipeline-complete");
    
                    return result;
                }
                catch (Exception ex)
                {
                    context.Stage = StoreMiddlewareStage.OnError;
                    TrackMiddlewareTrace(correlationId, attempt, context.Stage, message: "pipeline-error", exception: ex);
                    for (var i = 0; i < middlewares.Length; i++)
                    {
                        if (middlewares[i] is not IStoreMiddlewareV2 middlewareV2)
                        {
                            continue;
                        }
    
                        try
                        {
                            TrackMiddlewareTrace(correlationId, attempt, context.Stage, middlewares[i], "error-hook", ex);
                            await middlewareV2.OnErrorAsync(context, ex);
                        }
                        catch (Exception hookException)
                        {
                            TrackMiddlewareTrace(correlationId, attempt, context.Stage, middlewares[i], "error-hook-failed", hookException);
                            if (MviDiagnostics.Enabled)
                            {
                                MviDiagnostics.Trace($"[Store:{_store.GetType().Name}] Middleware OnError hook failed: {hookException}");
                            }
                        }
                    }
    
                    throw;
                }
                finally
                {
                    StoreMiddlewareContextPool.Return(context);
                }
            }

            private void TrackMiddlewareTrace(
                string correlationId,
                int attempt,
                StoreMiddlewareStage stage,
                IStoreMiddleware middleware = null,
                string message = null,
                Exception exception = null)
            {
                if (string.IsNullOrWhiteSpace(correlationId))
                {
                    return;
                }
    
                var trace = new MviMiddlewareTraceEvent(
                    correlationId: correlationId,
                    attempt: attempt,
                    stage: stage,
                    middlewareType: middleware?.GetType().Name,
                    message: message,
                    exceptionType: exception?.GetType().Name,
                    exceptionMessage: exception?.Message);
                _store.DevToolsHost.Track(_store, MviTimelineEventKind.Middleware, trace, message);
            }
        }
    }
}
