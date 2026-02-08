using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace MVI
{
    // Store：负责处理 Intent、生成 Result，并通过 Reducer 产出新 State。
    public abstract class Store : IDisposable
    {
        private readonly Subject<IState> _stateSubject = new();
        private readonly Subject<IntentEnvelope> _intentSubject = new();
        private readonly Subject<IMviEffect> _effectSubject = new();
        private readonly Subject<MviErrorEffect> _errorSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private readonly CancellationTokenSource _storeCts = new();
        private readonly List<IStoreMiddleware> _middlewares = new();
        private readonly Dictionary<Type, IntentProcessingPolicy> _intentPolicies = new();
        private readonly List<IState> _stateHistory = new();
        private readonly object _middlewareSyncRoot = new();
        private IState _currentState;
        private int _stateHistoryIndex = -1;
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
        public int StateHistoryCount => _stateHistory.Count;

        // 当前历史游标（-1 表示无历史）。
        public int CurrentStateHistoryIndex => _stateHistoryIndex;

        public bool CanUndo => _stateHistoryIndex > 0;

        public bool CanRedo => _stateHistoryIndex >= 0 && _stateHistoryIndex < _stateHistory.Count - 1;

        protected Store()
        {
            State = _stateSubject.ToReadOnlyReactiveProperty();
            ConfigureMiddlewares(_middlewares);
            ConfigureIntentProcessingPolicies(_intentPolicies);
            if (!TryRestorePersistedState())
            {
                InitializeState();
            }

            Process(_intentSubject);
        }

        // Intent 并发策略（默认 Switch: 取消上一个意图）。
        protected virtual AwaitOperation ProcessingMode => AwaitOperation.Switch;

        // 并发上限（仅对 Parallel/SequentialParallel 生效，-1 为不限制）。
        protected virtual int MaxConcurrent => -1;

        // 状态历史容量（<=0 表示关闭历史）。
        protected virtual int StateHistoryCapacity => 64;

        // 配置 Store 级中间件（可覆写）。
        protected virtual void ConfigureMiddlewares(IList<IStoreMiddleware> middlewares)
        {
        }

        // 配置按 Intent 类型的并发策略（可覆写）。
        protected virtual void ConfigureIntentProcessingPolicies(IDictionary<Type, IntentProcessingPolicy> policies)
        {
        }

        // 当前 Store 的状态持久化插件（默认使用全局选项）。
        protected virtual IStoreStatePersistence Persistence => MviStoreOptions.DefaultStatePersistence;

        // 当前 Store 的错误处理策略（默认发出 Error/Effect）。
        protected virtual IMviErrorStrategy ErrorStrategy => MviStoreOptions.DefaultErrorStrategy ?? DefaultMviErrorStrategy.Instance;

        // 持久化键（默认使用完整类型名）。
        protected virtual string PersistenceKey => GetType().FullName;

        // 持久化状态迁移钩子（用于版本升级）。
        protected virtual IState MigratePersistedState(IState persistedState)
        {
            return persistedState;
        }

        // 运行时注册中间件。
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
            return MviDevTools.GetTimelineSnapshot(this);
        }

        // DevTools：清空当前 Store 时间线。
        public void ClearTimeline()
        {
            MviDevTools.Clear(this);
        }

        // DevTools：重放时间线中的 Intent（顺序执行）。
        public async ValueTask<int> ReplayIntentsAsync(CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                return 0;
            }

            var timeline = MviDevTools.GetTimelineSnapshot(this);
            if (timeline == null || timeline.Count == 0)
            {
                return 0;
            }

            var replayed = 0;
            for (var i = 0; i < timeline.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = timeline[i];
                if (entry.Kind != MviTimelineEventKind.Intent || entry.Payload is not IIntent intent)
                {
                    continue;
                }

                var result = await ExecuteIntentWithStrategyAsync(intent, cancellationToken, MviErrorPhase.Replay);
                if (result == null)
                {
                    continue;
                }

                try
                {
                    Reduce(result);
                    replayed++;
                    MviDevTools.Track(this, MviTimelineEventKind.Replay, intent, $"replay:{intent.GetType().Name}");
                }
                catch (Exception ex)
                {
                    HandleNonIntentError(ex, MviErrorPhase.Reducing);
                }
            }

            return replayed;
        }

        // Undo 到前一个状态。
        public bool UndoState()
        {
            return TryApplyHistoryAt(_stateHistoryIndex - 1, MviTimelineEventKind.Undo, "undo");
        }

        // Redo 到后一个状态。
        public bool RedoState()
        {
            return TryApplyHistoryAt(_stateHistoryIndex + 1, MviTimelineEventKind.Redo, "redo");
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

            var timeline = MviDevTools.GetTimelineSnapshot(this);
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
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.CancellationToken, _storeCts.Token);
            return await ExecuteIntentWithStrategyAsync(envelope.Intent, linkedCts.Token, MviErrorPhase.IntentProcessing);
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
            MviDevTools.Track(this, MviTimelineEventKind.Effect, effect);
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

            MviDevTools.Track(this, MviTimelineEventKind.Intent, intent);
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
            if (ex == null)
            {
                return;
            }

            var error = new MviErrorEffect(ex, GetType().Name);
            _errorSubject.OnNext(error);
            MviDevTools.Track(this, MviTimelineEventKind.Error, error, ex.Message);
            EmitEffect(error);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] Error: {ex}");
            }
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

        private async ValueTask<IMviResult> ExecuteIntentWithStrategyAsync(IIntent intent, CancellationToken cancellationToken, MviErrorPhase phase)
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
                    var result = await InvokeMiddlewarePipelineAsync(intent, cancellationToken);
                    if (result != null)
                    {
                        MviDevTools.Track(this, MviTimelineEventKind.Result, result);
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    var decision = await ResolveErrorDecisionAsync(ex, intent, attempt, phase, cancellationToken);
                    if (decision.EmitError)
                    {
                        OnProcessError(ex);
                    }

                    if (decision.FallbackResult != null)
                    {
                        MviDevTools.Track(this, MviTimelineEventKind.Result, decision.FallbackResult, "fallback");
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

        private ValueTask<IMviResult> InvokeMiddlewarePipelineAsync(IIntent intent, CancellationToken cancellationToken)
        {
            if (intent == null)
            {
                return default;
            }

            IStoreMiddleware[] middlewares;
            lock (_middlewareSyncRoot)
            {
                if (_middlewares.Count == 0)
                {
                    return ProcessIntentAsync(intent, cancellationToken);
                }

                middlewares = _middlewares.ToArray();
            }

            var context = new StoreMiddlewareContext(this, intent, cancellationToken);
            var index = -1;

            ValueTask<IMviResult> Next(StoreMiddlewareContext current)
            {
                index++;
                if (index >= middlewares.Length)
                {
                    if (current.Intent == null)
                    {
                        return default;
                    }

                    return ProcessIntentAsync(current.Intent, current.CancellationToken);
                }

                var middleware = middlewares[index];
                if (middleware == null)
                {
                    return Next(current);
                }

                return middleware.InvokeAsync(current, Next);
            }

            return Next(context);
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

            MviDevTools.Track(this, timelineKind, state, timelineNote);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] UpdateState -> {state.GetType().Name}");
            }
        }

        private void RecordStateHistory(IState state)
        {
            var capacity = StateHistoryCapacity;
            if (capacity <= 0)
            {
                _stateHistory.Clear();
                _stateHistoryIndex = -1;
                return;
            }

            if (_stateHistoryIndex >= 0 && _stateHistoryIndex < _stateHistory.Count - 1)
            {
                _stateHistory.RemoveRange(_stateHistoryIndex + 1, _stateHistory.Count - _stateHistoryIndex - 1);
            }

            _stateHistory.Add(state);
            if (_stateHistory.Count > capacity)
            {
                _stateHistory.RemoveRange(0, _stateHistory.Count - capacity);
            }

            _stateHistoryIndex = _stateHistory.Count - 1;
        }

        private bool TryApplyHistoryAt(int index, MviTimelineEventKind timelineKind, string note)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (index < 0 || index >= _stateHistory.Count)
            {
                return false;
            }

            _stateHistoryIndex = index;
            ApplyStateInternal(_stateHistory[index], trackHistory: false, persistState: true, timelineKind: timelineKind, timelineNote: note);
            return true;
        }

        private void UpdateHistoryIndexForState(IState state)
        {
            if (state == null || _stateHistory.Count == 0)
            {
                return;
            }

            for (var i = _stateHistory.Count - 1; i >= 0; i--)
            {
                var candidate = _stateHistory[i];
                if (ReferenceEquals(candidate, state) || Equals(candidate, state))
                {
                    _stateHistoryIndex = i;
                    return;
                }
            }
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
            catch
            {
                return MviErrorDecision.Emit();
            }
        }

        private MviErrorDecision ResolveErrorDecision(Exception ex, IIntent intent, int attempt, MviErrorPhase phase)
        {
            var strategy = ErrorStrategy ?? DefaultMviErrorStrategy.Instance;
            try
            {
                var context = new MviErrorContext(this, ex, intent, attempt, phase);
                var decisionTask = strategy.DecideAsync(context, CancellationToken.None);
                var decision = decisionTask.IsCompletedSuccessfully
                    ? decisionTask.Result
                    : decisionTask.AsTask().GetAwaiter().GetResult();
                return decision.IsConfigured ? decision : MviErrorDecision.Emit();
            }
            catch
            {
                return MviErrorDecision.Emit();
            }
        }

        private void HandleNonIntentError(Exception ex, MviErrorPhase phase)
        {
            if (ex == null)
            {
                return;
            }

            var decision = ResolveErrorDecision(ex, null, 0, phase);
            if (decision.EmitError)
            {
                OnProcessError(ex);
            }

            if (decision.Rethrow)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        private bool TryRestorePersistedState()
        {
            var persistence = Persistence;
            var key = PersistenceKey;
            if (persistence == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (!persistence.TryLoad(key, out var persisted) || persisted == null)
                {
                    return false;
                }

                var migrated = MigratePersistedState(persisted);
                if (migrated == null)
                {
                    return false;
                }

                SetInitialState(migrated);
                return true;
            }
            catch (Exception ex)
            {
                HandleNonIntentError(ex, MviErrorPhase.PersistenceLoad);
                return false;
            }
        }

        private void PersistState(IState state)
        {
            var persistence = Persistence;
            var key = PersistenceKey;
            if (persistence == null || string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            try
            {
                persistence.Save(key, state);
            }
            catch (Exception ex)
            {
                HandleNonIntentError(ex, MviErrorPhase.PersistenceSave);
            }
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
            _stateHistory.Clear();
            _stateHistoryIndex = -1;
            MviDevTools.Detach(this);

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
