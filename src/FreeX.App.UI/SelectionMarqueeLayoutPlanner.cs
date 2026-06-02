using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class SelectionMarqueeLayoutPlanner
{
    public readonly record struct SelectionMarqueeLayout(
        Rect Rect,
        bool HasTopEdge,
        bool HasLeftEdge,
        bool HasBottomEdge,
        bool HasRightEdge);

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)?.Rect;

    public static SelectionMarqueeLayout? CalculateVisibleSelectionLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)?.Rect;

    private static SelectionMarqueeLayout? CalculateVisibleRangeLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var hasVisibleRow = false;
        var hasTopEdge = false;
        var hasBottomEdge = false;
        var top = 0d;
        var bottom = 0d;
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row)
                continue;
            if (row.Row > range.End.Row)
                break;

            var rowTop = row.TopOffset;
            var rowBottom = row.TopOffset + row.Height;
            if (row.Row == range.Start.Row)
                hasTopEdge = true;
            if (row.Row == range.End.Row)
                hasBottomEdge = true;
            if (!hasVisibleRow)
            {
                top = rowTop;
                bottom = rowBottom;
                hasVisibleRow = true;
                continue;
            }

            if (rowTop < top)
                top = rowTop;
            if (rowBottom > bottom)
                bottom = rowBottom;
        }

        if (!hasVisibleRow)
            return null;

        var hasVisibleColumn = false;
        var hasLeftEdge = false;
        var hasRightEdge = false;
        var left = 0d;
        var right = 0d;
        foreach (var column in viewport.ColMetrics)
        {
            if (column.Col < range.Start.Col)
                continue;
            if (column.Col > range.End.Col)
                break;

            var columnLeft = column.LeftOffset;
            var columnRight = column.LeftOffset + column.Width;
            if (column.Col == range.Start.Col)
                hasLeftEdge = true;
            if (column.Col == range.End.Col)
                hasRightEdge = true;
            if (!hasVisibleColumn)
            {
                left = columnLeft;
                right = columnRight;
                hasVisibleColumn = true;
                continue;
            }

            if (columnLeft < left)
                left = columnLeft;
            if (columnRight > right)
                right = columnRight;
        }

        if (!hasVisibleColumn)
            return null;

        top += columnHeaderHeight;
        bottom += columnHeaderHeight;
        left += rowHeaderWidth;
        right += rowHeaderWidth;

        if (right <= left || bottom <= top)
            return null;

        return new SelectionMarqueeLayout(
            new Rect(new Point(left, top), new Point(right, bottom)),
            hasTopEdge,
            hasLeftEdge,
            hasBottomEdge,
            hasRightEdge);
    }
}
