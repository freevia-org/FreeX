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
        var row = FindLastVisibleRowInSelection(viewport.RowMetrics, selection.Start.Row, selection.End.Row);
        var column = FindLastVisibleColumnInSelection(viewport.ColMetrics, selection.Start.Col, selection.End.Col);

        row ??= viewport.RowMetrics.Count == 0 ? null : viewport.RowMetrics[^1];
        column ??= viewport.ColMetrics.Count == 0 ? null : viewport.ColMetrics[^1];

        if (row is null || column is null)
            return new Point(rowHeaderWidth + MenuOffset, columnHeaderHeight + MenuOffset);

        return new Point(
            rowHeaderWidth + column.LeftOffset + column.Width + MenuOffset,
            columnHeaderHeight + row.TopOffset + row.Height + MenuOffset);
    }

    private static RowMetric? FindLastVisibleRowInSelection(IReadOnlyList<RowMetric> metrics, uint startRow, uint endRow)
    {
        var index = FindLastRowIndexAtOrBefore(metrics, endRow);
        return index >= 0 && metrics[index].Row >= startRow ? metrics[index] : null;
    }

    private static ColMetric? FindLastVisibleColumnInSelection(IReadOnlyList<ColMetric> metrics, uint startCol, uint endCol)
    {
        var index = FindLastColumnIndexAtOrBefore(metrics, endCol);
        return index >= 0 && metrics[index].Col >= startCol ? metrics[index] : null;
    }

    private static int FindLastRowIndexAtOrBefore(IReadOnlyList<RowMetric> metrics, uint target)
    {
        var low = 0;
        var high = metrics.Count - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (metrics[mid].Row <= target)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private static int FindLastColumnIndexAtOrBefore(IReadOnlyList<ColMetric> metrics, uint target)
    {
        var low = 0;
        var high = metrics.Count - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (metrics[mid].Col <= target)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }
}
