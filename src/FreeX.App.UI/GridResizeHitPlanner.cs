using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public enum GridResizeHitTarget
{
    None,
    Row,
    Column
}

public readonly record struct GridResizeHit(GridResizeHitTarget Target, uint Index, double CurrentSize);

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

        if (pointer.Y <= columnHeaderHeight)
        {
            var columns = viewport.ColMetrics;
            GridResizeHit? nearestColumnHit = null;
            var nearestColumnDistance = double.MaxValue;
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var rightEdge = column.LeftOffset + column.Width + rowHeaderWidth;
                if (rightEdge - pointer.X > hitZone)
                    break;

                var distance = Math.Abs(pointer.X - rightEdge);
                if (distance <= hitZone && distance < nearestColumnDistance)
                {
                    nearestColumnHit = new GridResizeHit(GridResizeHitTarget.Column, column.Col, column.Width);
                    nearestColumnDistance = distance;
                }
            }

            if (nearestColumnHit is { } hit)
                return hit;
        }

        if (pointer.X <= rowHeaderWidth)
        {
            var rows = viewport.RowMetrics;
            GridResizeHit? nearestRowHit = null;
            var nearestRowDistance = double.MaxValue;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var bottomEdge = row.TopOffset + row.Height + columnHeaderHeight;
                if (bottomEdge - pointer.Y > hitZone)
                    break;

                var distance = Math.Abs(pointer.Y - bottomEdge);
                if (distance <= hitZone && distance < nearestRowDistance)
                {
                    nearestRowHit = new GridResizeHit(GridResizeHitTarget.Row, row.Row, row.Height);
                    nearestRowDistance = distance;
                }
            }

            if (nearestRowHit is { } hit)
                return hit;
        }

        return new GridResizeHit(GridResizeHitTarget.None, 0, 0);
    }
}
