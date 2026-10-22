using MVI;

namespace Loxodon.Framework.Examples
{
    public abstract class LoginEffect : IMviEffect
    {
    }

    public sealed class ShowToastEffect : LoginEffect
    {
        public ShowToastEffect(string message, float duration = 2f)
        {
            Message = message;
            Duration = duration;
        }

        public string Message { get; }

        public float Duration { get; }
    }

    public sealed class FinishLoginEffect : LoginEffect
    {
    }
}
