using System;
using System.Linq;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Views;
using MVI.Components;
using MVI.Composition;
using UnityEngine;

namespace MVI.UIAdapters.UGUI
{
    /// <summary>
    /// UGUI 适配器：负责 UGUI View 的加载、挂载、绑定与销毁。
    /// </summary>
    public sealed class UguiViewHost : IViewHost
    {
        private readonly IUIViewLocator _viewLocator;

        public UguiViewHost(IUIViewLocator viewLocator)
        {
            _viewLocator = viewLocator ?? throw new ArgumentNullException(nameof(viewLocator));
        }

        public object Load(Type viewType, string resourcePath)
        {
            if (viewType == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            var loadMethod = _viewLocator
                .GetType()
                .GetMethods()
                .FirstOrDefault(method =>
                    method.Name == "LoadView"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(string));

            if (loadMethod == null)
            {
                throw new MissingMethodException($"{_viewLocator.GetType().Name}.LoadView<T>(string) not found.");
            }

            var genericLoad = loadMethod.MakeGenericMethod(viewType);
            return genericLoad.Invoke(_viewLocator, new object[] { resourcePath });
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
}
