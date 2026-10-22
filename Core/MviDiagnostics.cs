using System;

namespace MVI
{
    /// <summary>
    /// 轻量诊断入口，用于追踪 Intent/Result/State/Effect 流程。
    /// </summary>
    public static class MviDiagnostics
    {
        public static bool Enabled { get; set; }

        public static Action<string> Log { get; set; }

        public static void Trace(string message)
        {
            if (!Enabled)
            {
                return;
            }

            Log?.Invoke(message);
        }
    }
}
