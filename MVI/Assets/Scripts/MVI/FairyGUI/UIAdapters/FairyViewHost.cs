using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FairyGUI;
using MVI.Composition;
using MVI.FairyGUI;
using MVI.FairyGUI.Composed;

namespace MVI.UIAdapters.FairyGUI
{
    /// <summary>
    /// FairyGUI 适配器：统一 Fairy 资源包加载与 View 生命周期管理。
    /// </summary>
    public sealed class FairyViewHost : IViewHost
    {
        private readonly IFairyPackageLoader _packageLoader;

        public FairyViewHost(IFairyPackageLoader packageLoader)
        {
            _packageLoader = packageLoader ?? throw new ArgumentNullException(nameof(packageLoader));
        }

        public ValueTask LoadPackagesAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default)
        {
            if (packagePaths == null || packagePaths.Count == 0)
            {
                return default;
            }

            return _packageLoader.LoadAsync(packagePaths, cancellationToken);
        }

        public object Load(Type viewType, string resourcePath)
        {
            if (viewType == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            var separatorIndex = resourcePath.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex >= resourcePath.Length - 1)
            {
                return null;
            }

            var packageName = resourcePath.Substring(0, separatorIndex);
            var componentName = resourcePath.Substring(separatorIndex + 1);
            var component = UIPackage.CreateObject(packageName, componentName)?.asCom;
            if (component == null)
            {
                return null;
            }

            return viewType.IsInstanceOfType(component) ? component : null;
        }

        public void Attach(object view, object mountPoint)
        {
            if (view is not GObject child)
            {
                return;
            }

            if (mountPoint is GComponent parent)
            {
                parent.AddChild(child);
                return;
            }

            if (mountPoint == null)
            {
                GRoot.inst.AddChild(child);
            }
        }

        public void Bind(object view, object viewModel)
        {
            if (view is IFairyView fairyView)
            {
                fairyView.SetDataContext(viewModel);
            }
        }

        public void Destroy(object view)
        {
            if (view is IFairyView fairyView)
            {
                fairyView.Dispose();
                return;
            }

            if (view is GObject gObject)
            {
                gObject.RemoveFromParent();
                gObject.Dispose();
            }
        }
    }
}
