using Loxodon.Framework.ViewModels;
using MVI.Generated;
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

        // 绑定 Store 并订阅 State。
        public void BindStore(Store store)
        {
            Store = store;
            Store.State
                .ObserveOnMainThread()
                .Subscribe(OnStateChanged)
                .AddTo(_disposables);
        }

        // 状态变化回调：默认通过生成的映射器同步属性。
        protected virtual void OnStateChanged(IState? state)
        {
            if (state is null)
            {
                return;
            }
            
            GeneratedStateMapper.TryMap(state, this);
        }

        protected override void Dispose(bool disposing)
        {
            _disposables.Dispose();
            base.Dispose(disposing);
        }

        // 发起意图。
        protected void EmitIntent(IIntent intent)
        {
            Store.EmitIntent(intent);
        }
    }
}
