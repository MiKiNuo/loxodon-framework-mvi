using System;
using System.Collections.Generic;
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

        protected Store()
        {
            State = _stateSubject.ToReadOnlyReactiveProperty();
            InitializeState();
            Process(_intentSubject);
        }

        // Intent 并发策略（默认 Switch: 取消上一个意图）。
        protected virtual AwaitOperation ProcessingMode => AwaitOperation.Switch;

        // 并发上限（仅对 Parallel/SequentialParallel 生效，-1 为不限制）。
        protected virtual int MaxConcurrent => -1;

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

        private void Process(Observable<IntentEnvelope> intents)
        {
            intents
                .SelectAwait(ProcessIntentEnvelopeAsync, ProcessingMode, maxConcurrent: MaxConcurrent)
                .Where(result => result != null)
                .Subscribe(result =>
                {
                    try
                    {
                        Reduce(result);
                    }
                    catch (Exception ex)
                    {
                        OnProcessError(ex);
                    }
                })
                .AddTo(_disposables);
        }

        private async ValueTask<IMviResult> ProcessIntentEnvelopeAsync(IntentEnvelope envelope, CancellationToken ct = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.CancellationToken, _storeCts.Token);
            try
            {
                return await ProcessIntentAsync(envelope.Intent, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                OnProcessError(ex);
                return null;
            }
        }

        // 主动更新状态（通常由 Reducer 调用）。
        public void UpdateState(IState state)
        {
            if (_isDisposed || state == null)
            {
                return;
            }

            _currentState = state;
            _stateSubject.OnNext(state);
            if (MviDiagnostics.Enabled)
            {
                MviDiagnostics.Trace($"[Store:{GetType().Name}] UpdateState -> {state.GetType().Name}");
            }
        }

        // 发送一次性 Effect。
        protected void EmitEffect(IMviEffect effect)
        {
            if (_isDisposed || effect == null)
            {
                return;
            }

            _effectSubject.OnNext(effect);
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
