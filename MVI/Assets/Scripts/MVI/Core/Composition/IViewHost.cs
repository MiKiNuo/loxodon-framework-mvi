using System;

namespace MVI.Composition
{
    /// <summary>
    /// UI 适配层统一抽象：负责 View 的加载、挂载、绑定与销毁。
    /// </summary>
    public interface IViewHost
    {
        object Load(Type viewType, string resourcePath);

        void Attach(object view, object mountPoint);

        void Bind(object view, object viewModel);

        void Destroy(object view);
    }
}
