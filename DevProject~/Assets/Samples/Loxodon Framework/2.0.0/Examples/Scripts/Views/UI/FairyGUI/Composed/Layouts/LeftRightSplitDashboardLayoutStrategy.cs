using FairyGUI;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 左右分栏布局：左侧 UserCard，右侧 Counter + Status 纵向排列。
    public sealed class LeftRightSplitDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private readonly float leftWidthRatio;
        private readonly float splitGap;
        private readonly float topPadding;
        private readonly float verticalSpacing;

        public LeftRightSplitDashboardLayoutStrategy(
            float leftWidthRatio = 0.45f,
            float splitGap = 16f,
            float topPadding = 24f,
            float verticalSpacing = 16f)
        {
            this.leftWidthRatio = leftWidthRatio;
            this.splitGap = splitGap;
            this.topPadding = topPadding;
            this.verticalSpacing = verticalSpacing;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (container == null || userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            float leftWidth = container.width * leftWidthRatio;
            float rightX = leftWidth + splitGap;
            float rightWidth = container.width - rightX;

            float leftX = (leftWidth - userCard.width) * 0.5f;
            userCard.SetXY(leftX, topPadding);

            float rightTop = topPadding;
            float counterX = rightX + (rightWidth - counterCard.width) * 0.5f;
            counterCard.SetXY(counterX, rightTop);

            float statusY = rightTop + counterCard.height + verticalSpacing;
            float statusX = rightX + (rightWidth - statusBadge.width) * 0.5f;
            statusBadge.SetXY(statusX, statusY);
        }
    }
}
