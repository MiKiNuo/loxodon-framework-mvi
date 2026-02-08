using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace MVI
{
    // Store 测试辅助器：用于采集状态/Effect/Error，并提供等待断言辅助。
    public sealed class StoreTestKit : IDisposable
    {
        private readonly CompositeDisposable _subscriptions = new();
        private readonly List<IState> _states = new();
        private readonly List<IMviEffect> _effects = new();
        private readonly List<MviErrorEffect> _errors = new();

        public StoreTestKit(Store store, bool includeCurrentState = true)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));

            if (includeCurrentState && Store.CurrentState != null)
            {
                _states.Add(Store.CurrentState);
            }

            Store.State.Subscribe(state =>
            {
                if (state != null)
                {
                    _states.Add(state);
                }
            }).AddTo(_subscriptions);

            Store.Effects.Subscribe(effect =>
            {
                if (effect != null)
                {
                    _effects.Add(effect);
                }
            }).AddTo(_subscriptions);

            Store.Errors.Subscribe(error =>
            {
                if (error != null)
                {
                    _errors.Add(error);
                }
            }).AddTo(_subscriptions);
        }

        public Store Store { get; }

        public IReadOnlyList<IState> States => _states;

        public IReadOnlyList<IMviEffect> Effects => _effects;

        public IReadOnlyList<MviErrorEffect> Errors => _errors;

        public StoreTestKit Emit(IIntent intent, CancellationToken cancellationToken = default)
        {
            Store.EmitIntent(intent, cancellationToken);
            return this;
        }

        public async ValueTask<bool> WaitForStateAsync(
            Func<IState, bool> predicate,
            int timeoutMs = 2000,
            int pollIntervalMs = 10,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs <= 0 ? 1 : timeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = Store.CurrentState;
                if (state != null && predicate(state))
                {
                    return true;
                }

                await Task.Delay(pollIntervalMs <= 0 ? 1 : pollIntervalMs, cancellationToken);
            }

            return false;
        }

        public TState LastStateAs<TState>() where TState : class, IState
        {
            if (_states.Count == 0)
            {
                return null;
            }

            return _states[_states.Count - 1] as TState;
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
        }
    }

    public static class StoreTestKitExtensions
    {
        public static StoreTestKit CreateTestKit(this Store store, bool includeCurrentState = true)
        {
            return new StoreTestKit(store, includeCurrentState);
        }
    }
}
