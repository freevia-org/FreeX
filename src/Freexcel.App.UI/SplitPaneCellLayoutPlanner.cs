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
        var cells = splitPanes.Cells ?? [];
        var mergeLookup = MergeRangeIndex.Create(mergedRegions, cells);
        var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
        var horizontalY = dividerLayout.HorizontalY ?? GridView.ColHeaderHeight;
        var verticalX = dividerLayout.VerticalX ?? GridView.CalculateRowHeaderWidth(viewport);
        var layouts = new List<SplitPaneCellLayout>();
        var occupied = new HashSet<(uint Row, uint Col)>(
            cells
            .Where(cell => !string.IsNullOrEmpty(cell.DisplayText))
            .Select(cell => (cell.Row, cell.Col)));

        foreach (var cell in cells)
        {
            var merge = mergeLookup.Find(cell.Row, cell.Col);
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

            var rowMetrics = isTopPane ? topRowLookup : bottomLeftRowLookup;
            var colMetrics = isLeftPane ? leftColumnLookup : topRightColumnLookup;
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
        IReadOnlyDictionary<uint, ColMetric> columns,
        HashSet<(uint Row, uint Col)> occupied)
    {
        double width = 0;
        var nextCol = cell.Col + 1;
        while (columns.TryGetValue(nextCol, out var nextMetric) &&
               !occupied.Contains((cell.Row, nextCol)))
        {
            width += nextMetric.Width;
            nextCol++;
        }

        return width;
    }

    private static double SumMergedColumnWidths(GridRange merge, IReadOnlyDictionary<uint, ColMetric> columns, uint anchorCol)
    {
        double width = 0;
        foreach (var metric in columns.Values)
        {
            if (metric.Col > anchorCol && metric.Col <= merge.End.Col)
                width += metric.Width;
        }

        return width;
    }

    private static double SumMergedRowHeights(GridRange merge, IReadOnlyDictionary<uint, RowMetric> rows, uint anchorRow)
    {
        double height = 0;
        foreach (var metric in rows.Values)
        {
            if (metric.Row > anchorRow && metric.Row <= merge.End.Row)
                height += metric.Height;
        }

        return height;
    }

    private sealed class MergeRangeIndex
    {
        private static readonly MergeRangeIndex Empty = new(new Dictionary<uint, List<GridRange>>());

        private readonly IReadOnlyDictionary<uint, List<GridRange>> _mergesByRow;

        private MergeRangeIndex(IReadOnlyDictionary<uint, List<GridRange>> mergesByRow)
        {
            _mergesByRow = mergesByRow;
        }

        public static MergeRangeIndex Create(IReadOnlyList<GridRange>? mergedRegions, IReadOnlyList<DisplayCell> cells)
        {
            if (mergedRegions is not { Count: > 0 } || cells.Count == 0)
                return Empty;

            var queryRows = cells.Select(cell => cell.Row).Distinct().ToArray();
            var mergesByRow = new Dictionary<uint, List<GridRange>>();
            foreach (var mergedRegion in mergedRegions)
            {
                foreach (var row in queryRows)
                {
                    if (row < mergedRegion.Start.Row || row > mergedRegion.End.Row)
                        continue;

                    if (!mergesByRow.TryGetValue(row, out var rowMerges))
                    {
                        rowMerges = [];
                        mergesByRow[row] = rowMerges;
                    }

                    rowMerges.Add(mergedRegion);
                }
            }

            return new MergeRangeIndex(mergesByRow);
        }

        public GridRange? Find(uint row, uint col)
        {
            if (!_mergesByRow.TryGetValue(row, out var rowMerges))
                return null;

            foreach (var merge in rowMerges)
            {
                if (col >= merge.Start.Col && col <= merge.End.Col)
                    return merge;
            }

            return null;
        }
    }
}
