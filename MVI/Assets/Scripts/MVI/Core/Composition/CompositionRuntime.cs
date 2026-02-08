using System;
using System.Collections.Generic;
using MVI.Components;

namespace MVI.Composition
{
    /// <summary>
    /// 组合运行时：统一管理组件注册、props diff、事件路由与清理动作。
    /// </summary>
    public sealed class CompositionRuntime : IDisposable
    {
        private sealed class ComponentRegistration
        {
            public ComponentRegistration(string id, object view, object viewModel, Func<object, object, bool> propsComparer)
            {
                Id = id;
                View = view;
                ViewModel = viewModel;
                PropsComparer = propsComparer;
            }

            public string Id { get; }

            public object View { get; }

            public object ViewModel { get; }

            public object LastProps { get; set; }

            public Func<object, object, bool> PropsComparer { get; set; }
        }

        private sealed class EventRoute
        {
            public EventRoute(Type payloadType, Action<object> handler)
            {
                PayloadType = payloadType;
                Handler = handler;
            }

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

        private readonly Dictionary<string, ComponentRegistration> _registry = new(StringComparer.Ordinal);
        private readonly List<Action> _cleanupActions = new();
        private readonly Dictionary<EventRouteKey, List<EventRoute>> _eventRoutes = new();
        private bool _disposed;

        public event Action<ComponentEvent> ComponentEventRaised;

        public bool TryRegisterComponent(string componentId, object view, object viewModel, Func<object, object, bool> propsComparer = null)
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("componentId is required.", nameof(componentId));
            }

            if (_registry.ContainsKey(componentId))
            {
                return false;
            }

            _registry[componentId] = new ComponentRegistration(componentId, view, viewModel, propsComparer);
            return true;
        }

        public TView GetView<TView>(string componentId) where TView : class
        {
            if (!_registry.TryGetValue(componentId, out var entry))
            {
                return null;
            }

            return entry.View as TView;
        }

        public TViewModel GetViewModel<TViewModel>(string componentId) where TViewModel : class
        {
            if (!_registry.TryGetValue(componentId, out var entry))
            {
                return null;
            }

            return entry.ViewModel as TViewModel;
        }

        public bool HasComponent(string componentId)
        {
            return !string.IsNullOrWhiteSpace(componentId) && _registry.ContainsKey(componentId);
        }

        public void SetPropsComparer<TProps>(string componentId, Func<TProps, TProps, bool> comparer)
        {
            if (comparer == null || !_registry.TryGetValue(componentId, out var entry))
            {
                return;
            }

            entry.LastProps = null;
            entry.PropsComparer = WrapPropsComparer(comparer);
        }

        public void ApplyProps<TProps>(string componentId, TProps props)
        {
            if (!_registry.TryGetValue(componentId, out var entry))
            {
                return;
            }

            if (props is IForceUpdateProps forceUpdate && forceUpdate.ForceUpdate)
            {
                ApplyPropsDirect(entry.ViewModel, props);
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

            ApplyPropsDirect(entry.ViewModel, props);
            entry.LastProps = props;
        }

        public static void ApplyPropsDirect<TProps>(object viewModel, TProps props)
        {
            if (viewModel is IPropsReceiver<TProps> receiver)
            {
                receiver.SetProps(props);
            }
        }

        public void AddEventRoute(string componentId, string eventName, Type payloadType, Action<object> handler)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(eventName) || handler == null)
            {
                return;
            }

            var key = new EventRouteKey(componentId, eventName);
            if (!_eventRoutes.TryGetValue(key, out var routes))
            {
                routes = new List<EventRoute>();
                _eventRoutes[key] = routes;
            }

            routes.Add(new EventRoute(payloadType, handler));
        }

        public void EmitComponentEvent(string componentId, string eventName, object payload)
        {
            var componentEvent = new ComponentEvent(componentId, eventName, payload);
            ComponentEventRaised?.Invoke(componentEvent);
        }

        public void DispatchEventRoutes(ComponentEvent componentEvent)
        {
            if (componentEvent == null)
            {
                return;
            }

            var key = new EventRouteKey(componentEvent.ComponentId, componentEvent.EventName);
            if (!_eventRoutes.TryGetValue(key, out var routes))
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

        public void TrackSubscription(Action subscribe, Action unsubscribe)
        {
            subscribe?.Invoke();
            if (unsubscribe != null)
            {
                _cleanupActions.Add(unsubscribe);
            }
        }

        public void TrackDisposable(IDisposable disposable)
        {
            if (disposable != null)
            {
                _cleanupActions.Add(disposable.Dispose);
            }
        }

        public void TrackCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                _cleanupActions.Add(cleanup);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var i = _cleanupActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _cleanupActions[i]?.Invoke();
                }
                catch (Exception)
                {
                    // Ignore cleanup errors to avoid masking Unity destroy.
                }
            }

            _cleanupActions.Clear();
            _registry.Clear();
            _eventRoutes.Clear();
            ComponentEventRaised = null;
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
    }
}
