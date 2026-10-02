using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MVI;
using NUnit.Framework;
using R3;

namespace MVI.Tests
{
#if !UNITY_5_3_OR_NEWER
    [NonParallelizable]
#endif
    public class StoreExecutionTests
    {
        private IStoreStatePersistence _previousPersistence;
        private IMviErrorStrategy _previousStrategy;
        private StoreProfile _previousProfile;
        private Action<Exception> _previousUnhandled;
        private bool _previousDevToolsEnabled;

        [SetUp]
        public void SetUp()
        {
            _previousPersistence = MviStoreOptions.DefaultStatePersistence;
            _previousStrategy = MviStoreOptions.DefaultErrorStrategy;
            _previousProfile = MviStoreOptions.DefaultProfile;
            _previousUnhandled = ObservableSystem.GetUnhandledExceptionHandler();
            _previousDevToolsEnabled = MviDevTools.Enabled;
            MviStoreOptions.DefaultStatePersistence = null;
            MviStoreOptions.DefaultErrorStrategy = null;
            MviStoreOptions.DefaultProfile = null;
            MviDevTools.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            MviStoreOptions.DefaultStatePersistence = _previousPersistence;
            MviStoreOptions.DefaultErrorStrategy = _previousStrategy;
            MviStoreOptions.DefaultProfile = _previousProfile;
            ObservableSystem.RegisterUnhandledExceptionHandler(_previousUnhandled);
            MviDevTools.Enabled = _previousDevToolsEnabled;
        }

        [Test]
        public void MiddlewareExecution_ShouldPreserveHookAndCommitOrder()
        {
            var order = new List<string>();
            using var store = new ExecutionStore(order);
            store.UseMiddleware(new RecordingMiddleware("a", order));
            store.UseMiddleware(new RecordingMiddleware("b", order));
            using var subscription = store.State.Subscribe(_ => order.Add("state"));
            order.Clear();

            store.EmitIntent(new DelegateIntent(() =>
            {
                order.Add("handler");
                return new Result(3);
            }));

            CollectionAssert.AreEqual(new[]
            {
                "before:a:0", "before:b:0", "enter:a", "enter:b", "handler",
                "leave:b", "leave:a", "after:a:0", "after:b:0", "reduce", "state"
            }, order);
            Assert.AreEqual(3, store.CurrentState.Value);
        }

        [Test]
        public void Retry_ShouldReenterHooksAndCommitOnlySuccessfulResult()
        {
            var order = new List<string>();
            MviStoreOptions.DefaultErrorStrategy = new FixedStrategy(MviErrorDecision.Retry(1, emitError: false));
            using var store = new ExecutionStore(order);
            store.UseMiddleware(new RecordingMiddleware("a", order));
            var attempts = 0;
            store.EmitIntent(new DelegateIntent(() =>
            {
                if (++attempts == 1)
                {
                    throw new InvalidOperationException("retry-me");
                }
                return new Result(7);
            }));

            Assert.AreEqual(2, attempts);
            Assert.AreEqual(7, store.CurrentState.Value);
            Assert.AreEqual(1, order.Count(value => value == "reduce"));
            CollectionAssert.AreEqual(new[] { "before:a:0", "before:a:1" }, order.Where(value => value.StartsWith("before:")).ToArray());
            Assert.Contains("error:a:0", order);
            Assert.Contains("after:a:1", order);
        }

        [Test]
        public void ShortCircuit_ShouldSkipIntentButStillCompleteHooks()
        {
            var order = new List<string>();
            using var store = new ExecutionStore(order);
            store.UseMiddleware(new RecordingMiddleware("a", order));
            store.UseMiddleware(new DelegateStoreMiddleware((context, next) => new ValueTask<IMviResult>(new Result(9))));
            var called = false;
            store.EmitIntent(new DelegateIntent(() => { called = true; return new Result(1); }));
            Assert.IsFalse(called);
            Assert.AreEqual(9, store.CurrentState.Value);
            Assert.Contains("after:a:0", order);
        }

