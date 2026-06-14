using System;
using Loxodon.Framework.Views;
using MVI.Components;
using MVI.Composition;
using UnityEngine;

namespace MVI.UIAdapters.UGUI
{
    /// <summary>
    /// UGUI 适配器：负责 UGUI View 的加载、挂载、绑定与销毁。
    /// 通过显式 IViewLoader 委托加载，移除原先的反射调用。
    /// </summary>
    public sealed class UguiViewHost : IViewHost
    {
        private readonly IViewLoader _viewLoader;

        public UguiViewHost(IViewLoader viewLoader)
        {
            _viewLoader = viewLoader ?? throw new ArgumentNullException(nameof(viewLoader));
        }

        /// <summary>
        /// 兼容旧调用：传入 IUIViewLocator 时自动包装为反射式 IViewLoader（仅用于过渡）。
        /// 业务侧应改为直接注入 IViewLoader。
        /// </summary>
        [Obsolete("Use UguiViewHost(IViewLoader) directly. The IUIViewLocator path uses reflection and will be removed.")]
        public UguiViewHost(object viewLocator)
        {
            if (viewLocator == null)
            {
                throw new ArgumentNullException(nameof(viewLocator));
            }

            _viewLoader = new ReflectionFallbackViewLoader(viewLocator);
        }

        public object Load(Type viewType, string resourcePath)
        {
            if (viewType == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            return _viewLoader.Load(viewType, resourcePath);
        }

        public void Attach(object view, object mountPoint)
        {
            if (view is not Component component || mountPoint is not Transform root)
            {
                return;
            }

            component.transform.SetParent(root, false);
            component.gameObject.SetActive(true);
        }

        public void Bind(object view, object viewModel)
        {
            if (view is not UIView uiView)
            {
                return;
            }

            uiView.SetDataContext(viewModel);
            if (view is IViewBinder binder)
            {
                binder.Bind();
            }
        }

        public void Destroy(object view)
        {
            if (view is Component component)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }
        }
    }

    /// <summary>
    /// 反射式 IViewLoader 兜底实现：仅用于旧 IUIViewLocator 调用路径，新业务应直接实现 IViewLoader。
    /// 该类型属于过渡实现，保留是为了不立即破坏公开 API；后续版本会彻底移除反射。
    /// </summary>
    internal sealed class ReflectionFallbackViewLoader : IViewLoader
    {
        private readonly object _viewLocator;
        private readonly System.Reflection.MethodInfo _loadMethod;

        public ReflectionFallbackViewLoader(object viewLocator)
        {
            _viewLocator = viewLocator;
            _loadMethod = FindLoadViewMethod(viewLocator?.GetType());
        }

        public object Load(Type viewType, string resourcePath)
        {
            if (_loadMethod == null || _viewLocator == null || viewType == null)
            {
                return null;
            }

            try
            {
                var genericLoad = _loadMethod.MakeGenericMethod(viewType);
                return genericLoad.Invoke(_viewLocator, new object[] { resourcePath });
            }
            catch
            {
                return null;
            }
        }

        private static System.Reflection.MethodInfo FindLoadViewMethod(Type locatorType)
        {
            if (locatorType == null)
            {
                return null;
            }

            // 反射仅在构造时发生一次（缓存到 _loadMethod），运行期 Load 走缓存后的泛型方法绑定。
            return System.Linq.Enumerable.FirstOrDefault(
                locatorType.GetMethods(),
                method =>
                    method.Name == "LoadView"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(string));
        }
    }
}
