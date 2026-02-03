namespace MVI
{
    /// <summary>
    /// 通用结果模型，可按需扩展。
    /// </summary>
    public class MviResult : IMviResult
    {
        // 提示信息。
        public string Msg { set; get; }

        // 业务码。
        public int Code { set; get; }

        // 结果数据。
        public object Data { set; get; }
    }
}
