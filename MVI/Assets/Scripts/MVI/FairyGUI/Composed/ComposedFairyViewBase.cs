using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FairyGUI;
using MVI.Components;
using UnityEngine;

namespace MVI.FairyGUI.Composed
{
    // FairyGUI 组合式 View 基类：负责资源加载、组件注册与事件路由。
    public abstract class ComposedFairyViewBase : MonoBehaviour
    {
        [Header("FairyGUI 资源加载")]
        [SerializeField] private string[] packagePaths;
        [SerializeField] private string packageName;
        [SerializeField] private string componentName;
        [SerializeField] private bool preferUIPanel = true;
        [SerializeField] private bool addToGRoot = true;
        [SerializeField] private bool autoCreateView = true;

        private sealed class ComponentRegistration
        {
            public ComponentRegistration(string id, IFairyView view, object viewModel, Func<object, object, bool> propsComparer)
            {
                Id = id;
                View = view;
                ViewModel = viewModel;
                PropsComparer = propsComparer;
            }

            public string Id { get; }
            public IFairyView View { get; }
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

        private readonly struct EventRouteKey : IEquatable<EventRouteKey>
        {
            public EventRouteKey(string componentId, string eventName)
            {
                ComponentId = componentId ?? string.Empty;
                EventName = eventName ?? string.Empty;
            }

            public string ComponentId { get; }
            public string EventName { get; }

            public bool Equals(EventRouteKey other)
            {
                return string.Equals(ComponentId, other.ComponentId, StringComparison.Ordinal)
                    && string.Equals(EventName, other.EventName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is EventRouteKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + ComponentId.GetHashCode();
                    hash = (hash * 31) + EventName.GetHashCode();
                    return hash;
                }
            }
        }

        protected sealed class CompositionBuilder
        {
            private readonly ComposedFairyViewBase owner;

            public CompositionBuilder(ComposedFairyViewBase owner)
            {
                this.owner = owner;
            }

            // 声明式注册组件，返回链式 builder。
            public ComponentBuilder<TView, TViewModel> Component<TView, TViewModel>(
                string componentId,
                TView view,
                TViewModel viewModel)
                where TView : class, IFairyView
            {
                owner.RegisterComponent<TView, TViewModel>(componentId, view, viewModel);
                return new ComponentBuilder<TView, TViewModel>(owner, componentId, viewModel);
            }
        }

        protected sealed class ComponentBuilder<TView, TViewModel>
            where TView : class, IFairyView
        {
            private readonly ComposedFairyViewBase owner;
            private readonly string componentId;
            private readonly TViewModel viewModel;

            public ComponentBuilder(ComposedFairyViewBase owner, string componentId, TViewModel viewModel)
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
            private readonly ComposedFairyViewBase owner;

            public EventRouteBuilder(ComposedFairyViewBase owner)
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

        // FairyGUI 面板组件，负责承载 GComponent 根节点。
        protected UIPanel Panel { get; private set; }
        // FairyGUI 根组件（可能来自 UIPanel 或 GRoot）。
        protected GComponent Root => root;

        // 资源路径（Resources 下的路径），可由子类覆写。
        protected virtual string[] PackagePaths => packagePaths;
        // 包名（用于 CreateObject），可由子类覆写。
        protected virtual string PackageName => packageName;
        // 组件名（用于 CreateObject），可由子类覆写。
        protected virtual string ComponentName => componentName;
        // 是否优先使用 UIPanel（Inspector 里配置的组件）。
        protected virtual bool PreferUIPanel => preferUIPanel;
        // 非 UIPanel 模式时是否自动加入到 GRoot。
        protected virtual bool AddToGRoot => addToGRoot;
        // 是否自动创建并初始化 View。
        protected virtual bool AutoCreateView => autoCreateView;
        // 自定义包加载器（默认使用 Resources 模式）。
        protected virtual IFairyPackageLoader PackageLoader => packageLoader ??= new ResourcesPackageLoader();

        private readonly CancellationTokenSource viewCts = new();
        private GComponent root;
        private bool isComposed;
        private IFairyPackageLoader packageLoader;

        private readonly Dictionary<string, ComponentRegistration> registry = new();
        private readonly List<Action> cleanupActions = new();
        private readonly Dictionary<EventRouteKey, List<EventRoute>> eventRoutes = new();

        // 全局组件事件通知（可选订阅）。
        public event Action<ComponentEvent> ComponentEventRaised;

        protected virtual void Awake()
        {
            // 缓存 UIPanel，避免重复 GetComponent。
            Panel = GetComponent<UIPanel>();
        }

        protected virtual void Start()
        {
            if (!AutoCreateView)
            {
                return;
            }

            // 异步加载资源包，再创建视图。
            StartCoroutine(LoadAndCreateView());
        }

        // View 就绪后回调（子类可在此缓存组件引用）。
        protected virtual void OnViewReady(GComponent root)
        {
        }

        // 资源包加载失败回调（子类可覆写做 UI 提示）。
        protected virtual void OnPackageLoadFailed(Exception exception)
        {
            Debug.LogException(exception);
        }

        // 组合式入口（子类在此注册组件与路由）。
        protected abstract void OnCompose();

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

        // 注册组件并绑定。
        protected TView RegisterComponent<TView, TViewModel>(string componentId, TView view, TViewModel viewModel)
            where TView : class, IFairyView
        {
            return RegisterComponentInternal(componentId, view, viewModel, null);
        }

        // 注册组件并设置自定义 props 比较器。
        protected TView RegisterComponent<TView, TViewModel, TProps>(
            string componentId,
            TView view,
            TViewModel viewModel,
            Func<TProps, TProps, bool> comparer)
            where TView : class, IFairyView
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

            return RegisterComponentInternal(componentId, view, viewModel, objectComparer);
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
            TView view,
            TViewModel viewModel,
            Func<object, object, bool> propsComparer)
            where TView : class, IFairyView
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("componentId is required.");
            }

