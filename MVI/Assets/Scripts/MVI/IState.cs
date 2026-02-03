namespace MVI
{
    public interface IState
    {
        // 是否强制更新状态（true 表示即使相同也触发更新）。
        bool IsUpdateNewState { set; get; }
    }
}
