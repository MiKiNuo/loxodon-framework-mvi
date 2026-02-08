using FairyGUI;
using MVI.FairyGUI.Utils;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 占位对齐布局：根据占位节点对齐组件位置。
    public sealed class PlaceholderAlignDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private static readonly string[] UserCardFallbackNames =
        {
            "UserCardPlaceholder",
            "UserCardSlot",
            "UserCardAnchor",
            "UserCard"
        };

        private static readonly string[] CounterCardFallbackNames =
        {
            "CounterCardPlaceholder",
            "CounterCardSlot",                                            
            "CounterCardAnchor",
            "CounterCard"
        };

        private static readonly string[] StatusBadgeFallbackNames =
        {
            "StatusBadgePlaceholder",
            "StatusBadgeSlot",
            "StatusBadgeAnchor",
            "StatusBadge"
        };

        private readonly string userCardPlaceholder;
        private readonly string counterCardPlaceholder;
        private readonly string statusBadgePlaceholder;
        private readonly Vector2 userCardOffset;
        private readonly Vector2 counterCardOffset;
        private readonly Vector2 statusBadgeOffset;

        public PlaceholderAlignDashboardLayoutStrategy(
            string userCardPlaceholder,
            string counterCardPlaceholder,
            string statusBadgePlaceholder,
            Vector2 userCardOffset,
            Vector2 counterCardOffset,
            Vector2 statusBadgeOffset)
        {
            this.userCardPlaceholder = userCardPlaceholder;
            this.counterCardPlaceholder = counterCardPlaceholder;
            this.statusBadgePlaceholder = statusBadgePlaceholder;
            this.userCardOffset = userCardOffset;
            this.counterCardOffset = counterCardOffset;
            this.statusBadgeOffset = statusBadgeOffset;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (container == null || userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            AlignByPlaceholder(container, ResolvePlaceholder(container, userCardPlaceholder, UserCardFallbackNames), userCard, userCardOffset);
            AlignByPlaceholder(container, ResolvePlaceholder(container, counterCardPlaceholder, CounterCardFallbackNames), counterCard, counterCardOffset);
            AlignByPlaceholder(container, ResolvePlaceholder(container, statusBadgePlaceholder, StatusBadgeFallbackNames), statusBadge, statusBadgeOffset);
        }

        private static string ResolvePlaceholder(GComponent container, string placeholderName, string[] fallbackNames)
        {
            if (!string.IsNullOrWhiteSpace(placeholderName))
            {
                if (FairyGuiViewHelper.FindByName(container, placeholderName) != null)
                {
                    return placeholderName;
                }
            }

            foreach (var name in fallbackNames)
            {
                if (FairyGuiViewHelper.FindByName(container, name) != null)
                {
                    return name;
                }
            }

            return null;
        }

        private static void AlignByPlaceholder(GComponent container, string placeholderName, GObject target, Vector2 offset)
        {
            if (string.IsNullOrWhiteSpace(placeholderName))
            {
                return;
            }

            var placeholder = FairyGuiViewHelper.FindByName(container, placeholderName);
            if (placeholder == null)
            {
                return;
            }

            target.SetXY(placeholder.x + offset.x, placeholder.y + offset.y);
        }
    }
}
