using Loxodon.Framework.ViewModels;
using MVI.Generated;
using R3;

namespace MVI
{
    public abstract class MviViewModel : ViewModelBase
    {
        protected Store Store { get; private set; }
        private readonly CompositeDisposable _disposables = new();

        public void BindStore(Store store)
        {
            Store = store;
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
            
            GeneratedStateMapper.TryMap(state, this);
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
