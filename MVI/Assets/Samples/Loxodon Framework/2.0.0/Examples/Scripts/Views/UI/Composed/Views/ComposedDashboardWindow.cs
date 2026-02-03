using Loxodon.Framework.Views;
using UnityEngine;
using MVI.Composed;
using Loxodon.Framework.Examples.Components.CounterCard.ViewModels;
using Loxodon.Framework.Examples.Components.CounterCard.Views;
using Loxodon.Framework.Examples.Components.UserCard.State;
using Loxodon.Framework.Examples.Components.UserCard.ViewModels;
using Loxodon.Framework.Examples.Components.UserCard.Views;
using Loxodon.Framework.Examples.Components.StatusBadge.ViewModels;
using Loxodon.Framework.Examples.Components.StatusBadge.Views;
using MVI.Composed;

namespace Loxodon.Framework.Examples.Composed.Views
{
    // 组合式 Demo 页面：演示组件注册、props 传递与事件路由。
    public class ComposedDashboardWindow : ComposedWindowBase
    {
        public Transform userCardRoot;
        public Transform counterCardRoot;
        public Transform statusBadgeRoot;

        private UserCardViewModel userCardViewModel;
        private CounterCardViewModel counterCardViewModel;
        private StatusBadgeViewModel statusBadgeViewModel;

        private const string UserCardId = "UserCard";
        private const string CounterCardId = "CounterCard";
        private const string StatusBadgeId = "StatusBadge";

        // 组合入口：声明式注册组件与事件路由。
        protected override void OnCompose(IBundle bundle)
        {
            userCardViewModel = new UserCardViewModel();
            counterCardViewModel = new CounterCardViewModel();
            statusBadgeViewModel = new StatusBadgeViewModel();

            Compose(composition =>
            {
                composition.Component<UserCardView, UserCardViewModel>(
                        UserCardId, "UI/Components/UserCard", userCardRoot, userCardViewModel)
                    .WithProps(new UserCardProps("Alice", 3))
                    .On<UserCardState>(
                        "Selected",
                        OnUserSelected,
                        handler => userCardViewModel.Selected += handler,
                        handler => userCardViewModel.Selected -= handler);

                composition.Component<CounterCardView, CounterCardViewModel>(
                        CounterCardId, "UI/Components/CounterCard", counterCardRoot, counterCardViewModel)
                    .WithProps(new CounterCardProps("Clicks", 0), CounterPropsComparer)
                    .On<int>(
                        "CountChanged",
                        OnCounterChanged,
                        handler => counterCardViewModel.CountChanged += handler,
                        handler => counterCardViewModel.CountChanged -= handler);

                composition.Component<StatusBadgeView, StatusBadgeViewModel>(
                        StatusBadgeId, "UI/Components/StatusBadge", statusBadgeRoot, statusBadgeViewModel)
                    .WithProps(new StatusBadgeProps("Ready", true));
            });
        }

        // 用户选中后联动其它组件。
        private void OnUserSelected(UserCardState userState)
        {
            var counterVm = GetViewModel<CounterCardViewModel>(CounterCardId);
            var currentCount = counterVm?.Count ?? 0;
            ApplyProps(CounterCardId, new CounterCardProps($"Hello, {userState.UserName}", currentCount));
            ApplyProps(StatusBadgeId, new StatusBadgeProps($"User: {userState.UserName} Lv.{userState.Level}"));
        }

        // 计数变化后更新状态徽标。
        private void OnCounterChanged(int count)
        {
            ApplyProps(StatusBadgeId, new StatusBadgeProps($"Counter: {count}"));
        }

        // 自定义 props 比较器（用于演示）。
        private static bool CounterPropsComparer(CounterCardProps left, CounterCardProps right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.Count == right.Count && string.Equals(left.Label, right.Label);
        }
    }
}
