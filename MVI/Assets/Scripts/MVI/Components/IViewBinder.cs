namespace MVI.Components
{
    public interface IViewBinder
    {
        // 组件视图的绑定入口，负责建立 View 与 ViewModel 的数据绑定。
        void Bind();
    }
}
