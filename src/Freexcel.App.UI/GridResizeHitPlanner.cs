using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public enum GridResizeHitTarget
{
    None,
    Row,
    Column
}

public sealed record GridResizeHit(GridResizeHitTarget Target, uint Index, double CurrentSize);

public static class GridResizeHitPlanner
{
    public static GridResizeHit HitTest(
        ViewportModel? viewport,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double hitZone)
    {
        if (viewport is null)
            return new GridResizeHit(GridResizeHitTarget.None, 0, 0);

        if (pointer.Y < columnHeaderHeight)
        {
            foreach (var column in viewport.ColMetrics)
            {
                var rightEdge = column.LeftOffset + column.Width + rowHeaderWidth;
                if (Math.Abs(pointer.X - rightEdge) <= hitZone)
                    return new GridResizeHit(GridResizeHitTarget.Column, column.Col, column.Width);
            }
        }

        if (pointer.X < rowHeaderWidth)
        {
            foreach (var row in viewport.RowMetrics)
            {
                var bottomEdge = row.TopOffset + row.Height + columnHeaderHeight;
                if (Math.Abs(pointer.Y - bottomEdge) <= hitZone)
                    return new GridResizeHit(GridResizeHitTarget.Row, row.Row, row.Height);
            }
        }

        return new GridResizeHit(GridResizeHitTarget.None, 0, 0);
    }
}
