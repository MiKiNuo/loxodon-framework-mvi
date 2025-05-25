namespace MVI
{

    public interface IState
    {
        /// <summary>
        /// 是否更新最新的状态
        /// </summary>
        bool IsUpdateNewState { set; get; }
    }
}