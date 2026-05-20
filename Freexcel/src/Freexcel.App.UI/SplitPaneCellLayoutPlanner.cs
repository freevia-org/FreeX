using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class SplitPaneCellLayoutPlanner
{
    public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions = null)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return [];

        var topRows = splitPanes.TopRows ?? [];
        var leftColumns = splitPanes.LeftColumns ?? [];
        var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
        var topRowLookup = topRows.ToDictionary(row => row.Row);
        var bottomLeftRowLookup = bottomLeftRows.ToDictionary(row => row.Row);
        var leftColumnLookup = leftColumns.ToDictionary(column => column.Col);
        var topRightColumnLookup = topRightColumns.ToDictionary(column => column.Col);
        var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
        var horizontalY = dividerLayout.HorizontalY ?? GridView.ColHeaderHeight;
        var verticalX = dividerLayout.VerticalX ?? GridView.CalculateRowHeaderWidth(viewport);
        var layouts = new List<SplitPaneCellLayout>();
        var occupied = new HashSet<(uint Row, uint Col)>(
            (splitPanes.Cells ?? [])
            .Where(cell => !string.IsNullOrEmpty(cell.DisplayText))
            .Select(cell => (cell.Row, cell.Col)));

        foreach (var cell in splitPanes.Cells ?? [])
        {
            var merge = FindMerge(mergedRegions, cell.Row, cell.Col);
            if (merge.HasValue && (cell.Row != merge.Value.Start.Row || cell.Col != merge.Value.Start.Col))
                continue;

            var isTopPane = topRowLookup.TryGetValue(cell.Row, out var topRow);
            var isLeftPane = leftColumnLookup.TryGetValue(cell.Col, out var leftColumn);
            var row = isTopPane
                ? topRow!
                : bottomLeftRowLookup.GetValueOrDefault(cell.Row);
            var column = isLeftPane
                ? leftColumn!
                : topRightColumnLookup.GetValueOrDefault(cell.Col);

            if (row is null || column is null)
                continue;

            var rowMetrics = isTopPane ? topRows : bottomLeftRows;
            var colMetrics = isLeftPane ? leftColumns : topRightColumns;
            var width = column.Width;
            var height = row.Height;
            if (merge.HasValue)
            {
                width += SumMergedColumnWidths(merge.Value, colMetrics, cell.Col);
                height += SumMergedRowHeights(merge.Value, rowMetrics, cell.Row);
            }

            var x = isLeftPane
                ? GridView.CalculateRowHeaderWidth(viewport) + column.LeftOffset
                : verticalX + column.LeftOffset;
            var y = isTopPane
                ? GridView.ColHeaderHeight + row.TopOffset
                : horizontalY + row.TopOffset;

            var rect = new Rect(x, y, width, height);
            var textClipRect = rect;
            if (CanOverflowSplitPaneText(cell, merge))
            {
                var renderWidth = width + SumEmptyOverflowColumnWidths(cell, colMetrics, occupied);
                textClipRect = new Rect(x, y, renderWidth, height);
            }

            layouts.Add(new SplitPaneCellLayout(cell, rect, textClipRect));
        }

        return layouts;
    }

    private static bool CanOverflowSplitPaneText(DisplayCell cell, GridRange? merge) =>
        GridView.CanOverflowCellText(cell.Style, cell.RawValue, cell.DisplayText, merge);

    private static double SumEmptyOverflowColumnWidths(
        DisplayCell cell,
        IReadOnlyList<ColMetric> columns,
        HashSet<(uint Row, uint Col)> occupied)
    {
        double width = 0;
        var nextCol = cell.Col + 1;
        while (columns.FirstOrDefault(column => column.Col == nextCol) is { } nextMetric &&
               !occupied.Contains((cell.Row, nextCol)))
        {
            width += nextMetric.Width;
            nextCol++;
        }

        return width;
    }

    private static GridRange? FindMerge(IReadOnlyList<GridRange>? mergedRegions, uint row, uint col)
    {
        if (mergedRegions is null)
            return null;

        foreach (var merge in mergedRegions)
        {
            if (row >= merge.Start.Row && row <= merge.End.Row &&
                col >= merge.Start.Col && col <= merge.End.Col)
                return merge;
        }

        return null;
    }

    private static double SumMergedColumnWidths(GridRange merge, IReadOnlyList<ColMetric> columns, uint anchorCol)
    {
        double width = 0;
        for (uint col = anchorCol + 1; col <= merge.End.Col; col++)
        {
            var metric = columns.FirstOrDefault(column => column.Col == col);
            if (metric is not null)
                width += metric.Width;
        }

        return width;
    }

    private static double SumMergedRowHeights(GridRange merge, IReadOnlyList<RowMetric> rows, uint anchorRow)
    {
        double height = 0;
        for (uint row = anchorRow + 1; row <= merge.End.Row; row++)
        {
            var metric = rows.FirstOrDefault(metric => metric.Row == row);
            if (metric is not null)
                height += metric.Height;
        }

        return height;
    }
}
