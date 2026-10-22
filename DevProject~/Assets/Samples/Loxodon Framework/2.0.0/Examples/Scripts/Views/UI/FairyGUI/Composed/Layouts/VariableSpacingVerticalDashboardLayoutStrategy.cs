using FairyGUI;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 纵向布局策略：可分别设置顶部/中部/底部间距。
    public sealed class VariableSpacingVerticalDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private readonly float topPadding;
        private readonly float middleSpacing;
        private readonly float bottomSpacing;

        public VariableSpacingVerticalDashboardLayoutStrategy(float topPadding, float middleSpacing, float bottomSpacing)
        {
            this.topPadding = topPadding;
            this.middleSpacing = middleSpacing;
            this.bottomSpacing = bottomSpacing;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (container == null || userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            float y = topPadding;
            LayoutCenter(container, userCard, y);

            y += userCard.height + middleSpacing;
            LayoutCenter(container, counterCard, y);

            y += counterCard.height + bottomSpacing;
            LayoutCenter(container, statusBadge, y);
        }

        // 按容器宽度水平居中。
        private static void LayoutCenter(GComponent container, GObject component, float y)
        {
            float x = (container.width - component.width) * 0.5f;
            component.SetXY(x, y);
        }
    }
}
