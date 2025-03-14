using Loxodon.Framework.ViewModels;
using R3;

namespace MVI
{
    public abstract class MviViewModel<TState, TIntent> : ViewModelBase
        where TState : IState, new()
        where TIntent : IIntent
    {
        protected readonly Store<TState, TIntent> Store;
        private readonly CompositeDisposable _disposables = new();

        protected MviViewModel()
        {
            Store = new Store<TState, TIntent>(new TState());
            // 使用 ObserveOnMainThread 确保 UI 线程更新
            Store.State
                .ObserveOnMainThread()
                .Subscribe(OnStateChanged)
                .AddTo(_disposables);
        }

        protected virtual void OnStateChanged(TState state)
        {
            // 自动映射状态到 ViewModel 属性（带脏检查优化）
            var vmType = GetType();
            var stateType = typeof(TState);

            foreach (var prop in vmType.GetProperties())
            {
                if (!prop.CanWrite) continue;

                var stateProp = stateType.GetProperty(prop.Name);
                if (stateProp?.CanRead != true) continue;

                var newValue = stateProp.GetValue(state);
                var currentValue = prop.GetValue(this);

                if (!Equals(currentValue, newValue))
                {
                    prop.SetValue(this, newValue);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _disposables.Dispose();
            base.Dispose(disposing);
        }

        protected void EmitIntent(TIntent intent)
        {
            Store.EmitIntent(intent);
        }
    }
}