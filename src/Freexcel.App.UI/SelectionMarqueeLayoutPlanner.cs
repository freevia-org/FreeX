using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class SelectionMarqueeLayoutPlanner
{
    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    private static Rect? CalculateVisibleRangeRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var hasVisibleRow = false;
        var top = 0d;
        var bottom = 0d;
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row || row.Row > range.End.Row)
                continue;

            var rowTop = row.TopOffset;
            var rowBottom = row.TopOffset + row.Height;
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
        var left = 0d;
        var right = 0d;
        foreach (var column in viewport.ColMetrics)
        {
            if (column.Col < range.Start.Col || column.Col > range.End.Col)
                continue;

            var columnLeft = column.LeftOffset;
            var columnRight = column.LeftOffset + column.Width;
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

        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