        [Test]
        public void SynchronousRethrow_ShouldStillThrowOriginalException()
        {
            var expected = new InvalidOperationException("save-failed");
            MviStoreOptions.DefaultStatePersistence = new FailingPersistence(expected);
            MviStoreOptions.DefaultErrorStrategy = new FixedStrategy(MviErrorDecision.Emit(rethrow: true));
            using var store = new NoInitialStore();
            var errors = new List<MviErrorEffect>();
            using var subscription = store.Errors.Subscribe(errors.Add);
            var actual = Assert.Throws<InvalidOperationException>(() => store.UpdateState(new State { Value = 4 }));
            Assert.AreSame(expected, actual);
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(4, ((State)store.CurrentState).Value);
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void DeferredDecision_ShouldReturnAndPreserveCapturedContext(bool rethrow, bool disposeBeforePump, bool failLoad)
        {
            var outcome = RunDeferredDecision(rethrow, disposeBeforePump, failLoad, handlerThrows: false);
            Assert.IsFalse(outcome.Blocked, "同步入口不能等待尚未完成的异步错误策略。");
            Assert.IsNull(outcome.ThreadFailure);
            Assert.AreEqual(0, outcome.ErrorsBeforePump);
            Assert.AreEqual(failLoad ? MviErrorPhase.PersistenceLoad : MviErrorPhase.PersistenceSave, outcome.Phase);
            Assert.AreEqual(disposeBeforePump ? 0 : rethrow ? 0 : 1, outcome.Errors);
            Assert.AreEqual(disposeBeforePump ? 0 : rethrow ? 1 : 0, outcome.Unhandled);
            Assert.AreEqual(outcome.Errors, outcome.Effects);
            if (!disposeBeforePump)
            {
                Assert.AreEqual(outcome.OwnerThread, outcome.DeliveryThread);
            }
            Assert.IsTrue(outcome.Token.IsCancellationRequested, "释放后原先交给策略的 token 应已取消。");
        }

        [Test]
        public void DeferredRethrow_WithThrowingUnhandledHandler_ShouldBeObserved()
        {
#if UNITY_EDITOR
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("handler-failed"));
#endif
            var outcome = RunDeferredDecision(rethrow: true, disposeBeforePump: false, failLoad: false, handlerThrows: true);
            Assert.IsFalse(outcome.Blocked);
            Assert.IsNull(outcome.ThreadFailure);
            Assert.AreEqual(1, outcome.Unhandled);
            Assert.AreEqual(0, outcome.Errors);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Replay_ShouldAwaitDirectReducerErrorDecision(bool rethrow)
        {
            var decision = new TaskCompletionSource<MviErrorDecision>();
            MviStoreOptions.DefaultErrorStrategy = new PendingStrategy(decision.Task);
            MviDevTools.Enabled = true;
            using var store = new ExecutionStore(new List<string>());
            store.EmitIntent(new DelegateIntent(() => new Result(2)));
            store.FailReduce = true;

            var replay = store.ReplayIntentsAsync().AsTask();
            Assert.IsFalse(replay.IsCompleted);
            decision.SetResult(rethrow ? MviErrorDecision.Emit(rethrow: true) : MviErrorDecision.Ignore());
            if (rethrow)
            {
                var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await replay);
                Assert.AreEqual("reduce-failed", error.Message);
            }
            else
            {
                Assert.AreEqual(0, await replay);
            }
        }

