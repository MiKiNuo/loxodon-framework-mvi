using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MVI;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace MVI.Tests
{
    public class MviStoreTests
    {
        internal sealed class TestState : IState
        {
            public int Value { get; set; }
            public bool IsUpdateNewState { get; set; } = true;
        }

        private sealed class TestResult : IMviResult
        {
            public TestResult(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private interface ITestIntent : IIntent
        {
        }

        private sealed class SetValueIntent : ITestIntent
        {
            private readonly int value;

            public SetValueIntent(int value)
            {
                this.value = value;
            }

            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                IMviResult result = new TestResult(value);
                return new ValueTask<IMviResult>(result);
            }
        }

        private sealed class ThrowIntent : ITestIntent
        {
            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class TestEffect : IMviEffect
        {
            public TestEffect(string message)
            {
                Message = message;
            }

            public string Message { get; }
        }

        private sealed class TestStore : Store<TestState, ITestIntent, TestResult, TestEffect>
        {
            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override TestState Reduce(TestResult result)
            {
                EmitEffect(new TestEffect($"v:{result.Value}"));
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        private sealed class TestStoreNoEffect : Store<TestState, ITestIntent, TestResult>
        {
            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override TestState Reduce(TestResult result)
            {
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        internal sealed class MapState : IState
        {
            [MviMap("UserName")] public string Name { get; set; }
            [MviIgnore] public string Hidden { get; set; }
            public bool IsUpdateNewState { get; set; } = true;
        }

        internal sealed class MapViewModel : MviViewModel
        {
            public string UserName { get; set; }
            public string Hidden { get; set; }
        }

        private sealed class GenericViewModel : MviViewModel<TestState, ITestIntent, TestResult>
        {
            public Store<TestState, ITestIntent, TestResult> ExposedStore => Store;
        }

        private sealed class GenericEffectViewModel : MviViewModel<TestState, ITestIntent, TestResult, TestEffect>
        {
            public Store<TestState, ITestIntent, TestResult, TestEffect> ExposedStore => Store;
        }

        private sealed class MapStore : Store<MapState, ITestIntent, TestResult>
        {
            protected override MapState Reduce(TestResult result)
            {
                return null;
            }

            public void Push(MapState state)
            {
                UpdateState(state);
            }
        }

        private sealed class MiddlewareStore : Store<TestState, ITestIntent, TestResult>
        {
            public readonly List<string> Steps = new();

            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override void ConfigureMiddlewares(IList<IStoreMiddleware> middlewares)
            {
                middlewares.Add(new DelegateStoreMiddleware(async (context, next) =>
                {
                    Steps.Add($"before:{context.Intent.GetType().Name}");
                    var result = await next(context);
                    Steps.Add($"after:{context.Intent.GetType().Name}");
                    return result;
                }));
            }

            protected override TestState Reduce(TestResult result)
            {
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        private sealed class SlowSetValueIntent : ITestIntent
        {
            private readonly int _value;
            private readonly int _delayMs;

            public SlowSetValueIntent(int value, int delayMs)
            {
                _value = value;
                _delayMs = delayMs;
            }

            public async ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                await Task.Delay(_delayMs, ct);
                return new TestResult(_value);
            }
        }

        private sealed class PolicyStore : Store<TestState, ITestIntent, TestResult>
        {
            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override AwaitOperation ProcessingMode => AwaitOperation.Sequential;

            protected override void ConfigureIntentProcessingPolicies(IDictionary<Type, IntentProcessingPolicy> policies)
            {
                // 同类慢意图走 Switch，后发请求会取消前一个请求。
                policies[typeof(SlowSetValueIntent)] = IntentProcessingPolicy.Switch();
            }

            protected override TestState Reduce(TestResult result)
            {
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        private sealed class TestStateValueComparer : IEqualityComparer<TestState>
        {
            public bool Equals(TestState x, TestState y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.Value == y.Value;
            }

            public int GetHashCode(TestState obj)
            {
                return obj?.Value ?? 0;
            }
        }

        private sealed class PersistentStore : Store<TestState, ITestIntent, TestResult>
        {
            public static string Key { get; set; } = "MVI.Tests.PersistentStore";

            protected override string PersistenceKey => Key;

            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override TestState Reduce(TestResult result)
            {
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        private sealed class MigratingPersistentStore : Store<TestState, ITestIntent, TestResult>
        {
            public static string Key { get; set; } = "MVI.Tests.MigratingPersistentStore";

            protected override string PersistenceKey => Key;

            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override IState MigratePersistedState(IState persistedState)
            {
                if (persistedState is TestState typed)
                {
                    return new TestState { Value = typed.Value + 10, IsUpdateNewState = true };
                }

                return persistedState;
            }

            protected override TestState Reduce(TestResult result)
            {
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        private sealed class RetryThenSuccessIntent : ITestIntent
        {
            private readonly int _value;
            private int _remainingFailures;

            public RetryThenSuccessIntent(int value, int failTimes)
            {
                _value = value;
                _remainingFailures = failTimes;
            }

            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                if (_remainingFailures > 0)
                {
                    _remainingFailures--;
                    throw new InvalidOperationException("retry-me");
                }

                return new ValueTask<IMviResult>(new TestResult(_value));
            }
        }

        private sealed class RetryOnceErrorStrategy : IMviErrorStrategy
        {
            public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
            {
                if (context == null)
                {
                    return new ValueTask<MviErrorDecision>(MviErrorDecision.Emit());
                }

                if (context.Phase == MviErrorPhase.IntentProcessing && context.Attempt == 0)
                {
                    return new ValueTask<MviErrorDecision>(MviErrorDecision.Retry(retryCount: 1, emitError: true));
                }

                return new ValueTask<MviErrorDecision>(MviErrorDecision.Emit());
            }
        }

        private sealed class IgnoreErrorStrategy : IMviErrorStrategy
        {
            public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
            {
                return new ValueTask<MviErrorDecision>(MviErrorDecision.Ignore());
            }
        }

        [Serializable]
        private sealed class SerializableState : IState
        {
            public int value;
            public bool isUpdateNewState = true;

            public bool IsUpdateNewState
            {
                get => isUpdateNewState;
                set => isUpdateNewState = value;
            }
        }

        private sealed class CountingIntent : ITestIntent
        {
            public static int InvocationCount;
            private readonly int _value;

            public CountingIntent(int value)
            {
                _value = value;
            }

            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                InvocationCount++;
                return new ValueTask<IMviResult>(new TestResult(_value));
            }

            public override string ToString()
            {
                return $"CountingIntent:{_value}";
            }
        }

        private sealed class DelayedCountingIntent : ITestIntent
        {
            public static int InvocationCount;
            private readonly int _value;
            private readonly int _delayMs;

            public DelayedCountingIntent(int value, int delayMs)
            {
                _value = value;
                _delayMs = delayMs;
            }

            public async ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                InvocationCount++;
                await Task.Delay(_delayMs, ct);
                return new TestResult(_value);
            }

            public override string ToString()
            {
                return $"DelayedCountingIntent:{_value}:{_delayMs}";
            }
        }

        [UnityTest]
        public IEnumerator InitialState_IsEmitted()
        {
            var store = new TestStore();
            var states = new List<TestState>();
            store.State.Subscribe(states.Add);

            yield return null;

            Assert.IsNotEmpty(states);
            Assert.AreEqual(0, states[0].Value);
        }

        [UnityTest]
        public IEnumerator EmitIntent_UpdatesState_AndEmitsEffect()
        {
            var store = new TestStore();
            var states = new List<TestState>();
            var effects = new List<TestEffect>();

            store.State.Subscribe(states.Add);
            store.Effects.Subscribe(effect =>
            {
                if (effect is TestEffect typed)
                {
                    effects.Add(typed);
                }
            });

            store.EmitIntent(new SetValueIntent(5));

            yield return null;

            Assert.IsNotEmpty(states);
            Assert.AreEqual(5, states[states.Count - 1].Value);
            Assert.IsNotEmpty(effects);
            Assert.AreEqual("v:5", effects[effects.Count - 1].Message);
        }

        [UnityTest]
        public IEnumerator EmitIntent_ErrorEmitsErrorEffect()
        {
            var store = new TestStore();
            var errors = new List<MviErrorEffect>();

            store.Errors.Subscribe(errors.Add);
            store.EmitIntent(new ThrowIntent());

            yield return null;

            Assert.IsNotEmpty(errors);
            Assert.AreEqual("boom", errors[errors.Count - 1].Message);
        }

        [UnityTest]
        public IEnumerator MappingAttributes_Work()
        {
            var store = new MapStore();
            var viewModel = new MapViewModel();
            viewModel.BindStore(store, disposeStore: true);

            store.Push(new MapState { Name = "Alice", Hidden = "secret", IsUpdateNewState = true });

            yield return null;

            Assert.AreEqual("Alice", viewModel.UserName);
            Assert.IsNull(viewModel.Hidden);
        }

        [UnityTest]
        public IEnumerator BindStore_Rebind_ShouldDisposeOldStore_AndOnlyListenNewStore()
        {
            var oldStore = new MapStore();
            var newStore = new MapStore();
            var viewModel = new MapViewModel();

            viewModel.BindStore(oldStore, disposeStore: true);
            oldStore.Push(new MapState { Name = "Old", Hidden = "hidden", IsUpdateNewState = true });
            yield return null;
            Assert.AreEqual("Old", viewModel.UserName);

            viewModel.BindStore(newStore, disposeStore: true);
            Assert.IsTrue(IsDisposed(oldStore));

            oldStore.Push(new MapState { Name = "Old-2", Hidden = "hidden", IsUpdateNewState = true });
            newStore.Push(new MapState { Name = "New", Hidden = "hidden", IsUpdateNewState = true });
            yield return null;

            Assert.AreEqual("New", viewModel.UserName);
        }

        [UnityTest]
        public IEnumerator BindStore_RebindWithoutDispose_ShouldKeepOldStoreAlive()
        {
            var oldStore = new MapStore();
            var newStore = new MapStore();
            var viewModel = new MapViewModel();

            viewModel.BindStore(oldStore, disposeStore: false);
            viewModel.BindStore(newStore, disposeStore: false);

            yield return null;
            Assert.IsFalse(IsDisposed(oldStore));
            Assert.IsFalse(IsDisposed(newStore));
        }

        [Test]
        public void GenericViewModel_Dispose_ShouldClearTypedStoreReference()
        {
            var store = new TestStoreNoEffect();
            var viewModel = new GenericViewModel();
            viewModel.BindStore(store, disposeStore: false);

            viewModel.Dispose();

            Assert.IsNull(viewModel.ExposedStore);
        }

        [Test]
        public void GenericEffectViewModel_Dispose_ShouldClearTypedStoreReference()
        {
            var store = new TestStore();
            var viewModel = new GenericEffectViewModel();
            viewModel.BindStore(store, disposeStore: false);

            viewModel.Dispose();

            Assert.IsNull(viewModel.ExposedStore);
        }

        [UnityTest]
        public IEnumerator StoreMiddleware_ShouldWrapIntentPipeline()
        {
            var store = new MiddlewareStore();

            store.EmitIntent(new SetValueIntent(8));
            yield return null;

            Assert.AreEqual(8, store.CurrentState.Value);
            Assert.AreEqual(2, store.Steps.Count);
            Assert.AreEqual("before:SetValueIntent", store.Steps[0]);
            Assert.AreEqual("after:SetValueIntent", store.Steps[1]);
        }

        [UnityTest]
        public IEnumerator IntentPolicy_Switch_ShouldCancelPreviousSameIntentType()
        {
            var store = new PolicyStore();
            var states = new List<int>();
            store.State.Subscribe(state => states.Add(state.Value));

            store.EmitIntent(new SlowSetValueIntent(1, 240));
            yield return null;
            store.EmitIntent(new SlowSetValueIntent(2, 20));

            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < timeout && store.CurrentState.Value != 2)
            {
                yield return null;
            }

            Assert.AreEqual(2, store.CurrentState.Value);
            Assert.IsFalse(states.Contains(1));
        }

        [Test]
        public void Selector_Memoize_ShouldReuseSelectedValue()
        {
            var invoked = 0;
            var selector = MviSelector.Memoize<TestState, int>(
                state =>
                {
                    invoked++;
                    return state?.Value ?? 0;
                },
                stateComparer: new TestStateValueComparer());

            var first = selector(new TestState { Value = 5, IsUpdateNewState = true });
            var second = selector(new TestState { Value = 5, IsUpdateNewState = true });
            var third = selector(new TestState { Value = 6, IsUpdateNewState = true });

            Assert.AreEqual(5, first);
            Assert.AreEqual(5, second);
            Assert.AreEqual(6, third);
            Assert.AreEqual(2, invoked);
        }

        [UnityTest]
        public IEnumerator Persistence_ShouldRestoreLastState()
        {
            var previousPersistence = MviStoreOptions.DefaultStatePersistence;
            var persistence = new InMemoryStoreStatePersistence();
            MviStoreOptions.DefaultStatePersistence = persistence;
            PersistentStore.Key = "MVI.Tests.Persistence.Restore";

            try
            {
                var first = new PersistentStore();
                first.EmitIntent(new SetValueIntent(9));
                yield return null;
                Assert.AreEqual(9, first.CurrentState.Value);
                first.Dispose();

                var second = new PersistentStore();
                yield return null;
                Assert.AreEqual(9, second.CurrentState.Value);
                second.Dispose();
            }
            finally
            {
                MviStoreOptions.DefaultStatePersistence = previousPersistence;
            }
        }

        [Test]
        public void Persistence_Migrate_ShouldApplyMigrationHook()
        {
            var previousPersistence = MviStoreOptions.DefaultStatePersistence;
            var persistence = new InMemoryStoreStatePersistence();
            MviStoreOptions.DefaultStatePersistence = persistence;
            MigratingPersistentStore.Key = "MVI.Tests.Persistence.Migrate";
            persistence.Save(MigratingPersistentStore.Key, new TestState { Value = 7, IsUpdateNewState = true });

            try
            {
                var store = new MigratingPersistentStore();
                Assert.AreEqual(17, store.CurrentState.Value);
                store.Dispose();
            }
            finally
            {
                MviStoreOptions.DefaultStatePersistence = previousPersistence;
            }
        }

        [UnityTest]
        public IEnumerator DevTools_TimelineReplayAndTimeTravel_ShouldWork()
        {
            var previousEnabled = MviDevTools.Enabled;
            var previousMaxEvents = MviDevTools.MaxEventsPerStore;
            MviDevTools.Enabled = true;
            MviDevTools.MaxEventsPerStore = 200;

            try
            {
                var store = new TestStoreNoEffect();
                store.ClearTimeline();

                store.EmitIntent(new SetValueIntent(1));
                yield return null;
                store.EmitIntent(new SetValueIntent(2));
                yield return null;

                var timeline = store.GetTimelineSnapshot();
                Assert.IsTrue(timeline.Any(entry => entry.Kind == MviTimelineEventKind.Intent));
                Assert.IsTrue(timeline.Any(entry => entry.Kind == MviTimelineEventKind.Result));
                Assert.IsTrue(timeline.Any(entry => entry.Kind == MviTimelineEventKind.State));

                long sequenceForValueOne = 0;
                for (var i = 0; i < timeline.Count; i++)
                {
                    if (timeline[i].Kind == MviTimelineEventKind.State
                        && timeline[i].Payload is TestState state
                        && state.Value == 1)
                    {
                        sequenceForValueOne = timeline[i].Sequence;
                        break;
                    }
                }

                Assert.Greater(sequenceForValueOne, 0);
                Assert.IsTrue(store.TryTimeTravelToTimelineSequence(sequenceForValueOne));
                Assert.AreEqual(1, store.CurrentState.Value);

                var replayTask = store.ReplayIntentsAsync().AsTask();
                while (!replayTask.IsCompleted)
                {
                    yield return null;
                }

                Assert.AreEqual(2, replayTask.Result);
                Assert.AreEqual(2, store.CurrentState.Value);
                store.Dispose();
            }
            finally
            {
                MviDevTools.Enabled = previousEnabled;
                MviDevTools.MaxEventsPerStore = previousMaxEvents;
            }
        }

        [UnityTest]
        public IEnumerator GlobalErrorStrategy_Retry_ShouldRecoverIntent()
        {
            var previousStrategy = MviStoreOptions.DefaultErrorStrategy;
            MviStoreOptions.DefaultErrorStrategy = new RetryOnceErrorStrategy();
            try
            {
                var store = new TestStoreNoEffect();
                var errors = new List<MviErrorEffect>();
                store.Errors.Subscribe(errors.Add);

                store.EmitIntent(new RetryThenSuccessIntent(12, failTimes: 1));

                var timeout = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < timeout && store.CurrentState.Value != 12)
                {
                    yield return null;
                }

                Assert.AreEqual(12, store.CurrentState.Value);
                Assert.IsNotEmpty(errors);
                store.Dispose();
            }
            finally
            {
                MviStoreOptions.DefaultErrorStrategy = previousStrategy;
            }
        }

        [UnityTest]
        public IEnumerator GlobalErrorStrategy_Ignore_ShouldSuppressErrorChannel()
        {
            var previousStrategy = MviStoreOptions.DefaultErrorStrategy;
            MviStoreOptions.DefaultErrorStrategy = new IgnoreErrorStrategy();
            try
            {
                var store = new TestStoreNoEffect();
                var errors = new List<MviErrorEffect>();
                store.Errors.Subscribe(errors.Add);

                store.EmitIntent(new ThrowIntent());
                yield return null;

                Assert.IsEmpty(errors);
                store.Dispose();
            }
            finally
            {
                MviStoreOptions.DefaultErrorStrategy = previousStrategy;
            }
        }

        [UnityTest]
        public IEnumerator UndoRedo_StateHistory_ShouldWork()
        {
            var store = new TestStoreNoEffect();
            store.EmitIntent(new SetValueIntent(1));
            yield return null;
            store.EmitIntent(new SetValueIntent(2));
            yield return null;

            Assert.IsTrue(store.CanUndo);
            Assert.IsTrue(store.UndoState());
            Assert.AreEqual(1, store.CurrentState.Value);
            Assert.IsTrue(store.CanRedo);
            Assert.IsTrue(store.RedoState());
            Assert.AreEqual(2, store.CurrentState.Value);
            Assert.GreaterOrEqual(store.StateHistoryCount, 3);
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator StoreTestKit_ShouldCaptureAndWaitState()
        {
            var store = new TestStoreNoEffect();
            using var kit = store.CreateTestKit();

            kit.Emit(new SetValueIntent(21));
            var waitTask = kit.WaitForStateAsync(
                    state => state is TestState typed && typed.Value == 21,
                    timeoutMs: 2000)
                .AsTask();

            while (!waitTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(waitTask.Result);
            Assert.IsNotEmpty(kit.States);
            Assert.AreEqual(21, kit.LastStateAs<TestState>()?.Value ?? -1);
            store.Dispose();
        }

        [Test]
        public void SerializedPersistence_JsonMigrationAndEncryption_ShouldRoundTrip()
        {
            var storage = new InMemoryStoreStateStorage();
            var serializer = new JsonStoreStateSerializer(schemaVersion: 1);
            var migrator = new VersionedStoreStateMigrator()
                .AddStep("json", 1, snapshot =>
                {
                    var json = Encoding.UTF8.GetString(snapshot.Payload);
                    var state = JsonUtility.FromJson<SerializableState>(json) ?? new SerializableState();
                    state.value += 10;
                    return snapshot.With(
                        schemaVersion: 2,
                        payload: Encoding.UTF8.GetBytes(JsonUtility.ToJson(state)));
                });

            var persistence = new SerializedStoreStatePersistence(
                storage: storage,
                serializers: new[] { serializer },
                defaultSerializerId: serializer.SerializerId,
                encryptor: new XorStoreStateEncryptor("ut-secret"),
                migrator: migrator);

            persistence.Save("mvi.persistence.json", new SerializableState { value = 5, isUpdateNewState = true });

            Assert.IsTrue(persistence.TryLoad("mvi.persistence.json", out var loaded));
            Assert.IsInstanceOf<SerializableState>(loaded);
            Assert.AreEqual(15, ((SerializableState)loaded).value);
        }

        [Test]
        public void SerializedPersistence_BinaryFactory_ShouldRoundTrip()
        {
            var storage = new InMemoryStoreStateStorage();
            var persistence = StoreStatePersistenceFactory.CreateBinaryPersistence(
                storage: storage,
                schemaVersion: 3,
                compress: true,
                encryptor: new PassthroughStoreStateEncryptor());

            persistence.Save("mvi.persistence.binary", new SerializableState { value = 42, isUpdateNewState = true });

            Assert.IsTrue(persistence.TryLoad("mvi.persistence.binary", out var loaded));
            Assert.IsInstanceOf<SerializableState>(loaded);
            Assert.AreEqual(42, ((SerializableState)loaded).value);
        }

        [Test]
        public void SerializedPersistence_LoadCorruptedData_ShouldClearAndReport()
        {
            var storage = new InMemoryStoreStateStorage();
            storage.Write("mvi.persistence.corrupted", Encoding.UTF8.GetBytes("{invalid-json"));
            var failedReason = string.Empty;

            var persistence = new SerializedStoreStatePersistence(
                storage: storage,
                serializers: new IStoreStateSerializer[] { new JsonStoreStateSerializer() },
                defaultSerializerId: "json",
                options: new SerializedStoreStatePersistenceOptions
                {
                    ClearCorruptedDataOnLoadFailure = true,
                    OnLoadFailed = (_, reason) => failedReason = reason
                });

            Assert.IsFalse(persistence.TryLoad("mvi.persistence.corrupted", out _));
            Assert.IsFalse(storage.TryRead("mvi.persistence.corrupted", out _));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failedReason));
        }

        [Test]
        public void FileStoreStateStorage_ShouldRoundTripAndClear()
        {
            var root = Path.Combine(Path.GetTempPath(), "mvi-state-storage-tests", Guid.NewGuid().ToString("N"));
            var storage = new FileStoreStateStorage(rootDirectory: root, fileExtension: ".bin");
            var payload = Encoding.UTF8.GetBytes("hello-mvi");

            storage.Write("test/key:1", payload);
            Assert.IsTrue(storage.TryRead("test/key:1", out var loaded));
            CollectionAssert.AreEqual(payload, loaded);

            storage.Clear("test/key:1");
            Assert.IsFalse(storage.TryRead("test/key:1", out _));

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_Debounce_ShouldDropRapidDuplicateIntents()
        {
            CountingIntent.InvocationCount = 0;
            var droppedCount = 0;
            var store = new TestStoreNoEffect();
            store.UseMiddleware(new DebounceIntentMiddleware(
                TimeSpan.FromMilliseconds(300),
                _ => "same-key",
                onDropped: (_, _) => droppedCount++));

            store.EmitIntent(new CountingIntent(1));
            store.EmitIntent(new CountingIntent(2));
            yield return null;

            Assert.AreEqual(1, CountingIntent.InvocationCount);
            Assert.AreEqual(1, droppedCount);
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_Deduplicate_ShouldSkipInFlightIntent()
        {
            DelayedCountingIntent.InvocationCount = 0;
            var skippedCount = 0;
            var store = new TestStoreNoEffect();
            store.UseMiddleware(new DeduplicateIntentMiddleware(
                _ => "same-key",
                onSkipped: (_, _) => skippedCount++));

            store.EmitIntent(new DelayedCountingIntent(1, 220));
            store.EmitIntent(new DelayedCountingIntent(2, 10));

            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < timeout && store.CurrentState.Value != 1)
            {
                yield return null;
            }

            Assert.AreEqual(1, DelayedCountingIntent.InvocationCount);
            Assert.AreEqual(1, skippedCount);
            Assert.AreEqual(1, store.CurrentState.Value);
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_Cache_ShouldReuseResult()
        {
            CountingIntent.InvocationCount = 0;
            var store = new TestStoreNoEffect();
            store.UseMiddleware(new CacheResultMiddleware(TimeSpan.FromSeconds(5), intent => intent.ToString()));

            store.EmitIntent(new CountingIntent(9));
            yield return null;
            store.EmitIntent(new CountingIntent(9));
            yield return null;

            Assert.AreEqual(1, CountingIntent.InvocationCount);
            Assert.AreEqual(9, store.CurrentState.Value);
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_CacheWithCapacity_ShouldEvictOldestEntry()
        {
            CountingIntent.InvocationCount = 0;
            var store = new TestStoreNoEffect();
            store.UseMiddleware(new CacheResultMiddleware(
                ttl: TimeSpan.FromSeconds(10),
                keyResolver: intent => intent.ToString(),
                maxEntryCount: 1));

            store.EmitIntent(new CountingIntent(1));
            yield return null;
            store.EmitIntent(new CountingIntent(2));
            yield return null;
            store.EmitIntent(new CountingIntent(1));
            yield return null;

            Assert.AreEqual(3, CountingIntent.InvocationCount);
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_Timeout_ShouldEmitError()
        {
            DelayedCountingIntent.InvocationCount = 0;
            var store = new TestStoreNoEffect();
            var errors = new List<MviErrorEffect>();
            store.Errors.Subscribe(errors.Add);
            store.UseMiddleware(new TimeoutIntentMiddleware(TimeSpan.FromMilliseconds(40)));

            store.EmitIntent(new DelayedCountingIntent(3, 260));

            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < timeout && errors.Count == 0)
            {
                yield return null;
            }

            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.Any(error => error.Exception is TimeoutException));
            store.Dispose();
        }

        [UnityTest]
        public IEnumerator BuiltinMiddleware_Metrics_ShouldTrackSuccessAndFailure()
        {
            var store = new TestStoreNoEffect();
            var collector = new StoreMiddlewareMetricsCollector();
            store.UseMiddleware(new MetricsStoreMiddleware(collector));

            store.EmitIntent(new SetValueIntent(10));
            yield return null;
            store.EmitIntent(new ThrowIntent());
            yield return null;

            var snapshot = collector.CaptureSnapshot();
            Assert.AreEqual(2, snapshot.TotalCount);
            Assert.AreEqual(1, snapshot.SuccessCount);
            Assert.AreEqual(1, snapshot.FailureCount);
            Assert.GreaterOrEqual(snapshot.TotalElapsedMs, 0);
            store.Dispose();
        }

        [Test]
        public void TemplateErrorStrategy_ShouldMatchBusinessCode()
        {
            var strategy = new TemplateMviErrorStrategyBuilder()
                .ForBusinessCode(401, _ => MviErrorDecision.Ignore())
                .Build();

            var context = new MviErrorContext(
                store: null,
                exception: new BusinessCodeException(401, "unauthorized"),
                intent: null,
                attempt: 0,
                phase: MviErrorPhase.IntentProcessing);

            var decision = strategy.DecideAsync(context).Result;
            Assert.IsFalse(decision.EmitError);
            Assert.AreEqual(0, decision.RetryCount);
        }

        [Test]
        public void TemplateErrorStrategy_ExponentialBackoff_ShouldApplyDelayCurve()
        {
            var strategy = new TemplateMviErrorStrategyBuilder()
                .UseExponentialBackoffForException<InvalidOperationException>(
                    maxRetryCount: 3,
                    baseDelayMs: 100,
                    maxDelayMs: 500,
                    emitErrorOnRetry: false)
                .Build();

            var d0 = strategy.DecideAsync(new MviErrorContext(null, new InvalidOperationException("x"), null, 0, MviErrorPhase.IntentProcessing)).Result;
            var d2 = strategy.DecideAsync(new MviErrorContext(null, new InvalidOperationException("x"), null, 2, MviErrorPhase.IntentProcessing)).Result;
            var d3 = strategy.DecideAsync(new MviErrorContext(null, new InvalidOperationException("x"), null, 3, MviErrorPhase.IntentProcessing)).Result;

            Assert.AreEqual(3, d0.RetryCount);
            Assert.AreEqual(100, (int)d0.RetryDelay.TotalMilliseconds);
            Assert.AreEqual(400, (int)d2.RetryDelay.TotalMilliseconds);
            Assert.IsTrue(d3.EmitError);
            Assert.AreEqual(0, d3.RetryCount);
        }

        [Test]
        public void TemplateErrorStrategy_ForExceptionInPhase_ShouldRespectPhase()
        {
            var strategy = new TemplateMviErrorStrategyBuilder()
                .ForExceptionInPhase<InvalidOperationException>(MviErrorPhase.Reducing, _ => MviErrorDecision.Ignore())
                .Build();

            var intentPhase = strategy.DecideAsync(new MviErrorContext(
                null,
                new InvalidOperationException("x"),
                null,
                0,
                MviErrorPhase.IntentProcessing)).Result;

            var reducePhase = strategy.DecideAsync(new MviErrorContext(
                null,
                new InvalidOperationException("x"),
                null,
                0,
                MviErrorPhase.Reducing)).Result;

            Assert.IsTrue(intentPhase.EmitError);
            Assert.IsFalse(reducePhase.EmitError);
        }

        [Test]
        public void TemplateErrorStrategy_ExponentialBackoffWithJitter_ShouldStayInExpectedRange()
        {
            var strategy = new TemplateMviErrorStrategyBuilder()
                .UseExponentialBackoffForExceptionWithJitter<InvalidOperationException>(
                    maxRetryCount: 2,
                    baseDelayMs: 100,
                    maxDelayMs: 500,
                    jitterRate: 0.2d,
                    emitErrorOnRetry: false)
                .Build();

            var decision = strategy.DecideAsync(
                new MviErrorContext(null, new InvalidOperationException("x"), null, 0, MviErrorPhase.IntentProcessing)).Result;

            Assert.AreEqual(2, decision.RetryCount);
            Assert.IsFalse(decision.EmitError);
            Assert.GreaterOrEqual((int)decision.RetryDelay.TotalMilliseconds, 80);
            Assert.LessOrEqual((int)decision.RetryDelay.TotalMilliseconds, 120);
        }

        [UnityTest]
        public IEnumerator DevTools_TrackedStoresSnapshot_ShouldContainActiveStore()
        {
            var previousEnabled = MviDevTools.Enabled;
            MviDevTools.Enabled = true;
            try
            {
                var store = new TestStoreNoEffect();
                store.EmitIntent(new SetValueIntent(5));
                yield return null;

                var tracked = MviDevTools.GetTrackedStoresSnapshot();
                Assert.IsTrue(tracked.Any(item => ReferenceEquals(item, store)));

                store.Dispose();
                yield return null;

                tracked = MviDevTools.GetTrackedStoresSnapshot();
                Assert.IsFalse(tracked.Any(item => ReferenceEquals(item, store)));
            }
            finally
            {
                MviDevTools.Enabled = previousEnabled;
            }
        }

        [UnityTest]
        public IEnumerator DevTools_ExportTimeline_ShouldReturnFilteredText()
        {
            var previousEnabled = MviDevTools.Enabled;
            MviDevTools.Enabled = true;
            try
            {
                var store = new TestStoreNoEffect();
                store.EmitIntent(new SetValueIntent(11));
                yield return null;

                var content = MviDevTools.ExportTimeline(
                    store,
                    includePayloadDetails: false,
                    filter: entry => entry.Kind == MviTimelineEventKind.Intent || entry.Kind == MviTimelineEventKind.Result);

                Assert.IsFalse(string.IsNullOrWhiteSpace(content));
                StringAssert.Contains("Intent", content);
                StringAssert.Contains("Result", content);
                store.Dispose();
            }
            finally
            {
                MviDevTools.Enabled = previousEnabled;
            }
        }

        [UnityTest]
        public IEnumerator DevTools_ExportTimelineJson_ShouldContainEventFields()
        {
            var previousEnabled = MviDevTools.Enabled;
            MviDevTools.Enabled = true;
            try
            {
                var store = new TestStoreNoEffect();
                store.EmitIntent(new SetValueIntent(15));
                yield return null;

                var content = MviDevTools.ExportTimelineJson(
                    store,
                    includePayloadDetails: false,
                    filter: entry => entry.Kind == MviTimelineEventKind.Intent);

                Assert.IsFalse(string.IsNullOrWhiteSpace(content));
                StringAssert.Contains("\"events\"", content);
                StringAssert.Contains("\"kind\": \"Intent\"", content);
                store.Dispose();
            }
            finally
            {
                MviDevTools.Enabled = previousEnabled;
            }
        }

        private static bool IsDisposed(Store store)
        {
            var field = typeof(Store).GetField("_isDisposed", BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && field.GetValue(store) is bool value && value;
        }
    }
}
