using System.Threading;
using Loxodon.Framework.ViewModels;
using R3;

namespace MVI
{
    /// <summary>
    /// ViewModel 基类：绑定 Store，响应 State，同步到 ViewModel 属性。
    /// </summary>
    public abstract class MviViewModel : ViewModelBase
    {
        // 当前 ViewModel 绑定的 Store。
        protected Store Store { get; private set; }
        private readonly CompositeDisposable _disposables = new();
        private bool _disposeStore;

        // 绑定 Store 并订阅 State。
        public void BindStore(Store store, bool disposeStore = true)
        {
            Store = store;
            _disposeStore = disposeStore;
            Store.State
                .ObserveOnMainThread()
                .Subscribe(OnStateChanged)
                .AddTo(_disposables);

            Store.Effects
                .ObserveOnMainThread()
                .Subscribe(OnEffect)
                .AddTo(_disposables);

            Store.Errors
                .ObserveOnMainThread()
                .Subscribe(OnError)
                .AddTo(_disposables);
        }

        // 状态变化回调：默认通过生成的映射器同步属性。
        protected virtual void OnStateChanged(IState? state)
        {
            if (state is null)
            {
                return;
            }
            
            MviStateMapper.TryMap(state, this);
        }

        // Effect 回调：一次性事件入口。
        protected virtual void OnEffect(IMviEffect effect)
        {
            if (effect is null)
            {
                return;
            }
        }

        // Error 回调：统一错误入口。
        protected virtual void OnError(MviErrorEffect error)
        {
            if (error is null)
            {
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            _disposables.Dispose();
            if (_disposeStore)
            {
                Store?.Dispose();
            }

            base.Dispose(disposing);
        }

        // 发起意图。
        protected void EmitIntent(IIntent intent)
        {
            Store.EmitIntent(intent);
        }

        // 发起可取消意图。
        protected void EmitIntent(IIntent intent, CancellationToken cancellationToken)
        {
            Store.EmitIntent(intent, cancellationToken);
        }
    }

    // 泛型 ViewModel：提供强类型 Intent 入口。
    public abstract class MviViewModel<TIntent> : MviViewModel where TIntent : IIntent
    {
        // 发起意图（强类型）。
        protected new void EmitIntent(TIntent intent)
        {
            base.EmitIntent(intent);
        }

        // 发起可取消意图（强类型）。
        protected new void EmitIntent(TIntent intent, CancellationToken cancellationToken)
        {
            base.EmitIntent(intent, cancellationToken);
        }
    }

    // 泛型 ViewModel：提供强类型 Store/State 入口。
    public abstract class MviViewModel<TState, TIntent, TResult> : MviViewModel<TIntent>
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
    {
        protected new Store<TState, TIntent, TResult> Store { get; private set; }

        protected ReadOnlyReactiveProperty<TState> State => Store?.State;

        public new void BindStore(Store<TState, TIntent, TResult> store, bool disposeStore = true)
        {
            Store = store;
            base.BindStore(store, disposeStore);
        }

        protected sealed override void OnStateChanged(IState? state)
        {
            base.OnStateChanged(state);
            if (state is TState typed)
            {
                OnStateChanged(typed);
            }
        }

        protected virtual void OnStateChanged(TState state)
        {
        }
    }

    // 泛型 ViewModel：增加类型安全的 Effect 回调。
    public abstract class MviViewModel<TState, TIntent, TResult, TEffect> : MviViewModel<TState, TIntent, TResult>
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
        where TEffect : class, IMviEffect
    {
        protected new Store<TState, TIntent, TResult, TEffect> Store { get; private set; }

        protected Observable<TEffect> Effects => Store?.Effects;

        public new void BindStore(Store<TState, TIntent, TResult, TEffect> store, bool disposeStore = true)
        {
            Store = store;
            base.BindStore(store, disposeStore);
        }

        protected sealed override void OnEffect(IMviEffect effect)
        {
            base.OnEffect(effect);
            if (effect is TEffect typed)
            {
                OnEffect(typed);
            }
        }

        protected virtual void OnEffect(TEffect effect)
        {
        }
    }
}
