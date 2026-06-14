using System;

namespace MVI.Composition
{
    /// <summary>
    /// 通用 View 加载器抽象：把 IUIViewLocator.LoadView&lt;T&gt;(string) 暴露为非反射的统一接口。
    /// 每个 UI 适配层（UGUI/FairyGUI 等）提供自己的 IViewLoader 实现，避免运行时反射。
    /// </summary>
    public interface IViewLoader
    {
        /// <summary>
        /// 按资源路径加载指定类型的 View。
        /// </summary>
        /// <param name="viewType">目标 View 类型，必须能被具体 Loader 识别。</param>
        /// <param name="resourcePath">资源定位符（资源路径、URL、Fairy 包键等）。</param>
        /// <returns>加载得到的 View 实例；加载失败时返回 null。</returns>
        object Load(Type viewType, string resourcePath);
    }

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
