namespace MVI
{
    public class MviResult : IMviResult
    {
        public string Msg { set; get; }
        public int Code { set; get; }
        public object Data { set; get; }
    }
}