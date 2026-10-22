using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace MVI
{
    // Store：负责处理 Intent、生成 Result，并通过 Reducer 产出新 State。
    public abstract partial class Store : IDisposable
    {
        private readonly Subject<IState> _stateSubject = new();
        private readonly Subject<IntentEnvelope> _intentSubject = new();
        private readonly Subject<IMviEffect> _effectSubject = new();
        private readonly Subject<MviErrorEffect> _errorSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private readonly CancellationTokenSource _storeCts = new();
        private readonly CancellationToken _lifetimeCancellationToken;
        private readonly IntentExecutor _intentExecutor;
        private readonly Dictionary<Type, IntentProcessingPolicy> _intentPolicies = new();
        private readonly StateHistoryStore _history = new();
        private StorePersistenceCoordinator _persistenceCoordinator;

        private IState _currentState;
        private bool _isDisposed;

        private readonly struct IntentEnvelope
        {
            public IntentEnvelope(IIntent intent, CancellationToken cancellationToken)
            {
                Intent = intent ?? throw new ArgumentNullException(nameof(intent));
                CancellationToken = cancellationToken;
            }

            public IIntent Intent { get; }

            public CancellationToken CancellationToken { get; }
        }

        // 当前状态快照。
        public IState CurrentState => _currentState;

        // 状态流（只读）。
        public ReadOnlyReactiveProperty<IState> State { get; }

        // Effects 流（一次性事件）。
        public Observable<IMviEffect> Effects => _effectSubject;

        // Errors 流（标准化错误通道）。
        public Observable<MviErrorEffect> Errors => _errorSubject;

        // Undo/Redo 状态历史总数。
        public int StateHistoryCount => _history.Count;

        // 当前历史游标（-1 表示无历史）。
        public int CurrentStateHistoryIndex => _history.CurrentIndex;

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        // Store 是否已释放（供测试与诊断使用，替代反射访问私有 _isDisposed）。
        public bool IsDisposed => _isDisposed;

        protected Store()
        {
            _lifetimeCancellationToken = _storeCts.Token;
            _intentExecutor = new IntentExecutor(this);
            State = _stateSubject.ToReadOnlyReactiveProperty();
            ApplyProfileDefaults();
            ConfigureMiddlewares(_intentExecutor.Middlewares);
            ApplyProfileMiddlewares();
            ConfigureIntentProcessingPolicies(_intentPolicies);
            ApplyProfileIntentPolicies();
            if (StateType != null)
            {
                StateTypeRegistry.Shared.Register(StateType);
            }
            _persistenceCoordinator = CreatePersistenceCoordinator();
            if (!TryRestorePersistedState())
            {
                InitializeState();
            }

            Process(_intentSubject);
        }

        /// <summary>
        /// 创建持久化协作器：把 Save/Restore/迁移委托到该对象，避免 Store 主体承担这些职责。
        /// </summary>
        private StorePersistenceCoordinator CreatePersistenceCoordinator()
        {
            return new StorePersistenceCoordinator(
                persistenceProvider: () => Persistence,
                keyProvider: () => PersistenceKey,
                migrator: MigratePersistedState,
                errorHandler: (ex, phase) => HandleNonIntentError(ex, phase));
        }

        // Store 统一配置（默认读取全局 Profile，可由子类覆写）。
        protected virtual StoreProfile Profile => MviStoreOptions.DefaultProfile;

        // Intent 并发策略（默认 Switch: 取消上一个意图）。
        protected virtual AwaitOperation ProcessingMode => Profile?.ProcessingMode ?? AwaitOperation.Switch;

        // 并发上限（仅对 Parallel/SequentialParallel 生效，-1 为不限制）。
        protected virtual int MaxConcurrent => Profile?.MaxConcurrent ?? -1;

        // 状态历史容量（<=0 表示关闭历史）。
        protected virtual int StateHistoryCapacity => Profile?.StateHistoryCapacity ?? 64;

        // 配置 Store 级中间件（可覆写）。
        protected virtual void ConfigureMiddlewares(IList<IStoreMiddleware> middlewares)
        {
        }

        // 配置按 Intent 类型的并发策略（可覆写）。
        protected virtual void ConfigureIntentProcessingPolicies(IDictionary<Type, IntentProcessingPolicy> policies)
        {
        }

        // 当前 Store 的状态持久化插件（默认使用全局选项）。
        protected virtual IStoreStatePersistence Persistence => Profile?.StatePersistence ?? MviStoreOptions.DefaultStatePersistence;

        // 当前 Store 的错误处理策略（默认发出 Error/Effect）。
        protected virtual IMviErrorStrategy ErrorStrategy => Profile?.ErrorStrategy ?? MviStoreOptions.DefaultErrorStrategy ?? DefaultMviErrorStrategy.Instance;

        // 错误钩子路由模式：业务可覆写以选择走旧签名（Legacy）还是新签名（Decision，默认）。
        // 该虚拟属性替代了之前通过反射探测子类 OnProcessError 覆写的实现。
        protected virtual MviErrorHookMode ErrorHookMode => MviErrorHookMode.Decision;

        // 持久化键（默认使用完整类型名）。
        protected virtual string PersistenceKey => GetType().FullName;

        // 在首次恢复前准备默认注册器；非泛型 Store 可声明自己的状态类型。
        protected virtual Type StateType => null;

        // 持久化状态迁移钩子（用于版本升级）。
        protected virtual IState MigratePersistedState(IState persistedState)
        {
            return persistedState;
        }

        /// <summary>
        /// DevTools 接入点：默认走 <see cref="MviDevToolsHost.Shared"/>，业务可在子类覆写以切换为 <see cref="NullMviDevToolsHost"/> 或自定义抓取器。
        /// </summary>
        protected virtual IMviDevToolsHost DevToolsHost => MviDevToolsHost.Shared;

        private void ApplyProfileDefaults()
        {
            var profile = Profile;
            if (profile == null)
            {
                return;
            }

            // 配置项全部走 DevToolsHost 契约，Store 不再直接写 MviDevTools 静态字段。
            var host = DevToolsHost;
            if (profile.DevToolsEnabled.HasValue)
            {
                host.Enabled = profile.DevToolsEnabled.Value;
            }

            if (profile.DevToolsMaxEventsPerStore.HasValue)
            {
                host.MaxEventsPerStore = Math.Max(1, profile.DevToolsMaxEventsPerStore.Value);
            }

            if (profile.DevToolsSamplingOptions != null)
            {
                host.SamplingOptions = profile.DevToolsSamplingOptions;
            }
        }

        private void ApplyProfileMiddlewares()
        {
            var profile = Profile;
            if (profile?.Middlewares == null || profile.Middlewares.Count == 0)
            {
                return;
            }

            for (var i = 0; i < profile.Middlewares.Count; i++)
            {
                var middleware = profile.Middlewares[i];
                if (middleware != null)
                {
                    _intentExecutor.UseMiddleware(middleware);
                }
            }
        }

        private void ApplyProfileIntentPolicies()
        {
            var profile = Profile;
            if (profile?.IntentPolicies == null || profile.IntentPolicies.Count == 0)
            {
                return;
            }

            foreach (var pair in profile.IntentPolicies)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                _intentPolicies[pair.Key] = pair.Value;
            }
        }

        // 运行时注册中间件。
        public void UseMiddleware(IStoreMiddleware middleware)
        {
            _intentExecutor.UseMiddleware(middleware);
        }

        // 初始化状态（可覆写）。
        protected virtual IState CreateInitialState()
        {
            return null;
        }

        // 处理单个意图，支持取消（可由子类覆写）。
        protected virtual ValueTask<IMviResult> ProcessIntentAsync(IIntent intent, CancellationToken ct = default)
        {
            return intent.HandleIntentAsync(ct);
        }

        // 订阅意图流并驱动 Reducer。
        public void Process(Observable<IIntent> intents)
        {
            if (intents == null)
            {
                return;
            }

            Process(intents.Select(intent => new IntentEnvelope(intent, CancellationToken.None)));
        }

        // DevTools：获取当前 Store 时间线快照。
        public IReadOnlyList<MviTimelineEvent> GetTimelineSnapshot()
        {
            return DevToolsHost.GetTimelineSnapshot(this);
        }

        // DevTools：清空当前 Store 时间线。
        public void ClearTimeline()
        {
            DevToolsHost.Clear(this);
        }

        // DevTools：重放时间线中的 Intent（顺序执行）。
        public async ValueTask<int> ReplayIntentsAsync(CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                return 0;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellationToken);
            var replayCancellationToken = linkedCts.Token;

            var timeline = DevToolsHost.GetTimelineSnapshot(this);
            if (timeline == null || timeline.Count == 0)
            {
                return 0;
            }

            var replayed = 0;
            for (var i = 0; i < timeline.Count; i++)
            {
                replayCancellationToken.ThrowIfCancellationRequested();

                var entry = timeline[i];
                if (entry.Kind != MviTimelineEventKind.Intent || entry.Payload is not IIntent intent)
                {
                    continue;
                }

                var result = await _intentExecutor.ExecuteAsync(intent, replayCancellationToken, MviErrorPhase.Replay);
                if (result == null)
                {
                    continue;
                }

                try
                {
                    Reduce(result);
                    replayed++;
                    DevToolsHost.Track(this, MviTimelineEventKind.Replay, intent, $"replay:{intent.GetType().Name}");
                }
                catch (Exception ex)
                {
                    await HandleNonIntentErrorAsync(ex, MviErrorPhase.Reducing, replayCancellationToken);
                }
            }

            return replayed;
        }

        // Undo 到前一个状态。
        public bool UndoState()
        {
            return TryApplyHistoryAt(_history.CurrentIndex - 1, MviTimelineEventKind.Undo, "undo");
        }

        // Redo 到后一个状态。
        public bool RedoState()
        {
            return TryApplyHistoryAt(_history.CurrentIndex + 1, MviTimelineEventKind.Redo, "redo");
        }

        // Time-travel 到指定历史索引。
        public bool TryTimeTravelToHistoryIndex(int index)
        {
            return TryApplyHistoryAt(index, MviTimelineEventKind.TimeTravel, $"history:{index}");
        }

        // Time-travel 到指定时间线序号（会选取该序号及之前最近的 State 事件）。
        public bool TryTimeTravelToTimelineSequence(long sequence)
        {
            if (sequence <= 0)
            {
                return false;
            }

            var timeline = DevToolsHost.GetTimelineSnapshot(this);
            if (timeline == null || timeline.Count == 0)
            {
                return false;
            }

            IState target = null;
            for (var i = timeline.Count - 1; i >= 0; i--)
            {
                var entry = timeline[i];
                if (entry.Sequence <= sequence && entry.Kind == MviTimelineEventKind.State && entry.Payload is IState state)
                {
                    target = state;
                    break;
                }
            }

            if (target == null)
            {
                return false;
            }

            UpdateHistoryIndexForState(target);
            ApplyStateInternal(target, trackHistory: false, persistState: true, timelineKind: MviTimelineEventKind.TimeTravel, timelineNote: $"timeline:{sequence}");
            return true;
        }

        private void Process(Observable<IntentEnvelope> intents)
        {
            if (_intentPolicies.Count == 0)
            {
                SubscribeIntentStream(intents, ProcessingMode, MaxConcurrent);
                return;
            }

            var routedTypes = new HashSet<Type>();
            foreach (var pair in _intentPolicies)
            {
                var intentType = pair.Key;
                if (intentType == null)
                {
                    continue;
                }

                routedTypes.Add(intentType);
                var policy = pair.Value;
                SubscribeIntentStream(
                    intents.Where(envelope => envelope.Intent != null && envelope.Intent.GetType() == intentType),
                    policy.Operation,
                    policy.MaxConcurrent);
            }

            SubscribeIntentStream(
                intents.Where(envelope => envelope.Intent == null || !routedTypes.Contains(envelope.Intent.GetType())),
                ProcessingMode,
                MaxConcurrent);
        }

        private void SubscribeIntentStream(Observable<IntentEnvelope> intents, AwaitOperation operation, int maxConcurrent)
        {
            intents
                .SelectAwait(ProcessIntentEnvelopeAsync, operation, maxConcurrent: maxConcurrent)
                .Where(result => result != null)
                .Subscribe(result =>
                {
                    try
                    {
                        Reduce(result);
                    }
                    catch (Exception ex)
                    {
                        HandleNonIntentError(ex, MviErrorPhase.Reducing);
                    }
                })
                .AddTo(_disposables);
        }

        private async ValueTask<IMviResult> ProcessIntentEnvelopeAsync(IntentEnvelope envelope, CancellationToken ct = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.CancellationToken, _lifetimeCancellationToken);
            return await _intentExecutor.ExecuteAsync(envelope.Intent, linkedCts.Token, MviErrorPhase.IntentProcessing);
        }

        // 主动更新状态（通常由 Reducer 调用）。
        public void UpdateState(IState state)
        {
            ApplyStateInternal(state, trackHistory: true, persistState: true, timelineKind: MviTimelineEventKind.State);
        }

        // 发送一次性 Effect。
        protected void EmitEffect(IMviEffect effect)
        {
            if (_isDisposed || effect == null)
            {
                return;
            }

            _effectSubject.OnNext(effect);
            DevToolsHost.Track(this, MviTimelineEventKind.Effect, effect);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] EmitEffect: {effect.GetType().Name}");
            }
        }

        // 外部触发意图。
        public void EmitIntent(IIntent intent, CancellationToken cancellationToken = default)
        {
            if (_isDisposed || intent == null)
            {
                return;
            }

            DevToolsHost.Track(this, MviTimelineEventKind.Intent, intent);
            _intentSubject.OnNext(new IntentEnvelope(intent, cancellationToken));
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] EmitIntent: {intent.GetType().Name}");
            }
        }

        private void Reduce(IMviResult result)
        {
            if (result == null)
            {
                return;
            }

            var newState = Reducer(result);
            if (newState is null)
            {
                return;
            }

            if (!newState.IsUpdateNewState && EqualityComparer<IState>.Default.Equals(_currentState, newState))
            {
                // 状态一致则不更新。
                return;
            }

            UpdateState(newState);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] Reduce -> {newState.GetType().Name}");
            }
        }

        // 由子类实现具体的 Result -> State 逻辑。
        protected virtual IState Reducer(IMviResult result)
        {
            return default;
        }

        protected virtual void OnProcessError(Exception ex)
        {
            PublishProcessError(ex, default);
        }

        protected virtual void OnProcessError(Exception ex, MviErrorDecision decision)
        {
            // 兼容旧扩展点：业务仅覆写旧签名时，由 ErrorHookMode 显式启用回退（旧实现是反射探测）。
            if (ErrorHookMode == MviErrorHookMode.Legacy)
            {
                OnProcessError(ex);
                return;
            }

            PublishProcessError(ex, decision);
        }

        private void PublishProcessError(Exception ex, MviErrorDecision decision)
        {
            if (ex == null)
            {
                return;
            }

            var error = new MviErrorEffect(ex, GetType().Name);
            _errorSubject.OnNext(error);
            var traceNote = BuildErrorTraceNote(ex, decision);
            DevToolsHost.Track(this, MviTimelineEventKind.Error, error, traceNote);
            EmitEffect(error);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] Error: {traceNote} | {ex}");
            }
        }

        private static string BuildErrorTraceNote(Exception ex, MviErrorDecision decision)
        {
            if (!decision.Trace.IsConfigured)
            {
                return ex?.Message ?? string.Empty;
            }

            return $"rule={decision.Trace.RuleId},priority={decision.Trace.Priority},phase={decision.Trace.Phase},attempt={decision.Trace.Attempt},matched={decision.Trace.IsMatched},note={decision.Trace.Note}";
        }

        protected void SetInitialState(IState state)
        {
            if (state == null)
            {
                return;
            }

            UpdateState(state);
        }

        private void InitializeState()
        {
            var initialState = CreateInitialState();
            if (initialState != null)
            {
                SetInitialState(initialState);
            }
        }

        private void ApplyStateInternal(IState state, bool trackHistory, bool persistState, MviTimelineEventKind timelineKind, string timelineNote = null)
        {
            if (_isDisposed || state == null)
            {
                return;
            }

            _currentState = state;
            if (trackHistory)
            {
                RecordStateHistory(state);
            }

            _stateSubject.OnNext(state);
            if (persistState)
            {
                PersistState(state);
            }

            DevToolsHost.Track(this, timelineKind, state, timelineNote);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] UpdateState -> {state.GetType().Name}");
            }
        }

        private void RecordStateHistory(IState state)
        {
            // 每次写入前同步一次容量，使 Store.StateHistoryCapacity 的动态覆写生效。
            _history.Capacity = StateHistoryCapacity;
            _history.Record(state);
        }

        private bool TryApplyHistoryAt(int index, MviTimelineEventKind timelineKind, string note)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (!_history.TryGetAt(index, out var state) || state == null)
            {
                return false;
            }

            ApplyStateInternal(state, trackHistory: false, persistState: true, timelineKind: timelineKind, timelineNote: note);
            return true;
        }

        private void UpdateHistoryIndexForState(IState state)
        {
            _history.TryLocate(state);
        }

        private async ValueTask<MviErrorDecision> ResolveErrorDecisionAsync(
            Exception ex,
            IIntent intent,
            int attempt,
            MviErrorPhase phase,
            CancellationToken cancellationToken)
        {
            var strategy = ErrorStrategy ?? DefaultMviErrorStrategy.Instance;
            try
            {
                var context = new MviErrorContext(this, ex, intent, attempt, phase);
                var decision = await strategy.DecideAsync(context, cancellationToken);
                return decision.IsConfigured ? decision : MviErrorDecision.Emit();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return MviErrorDecision.Emit();
            }
        }

        private async ValueTask HandleNonIntentErrorAsync(Exception ex, MviErrorPhase phase, CancellationToken cancellationToken)
        {
            var decision = await ResolveErrorDecisionAsync(ex, null, 0, phase, cancellationToken);
            if (_isDisposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (decision.EmitError)
            {
                OnProcessError(ex, decision);
            }

            if (decision.Rethrow)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        private void HandleNonIntentError(Exception ex, MviErrorPhase phase)
        {
            if (_isDisposed || ex == null)
            {
                return;
            }

            var completion = HandleNonIntentErrorAsync(ex, phase, _lifetimeCancellationToken);
            if (completion.IsCompleted)
            {
                // 只消费已完成的 ValueTask，保留同步策略的原始抛出行为。
                completion.GetAwaiter().GetResult();
                return;
            }

            _ = ObserveDeferredErrorAsync(completion);
        }

        private async Task ObserveDeferredErrorAsync(ValueTask completion)
        {
            try
            {
                await completion;
            }
            catch (OperationCanceledException) when (_lifetimeCancellationToken.IsCancellationRequested)
            {
                // Store 释放后停止交付尚未完成的错误决策。
            }
            catch (Exception ex)
            {
                if (_isDisposed)
                {
                    return;
                }

                // void 入口的延迟 Rethrow 交付到 R3 的未处理错误通道。
                // 不改 continuation 的线程，也不让观察器留下未观察的 faulted Task。
                try
                {
                    ObservableSystem.GetUnhandledExceptionHandler().Invoke(ex);
                }
                catch (Exception handlerException)
                {
                    UnityEngine.Debug.LogException(handlerException);
                }
            }
        }

        private bool TryRestorePersistedState()
        {
            if (_persistenceCoordinator == null)
            {
                return false;
            }

            if (!_persistenceCoordinator.TryRestore(out var migrated))
            {
                return false;
            }

            SetInitialState(migrated);
            return true;
        }

        private void PersistState(IState state)
        {
            _persistenceCoordinator?.Save(state);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _storeCts.Cancel();
            _disposables.Dispose();
            _storeCts.Dispose();
            _history.Clear();
            DevToolsHost.Detach(this);

            if (_stateSubject is IDisposable stateDisposable)
            {
                stateDisposable.Dispose();
            }

            if (_intentSubject is IDisposable intentDisposable)
            {
                intentDisposable.Dispose();
            }

            if (_effectSubject is IDisposable effectDisposable)
            {
                effectDisposable.Dispose();
            }

            if (_errorSubject is IDisposable errorDisposable)
            {
                errorDisposable.Dispose();
            }
        }
    }
}
