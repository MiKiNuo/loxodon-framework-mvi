using System.Threading;
using R3;

namespace MVI
{
    // 泛型 Store：提供类型安全的 State / Intent / Result。
    public abstract class Store<TState, TIntent, TResult> : Store
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
    {
        public new TState CurrentState => base.CurrentState as TState;

        public new ReadOnlyReactiveProperty<TState> State { get; }

        protected Store()
        {
            State = base.State.Select(state => state as TState).Where(state => state != null).ToReadOnlyReactiveProperty();
        }

        // 初始状态（可覆写）。
        protected virtual TState InitialState => null;

        protected override IState CreateInitialState()
        {
            return InitialState;
        }

        public void EmitIntent(TIntent intent, CancellationToken cancellationToken = default)
        {
            base.EmitIntent(intent, cancellationToken);
        }

        protected sealed override IState Reducer(IMviResult result)
        {
            if (result is TResult typed)
            {
                return Reduce(typed);
            }

            return null;
        }

        protected abstract TState Reduce(TResult result);
    }

    // 泛型 Store：增加类型安全的 Effect 通道。
    public abstract class Store<TState, TIntent, TResult, TEffect> : Store<TState, TIntent, TResult>
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
        where TEffect : class, IMviEffect
    {
        public new Observable<TEffect> Effects => base.Effects
            .Select(effect => effect as TEffect)
            .Where(effect => effect != null);

        protected void EmitEffect(TEffect effect)
        {
            base.EmitEffect(effect);
        }
    }
}
