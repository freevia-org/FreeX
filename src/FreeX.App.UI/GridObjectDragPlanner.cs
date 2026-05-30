using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public enum ObjectDragKind
{
    None,
    Move,
    ResizeNW,
    ResizeN,
    ResizeNE,
    ResizeE,
    ResizeSE,
    ResizeS,
    ResizeSW,
    ResizeW,
    Rotate
}

public static class GridObjectDragPlanner
{
    public const double MinimumObjectSize = 8;

    /// <summary>
    /// Vertical distance (in pixels) of the rotation grip's center above the top edge of the object.
    /// </summary>
    public const double RotationGripOffset = 20;

    public static Rect CalculateDragRect(
        ObjectDragKind dragKind,
        Rect startRect,
        Point startPosition,
        Point currentPosition,
        double minimumSize = MinimumObjectSize)
    {
        var dx = currentPosition.X - startPosition.X;
        var dy = currentPosition.Y - startPosition.Y;

        // Track each edge independently so opposite edges stay fixed and the rect
        // never inverts when an edge is dragged past its anchor.
        var left = startRect.Left;
        var top = startRect.Top;
        var right = startRect.Right;
        var bottom = startRect.Bottom;

        switch (dragKind)
        {
            case ObjectDragKind.Move:
                return new Rect(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height);
            case ObjectDragKind.ResizeNW:
                left += dx;
                top += dy;
                break;
            case ObjectDragKind.ResizeN:
                top += dy;
                break;
            case ObjectDragKind.ResizeNE:
                right += dx;
                top += dy;
                break;
            case ObjectDragKind.ResizeE:
                right += dx;
                break;
            case ObjectDragKind.ResizeSE:
                right += dx;
                bottom += dy;
                break;
            case ObjectDragKind.ResizeS:
                bottom += dy;
                break;
            case ObjectDragKind.ResizeSW:
                left += dx;
                bottom += dy;
                break;
            case ObjectDragKind.ResizeW:
                left += dx;
                break;
            default:
                return startRect;
        }

        // Clamp each axis to the minimum size while keeping the un-dragged edge fixed.
        var movesLeft = dragKind is ObjectDragKind.ResizeNW or ObjectDragKind.ResizeW or ObjectDragKind.ResizeSW;
        if (right - left < minimumSize)
        {
            if (movesLeft)
                left = right - minimumSize;
            else
                right = left + minimumSize;
        }

        var movesTop = dragKind is ObjectDragKind.ResizeNW or ObjectDragKind.ResizeN or ObjectDragKind.ResizeNE;
        if (bottom - top < minimumSize)
        {
            if (movesTop)
                top = bottom - minimumSize;
            else
                bottom = top + minimumSize;
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Computes the rotation angle (in degrees, clockwise, 0 = pointer straight up) of the
    /// pointer relative to the object center. Returns 0 when the pointer is at the center.
    /// </summary>
    public static double CalculateRotationDegrees(Point center, Point pointer)
    {
        var dx = pointer.X - center.X;
        var dy = pointer.Y - center.Y;
        if (dx == 0 && dy == 0)
            return 0;

        // Atan2(dx, -dy) gives 0 for straight up and increases clockwise (screen Y grows downward).
        var degrees = Math.Atan2(dx, -dy) * (180.0 / Math.PI);
        return degrees < 0 ? degrees + 360 : degrees;
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

        // Rotation grip sits above the top-center handle with a connector line.
        var gripCenter = new Point(
            objectRect.Left + objectRect.Width / 2,
            objectRect.Top - RotationGripOffset);
        if (Math.Abs(position.X - gripCenter.X) <= pad && Math.Abs(position.Y - gripCenter.Y) <= pad)
            return ObjectDragKind.Rotate;

        var nearLeft = Math.Abs(position.X - objectRect.Left) <= pad;
        var nearTop = Math.Abs(position.Y - objectRect.Top) <= pad;
        var nearRight = Math.Abs(position.X - objectRect.Right) <= pad;
        var nearBottom = Math.Abs(position.Y - objectRect.Bottom) <= pad;
        var inVertical = position.Y >= objectRect.Top - pad && position.Y <= objectRect.Bottom + pad;
        var inHorizontal = position.X >= objectRect.Left - pad && position.X <= objectRect.Right + pad;

        // Corners take priority over edges (a corner is near two perpendicular edges).
        if (nearLeft && nearTop) return ObjectDragKind.ResizeNW;
        if (nearRight && nearTop) return ObjectDragKind.ResizeNE;
        if (nearRight && nearBottom) return ObjectDragKind.ResizeSE;
        if (nearLeft && nearBottom) return ObjectDragKind.ResizeSW;

        // Edges: anywhere along the edge line within the object's span.
        if (nearTop && inHorizontal) return ObjectDragKind.ResizeN;
        if (nearBottom && inHorizontal) return ObjectDragKind.ResizeS;
        if (nearRight && inVertical) return ObjectDragKind.ResizeE;
        if (nearLeft && inVertical) return ObjectDragKind.ResizeW;
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
