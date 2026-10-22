using System;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Views;
using MVI.Components;
using MVI.Composition;
using MVI.UIAdapters.UGUI;
using UnityEngine;

namespace MVI.Composed
{
    // 组合式窗口基类：UGUI 适配 + 组合式组件中枢。
    // 该类只保留 UGUI 相关职责（视图加载、附加、绑定、销毁），其余通用职责交给 ComposableComponentHub。
    public abstract class ComposedWindowBase : Window
    {
        private readonly ComposableComponentHub componentHub = new();
        private bool isDestroyed;

        // View 定位器（ResourcesViewLocator）。
        protected IUIViewLocator ViewLocator { get; private set; }

        // UGUI 适配器。
        protected IViewHost ViewHost { get; private set; }

        // 全局组件事件通知（可选订阅）。
        public event Action<ComponentEvent> ComponentEventRaised;

        protected ComposedWindowBase()
        {
            componentHub.ComponentEventRaised += OnHubComponentEventRaised;
        }

        protected override void OnCreate(IBundle bundle)
        {
            ViewLocator = Context.GetApplicationContext().GetService<IUIViewLocator>();
            ViewHost = CreateViewHost();
            OnCompose(bundle);
        }

        /// <summary>
        /// 默认实现：直接用 <see cref="IUIViewLocator"/> 构造 <see cref="UguiViewHost"/>。
        /// 内部 <see cref="UIViewLoaderDispatcher"/> 通过一次性委托绑定完成类型擦除派发，
        /// 运行时无反射。子类可覆写以提供自定义 ViewHost。
        /// </summary>
        protected virtual IViewHost CreateViewHost()
        {
            return new UguiViewHost(ViewLocator);
        }

        protected abstract void OnCompose(IBundle bundle);

        // 组合式 DSL 入口：UGUI 走 resourcePath + root 加载，因此 ComponentBuilder 在此被 host-specific wrapper 包一层。
        protected void Compose(Action<CompositionBuilder> configure)
        {
            if (configure == null)
            {
                return;
            }

            var builder = new CompositionBuilder(this);
            configure(builder);
        }

        // 兼容：批量注册组件。
        protected void RegisterComponents(Action<ComponentRegistryBuilder> configure)
        {
            if (configure == null)
            {
                return;
            }

            var builder = new ComponentRegistryBuilder(this);
            configure(builder);
        }

        // 兼容：批量注册事件路由（走共享内核）。
        protected void RegisterEventRoutes(Action<ComposableEventRouteBuilder> configure)
        {
            if (configure == null)
            {
                return;
            }

            var builder = new ComposableEventRouteBuilder(componentHub);
            configure(builder);
        }

        // 加载视图。
        protected TView LoadView<TView>(string resourcePath) where TView : UIView, IView
        {
            return ViewHost.Load<TView>(resourcePath);
        }

        // 注册组件并绑定（提供旧 API 以保持向后兼容）。
        protected TView RegisterComponent<TView, TViewModel>(string componentId, string resourcePath, Transform root, TViewModel viewModel)
            where TView : UIView, IView
        {
            return RegisterComponentInternal<TView, TViewModel>(componentId, resourcePath, root, viewModel, null);
        }

        // 注册组件并设置自定义 props 比较器。
        protected TView RegisterComponent<TView, TViewModel, TProps>(
            string componentId,
            string resourcePath,
            Transform root,
            TViewModel viewModel,
            Func<TProps, TProps, bool> comparer)
            where TView : UIView, IView
        {
            return RegisterComponentInternal<TView, TViewModel>(
                componentId,
                resourcePath,
                root,
                viewModel,
                comparer == null ? null : ComposableComponentHub.WrapPropsComparer(comparer));
        }

        private TView RegisterComponentInternal<TView, TViewModel>(
            string componentId,
            string resourcePath,
            Transform root,
            TViewModel viewModel,
            Func<object, object, bool> propsComparer)
            where TView : UIView, IView
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("componentId is required.");
            }

            if (componentHub.HasComponent(componentId))
            {
                return componentHub.GetView<TView>(componentId);
            }

