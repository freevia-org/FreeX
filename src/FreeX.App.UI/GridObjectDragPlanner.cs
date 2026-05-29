using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public enum ObjectDragKind { None, Move, ResizeSE, ResizeE, ResizeS }

public static class GridObjectDragPlanner
{
    public static Rect CalculateDragRect(
        ObjectDragKind dragKind,
        Rect startRect,
        Point startPosition,
        Point currentPosition,
        double minimumSize = 8)
    {
        var dx = currentPosition.X - startPosition.X;
        var dy = currentPosition.Y - startPosition.Y;
        return dragKind switch
        {
            ObjectDragKind.Move => new Rect(
                startRect.X + dx,
                startRect.Y + dy,
                startRect.Width,
                startRect.Height),
            ObjectDragKind.ResizeSE => new Rect(
                startRect.X,
                startRect.Y,
                Math.Max(minimumSize, startRect.Width + dx),
                Math.Max(minimumSize, startRect.Height + dy)),
            ObjectDragKind.ResizeE => new Rect(
                startRect.X,
                startRect.Y,
                Math.Max(minimumSize, startRect.Width + dx),
                startRect.Height),
            ObjectDragKind.ResizeS => new Rect(
                startRect.X,
                startRect.Y,
                startRect.Width,
                Math.Max(minimumSize, startRect.Height + dy)),
            _ => startRect
        };
    }

    public static ObjectDragKind HitTestHandle(
        Point position,
        Rect objectRect,
        double handleSize = 8,
        double handleHitPadding = 4)
    {
        if (objectRect.IsEmpty)
            return ObjectDragKind.None;

        var pad = handleHitPadding + handleSize / 2;
        var nearRight = Math.Abs(position.X - objectRect.Right) <= pad;
        var nearBottom = Math.Abs(position.Y - objectRect.Bottom) <= pad;
        var inVertical = position.Y >= objectRect.Top - pad && position.Y <= objectRect.Bottom + pad;
        var inHorizontal = position.X >= objectRect.Left - pad && position.X <= objectRect.Right + pad;

        if (nearRight && nearBottom) return ObjectDragKind.ResizeSE;
        if (nearRight && inVertical) return ObjectDragKind.ResizeE;
        if (nearBottom && inHorizontal) return ObjectDragKind.ResizeS;
        if (objectRect.Contains(position)) return ObjectDragKind.Move;
        return ObjectDragKind.None;
    }

    public static CellAddress? HitTestAnchorCell(
        ViewportModel? viewport,
        Point position,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport is null)
            return null;

        if (position.X < rowHeaderWidth || position.Y < columnHeaderHeight)
            return null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            var divider = CalculateSplitDividerLayout(viewport, rowHeaderWidth, columnHeaderHeight);
            var topRows = splitPanes.TopRows ?? [];
            var leftColumns = splitPanes.LeftColumns ?? [];
            var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
            var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
            var isTop = divider.HorizontalY.HasValue && position.Y < divider.HorizontalY.Value;
            var isLeft = divider.VerticalX.HasValue && position.X < divider.VerticalX.Value;

            var rows = (isTop, isLeft) switch
            {
                (true, _) => topRows,
                (false, true) => bottomLeftRows,
                _ => viewport.RowMetrics
            };
            var columns = (isTop, isLeft) switch
            {
                (_, true) => leftColumns,
                (true, false) => topRightColumns,
                _ => viewport.ColMetrics
            };
            var rowOrigin = !isTop && divider.HorizontalY.HasValue
                ? divider.HorizontalY.Value
                : columnHeaderHeight;
            var columnOrigin = !isLeft && divider.VerticalX.HasValue
                ? divider.VerticalX.Value
                : rowHeaderWidth;

            return HitTestMetrics(rows, columns, position, rowOrigin, columnOrigin);
        }

        return HitTestMetrics(viewport.RowMetrics, viewport.ColMetrics, position, columnHeaderHeight, rowHeaderWidth);
    }

    private static CellAddress? HitTestMetrics(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns,
        Point position,
        double rowOrigin,
        double columnOrigin)
    {
        foreach (var row in rows)
        {
            var top = row.TopOffset + rowOrigin;
            if (position.Y < top)
                break;

            if (position.Y >= top + row.Height)
                continue;

            foreach (var column in columns)
            {
                var left = column.LeftOffset + columnOrigin;
                if (position.X < left)
                    break;

                if (position.X < left + column.Width)
                    return new CellAddress(default, row.Row, column.Col);
            }
        }

        return null;
    }

    private static (double? HorizontalY, double? VerticalX) CalculateSplitDividerLayout(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return (null, null);

        double? horizontalY = null;
        if (splitPanes.Row.HasValue)
        {
            var pinnedRows = splitPanes.TopRows ?? [];
            horizontalY = pinnedRows.Count > 0
                ? columnHeaderHeight + pinnedRows.Sum(row => row.Height)
                : FindRowMetric(viewport.RowMetrics, splitPanes.Row.Value)?.TopOffset + columnHeaderHeight;
        }

        double? verticalX = null;
        if (splitPanes.Column.HasValue)
        {
            var pinnedColumns = splitPanes.LeftColumns ?? [];
            verticalX = pinnedColumns.Count > 0
                ? rowHeaderWidth + pinnedColumns.Sum(column => column.Width)
                : FindColMetric(viewport.ColMetrics, splitPanes.Column.Value)?.LeftOffset + rowHeaderWidth;
        }

        return (horizontalY, verticalX);
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row)
    {
        foreach (var metric in metrics)
        {
            if (metric.Row == row)
                return metric;
        }

        return null;
    }

    private static ColMetric? FindColMetric(IReadOnlyList<ColMetric> metrics, uint column)
    {
        foreach (var metric in metrics)
        {
            if (metric.Col == column)
                return metric;
        }

        return null;
    }
}
