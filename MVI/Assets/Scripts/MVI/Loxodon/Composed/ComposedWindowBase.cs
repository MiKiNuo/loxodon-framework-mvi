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
    // 组合式窗口基类：统一组件注册、props 传递、事件路由与生命周期管理。
    public abstract class ComposedWindowBase : Window
    {
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

        protected sealed class CompositionBuilder
        {
            private readonly ComposedWindowBase owner;

            public CompositionBuilder(ComposedWindowBase owner)
            {
                this.owner = owner;
            }

            // 声明式注册组件，返回链式 builder。
            public ComponentBuilder<TView, TViewModel> Component<TView, TViewModel>(
                string componentId,
                string resourcePath,
                Transform root,
                TViewModel viewModel)
                where TView : UIView, IView
            {
                owner.RegisterComponent<TView, TViewModel>(componentId, resourcePath, root, viewModel);
                return new ComponentBuilder<TView, TViewModel>(owner, componentId, viewModel);
            }
        }

        protected sealed class ComponentBuilder<TView, TViewModel>
            where TView : UIView, IView
        {
            private readonly ComposedWindowBase owner;
            private readonly string componentId;

            public ComponentBuilder(ComposedWindowBase owner, string componentId, TViewModel viewModel)
            {
                this.owner = owner;
                this.componentId = componentId;
            }

            // 注入 props，并走自动 diff。
            public ComponentBuilder<TView, TViewModel> WithProps<TProps>(TProps props)
            {
                owner.ApplyProps(componentId, props);
                return this;
            }

            // 注入 props，并指定自定义比较器。
            public ComponentBuilder<TView, TViewModel> WithProps<TProps>(TProps props, Func<TProps, TProps, bool> comparer)
            {
                if (comparer != null)
                {
                    owner.SetPropsComparer(componentId, comparer);
                }

                owner.ApplyProps(componentId, props);
                return this;
            }

            // 仅设置 props 比较器（不立即注入）。
            public ComponentBuilder<TView, TViewModel> CompareProps<TProps>(Func<TProps, TProps, bool> comparer)
            {
                if (comparer != null)
                {
                    owner.SetPropsComparer(componentId, comparer);
                }

                return this;
            }

            // 统一事件输出与订阅管理。
            public ComponentBuilder<TView, TViewModel> On<TPayload>(
                string eventName,
                Action<TPayload> handler,
                Action<Action<TPayload>> subscribe,
                Action<Action<TPayload>> unsubscribe)
            {
                if (handler != null)
                {
                    owner.AddEventRoute(componentId, eventName, typeof(TPayload), payload =>
                    {
                        if (payload is TPayload typed)
                        {
                            handler(typed);
                        }
                    });
                }

                owner.TrackComponentEvent(componentId, eventName, subscribe, unsubscribe);
                return this;
            }
        }

        protected sealed class EventRouteBuilder
        {
            private readonly ComposedWindowBase owner;

            public EventRouteBuilder(ComposedWindowBase owner)
            {
                this.owner = owner;
            }

            public void On<TPayload>(string componentId, string eventName, Action<TPayload> handler)
            {
                if (handler == null)
                {
                    return;
                }

                owner.AddEventRoute(componentId, eventName, typeof(TPayload), payload =>
                {
                    if (payload is TPayload typed)
                    {
                        handler(typed);
                    }
                });
            }
        }

        private readonly CompositionRuntime composition = new();
        private bool isDestroyed;

        // View 定位器（ResourcesViewLocator）。
        protected IUIViewLocator ViewLocator { get; private set; }

        // UGUI 适配器。
        protected IViewHost ViewHost { get; private set; }

        // 全局组件事件通知（可选订阅）。
        public event Action<ComponentEvent> ComponentEventRaised;

        protected ComposedWindowBase()
        {
            composition.ComponentEventRaised += OnRuntimeComponentEventRaised;
        }

        protected override void OnCreate(IBundle bundle)
        {
            ViewLocator = Context.GetApplicationContext().GetService<IUIViewLocator>();
            ViewHost = CreateViewHost();
            OnCompose(bundle);
        }

        protected virtual IViewHost CreateViewHost()
        {
            return new UguiViewHost(ViewLocator);
        }

        protected abstract void OnCompose(IBundle bundle);

        // 组合式 DSL 入口。
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

        // 兼容：批量注册事件路由。
        protected void RegisterEventRoutes(Action<EventRouteBuilder> configure)
        {
            if (configure == null)
            {
                return;
            }

            var builder = new EventRouteBuilder(this);
            configure(builder);
        }

        // 加载视图。
        protected TView LoadView<TView>(string resourcePath) where TView : UIView, IView
        {
            return ViewHost.Load(typeof(TView), resourcePath) as TView;
        }

        // 注册组件并绑定。
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
                comparer == null ? null : WrapPropsComparer(comparer));
        }

        // 设置 props 比较器（会清空上一次 props）。
        protected void SetPropsComparer<TProps>(string componentId, Func<TProps, TProps, bool> comparer)
        {
            composition.SetPropsComparer(componentId, comparer);
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

            if (composition.HasComponent(componentId))
            {
                return composition.GetView<TView>(componentId);
            }

            var view = LoadView<TView>(resourcePath);
            AttachAndBind(view, root, viewModel);
            TrackView(view);
            TrackDisposable(viewModel as IDisposable);
            composition.TryRegisterComponent(componentId, view, viewModel, propsComparer);
            return view;
        }

        // 按组件 ID 获取视图。
        protected TView GetView<TView>(string componentId) where TView : class
        {
            return composition.GetView<TView>(componentId);
        }

        // 按组件 ID 获取 ViewModel。
        protected TViewModel GetViewModel<TViewModel>(string componentId) where TViewModel : class
        {
            return composition.GetViewModel<TViewModel>(componentId);
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
            CompositionRuntime.ApplyPropsDirect(viewModel, props);
        }

        // 对组件注入 props（自动 diff）。
        protected void ApplyProps<TProps>(string componentId, TProps props)
        {
            composition.ApplyProps(componentId, props);
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

            Action<TPayload> handler = payload => EmitComponentEvent(componentId, eventName, payload);
            TrackSubscription(() => subscribe(handler), () => unsubscribe(handler));
        }

        // 事件统一入口，默认走路由表。
        protected virtual void OnComponentEvent(ComponentEvent componentEvent)
        {
            composition.DispatchEventRoutes(componentEvent);
        }

        // 手动触发组件事件（必要时可直接调用）。
        protected void EmitComponentEvent(string componentId, string eventName, object payload)
        {
            composition.EmitComponentEvent(componentId, eventName, payload);
        }

        // 添加事件路由。
        protected void AddEventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
        {
            composition.AddEventRoute(componentId, eventName, payloadType, handler);
        }

        // 统一订阅/解绑管理。
        protected void TrackSubscription(Action subscribe, Action unsubscribe)
        {
            composition.TrackSubscription(subscribe, unsubscribe);
        }

        // 统一销毁 ViewModel。
        protected void TrackDisposable(IDisposable disposable)
        {
            composition.TrackDisposable(disposable);
        }

        // 统一销毁子视图。
        protected void TrackView(Component view)
        {
            if (view == null)
            {
                return;
            }

            composition.TrackCleanup(() => ViewHost.Destroy(view));
        }

        protected override void OnDestroy()
        {
            if (isDestroyed)
            {
                return;
            }

            isDestroyed = true;
            composition.Dispose();
            base.OnDestroy();
        }

        private static Func<object, object, bool> WrapPropsComparer<TProps>(Func<TProps, TProps, bool> comparer)
        {
            return (previous, next) =>
            {
                if (ReferenceEquals(previous, next))
                {
                    return true;
                }

                if (previous == null || next == null)
                {
                    return false;
                }

                if (previous is TProps prevProps && next is TProps nextProps)
                {
                    return comparer(prevProps, nextProps);
                }

                return Equals(previous, next);
            };
        }

        private void OnRuntimeComponentEventRaised(ComponentEvent componentEvent)
        {
            ComponentEventRaised?.Invoke(componentEvent);
            OnComponentEvent(componentEvent);
        }
    }
}
