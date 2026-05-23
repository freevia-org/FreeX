using Freexcel.Core.Model;
using System.Linq;
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
        var visibleRows = viewport.RowMetrics
            .Where(row => row.Row >= range.Start.Row && row.Row <= range.End.Row)
            .ToList();
        var visibleColumns = viewport.ColMetrics
            .Where(column => column.Col >= range.Start.Col && column.Col <= range.End.Col)
            .ToList();

        if (visibleRows.Count == 0 || visibleColumns.Count == 0)
            return null;

        var top = visibleRows.Min(row => row.TopOffset) + columnHeaderHeight;
        var bottom = visibleRows.Max(row => row.TopOffset + row.Height) + columnHeaderHeight;
        var left = visibleColumns.Min(column => column.LeftOffset) + rowHeaderWidth;
        var right = visibleColumns.Max(column => column.LeftOffset + column.Width) + rowHeaderWidth;

        if (right <= left || bottom <= top)
            return null;

        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
