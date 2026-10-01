using System;
using System.Reflection;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Views;
using MVI.Components;
using MVI.Composition;
using UnityEngine;

namespace MVI.UIAdapters.UGUI
{
    /// <summary>
    /// UGUI 适配器：负责 UGUI View 的加载、挂载、绑定与销毁。
    /// 通过显式 <see cref="IUIViewLocator"/> 委托加载，整个加载链路经
    /// <see cref="UIViewLoaderDispatcher"/> 的类型擦除桥完成，
    /// 桥接仅在初始化阶段通过 <see cref="Delegate.CreateDelegate(Type, MethodInfo)"/>
    /// 一次性绑定静态泛型方法，运行时无反射。
    /// </summary>
    public sealed class UguiViewHost : IViewHost
    {
        private readonly IUIViewLocator _viewLocator;

        public UguiViewHost(IUIViewLocator viewLocator)
        {
            _viewLocator = viewLocator ?? throw new ArgumentNullException(nameof(viewLocator));
        }

        public TView Load<TView>(string resourcePath) where TView : class
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            // UIViewLoaderDispatcher 内部已校验 UIView 派生关系并做类型擦除派发。
            return UIViewLoaderDispatcher.Load(_viewLocator, typeof(TView), resourcePath) as TView;
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