        [Test]
        public async Task Dispose_ShouldCancelPendingReplayErrorDecision()
        {
            MviStoreOptions.DefaultErrorStrategy = new CancellationStrategy();
            MviDevTools.Enabled = true;
            using var store = new ExecutionStore(new List<string>());
            store.EmitIntent(new DelegateIntent(() => new Result(2)));
            store.FailReduce = true;
            var replay = store.ReplayIntentsAsync().AsTask();
            Assert.IsFalse(replay.IsCompleted);

            store.Dispose();
            var completed = await Task.WhenAny(replay, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.AreSame(replay, completed, "释放 Store 必须取消回放等待中的策略。");
            Assert.That(async () => await replay, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void GenericStore_ShouldPrepareDefaultRegistryBeforeFirstRestore()
        {
            const string key = "store.cold-restore";
            var storage = new InMemoryStoreStateStorage();
            var writer = new SerializedStoreStatePersistence(storage, new[] { new JsonStoreStateSerializer(registry: new StateTypeRegistry()) }, "json");
            writer.Save(key, new ColdState { value = 7 });
            MviStoreOptions.DefaultStatePersistence = new SerializedStoreStatePersistence(storage, new[] { new JsonStoreStateSerializer() }, "json");
            using var store = new ColdRestoreStore();
            Assert.AreEqual(7, store.CurrentState.value);
            Assert.IsTrue(storage.TryRead(key, out _));
        }

        private static Outcome RunDeferredDecision(bool rethrow, bool disposeBeforePump, bool failLoad, bool handlerThrows)
        {
            var outcome = new Outcome();
            var exception = new InvalidOperationException(failLoad ? "load-failed" : "save-failed");
            var decision = new MviErrorDecision(emitError: !rethrow, rethrow: rethrow, retryCount: 0, retryDelay: default, fallbackResult: null);
            MviStoreOptions.DefaultStatePersistence = new FailingPersistence(exception, failLoad);
            MviStoreOptions.DefaultErrorStrategy = new YieldingStrategy(decision, outcome);
            ObservableSystem.RegisterUnhandledExceptionHandler(error =>
            {
                outcome.Unhandled++;
                outcome.DeliveryThread = Thread.CurrentThread.ManagedThreadId;
                Assert.AreSame(exception, error);
                if (handlerThrows) throw new InvalidOperationException("handler-failed");
            });
            var context = new QueuedContext();
            var worker = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(context);
                outcome.OwnerThread = Thread.CurrentThread.ManagedThreadId;
                Store instance = null;
                try
                {
                    instance = failLoad ? new InitialStateStore() : new NoInitialStore();
                    using var errors = instance.Errors.Subscribe(error =>
                    {
                        outcome.Errors++;
                        outcome.DeliveryThread = Thread.CurrentThread.ManagedThreadId;
                        Assert.AreSame(exception, error.Exception);
                    });
                    using var effects = instance.Effects.Subscribe(_ => outcome.Effects++);
                    if (!failLoad) instance.UpdateState(new State { Value = 4 });
                    outcome.ErrorsBeforePump = outcome.Errors;
                    if (disposeBeforePump) instance.Dispose();
                    context.Drain();
                }
                catch (Exception error) { outcome.ThreadFailure = error; }
                finally { instance?.Dispose(); }
            }) { IsBackground = true };
            worker.Start();
            outcome.Blocked = !worker.Join(TimeSpan.FromSeconds(2));
            if (outcome.Blocked)
            {
                context.Drain();
                worker.Join(TimeSpan.FromSeconds(2));
            }
            return outcome;
        }

        private sealed class Outcome
        {
            public bool Blocked;
            public Exception ThreadFailure;
            public int OwnerThread, DeliveryThread, ErrorsBeforePump, Errors, Effects, Unhandled;
            public MviErrorPhase Phase;
            public CancellationToken Token;
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _callbacks = new();
            public override void Post(SendOrPostCallback callback, object state) => _callbacks.Enqueue((callback, state));
            public void Drain()
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { while (_callbacks.TryDequeue(out var item)) item.Callback(item.State); }
                finally { SetSynchronizationContext(previous); }
            }
        }

        private sealed class FixedStrategy : IMviErrorStrategy
        {
            private readonly MviErrorDecision _decision;
            public FixedStrategy(MviErrorDecision decision) => _decision = decision;
            public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default) => new(_decision);
        }

