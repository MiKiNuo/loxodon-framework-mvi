using System;

namespace MVI
{
    /// <summary>
    /// 标准化错误 Effect，用于统一错误上报与 UI 提示。
    /// </summary>
    public sealed class MviErrorEffect : IMviEffect
    {
        public MviErrorEffect(Exception exception, string source = null)
        {
            Exception = exception;
            Source = source;
        }

        public Exception Exception { get; }

        public string Source { get; }

        public string Message => Exception?.Message;
    }
}
