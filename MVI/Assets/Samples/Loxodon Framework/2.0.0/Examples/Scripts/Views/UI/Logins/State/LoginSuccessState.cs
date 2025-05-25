namespace Loxodon.Framework.Examples
{
    public record LoginSuccessState : LoginState
    {
        public bool IsInteractionFinished { set; get; }
        public Account Account { set; get; }
    }
}