            var view = LoadView<TView>(resourcePath);
            AttachAndBind(view, root, viewModel);
            TrackView(view);
            TrackDisposable(viewModel as IDisposable);
            componentHub.TryRegisterComponent(componentId, view, viewModel, propsComparer);
            return view;
        }

        // 设置 props 比较器（会清空上一次 props）。
        protected void SetPropsComparer<TProps>(string componentId, Func<TProps, TProps, bool> comparer)
        {
            componentHub.SetPropsComparer(componentId, comparer);
        }

        // 按组件 ID 获取视图。
        protected TView GetView<TView>(string componentId) where TView : class
        {
            return componentHub.GetView<TView>(componentId);
        }

        // 按组件 ID 获取 ViewModel。
        protected TViewModel GetViewModel<TViewModel>(string componentId) where TViewModel : class
        {
            return componentHub.GetViewModel<TViewModel>(componentId);
        }

        // 挂载视图到父节点。
        protected void AttachView(Component view, Transform root)
        {
            ViewHost.Attach(view, root);
        }

        // 设置 DataContext 并绑定。
        protected void BindView(UIView view, object viewModel)
        {
            ViewHost.Bind(view, viewModel);
        }

        // 挂载并绑定。
        protected void AttachAndBind(UIView view, Transform root, object viewModel)
        {
            AttachView(view, root);
            BindView(view, viewModel);
        }

        // 直接对 ViewModel 注入 props（绕过 diff）。
        protected void ApplyProps<TProps>(object viewModel, TProps props)
        {
            ComposableComponentHub.ApplyPropsDirect(viewModel, props);
        }

        // 对组件注入 props（自动 diff）。
        protected void ApplyProps<TProps>(string componentId, TProps props)
        {
            componentHub.ApplyProps(componentId, props);
        }

        // 组件事件订阅，统一输出 ComponentEvent。
        protected void TrackComponentEvent<TPayload>(
            string componentId,
            string eventName,
            Action<Action<TPayload>> subscribe,
            Action<Action<TPayload>> unsubscribe)
        {
            if (subscribe == null || unsubscribe == null)
            {
                return;
            }

            Action<TPayload> handler = payload => componentHub.EmitComponentEvent(componentId, eventName, payload);
            componentHub.TrackSubscription(() => subscribe(handler), () => unsubscribe(handler));
        }

        // 组件事件通知扩展点；路由由 CompositionRuntime 统一派发一次。
        protected virtual void OnComponentEvent(ComponentEvent componentEvent)
        {
        }

        // 手动触发组件事件（必要时可直接调用）。
        protected void EmitComponentEvent(string componentId, string eventName, object payload)
        {
            componentHub.EmitComponentEvent(componentId, eventName, payload);
        }

        // 添加事件路由。
        protected void AddEventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
        {
            componentHub.AddEventRoute(componentId, eventName, payloadType, handler);
        }

        // 统一订阅/解绑管理。
        protected void TrackSubscription(Action subscribe, Action unsubscribe)
        {
            componentHub.TrackSubscription(subscribe, unsubscribe);
        }

        // 统一销毁 ViewModel。
        protected void TrackDisposable(IDisposable disposable)
        {
            componentHub.TrackDisposable(disposable);
        }

        // 统一销毁子视图。
        protected void TrackView(Component view)
        {
            if (view == null)
            {
                return;
            }

            componentHub.TrackCleanup(() => ViewHost.Destroy(view));
        }

        protected override void OnDestroy()
        {
            if (isDestroyed)
            {
                return;
            }

            isDestroyed = true;
            componentHub.Dispose();
            base.OnDestroy();
        }

        private void OnHubComponentEventRaised(ComponentEvent componentEvent)
        {
            ComponentEventRaised?.Invoke(componentEvent);
            OnComponentEvent(componentEvent);
        }

        // 旧版批量注册器（保留向后兼容）。
        protected sealed class ComponentRegistryBuilder
        {
            private readonly ComposedWindowBase owner;

            public ComponentRegistryBuilder(ComposedWindowBase owner)
            {
                this.owner = owner;
            }

            public TView Add<TView, TViewModel>(string componentId, string resourcePath, Transform root, TViewModel viewModel)
                where TView : UIView, IView
            {
                return owner.RegisterComponent<TView, TViewModel>(componentId, resourcePath, root, viewModel);
            }

            public TView Add<TView, TViewModel, TProps>(
                string componentId,
                string resourcePath,
                Transform root,
                TViewModel viewModel,
                Func<TProps, TProps, bool> comparer)
                where TView : UIView, IView
            {
                return owner.RegisterComponent<TView, TViewModel, TProps>(componentId, resourcePath, root, viewModel, comparer);
            }
        }

        // UGUI 专用 CompositionBuilder：因为视图需要从 resourcePath 加载、且要附加到 root，所以 Component 签名与 FairyGUI 不同。
        // 该 wrapper 自身不存储任何 UGUI 状态；链式返回的 ComponentBuilder 来自共享 ComposableComponentHub。
        protected sealed class CompositionBuilder
        {
            private readonly ComposedWindowBase owner;

            public CompositionBuilder(ComposedWindowBase owner)
            {
                this.owner = owner;
            }

            public ComposableComponentBuilder<TView, TViewModel> Component<TView, TViewModel>(
                string componentId,
                string resourcePath,
                Transform root,
                TViewModel viewModel)
                where TView : UIView, IView
            {
                owner.RegisterComponent<TView, TViewModel>(componentId, resourcePath, root, viewModel);
                return new ComposableComponentBuilder<TView, TViewModel>(owner.componentHub, componentId, viewModel);
            }
        }
    }
}
