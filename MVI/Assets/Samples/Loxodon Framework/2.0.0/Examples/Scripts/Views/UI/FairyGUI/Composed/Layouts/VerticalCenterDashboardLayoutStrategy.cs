using FairyGUI;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 简单垂直排列布局策略：顶部留白 + 固定间距 + 水平居中。
    public sealed class VerticalCenterDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private readonly float topPadding;
        private readonly float verticalSpacing;

        public VerticalCenterDashboardLayoutStrategy(float topPadding = 24f, float verticalSpacing = 16f)
        {
            this.topPadding = topPadding;
            this.verticalSpacing = verticalSpacing;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (container == null || userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            float currentY = topPadding;
            LayoutCenter(container, userCard, currentY);
            currentY += userCard.height + verticalSpacing;
            LayoutCenter(container, counterCard, currentY);
            currentY += counterCard.height + verticalSpacing;
            LayoutCenter(container, statusBadge, currentY);
        }

        // 按容器宽度水平居中。
        private static void LayoutCenter(GComponent container, GObject component, float y)
        {
            float x = (container.width - component.width) * 0.5f;
            component.SetXY(x, y);
        }
    }
}
