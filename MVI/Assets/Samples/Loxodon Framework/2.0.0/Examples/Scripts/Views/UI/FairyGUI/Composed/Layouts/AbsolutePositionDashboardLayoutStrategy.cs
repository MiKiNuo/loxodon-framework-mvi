using FairyGUI;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 绝对定位布局：直接使用固定坐标。
    public sealed class AbsolutePositionDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private readonly Vector2 userCardPosition;
        private readonly Vector2 counterCardPosition;
        private readonly Vector2 statusBadgePosition;

        public AbsolutePositionDashboardLayoutStrategy(Vector2 userCardPosition, Vector2 counterCardPosition, Vector2 statusBadgePosition)
        {
            this.userCardPosition = userCardPosition;
            this.counterCardPosition = counterCardPosition;
            this.statusBadgePosition = statusBadgePosition;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            userCard.SetXY(userCardPosition.x, userCardPosition.y);
            counterCard.SetXY(counterCardPosition.x, counterCardPosition.y);
            statusBadge.SetXY(statusBadgePosition.x, statusBadgePosition.y);
        }
    }
}
