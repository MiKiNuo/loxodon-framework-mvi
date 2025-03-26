using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace MVI
{
    public abstract class Store: IDisposable
    {
        private readonly Subject<IState> _stateSubject = new();
        private readonly Subject<IIntent> _intentSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private IState _currentState;

        public IState CurrentState => _currentState;
        public ReadOnlyReactiveProperty<IState> State { get; }

        protected Store()
        {
            State = _stateSubject!.ToReadOnlyReactiveProperty();
            Process(_intentSubject);
        }

        protected async ValueTask<Observable<IMviResult>> ProcessIntentAsync(IIntent intent,
            CancellationToken ct = default)
        {
            var result = await intent.HandleIntentAsync(ct);
            return Observable.Return(result);
        }

        public void Process(Observable<IIntent> intents)
        {
            intents
                .SelectAwait(ProcessIntentAsync)
                .Switch()
                .Subscribe(Reduce)
                .AddTo(_disposables);
        }

        public void UpdateState(IState state)
        {
            _stateSubject.OnNext(state);
        }

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

            if (EqualityComparer<IState>.Default.Equals(_currentState, newState))
                return;

            _currentState = newState;
            UpdateState(newState);
        }

        protected virtual IState Reducer(IMviResult result)
        {
            return default;
        }


        public void Dispose() => _disposables.Dispose();
    }
}