            if (registry.TryGetValue(componentId, out var existing))
            {
                // 使用 is 模式匹配，避免泛型约束导致的 as 编译错误。
                return existing.View is TView typed ? typed : null;
            }

            BindView(view, viewModel);
            TrackDisposable(view);
            TrackDisposable(viewModel as IDisposable);
            registry[componentId] = new ComponentRegistration(componentId, view, viewModel, propsComparer);
            return view;
        }

        // 按组件 ID 获取视图。
        protected TView GetView<TView>(string componentId) where TView : class, IFairyView
        {
            if (registry.TryGetValue(componentId, out var entry))
            {
                // 使用 is 模式匹配，避免泛型约束导致的 as 编译错误。
                return entry.View is TView typed ? typed : null;
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

        // 设置 DataContext 并绑定。
        protected void BindView(IFairyView view, object viewModel)
        {
            if (view == null)
            {
                return;
            }

            view.SetDataContext(viewModel);
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

            var key = new EventRouteKey(componentId, eventName);
            if (!eventRoutes.TryGetValue(key, out var routes))
            {
                routes = new List<EventRoute>();
                eventRoutes[key] = routes;
            }

            routes.Add(new EventRoute(componentId, eventName, payloadType, handler));
        }

        // 分发事件到路由表。
        private void DispatchEventRoutes(ComponentEvent componentEvent)
        {
            if (componentEvent == null)
            {
                return;
            }

            var key = new EventRouteKey(componentEvent.ComponentId, componentEvent.EventName);
            if (!eventRoutes.TryGetValue(key, out var routes))
            {
                return;
            }

            for (var i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
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

        // 统一销毁 ViewModel 或 View。
        protected void TrackDisposable(IDisposable disposable)
        {
            if (disposable != null)
            {
                cleanupActions.Add(disposable.Dispose);
            }
        }

        // 设置自定义包加载器（例如接入 YooAsset 时注入）。
        protected void SetPackageLoader(IFairyPackageLoader loader)
        {
            packageLoader = loader;
        }

        // 异步加载 FairyGUI 资源包（默认走 Resources）。
        protected virtual ValueTask LoadPackagesAsync(CancellationToken cancellationToken = default)
        {
            var paths = PackagePaths;
            if (paths == null || paths.Length == 0)
            {
                return default;
            }

            return PackageLoader.LoadAsync(paths, cancellationToken);
        }

        private IEnumerator LoadAndCreateView()
        {
            var task = LoadPackagesAsync(viewCts.Token).AsTask();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                OnPackageLoadFailed(task.Exception);
                yield break;
            }

            if (viewCts.IsCancellationRequested)
            {
                yield break;
            }

            // 创建或获取根组件（UIPanel 或 GRoot）。
            var createdRoot = EnsureRoot();
            if (createdRoot != null)
            {
                root = createdRoot;
                OnViewReady(root);
                if (!isComposed)
                {
                    isComposed = true;
                    OnCompose();
                }
            }
        }

        // 创建或获取 FairyGUI 根组件。
        protected virtual GComponent EnsureRoot()
        {
            if (PreferUIPanel && Panel != null)
            {
                ConfigurePanel();
                return Panel.ui;
            }

            if (root != null)
            {
                return root;
            }

            if (string.IsNullOrWhiteSpace(PackageName) || string.IsNullOrWhiteSpace(ComponentName))
            {
                Debug.LogWarning($"{nameof(ComposedFairyViewBase)} 未配置 PackageName/ComponentName，无法创建视图。");
                return null;
            }

            root = UIPackage.CreateObject(PackageName, ComponentName).asCom;
            if (root != null && AddToGRoot)
            {
                GRoot.inst.AddChild(root);
            }

            return root;
        }

        // 同步 Inspector 配置到 UIPanel（如果已有配置则保持不动）。
        private void ConfigurePanel()
        {
            if (Panel == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(PackageName))
            {
                Panel.packageName = PackageName;
            }

            if (!string.IsNullOrWhiteSpace(ComponentName))
            {
                Panel.componentName = ComponentName;
            }
        }

        protected virtual void OnDestroy()
        {
            if (!viewCts.IsCancellationRequested)
            {
                viewCts.Cancel();
            }
            viewCts.Dispose();

            for (var i = cleanupActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    cleanupActions[i]?.Invoke();
                }
                catch (Exception)
                {
                    // 忽略销毁异常，避免影响 Unity Destroy。
                }
            }

            cleanupActions.Clear();
            registry.Clear();
            eventRoutes.Clear();

            if (Panel == null && root != null)
            {
                root.RemoveFromParent();
                root.Dispose();
                root = null;
            }
        }

        // 默认的 Resources 包加载器（与 FairyGUI Demo 一致）。
        private sealed class ResourcesPackageLoader : IFairyPackageLoader
        {
            public ValueTask LoadAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default)
            {
                if (packagePaths == null)
                {
                    return default;
                }

                foreach (var path in packagePaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    // FairyGUI 会内部处理重复加载的情况。
                    UIPackage.AddPackage(path);
                }

                return default;
            }
        }
    }
}
