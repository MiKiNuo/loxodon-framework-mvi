using FairyGUI;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Composed.Layouts
{
    // 网格布局策略：支持自定义行列数与间距。
    public sealed class GridDashboardLayoutStrategy : IFairyComposedDashboardLayoutStrategy
    {
        private readonly int rows;
        private readonly int columns;
        private readonly float paddingX;
        private readonly float paddingY;
        private readonly float spacingX;
        private readonly float spacingY;

        public GridDashboardLayoutStrategy(
            int rows,
            int columns,
            float paddingX = 24f,
            float paddingY = 24f,
            float spacingX = 16f,
            float spacingY = 16f)
        {
            this.rows = Mathf.Max(1, rows);
            this.columns = Mathf.Max(1, columns);
            this.paddingX = paddingX;
            this.paddingY = paddingY;
            this.spacingX = spacingX;
            this.spacingY = spacingY;
        }

        public void Apply(GComponent container, GObject userCard, GObject counterCard, GObject statusBadge)
        {
            if (container == null || userCard == null || counterCard == null || statusBadge == null)
            {
                return;
            }

            var items = new[] { userCard, counterCard, statusBadge };
            int totalCells = rows * columns;
            int count = Mathf.Min(items.Length, totalCells);

            float cellWidth = (container.width - paddingX * 2 - spacingX * (columns - 1)) / columns;
            float cellHeight = (container.height - paddingY * 2 - spacingY * (rows - 1)) / rows;
            cellWidth = Mathf.Max(0, cellWidth);
            cellHeight = Mathf.Max(0, cellHeight);

            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                float cellX = paddingX + col * (cellWidth + spacingX);
                float cellY = paddingY + row * (cellHeight + spacingY);
                LayoutInCell(items[i], cellX, cellY, cellWidth, cellHeight);
            }
        }

        // 将组件居中放入单元格。
        private static void LayoutInCell(GObject component, float cellX, float cellY, float cellWidth, float cellHeight)
        {
            float x = cellX + (cellWidth - component.width) * 0.5f;
            float y = cellY + (cellHeight - component.height) * 0.5f;
            component.SetXY(x, y);
        }
    }
}
