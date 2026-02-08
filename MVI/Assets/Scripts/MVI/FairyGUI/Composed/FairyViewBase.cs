using System;
using Loxodon.Framework.Binding.Binders;
using Loxodon.Framework.Binding.Builder;
using Loxodon.Framework.Binding.Contexts;
using Loxodon.Framework.Contexts;

namespace MVI.FairyGUI.Composed
{
    // FairyGUI 组件 View 基类：提供 BindingContext 与 DataContext 绑定。
    public abstract class FairyViewBase<TViewModel> : IFairyView
    {
        private bool isBound;
        private bool isDisposed;

        // 组件绑定的 ViewModel。
        protected TViewModel ViewModel { get; private set; }

        // Loxodon BindingContext（用于 FairyGUI 绑定）。
        protected BindingContext BindingContext { get; private set; }

        // 设置 DataContext 并触发一次绑定。
        public void SetDataContext(object viewModel)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (viewModel is not TViewModel typed)
            {
                throw new ArgumentException($"ViewModel 类型不匹配，期望 {typeof(TViewModel).Name}");
            }

            ViewModel = typed;
            EnsureBindingContext();
            BindingContext.DataContext = ViewModel;

            if (!isBound)
            {
                isBound = true;
                Bind();
            }
        }

        // 创建绑定集合，子类直接使用即可。
        protected BindingSet<TTarget, TViewModel> CreateBindingSet<TTarget>(TTarget target)
            where TTarget : class
        {
            EnsureBindingContext();
            return new BindingSet<TTarget, TViewModel>(BindingContext, target);
        }

        // 子类实现具体绑定逻辑。
        public abstract void Bind();

        // 释放绑定资源。
        public virtual void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            BindingContext?.Dispose();
            BindingContext = null;
            ViewModel = default;
            isBound = false;
        }

        private void EnsureBindingContext()
        {
            if (BindingContext != null)
            {
                return;
            }

            IBinder binder = Context.GetApplicationContext().GetService<IBinder>();
            if (binder == null)
            {
                throw new Exception("数据绑定服务未初始化，请先启动 BindingServiceBundle。");
            }

            BindingContext = new BindingContext(this, binder);
        }
    }
}
