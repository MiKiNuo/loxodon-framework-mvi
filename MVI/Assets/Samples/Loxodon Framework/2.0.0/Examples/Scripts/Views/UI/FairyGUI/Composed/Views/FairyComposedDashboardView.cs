using System;
using ComposedDashboardWindow;
using FairyGUI;
using Loxodon.Framework.Examples.Components.CounterCard.ViewModels;
using Loxodon.Framework.Examples.Components.StatusBadge.ViewModels;
using Loxodon.Framework.Examples.Components.UserCard.State;
using Loxodon.Framework.Examples.Components.UserCard.ViewModels;
using MVI.Examples.FairyGUI.Composed.Layouts;
using MVI.FairyGUI.Utils;
using MVI.Examples.FairyGUI.Composed.Components.CounterCard.Views;
using MVI.Examples.FairyGUI.Composed.Components.StatusBadge.Views;
using MVI.Examples.FairyGUI.Composed.Components.UserCard.Views;
using MVI.FairyGUI.Composed;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Views
{
    // FairyGUI 版本的组合式 Dashboard（组件化方式）。
    public sealed class FairyComposedDashboardView : ComposedFairyViewBase
    {
        // 布局策略类型。
        public enum LayoutStrategyKind
        {
            VerticalCenter = 0,
            LeftRightSplit = 1,
            VariableSpacingVertical = 2,
            Grid = 3,
            AbsolutePosition = 4,
            PlaceholderAlign = 5
        }

        private const string UserCardId = "UserCard";
        private const string CounterCardId = "CounterCard";
        private const string StatusBadgeId = "StatusBadge";

        // 包加载由 Launcher 负责，这里不再重复加载。
        private static readonly string[] PackagePathList = Array.Empty<string>();

        private UIComposedDashboard dashboard;
        private FairyCounterCardView counterCardView;
        private FairyUserCardView userCardView;
        private FairyStatusBadgeView statusBadgeView;
        private UICounterCard counterCardComponent;
        private UIUserCard userCardComponent;
        private UIStatusBadge statusBadgeComponent;
        private IFairyComposedDashboardLayoutStrategy layoutStrategy;

        private UserCardViewModel userCardViewModel;
        private CounterCardViewModel counterCardViewModel;
        private StatusBadgeViewModel statusBadgeViewModel;
        private bool composed;

        [Header("布局配置")]
        [SerializeField] private LayoutStrategyKind layoutKind = LayoutStrategyKind.VerticalCenter;
        [SerializeField] private float layoutTopPadding = 24f;
        [SerializeField] private float layoutVerticalSpacing = 16f;

        [Header("布局配置-左右分栏")]
        [SerializeField, Range(0.1f, 0.9f)] private float splitLeftWidthRatio = 0.45f;
        [SerializeField] private float splitGap = 16f;

        [Header("布局配置-可变间距")]
        [SerializeField] private float variableMiddleSpacing = 16f;
        [SerializeField] private float variableBottomSpacing = 16f;

        [Header("布局配置-网格")]
        [SerializeField] private int gridRows = 2;
        [SerializeField] private int gridColumns = 2;
        [SerializeField] private float gridPaddingX = 24f;
        [SerializeField] private float gridPaddingY = 24f;
        [SerializeField] private float gridSpacingX = 16f;
        [SerializeField] private float gridSpacingY = 16f;

        [Header("布局配置-绝对坐标")]
        [SerializeField] private Vector2 userCardPosition = new Vector2(24f, 24f);
        [SerializeField] private Vector2 counterCardPosition = new Vector2(24f, 200f);
        [SerializeField] private Vector2 statusBadgePosition = new Vector2(24f, 360f);

        [Header("布局配置-占位对齐")]
        [SerializeField] private string userCardPlaceholderName = "UserCardPlaceholder";
        [SerializeField] private string counterCardPlaceholderName = "CounterCardPlaceholder";
        [SerializeField] private string statusBadgePlaceholderName = "StatusBadgePlaceholder";
        [SerializeField] private Vector2 userCardPlaceholderOffset = Vector2.zero;
        [SerializeField] private Vector2 counterCardPlaceholderOffset = Vector2.zero;
        [SerializeField] private Vector2 statusBadgePlaceholderOffset = Vector2.zero;

        // 指定包名与主组件名。
        protected override string[] PackagePaths => FairyComposedDashboardView.PackagePathList;
        protected override string PackageName => "ComposedDashboardWindow";
        protected override string ComponentName => "ComposedDashboard";
        protected override bool PreferUIPanel => false;

        protected override void OnViewReady(GComponent root)
        {
            // 让根节点铺满屏幕。
            root.MakeFullScreen();
            root.AddRelation(GRoot.inst, RelationType.Size);

            // 优先使用自动生成的 UIComposedDashboard。
            dashboard = root as UIComposedDashboard
                ?? FairyGuiViewHelper.FindByUrl<UIComposedDashboard>(root, UIComposedDashboard.URL);

            if (dashboard == null)
            {
                Debug.LogWarning("FairyComposedDashboardView 未找到 UIComposedDashboard，请检查包发布与绑定器注册。");
                return;
            }

            // 直接创建组件并添加到根容器（不使用 GLoader）。
            userCardComponent = UIUserCard.CreateInstance();
            counterCardComponent = UICounterCard.CreateInstance();
            statusBadgeComponent = UIStatusBadge.CreateInstance();

            // 确保组件可交互。
            userCardComponent.touchable = true;
            userCardComponent.rootContainer.touchChildren = true;
            counterCardComponent.touchable = true;
            counterCardComponent.rootContainer.touchChildren = true;
            statusBadgeComponent.touchable = true;
            statusBadgeComponent.rootContainer.touchChildren = true;

            dashboard.AddChild(userCardComponent);
            dashboard.AddChild(counterCardComponent);
            dashboard.AddChild(statusBadgeComponent);

            // 使用布局策略进行排列，避免 View 过于臃肿。
            RebuildLayoutStrategy();
            dashboard.onSizeChanged.Add(ApplyLayout);

            // 创建绑定 View。
            userCardView = new FairyUserCardView(userCardComponent);
            counterCardView = new FairyCounterCardView(counterCardComponent);
            statusBadgeView = new FairyStatusBadgeView(statusBadgeComponent);

        }

        // 组合式入口：注册组件、props 与事件路由。
        protected override void OnCompose()
        {
            if (dashboard == null)
            {
                Debug.LogWarning("FairyComposedDashboardView 组合式初始化失败：根组件为空。");
                return;
            }

            if (composed)
            {
                return;
            }

            composed = true;
            // 创建子组件的 ViewModel，并通过组合式 DSL 绑定到 View。
            userCardViewModel = new UserCardViewModel();
            counterCardViewModel = new CounterCardViewModel();
            statusBadgeViewModel = new StatusBadgeViewModel();

            Compose(composition =>
            {
                composition.Component<FairyUserCardView, UserCardViewModel>(
                        UserCardId, userCardView, userCardViewModel)
                    .WithProps(new UserCardProps("Alice", 3))
                    .On<UserCardState>(
                        FairyComposedEvents.UserSelected,
                        OnUserSelected,
                        handler => userCardViewModel.Selected += handler,
                        handler => userCardViewModel.Selected -= handler);

                composition.Component<FairyCounterCardView, CounterCardViewModel>(
                        CounterCardId, counterCardView, counterCardViewModel)
                    .WithProps(new CounterCardProps("Clicks", 0), CounterPropsComparer)
                    .On<int>(
                        FairyComposedEvents.CounterChanged,
                        OnCounterChanged,
                        handler => counterCardViewModel.CountChanged += handler,
                        handler => counterCardViewModel.CountChanged -= handler);

                composition.Component<FairyStatusBadgeView, StatusBadgeViewModel>(
                        StatusBadgeId, statusBadgeView, statusBadgeViewModel)
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

        // 创建布局策略（策略模式入口）。
        private IFairyComposedDashboardLayoutStrategy CreateLayoutStrategy()
        {
            switch (layoutKind)
            {
                case LayoutStrategyKind.VerticalCenter:
                default:
                    return new VerticalCenterDashboardLayoutStrategy(layoutTopPadding, layoutVerticalSpacing);
                case LayoutStrategyKind.LeftRightSplit:
                    return new LeftRightSplitDashboardLayoutStrategy(splitLeftWidthRatio, splitGap, layoutTopPadding, layoutVerticalSpacing);
                case LayoutStrategyKind.VariableSpacingVertical:
                    return new VariableSpacingVerticalDashboardLayoutStrategy(layoutTopPadding, variableMiddleSpacing, variableBottomSpacing);
                case LayoutStrategyKind.Grid:
                    return new GridDashboardLayoutStrategy(gridRows, gridColumns, gridPaddingX, gridPaddingY, gridSpacingX, gridSpacingY);
                case LayoutStrategyKind.AbsolutePosition:
                    return new AbsolutePositionDashboardLayoutStrategy(userCardPosition, counterCardPosition, statusBadgePosition);
                case LayoutStrategyKind.PlaceholderAlign:
                    return new PlaceholderAlignDashboardLayoutStrategy(
                        userCardPlaceholderName,
                        counterCardPlaceholderName,
                        statusBadgePlaceholderName,
                        userCardPlaceholderOffset,
                        counterCardPlaceholderOffset,
                        statusBadgePlaceholderOffset);
            }
        }

        // 执行布局。
        private void ApplyLayout()
        {
            if (layoutStrategy == null)
            {
                layoutStrategy = CreateLayoutStrategy();
            }

            layoutStrategy?.Apply(dashboard, userCardComponent, counterCardComponent, statusBadgeComponent);
        }

        // 运行时切换布局策略（供外部调用）。
        public void SwitchLayout(LayoutStrategyKind kind)
        {
            layoutKind = kind;
            RebuildLayoutStrategy();
        }

        // 运行时切换布局策略（供 UI 绑定 int 调用）。
        public void SwitchLayout(int kind)
        {
            if (Enum.IsDefined(typeof(LayoutStrategyKind), kind))
            {
                SwitchLayout((LayoutStrategyKind)kind);
            }
        }

        // 注入自定义布局策略（策略模式扩展点）。
        public void SetCustomLayoutStrategy(IFairyComposedDashboardLayoutStrategy strategy)
        {
            layoutStrategy = strategy;
            ApplyLayout();
        }

        // 强制刷新布局。
        public void RefreshLayout()
        {
            ApplyLayout();
        }

        // 重新创建布局策略并应用（适用于运行时修改参数）。
        public void RebuildLayout()
        {
            RebuildLayoutStrategy();
        }

        // 根据当前配置重建策略并执行布局。
        private void RebuildLayoutStrategy()
        {
            layoutStrategy = CreateLayoutStrategy();
            ApplyLayout();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                RebuildLayoutStrategy();
            }
        }

    }
}
