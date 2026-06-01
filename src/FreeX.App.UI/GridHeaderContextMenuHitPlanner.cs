using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct GridHeaderContextMenuHit(GridHeaderContextMenuTarget Target, uint Index);

public static class GridHeaderContextMenuHitPlanner
{
    public static GridHeaderContextMenuHit? HitTest(
        ViewportModel? viewport,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport is null)
            return null;

        if (pointer.Y <= columnHeaderHeight && pointer.X >= rowHeaderWidth)
        {
            foreach (var cm in viewport.ColMetrics)
            {
                var left = cm.LeftOffset + rowHeaderWidth;
                if (pointer.X < left)
                    break;

                if (pointer.X < left + cm.Width)
                    return new GridHeaderContextMenuHit(GridHeaderContextMenuTarget.Column, cm.Col);
            }

            return null;
        }

        if (pointer.X <= rowHeaderWidth && pointer.Y >= columnHeaderHeight)
        {
            foreach (var rm in viewport.RowMetrics)
            {
                var top = rm.TopOffset + columnHeaderHeight;
                if (pointer.Y < top)
                    break;

                if (pointer.Y < top + rm.Height)
                    return new GridHeaderContextMenuHit(GridHeaderContextMenuTarget.Row, rm.Row);
            }
        }

        return null;
    }
}
