using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

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
        var topRowLookup = BuildRowLookup(topRows);
        var bottomLeftRowLookup = BuildRowLookup(bottomLeftRows);
        var leftColumnLookup = BuildColumnLookup(leftColumns);
        var topRightColumnLookup = BuildColumnLookup(topRightColumns);
        var cells = splitPanes.Cells ?? [];
        var mergeLookup = MergeRangeIndex.Create(mergedRegions, cells);
        var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
        var horizontalY = dividerLayout.HorizontalY ?? GridView.ColHeaderHeight;
        var verticalX = dividerLayout.VerticalX ?? GridView.CalculateRowHeaderWidth(viewport);
        var layouts = new List<SplitPaneCellLayout>(cells.Count);
        HashSet<(uint Row, uint Col)>? occupied = null;

        foreach (var cell in cells)
        {
            var merge = mergeLookup.Find(cell.Row, cell.Col);
            if (merge.HasValue && (cell.Row != merge.Value.Start.Row || cell.Col != merge.Value.Start.Col))
                continue;

            var isTopPane = topRowLookup.TryGetValue(cell.Row, out var topRow);
            var isLeftPane = leftColumnLookup.TryGetValue(cell.Col, out var leftColumn);
            var region = ResolveSplitPaneRegion(isTopPane, isLeftPane);
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
                occupied ??= BuildOccupiedCells(cells);
                var renderWidth = width + SumEmptyOverflowColumnWidths(cell, colMetrics, occupied);
                textClipRect = new Rect(x, y, renderWidth, height);
            }

            layouts.Add(new SplitPaneCellLayout(cell, rect, textClipRect, region));
        }

        return layouts;
    }

    private static SplitPaneRegion ResolveSplitPaneRegion(bool isTopPane, bool isLeftPane) =>
        (isTopPane, isLeftPane) switch
        {
            (true, true) => SplitPaneRegion.TopLeft,
            (true, false) => SplitPaneRegion.TopRight,
            (false, true) => SplitPaneRegion.BottomLeft,
            _ => SplitPaneRegion.BottomRight
        };

    private static Dictionary<uint, RowMetric> BuildRowLookup(IReadOnlyList<RowMetric> rows)
    {
        var lookup = new Dictionary<uint, RowMetric>(rows.Count);
        foreach (var row in rows)
            lookup.Add(row.Row, row);

        return lookup;
    }

    private static Dictionary<uint, ColMetric> BuildColumnLookup(IReadOnlyList<ColMetric> columns)
    {
        var lookup = new Dictionary<uint, ColMetric>(columns.Count);
        foreach (var column in columns)
            lookup.Add(column.Col, column);

        return lookup;
    }

    private static bool CanOverflowSplitPaneText(DisplayCell cell, GridRange? merge) =>
        GridView.CanOverflowCellText(cell.Style, cell.RawValue, cell.DisplayText, merge);

    private static HashSet<(uint Row, uint Col)> BuildOccupiedCells(IReadOnlyList<DisplayCell> cells)
    {
        var occupied = new HashSet<(uint Row, uint Col)>();
        foreach (var cell in cells)
        {
            if (GridView.IsOverflowOccupied(cell, editingCell: null))
                occupied.Add((cell.Row, cell.Col));
        }

        return occupied;
    }

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

            var queryRows = BuildQueryRows(cells);
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

        private static HashSet<uint> BuildQueryRows(IReadOnlyList<DisplayCell> cells)
        {
            var rows = new HashSet<uint>();
            foreach (var cell in cells)
                rows.Add(cell.Row);

            return rows;
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
