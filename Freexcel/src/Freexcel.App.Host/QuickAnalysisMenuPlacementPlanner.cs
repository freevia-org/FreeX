using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class QuickAnalysisMenuPlacementPlanner
{
    private const double MenuOffset = 4;

    public static Point BuildAnchor(
        GridRange selection,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var row = viewport.RowMetrics
            .Where(metric => metric.Row >= selection.Start.Row && metric.Row <= selection.End.Row)
            .OrderByDescending(metric => metric.Row)
            .FirstOrDefault();
        var column = viewport.ColMetrics
            .Where(metric => metric.Col >= selection.Start.Col && metric.Col <= selection.End.Col)
            .OrderByDescending(metric => metric.Col)
            .FirstOrDefault();

        row ??= viewport.RowMetrics.LastOrDefault();
        column ??= viewport.ColMetrics.LastOrDefault();

        if (row is null || column is null)
            return new Point(rowHeaderWidth + MenuOffset, columnHeaderHeight + MenuOffset);

        return new Point(
            rowHeaderWidth + column.LeftOffset + column.Width + MenuOffset,
            columnHeaderHeight + row.TopOffset + row.Height + MenuOffset);
    }
}
