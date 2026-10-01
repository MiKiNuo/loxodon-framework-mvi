using System;
using MVI.Components;

namespace MVI.Composition
{
    /// <summary>
    /// 组合式组件中枢：把 UGUI 与 FairyGUI 宿主
    /// 之间复用的"组件注册/查询/props diff/事件路由/订阅清理"职责集中到一个协作器中。
    /// </summary>
    /// <remarks>
    /// 设计动机：
    /// <list type="bullet">
    /// <item>两个基类以前各自实现一份 ComponentBuilder / EventRouteBuilder / WrapPropsComparer / TrackSubscription / TrackDisposable 等逻辑，几乎完全一致；抽出本类后两边共用。</item>
    /// <item>该类不依赖任何 UI 框架接口（UView/IFairyView），纯运行时职责，可以独立单测。</item>
    /// <item>宿主类只需要持有一个 <see cref="ComposableComponentHub"/> 字段并委托组件注册与事件路由。</item>
    /// </list>
    /// </remarks>
    public sealed class ComposableComponentHub : IDisposable
    {
        private readonly CompositionRuntime _runtime = new();
        private bool _disposed;

        /// <summary>
        /// 全局组件事件（<see cref="EmitComponentEvent"/> 触发后由该事件透出）。
        /// </summary>
        public event Action<ComponentEvent> ComponentEventRaised;

        public ComposableComponentHub()
        {
            _runtime.ComponentEventRaised += OnRuntimeComponentEventRaised;
        }

        public bool HasComponent(string componentId) => _runtime.HasComponent(componentId);

        public TView GetView<TView>(string componentId) where TView : class
        {
            return _runtime.GetView<TView>(componentId);
        }

        public TViewModel GetViewModel<TViewModel>(string componentId) where TViewModel : class
        {
            return _runtime.GetViewModel<TViewModel>(componentId);
        }

        public bool TryRegisterComponent(string componentId, object view, object viewModel, Func<object, object, bool> propsComparer = null)
        {
            return _runtime.TryRegisterComponent(componentId, view, viewModel, propsComparer);
        }

        public void SetPropsComparer<TProps>(string componentId, Func<TProps, TProps, bool> comparer)
        {
            _runtime.SetPropsComparer(componentId, comparer);
        }

        public void ApplyProps<TProps>(string componentId, TProps props)
        {
            _runtime.ApplyProps(componentId, props);
        }

        /// <summary>
        /// 绕过 diff 直接对 ViewModel 注入 props（用于一次性 Props 接收器）。
        /// </summary>
        public static void ApplyPropsDirect<TProps>(object viewModel, TProps props)
        {
            CompositionRuntime.ApplyPropsDirect(viewModel, props);
        }

        public void AddEventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
        {
            _runtime.AddEventRoute(componentId, eventName, payloadType, handler);
        }

        public void EmitComponentEvent(string componentId, string eventName, object payload)
        {
            _runtime.EmitComponentEvent(componentId, eventName, payload);
        }

        public void DispatchEventRoutes(ComponentEvent componentEvent)
        {
            _runtime.DispatchEventRoutes(componentEvent);
        }

        public void TrackSubscription(Action subscribe, Action unsubscribe)
        {
            _runtime.TrackSubscription(subscribe, unsubscribe);
        }

        public void TrackDisposable(IDisposable disposable)
        {
            _runtime.TrackDisposable(disposable);
        }

        public void TrackCleanup(Action cleanup)
        {
            _runtime.TrackCleanup(cleanup);
        }

        /// <summary>
        /// 把组件事件转发到 <see cref="ComponentEventRaised"/>：宿主可在该事件基础上再叠加 <c>OnComponentEvent</c> 等扩展点。
        /// </summary>
        private void OnRuntimeComponentEventRaised(ComponentEvent componentEvent)
        {
            ComponentEventRaised?.Invoke(componentEvent);
        }

        /// <summary>
        /// 泛型比较器到 object-object 比较器的统一包装。
        /// </summary>
        public static Func<object, object, bool> WrapPropsComparer<TProps>(Func<TProps, TProps, bool> comparer)
        {
            return CompositionRuntime.WrapPropsComparer(comparer);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runtime.Dispose();
        }
    }

    /// <summary>
    /// 链式 Component 配置：WithProps/CompareProps/On 等方法统一走 <see cref="ComposableComponentHub"/>。
    /// </summary>
    public sealed class ComposableComponentBuilder<TView, TViewModel>
    {
        private readonly ComposableComponentHub _hub;
        private readonly string _componentId;

        public ComposableComponentBuilder(ComposableComponentHub hub, string componentId, TViewModel viewModel)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _componentId = componentId ?? throw new ArgumentNullException(nameof(componentId));
        }

        // 注入 props，走自动 diff。
        public ComposableComponentBuilder<TView, TViewModel> WithProps<TProps>(TProps props)
        {
            _hub.ApplyProps(_componentId, props);
            return this;
        }

        // 注入 props，并指定自定义比较器。
        public ComposableComponentBuilder<TView, TViewModel> WithProps<TProps>(TProps props, Func<TProps, TProps, bool> comparer)
        {
            if (comparer != null)
            {
                _hub.SetPropsComparer(_componentId, comparer);
            }

            _hub.ApplyProps(_componentId, props);
            return this;
        }

        // 仅设置 props 比较器（不立即注入）。
        public ComposableComponentBuilder<TView, TViewModel> CompareProps<TProps>(Func<TProps, TProps, bool> comparer)
        {
            if (comparer != null)
            {
                _hub.SetPropsComparer(_componentId, comparer);
            }

            return this;
        }

        // 统一事件输出与订阅管理。
        public ComposableComponentBuilder<TView, TViewModel> On<TPayload>(
            string eventName,
            Action<TPayload> handler,
            Action<Action<TPayload>> subscribe,
            Action<Action<TPayload>> unsubscribe)
        {
            if (handler != null)
            {
                _hub.AddEventRoute(_componentId, eventName, typeof(TPayload), payload =>
                {
                    if (payload is TPayload typed)
                    {
                        handler(typed);
                    }
                });
            }

            if (subscribe != null && unsubscribe != null)
            {
                Action<TPayload> emission = payload => _hub.EmitComponentEvent(_componentId, eventName, payload);
                _hub.TrackSubscription(() => subscribe(emission), () => unsubscribe(emission));
            }

            return this;
        }
    }

    /// <summary>
    /// 事件路由 Builder：把外部 handler 接到 <see cref="ComposableComponentHub"/> 的事件路由表上。
    /// </summary>
    public sealed class ComposableEventRouteBuilder
    {
        private readonly ComposableComponentHub _hub;

        public ComposableEventRouteBuilder(ComposableComponentHub hub)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        }

        public void On<TPayload>(string componentId, string eventName, Action<TPayload> handler)
        {
            if (handler == null)
            {
                return;
            }

            _hub.AddEventRoute(componentId, eventName, typeof(TPayload), payload =>
            {
                if (payload is TPayload typed)
                {
                    handler(typed);
                }
            });
        }
    }
}
