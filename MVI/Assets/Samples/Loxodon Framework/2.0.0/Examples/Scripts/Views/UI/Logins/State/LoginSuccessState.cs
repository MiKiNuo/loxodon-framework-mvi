namespace Loxodon.Framework.Examples
{
    public record LoginSuccessState : LoginState
    {
        public Account Account { set; get; }
    }
}
