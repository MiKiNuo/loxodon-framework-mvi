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
        private readonly Subject<IIntent> _intentSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private IState _currentState;

        // 当前状态快照。
        public IState CurrentState => _currentState;

        // 状态流（只读）。
        public ReadOnlyReactiveProperty<IState> State { get; }

        protected Store()
        {
            State = _stateSubject!.ToReadOnlyReactiveProperty();
            Process(_intentSubject);
        }

        // 处理单个意图，返回结果流。
        protected async ValueTask<Observable<IMviResult>> ProcessIntentAsync(IIntent intent,
            CancellationToken ct = default)
        {
            var result = await intent.HandleIntentAsync(ct);
            return Observable.Return(result);
        }

        // 订阅意图流并驱动 Reducer。
        public void Process(Observable<IIntent> intents)
        {
            intents
                .SelectAwait(ProcessIntentAsync)
                .Switch()
                .Subscribe(Reduce)
                .AddTo(_disposables);
        }

        // 主动更新状态（通常由 Reducer 调用）。
        public void UpdateState(IState state)
        {
            _stateSubject.OnNext(state);
        }

        // 外部触发意图。
        public void EmitIntent(IIntent intent)
        {
            _intentSubject.OnNext(intent);
        }

        private void Reduce(IMviResult result)
        {
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

            _currentState = newState;
            UpdateState(newState);
        }

        // 由子类实现具体的 Result -> State 逻辑。
        protected virtual IState Reducer(IMviResult result)
        {
            return default;
        }

        public void Dispose() => _disposables.Dispose();
    }
}
