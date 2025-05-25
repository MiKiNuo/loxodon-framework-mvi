using Loxodon.Framework.ViewModels;
using R3;

namespace MVI
{
    public abstract class MviViewModel: ViewModelBase
    {
        protected Store Store { get; private set; }
        private readonly CompositeDisposable _disposables = new();

        public void BindStore(Store store)
        {
            Store = store;
            // 使用 ObserveOnMainThread 确保 UI 线程更新
            Store.State
                .ObserveOnMainThread()
                .Subscribe(OnStateChanged)
                .AddTo(_disposables);
        }

        protected virtual void OnStateChanged(IState? state)
        {
            if (state is null)
            {
                return;
            }
            var vmType = GetType();
            var stateType = state.GetType();
            var stateProps = stateType.GetProperties();

            foreach (var stateProp in stateProps)
            {
                if (stateProp?.CanRead != true) continue;

                var vmProp = vmType.GetProperty(stateProp.Name);

                if (vmProp == null) continue;
                if (!vmProp.CanWrite) continue;

                var newValue = stateProp.GetValue(state);
                if (!state.IsUpdateNewState)
                {
                    //如果当前的VM值跟新的一样则不需要更新
                    var currentValue = vmProp.GetValue(this);
                     if (!Equals(currentValue, newValue))
                     {
                         vmProp.SetValue(this, newValue);
                     }
                }
                else
                {
                    vmProp.SetValue(this, newValue);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _disposables.Dispose();
            base.Dispose(disposing);
        }

        protected void EmitIntent(IIntent intent)
        {
            Store.EmitIntent(intent);
        }
    }
}