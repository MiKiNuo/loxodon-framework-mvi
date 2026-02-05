using System.Threading;
using System.Threading.Tasks;
using MVI;
using Loxodon.Framework.Examples.Components.UserCard.Store;

namespace Loxodon.Framework.Examples.Components.UserCard.Intent
{
    public interface IUserCardIntent : IIntent
    {
    }

    // 初始化用户信息。
    public sealed class UserInitIntent : IUserCardIntent
    {
        private readonly string userName;
        private readonly int level;

        public UserInitIntent(string userName, int level)
        {
            this.userName = userName;
            this.level = level;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new UserCardResult(userName, level, true);
            return new ValueTask<IMviResult>(result);
        }
    }

    // 设置用户信息。
    public sealed class UserSetIntent : IUserCardIntent
    {
        private readonly string userName;
        private readonly int level;

        public UserSetIntent(string userName, int level)
        {
            this.userName = userName;
            this.level = level;
        }

        public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
        {
            IMviResult result = new UserCardResult(userName, level, false);
            return new ValueTask<IMviResult>(result);
        }
    }
}
