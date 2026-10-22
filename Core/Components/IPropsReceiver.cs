namespace MVI.Components
{
    public interface IPropsReceiver<in TProps>
    {
        // 组件接收外部 props 的统一入口。
        void SetProps(TProps props);
    }
}
