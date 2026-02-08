using System;
using MVI.Components;

namespace MVI.FairyGUI.Composed
{
    // FairyGUI 组件视图通用接口：提供绑定入口与释放能力。
    public interface IFairyView : IViewBinder, IDisposable
    {
        // 设置 DataContext 并触发绑定。
        void SetDataContext(object viewModel);
    }
}
