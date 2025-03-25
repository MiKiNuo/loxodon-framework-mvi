using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace MVI
{
    public abstract class Store<TState, TIntent> : IDisposable
        where TState : IState
        where TIntent : IIntent
    {
        private readonly Subject<TState> _stateSubject = new();
        private readonly Subject<TIntent> _intentSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private TState _currentState;

        public TState CurrentState => _currentState;
        public ReadOnlyReactiveProperty<TState> State { get; }

        protected Store()
        {
            State = _stateSubject!.ToReadOnlyReactiveProperty();
            Process(_intentSubject);
        }

        protected async ValueTask<Observable<IMviResult>> ProcessIntentAsync(TIntent intent,
            CancellationToken ct = default)
        {
            var result = await intent.HandleIntentAsync(ct);
            return Observable.Return(result);
        }

        public void Process(Observable<TIntent> intents)
        {
            intents
                .SelectAwait(ProcessIntentAsync)
                .Switch()
                .Subscribe(Reduce)
                .AddTo(_disposables);
        }

        public void UpdateState(TState state)
        {
            _stateSubject.OnNext(state);
        }

        public void EmitIntent(TIntent intent)
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

            if (EqualityComparer<TState>.Default.Equals(_currentState, newState))
                return;

            _currentState = newState;
            UpdateState(newState);
        }

        protected virtual TState Reducer(IMviResult result)
        {
            return default;
        }


        public void Dispose() => _disposables.Dispose();
    }
}