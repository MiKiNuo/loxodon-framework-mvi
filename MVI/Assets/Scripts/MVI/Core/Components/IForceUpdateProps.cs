namespace MVI.Components
{
    public interface IForceUpdateProps
    {
        // true 时跳过 props diff，强制更新。
        bool ForceUpdate { get; }
    }
}