        private sealed class YieldingStrategy : IMviErrorStrategy
        {
            private readonly MviErrorDecision _decision;
            private readonly Outcome _outcome;
            public YieldingStrategy(MviErrorDecision decision, Outcome outcome) { _decision = decision; _outcome = outcome; }
            public async ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
            {
                _outcome.Phase = context.Phase;
                _outcome.Token = cancellationToken;
                await Task.Yield();
                return _decision;
            }
        }

        private sealed class PendingStrategy : IMviErrorStrategy
        {
            private readonly Task<MviErrorDecision> _decision;
            public PendingStrategy(Task<MviErrorDecision> decision) => _decision = decision;
            public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default) => new(_decision);
        }

        private sealed class CancellationStrategy : IMviErrorStrategy
        {
            public async ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return MviErrorDecision.Ignore();
            }
        }

        private sealed class FailingPersistence : IStoreStatePersistence
        {
            private readonly Exception _exception;
            private readonly bool _failLoad;
            public FailingPersistence(Exception exception, bool failLoad = false) { _exception = exception; _failLoad = failLoad; }
            public bool TryLoad(string key, out IState state)
            {
                state = null;
                if (_failLoad) throw _exception;
                return false;
            }
            public void Save(string key, IState state) { if (!_failLoad) throw _exception; }
            public void Clear(string key) { }
        }

        private class NoInitialStore : Store
        {
            protected override IMviDevToolsHost DevToolsHost => NullMviDevToolsHost.Shared;
        }
        private sealed class InitialStateStore : NoInitialStore
        {
            protected override IState CreateInitialState() => new State();
        }
        private sealed class State : IState
        {
            public int Value { get; set; }
            public bool IsUpdateNewState { get; set; } = true;
        }
        private sealed class Result : IMviResult
        {
            public Result(int value) => Value = value;
            public int Value { get; }
        }
        private sealed class DelegateIntent : IIntent
        {
            private readonly Func<IMviResult> _handler;
            public DelegateIntent(Func<IMviResult> handler) => _handler = handler;
            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken cancellationToken = default) => new(_handler());
        }
        private sealed class ExecutionStore : Store<State, DelegateIntent, Result>
        {
            private readonly List<string> _order;
            public ExecutionStore(List<string> order) => _order = order;
            public bool FailReduce { get; set; }
            protected override State InitialState => new();
            protected override AwaitOperation ProcessingMode => AwaitOperation.Sequential;
            protected override State Reduce(Result result)
            {
                if (FailReduce) throw new InvalidOperationException("reduce-failed");
                _order.Add("reduce");
                return new State { Value = result.Value };
            }
        }
        private sealed class RecordingMiddleware : StoreMiddlewareV2Base
        {
            private readonly string _name;
            private readonly List<string> _order;
            public RecordingMiddleware(string name, List<string> order) { _name = name; _order = order; }
            public override ValueTask OnBeforeIntentAsync(StoreMiddlewareContext context) { _order.Add($"before:{_name}:{context.Attempt}"); return default; }
            public override async ValueTask<IMviResult> InvokeAsync(StoreMiddlewareContext context, StoreMiddlewareNext next)
            {
                _order.Add("enter:" + _name);
                var result = await next(context);
                _order.Add("leave:" + _name);
                return result;
            }
            public override ValueTask OnAfterResultAsync(StoreMiddlewareContext context, IMviResult result) { _order.Add($"after:{_name}:{context.Attempt}"); return default; }
            public override ValueTask OnErrorAsync(StoreMiddlewareContext context, Exception error) { _order.Add($"error:{_name}:{context.Attempt}"); return default; }
        }
        [Serializable]
        private sealed class ColdState : IState
        {
            public int value;
            public bool refresh;
            public bool IsUpdateNewState { get => refresh; set => refresh = value; }
        }
        private sealed class ColdRestoreStore : Store<ColdState, DelegateIntent, Result>
        {
            protected override string PersistenceKey => "store.cold-restore";
            protected override ColdState InitialState => new();
            protected override ColdState Reduce(Result result) => new ColdState { value = result.Value };
        }
    }
}
