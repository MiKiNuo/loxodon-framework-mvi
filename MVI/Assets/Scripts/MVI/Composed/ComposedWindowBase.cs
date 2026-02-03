using System;
using System.Collections.Generic;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Views;
using UnityEngine;
using MVI.Components;

namespace MVI.Composed
{
    // 组合式窗口基类：统一组件注册、props 传递、事件路由与生命周期管理。
    public abstract class ComposedWindowBase : Window
    {
        private sealed class ComponentRegistration
        {
            public ComponentRegistration(string id, UIView view, object viewModel, Func<object, object, bool> propsComparer)
            {
                Id = id;
                View = view;
                ViewModel = viewModel;
                PropsComparer = propsComparer;
            }

            public string Id { get; }
            public UIView View { get; }
            public object ViewModel { get; }
            public object LastProps { get; set; }
            public Func<object, object, bool> PropsComparer { get; set; }
        }

        private sealed class EventRoute
        {
            public EventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
            {
                ComponentId = componentId;
                EventName = eventName;
                PayloadType = payloadType;
                Handler = handler;
            }

            public string ComponentId { get; }
            public string EventName { get; }
            public Type PayloadType { get; }
            public Action<object> Handler { get; }
        }

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
            private readonly TViewModel viewModel;

            public ComponentBuilder(ComposedWindowBase owner, string componentId, TViewModel viewModel)
            {
                this.owner = owner;
                this.componentId = componentId;
                this.viewModel = viewModel;
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

        // View 定位器（ResourcesViewLocator）。
        protected IUIViewLocator ViewLocator { get; private set; }
        private readonly Dictionary<string, ComponentRegistration> registry = new();
        private readonly List<Action> cleanupActions = new();
        private readonly List<EventRoute> eventRoutes = new();
        private bool isDestroyed;

        // 全局组件事件通知（可选订阅）。
        public event Action<ComponentEvent> ComponentEventRaised;

        protected override void OnCreate(IBundle bundle)
        {
            ViewLocator = Context.GetApplicationContext().GetService<IUIViewLocator>();
            OnCompose(bundle);
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
            return ViewLocator.LoadView<TView>(resourcePath);
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
            Func<object, object, bool> objectComparer = null;
            if (comparer != null)
            {
                objectComparer = (previous, next) =>
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

            return RegisterComponentInternal<TView, TViewModel>(componentId, resourcePath, root, viewModel, objectComparer);
        }

        // 设置 props 比较器（会清空上一次 props）。
        protected void SetPropsComparer<TProps>(string componentId, Func<TProps, TProps, bool> comparer)
        {
            if (!registry.TryGetValue(componentId, out var entry))
            {
                return;
            }

            if (comparer == null)
            {
                return;
            }

            entry.LastProps = null;
            entry.PropsComparer = (previous, next) =>
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

            if (registry.TryGetValue(componentId, out var existing))
            {
                return existing.View as TView;
            }

            var view = LoadView<TView>(resourcePath);
            AttachAndBind(view, root, viewModel);
            TrackView(view);
            TrackDisposable(viewModel as IDisposable);
            registry[componentId] = new ComponentRegistration(componentId, view, viewModel, propsComparer);
            return view;
        }

        // 按组件 ID 获取视图。
        protected TView GetView<TView>(string componentId) where TView : UIView
        {
            if (registry.TryGetValue(componentId, out var entry))
            {
                return entry.View as TView;
            }

            return null;
        }

        // 按组件 ID 获取 ViewModel。
        protected TViewModel GetViewModel<TViewModel>(string componentId) where TViewModel : class
        {
            if (registry.TryGetValue(componentId, out var entry))
            {
                return entry.ViewModel as TViewModel;
            }

            return null;
        }

        // 挂载视图到父节点。
        protected void AttachView(Component view, Transform root)
        {
            if (view == null || root == null)
            {
                return;
            }

            view.transform.SetParent(root, false);
            view.gameObject.SetActive(true);
        }

        // 设置 DataContext 并绑定。
        protected void BindView(UIView view, object viewModel)
        {
            if (view == null)
            {
                return;
            }

            view.SetDataContext(viewModel);
            if (view is IViewBinder binder)
            {
                binder.Bind();
            }
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
            if (viewModel is IPropsReceiver<TProps> receiver)
            {
                receiver.SetProps(props);
            }
        }

        // 对组件注入 props（自动 diff）。
        protected void ApplyProps<TProps>(string componentId, TProps props)
        {
            if (!registry.TryGetValue(componentId, out var entry))
            {
                return;
            }

            if (props is IForceUpdateProps forceUpdate && forceUpdate.ForceUpdate)
            {
                ApplyProps(entry.ViewModel, props);
                entry.LastProps = props;
                return;
            }

            if (entry.LastProps != null)
            {
                if (entry.PropsComparer != null)
                {
                    if (entry.PropsComparer(entry.LastProps, props))
                    {
                        return;
                    }
                }
                else if (Equals(entry.LastProps, props))
                {
                    return;
                }
            }

            ApplyProps(entry.ViewModel, props);
            entry.LastProps = props;
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
            DispatchEventRoutes(componentEvent);
        }

        // 手动触发组件事件（必要时可直接调用）。
        protected void EmitComponentEvent(string componentId, string eventName, object payload)
        {
            var componentEvent = new ComponentEvent(componentId, eventName, payload);
            ComponentEventRaised?.Invoke(componentEvent);
            OnComponentEvent(componentEvent);
        }

        // 添加事件路由。
        protected void AddEventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(eventName) || handler == null)
            {
                return;
            }

            eventRoutes.Add(new EventRoute(componentId, eventName, payloadType, handler));
        }

        // 分发事件到路由表。
        private void DispatchEventRoutes(ComponentEvent componentEvent)
        {
            if (componentEvent == null)
            {
                return;
            }

            for (var i = 0; i < eventRoutes.Count; i++)
            {
                var route = eventRoutes[i];
                if (!string.Equals(route.ComponentId, componentEvent.ComponentId)
                    || !string.Equals(route.EventName, componentEvent.EventName))
                {
                    continue;
                }

                if (route.PayloadType != null
                    && componentEvent.Payload != null
                    && !route.PayloadType.IsInstanceOfType(componentEvent.Payload))
                {
                    continue;
                }

                route.Handler(componentEvent.Payload);
            }
        }

        // 统一订阅/解绑管理。
        protected void TrackSubscription(Action subscribe, Action unsubscribe)
        {
            subscribe?.Invoke();
            if (unsubscribe != null)
            {
                cleanupActions.Add(unsubscribe);
            }
        }

        // 统一销毁 ViewModel。
        protected void TrackDisposable(IDisposable disposable)
        {
            if (disposable != null)
            {
                cleanupActions.Add(disposable.Dispose);
            }
        }

        // 统一销毁子视图。
        protected void TrackView(Component view)
        {
            if (view == null)
            {
                return;
            }

            cleanupActions.Add(() =>
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            });
        }

        protected override void OnDestroy()
        {
            if (isDestroyed)
            {
                return;
            }

            isDestroyed = true;
            for (var i = cleanupActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    cleanupActions[i]?.Invoke();
                }
                catch (Exception)
                {
                    // Ignore cleanup errors to avoid masking Unity destroy.
                }
            }

            cleanupActions.Clear();
            registry.Clear();
            eventRoutes.Clear();
            base.OnDestroy();
        }
    }
}
