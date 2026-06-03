using FreeX.Core.Model;
using System.Collections;
using System.Windows;

namespace FreeX.App.UI;

public interface ISplitPaneCellLayoutConsumer
{
    void AcceptLayout(SplitPaneCellLayout layout);
}

public static class SplitPaneCellLayoutPlanner
{
    public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions = null,
        CellAddress? editingCell = null)
    {
        var cells = viewport.SplitPanes?.Cells ?? [];
        if (cells.Count == 0)
            return [];

        var consumer = new SplitPaneCellLayoutCollector(cells);
        VisitLayouts(viewport, mergedRegions, editingCell, ref consumer);
        return consumer.ToLayouts();
    }

    public static void VisitLayouts<TConsumer>(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions,
        CellAddress? editingCell,
        ref TConsumer consumer)
        where TConsumer : struct, ISplitPaneCellLayoutConsumer
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return;

        var cells = splitPanes.Cells ?? [];
        if (cells.Count == 0)
            return;

        var topRows = splitPanes.TopRows ?? [];
        var leftColumns = splitPanes.LeftColumns ?? [];
        var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
        var topRowLookup = BuildRowLookup(topRows);
        var bottomLeftRowLookup = BuildRowLookup(bottomLeftRows);
        var leftColumnLookup = BuildColumnLookup(leftColumns);
        var topRightColumnLookup = BuildColumnLookup(topRightColumns);
        var mergeLookup = MergeRangeIndex.Create(mergedRegions, cells);
        var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        var horizontalY = dividerLayout.HorizontalY ?? GridView.ColHeaderHeight;
        var verticalX = dividerLayout.VerticalX ?? rowHeaderWidth;
        SplitPaneOccupiedCellMap? occupied = null;

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
                ? rowHeaderWidth + column.LeftOffset
                : verticalX + column.LeftOffset;
            var y = isTopPane
                ? GridView.ColHeaderHeight + row.TopOffset
                : horizontalY + row.TopOffset;

            var rect = new Rect(x, y, width, height);
            var textClipRect = rect;
            if (CanOverflowSplitPaneText(cell, merge))
            {
                occupied ??= BuildOccupiedCells(cells, editingCell);
                var renderWidth = width + SumEmptyOverflowColumnWidths(cell, colMetrics, occupied.Value);
                textClipRect = new Rect(x, y, renderWidth, height);
            }

            consumer.AcceptLayout(new SplitPaneCellLayout(cell, rect, textClipRect, region));
        }
    }

    private struct SplitPaneCellLayoutCollector(IReadOnlyList<DisplayCell> cells) : ISplitPaneCellLayoutConsumer
    {
        private readonly IReadOnlyList<DisplayCell> _cells = cells;
        private int _nextCellIndex;
        private int[]? _cellIndexes;
        private Rect[]? _rects;
        private Rect[]? _textClipRects;
        private SplitPaneRegion[]? _regions;
        private int _count;

        public void AcceptLayout(SplitPaneCellLayout layout)
        {
            EnsureCapacity();
            _cellIndexes![_count] = FindCellIndex(layout.Cell.Row, layout.Cell.Col);
            _rects![_count] = layout.Rect;
            _textClipRects![_count] = layout.TextClipRect;
            _regions![_count] = layout.Region;
            _count++;
        }

        public readonly IReadOnlyList<SplitPaneCellLayout> ToLayouts() =>
            _count == 0
                ? []
                : new SplitPaneCellLayoutList(
                    _cells,
                    _cellIndexes!,
                    _rects!,
                    _textClipRects!,
                    _regions!,
                    _count);

        private void EnsureCapacity()
        {
            if (_cellIndexes is not null)
                return;

            var capacity = _cells.Count;
            _cellIndexes = new int[capacity];
            _rects = new Rect[capacity];
            _textClipRects = new Rect[capacity];
            _regions = new SplitPaneRegion[capacity];
        }

        private int FindCellIndex(uint row, uint col)
        {
            for (var i = _nextCellIndex; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell.Row != row || cell.Col != col)
                    continue;

                _nextCellIndex = i + 1;
                return i;
            }

            for (var i = 0; i < _nextCellIndex; i++)
            {
                var cell = _cells[i];
                if (cell.Row == row && cell.Col == col)
                    return i;
            }

            throw new InvalidOperationException("Split pane layout cell was not found in the source viewport cells.");
        }
    }

    private sealed class SplitPaneCellLayoutList(
        IReadOnlyList<DisplayCell> cells,
        int[] cellIndexes,
        Rect[] rects,
        Rect[] textClipRects,
        SplitPaneRegion[] regions,
        int count) : IReadOnlyList<SplitPaneCellLayout>
    {
        public int Count { get; } = count;

        public SplitPaneCellLayout this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                if (index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return new SplitPaneCellLayout(
                    cells[cellIndexes[index]],
                    rects[index],
                    textClipRects[index],
                    regions[index]);
            }
        }

        public IEnumerator<SplitPaneCellLayout> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

    private readonly record struct OccupiedColumnSpan(uint Start, uint End);

    private readonly struct SplitPaneOccupiedCellMap(Dictionary<uint, List<OccupiedColumnSpan>> spansByRow)
    {
        private readonly Dictionary<uint, List<OccupiedColumnSpan>> _spansByRow = spansByRow;

        public bool Contains(uint row, uint col)
        {
            if (!_spansByRow.TryGetValue(row, out var spans))
                return false;

            foreach (var span in spans)
            {
                if (col < span.Start)
                    return false;
                if (col <= span.End)
                    return true;
            }

            return false;
        }
    }

    private static SplitPaneOccupiedCellMap BuildOccupiedCells(
        IReadOnlyList<DisplayCell> cells,
        CellAddress? editingCell)
    {
        var spansByRow = new Dictionary<uint, List<OccupiedColumnSpan>>();
        var needsNormalize = false;
        foreach (var cell in cells)
        {
            if (!GridView.IsOverflowOccupied(cell, editingCell))
                continue;

            if (!spansByRow.TryGetValue(cell.Row, out var spans))
            {
                spans = [];
                spansByRow.Add(cell.Row, spans);
            }

            AddOccupiedColumn(spans, cell.Col, ref needsNormalize);
        }

        if (needsNormalize)
            NormalizeOccupiedColumnSpans(spansByRow);

        return new SplitPaneOccupiedCellMap(spansByRow);
    }

    private static void AddOccupiedColumn(List<OccupiedColumnSpan> spans, uint col, ref bool needsNormalize)
    {
        if (spans.Count == 0)
        {
            spans.Add(new OccupiedColumnSpan(col, col));
            return;
        }

        var last = spans[^1];
        if (last.End < uint.MaxValue && col == last.End + 1)
        {
            spans[^1] = new OccupiedColumnSpan(last.Start, col);
            return;
        }

        if (col > last.End)
        {
            spans.Add(new OccupiedColumnSpan(col, col));
            return;
        }

        if (col >= last.Start)
            return;

        needsNormalize = true;
        spans.Add(new OccupiedColumnSpan(col, col));
    }

    private static void NormalizeOccupiedColumnSpans(Dictionary<uint, List<OccupiedColumnSpan>> spansByRow)
    {
        foreach (var spans in spansByRow.Values)
        {
            if (spans.Count <= 1)
                continue;

            spans.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            var writeIndex = 0;
            for (var readIndex = 1; readIndex < spans.Count; readIndex++)
            {
                var current = spans[readIndex];
                var merged = spans[writeIndex];
                if (current.Start <= merged.End ||
                    (merged.End < uint.MaxValue && current.Start == merged.End + 1))
                {
                    spans[writeIndex] = new OccupiedColumnSpan(
                        merged.Start,
                        Math.Max(merged.End, current.End));
                    continue;
                }

                writeIndex++;
                spans[writeIndex] = current;
            }

            var keepCount = writeIndex + 1;
            if (keepCount < spans.Count)
                spans.RemoveRange(keepCount, spans.Count - keepCount);
        }
    }

    private static double SumEmptyOverflowColumnWidths(
        DisplayCell cell,
        IReadOnlyDictionary<uint, ColMetric> columns,
        SplitPaneOccupiedCellMap occupied)
    {
        double width = 0;
        var nextCol = cell.Col + 1;
        while (columns.TryGetValue(nextCol, out var nextMetric) &&
               !occupied.Contains(cell.Row, nextCol))
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

            var queryCells = BuildQueryCells(cells);
            var mergesByRow = new Dictionary<uint, List<GridRange>>();
            foreach (var mergedRegion in mergedRegions)
            {
                if (mergedRegion.End.Row < queryCells.MinRow ||
                    mergedRegion.Start.Row > queryCells.MaxRow ||
                    mergedRegion.End.Col < queryCells.MinCol ||
                    mergedRegion.Start.Col > queryCells.MaxCol)
                {
                    continue;
                }

                var startRow = Math.Max(mergedRegion.Start.Row, queryCells.MinRow);
                var endRow = Math.Min(mergedRegion.End.Row, queryCells.MaxRow);
                if (startRow > endRow)
                    continue;

                AddMergeRows(mergesByRow, queryCells, mergedRegion, startRow, endRow);
            }

            return new MergeRangeIndex(mergesByRow);
        }

        private static void AddMergeRows(
            Dictionary<uint, List<GridRange>> mergesByRow,
            QueryCells queryCells,
            GridRange mergedRegion,
            uint startRow,
            uint endRow)
        {
            var intersectedRowSpan = endRow - startRow + 1;
            if (intersectedRowSpan <= queryCells.Rows.Count)
            {
                var row = startRow;
                while (true)
                {
                    if (queryCells.Rows.Contains(row))
                        AddMergeRow(mergesByRow, row, mergedRegion);

                    if (row == endRow)
                        break;
                    row++;
                }

                return;
            }

            foreach (var row in queryCells.Rows)
            {
                if (row >= startRow && row <= endRow)
                    AddMergeRow(mergesByRow, row, mergedRegion);
            }
        }

        private static void AddMergeRow(
            Dictionary<uint, List<GridRange>> mergesByRow,
            uint row,
            GridRange mergedRegion)
        {
            if (!mergesByRow.TryGetValue(row, out var rowMerges))
            {
                rowMerges = [];
                mergesByRow[row] = rowMerges;
            }

            rowMerges.Add(mergedRegion);
        }

        private static QueryCells BuildQueryCells(IReadOnlyList<DisplayCell> cells)
        {
            var rows = new HashSet<uint>();
            var minRow = uint.MaxValue;
            var maxRow = uint.MinValue;
            var minCol = uint.MaxValue;
            var maxCol = uint.MinValue;

            foreach (var cell in cells)
            {
                rows.Add(cell.Row);

                if (cell.Row < minRow)
                    minRow = cell.Row;
                if (cell.Row > maxRow)
                    maxRow = cell.Row;
                if (cell.Col < minCol)
                    minCol = cell.Col;
                if (cell.Col > maxCol)
                    maxCol = cell.Col;
            }

            return new QueryCells(rows, minRow, maxRow, minCol, maxCol);
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

        private readonly record struct QueryCells(
            HashSet<uint> Rows,
            uint MinRow,
            uint MaxRow,
            uint MinCol,
            uint MaxCol);
    }
}
