using System;
using Loxodon.Framework.Commands;
using MVI;
using MVI.Components;
using Loxodon.Framework.Examples.Components.UserCard.Intent;
using Loxodon.Framework.Examples.Components.UserCard.State;
using Loxodon.Framework.Examples.Components.UserCard.Store;

namespace Loxodon.Framework.Examples.Components.UserCard.ViewModels
{
    // 用户卡片 ViewModel：接收 props / 发出 Selected 事件。
    public sealed class UserCardViewModel : MviViewModel, IPropsReceiver<UserCardProps>
    {
        private string userName;
        private int level;
        private readonly SimpleCommand selectCommand;

        public UserCardViewModel()
        {
            selectCommand = new SimpleCommand(OnSelect);
            BindStore(new UserCardStore());
            EmitIntent(new UserInitIntent("Guest", 1));
        }

        // 用户名。
        public string UserName
        {
            get => userName;
            set => Set(ref userName, value);
        }

        // 等级。
        public int Level
        {
            get => level;
            set => Set(ref level, value);
        }

        // 点击事件绑定命令。
        public ICommand SelectCommand => selectCommand;

        // 选中事件（用于父组件联动）。
        public event Action<UserCardState> Selected;

        public void SetUser(string newUserName, int newLevel)
        {
            EmitIntent(new UserSetIntent(newUserName, newLevel));
        }

        // 统一 props 入口。
        public void SetProps(UserCardProps props)
        {
            if (props == null)
            {
                return;
            }

            EmitIntent(new UserSetIntent(props.UserName, props.Level));
        }

        private void OnSelect()
        {
            Selected?.Invoke(new UserCardState(UserName ?? string.Empty, Level));
        }
    }
}
