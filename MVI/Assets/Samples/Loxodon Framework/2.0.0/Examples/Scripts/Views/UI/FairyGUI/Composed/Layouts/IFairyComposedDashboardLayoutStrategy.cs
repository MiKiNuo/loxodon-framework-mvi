using FairyGUI;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 组合式 Dashboard 布局策略接口。
    public interface IFairyComposedDashboardLayoutStrategy
    {
        // 执行布局。
        void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge);
    }
}
