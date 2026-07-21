using FreeX.Core.Model;
using System.Collections;
using System.Windows;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

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
        var topRowLookup = new SplitPaneRowMetricLookup(topRows);
        var bottomLeftRowLookup = new SplitPaneRowMetricLookup(bottomLeftRows);
        var leftColumnLookup = new SplitPaneColumnMetricLookup(leftColumns);
        var topRightColumnLookup = new SplitPaneColumnMetricLookup(topRightColumns);
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

            if (merge.HasValue)
            {
                EmitMergeLayouts(
                    cell,
                    merge.Value,
                    isTopPane,
                    isLeftPane,
                    topRowLookup,
                    bottomLeftRowLookup,
                    leftColumnLookup,
                    topRightColumnLookup,
                    rowHeaderWidth,
                    horizontalY,
                    verticalX,
                    ref consumer);
                continue;
            }

            var region = ResolveSplitPaneRegion(isTopPane, isLeftPane);
            var row = isTopPane
                ? topRow
                : bottomLeftRowLookup.TryGetValue(cell.Row, out var bottomLeftRow)
                    ? bottomLeftRow
                    : null;
            var column = isLeftPane
                ? leftColumn
                : topRightColumnLookup.TryGetValue(cell.Col, out var topRightColumn)
                    ? topRightColumn
                    : null;

            if (row is null || column is null)
                continue;

            var colMetrics = isLeftPane ? leftColumnLookup : topRightColumnLookup;
            var width = column.Width;
            var height = row.Height;

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
                var hAlign = cell.Style?.HorizontalAlignment ?? CellHAlign.General;
                double rightOverflow = hAlign == CellHAlign.Right
                    ? 0
                    : SumEmptyOverflowColumnWidths(cell, colMetrics, occupied.Value);
                double leftOverflow = hAlign == CellHAlign.Right || hAlign == CellHAlign.Center
                    ? SumEmptyOverflowColumnWidthsLeft(cell, colMetrics, occupied.Value)
                    : 0;
                var renderWidth = width + leftOverflow + rightOverflow;
                textClipRect = new Rect(x - leftOverflow, y, renderWidth, height);
            }

            consumer.AcceptLayout(new SplitPaneCellLayout(cell, rect, textClipRect, region));
        }
    }

    /// <summary>
    /// A merged cell that crosses a View&gt;Split divider must still render across every pane it
    /// touches (clipped per pane), matching real Excel, instead of being truncated to whichever pane
    /// the anchor cell falls in. This applies independently on both axes: the column footprint is
    /// widened to cross the vertical divider (left/right panes) and the row footprint is widened to
    /// cross the horizontal divider (top/bottom panes) — so a merge straddling both splits at once can
    /// emit up to 4 quadrant layouts (one per pane combination), while a merge crossing only one axis
    /// emits 2, and a merge crossing neither emits just the anchor's own 1 (mirroring
    /// SplitPaneCellLayoutPlanner_BoundsTallMergeWorkToVisibleCells, which pins that a giant merge
    /// crossing both splits stays bounded to the handful of visible-cell quadrants, not proportional
    /// to the merge's row/column span). Only the quadrant containing the merge's anchor cell renders
    /// text/icon/comment/data-bar content — every other quadrant renders fill/border only, via
    /// <see cref="StripContentForSecondaryMergeQuadrant"/>, so content is never drawn twice.
    /// </summary>
    private static void EmitMergeLayouts<TConsumer>(
        DisplayCell cell,
        GridRange merge,
        bool anchorIsTopPane,
        bool anchorIsLeftPane,
        SplitPaneRowMetricLookup topRowLookup,
        SplitPaneRowMetricLookup bottomLeftRowLookup,
        SplitPaneColumnMetricLookup leftColumnLookup,
        SplitPaneColumnMetricLookup topRightColumnLookup,
        double rowHeaderWidth,
        double horizontalY,
        double verticalX,
        ref TConsumer consumer)
        where TConsumer : struct, ISplitPaneCellLayoutConsumer
    {
        var primaryRows = anchorIsTopPane ? topRowLookup : bottomLeftRowLookup;
        var secondaryRows = anchorIsTopPane ? bottomLeftRowLookup : topRowLookup;
        if (FindMergeRowSpan(primaryRows, merge) is not { } primaryRowSpan)
            return;

        // The secondary row band's box only covers the part of the merge's rows that ISN'T already
        // shown by the anchor's own row band. Normally the two bands' row sets are disjoint, so this
        // is simply the whole remainder; but it also correctly yields "nothing more to draw" for a
        // (degenerate) viewport where both bands happen to share the same row metrics.
        var secondaryRowSpan = FindMergeRowSpan(secondaryRows, merge, exclude: primaryRows);

        var primaryYOrigin = anchorIsTopPane ? GridView.ColHeaderHeight : horizontalY;
        var secondaryYOrigin = anchorIsTopPane ? horizontalY : GridView.ColHeaderHeight;

        var primaryColumns = anchorIsLeftPane ? leftColumnLookup : topRightColumnLookup;
        var secondaryColumns = anchorIsLeftPane ? topRightColumnLookup : leftColumnLookup;
        var primaryColSpan = FindMergeColumnSpan(primaryColumns, merge);

        // Column counterpart of the secondary-row exclusion above: only the part of the merge's
        // columns not already shown by the anchor's own column pane.
        var secondaryColSpan = FindMergeColumnSpan(secondaryColumns, merge, exclude: primaryColumns);
        var primaryXOrigin = anchorIsLeftPane ? rowHeaderWidth : verticalX;
        var secondaryXOrigin = anchorIsLeftPane ? verticalX : rowHeaderWidth;

        var primaryRowIsTop = anchorIsTopPane;
        var secondaryRowIsTop = !anchorIsTopPane;

        EmitMergeQuadrant(cell, primaryRowSpan, primaryColSpan, ResolveSplitPaneRegion(primaryRowIsTop, anchorIsLeftPane), primaryXOrigin, primaryYOrigin, isPrimary: true, ref consumer);
        EmitMergeQuadrant(cell, primaryRowSpan, secondaryColSpan, ResolveSplitPaneRegion(primaryRowIsTop, !anchorIsLeftPane), secondaryXOrigin, primaryYOrigin, isPrimary: false, ref consumer);

        if (secondaryRowSpan is { } secondaryRowSpanValue)
        {
            EmitMergeQuadrant(cell, secondaryRowSpanValue, primaryColSpan, ResolveSplitPaneRegion(secondaryRowIsTop, anchorIsLeftPane), primaryXOrigin, secondaryYOrigin, isPrimary: false, ref consumer);
            EmitMergeQuadrant(cell, secondaryRowSpanValue, secondaryColSpan, ResolveSplitPaneRegion(secondaryRowIsTop, !anchorIsLeftPane), secondaryXOrigin, secondaryYOrigin, isPrimary: false, ref consumer);
        }
    }

    private static void EmitMergeQuadrant<TConsumer>(
        DisplayCell cell,
        (double Offset, double Size) rowSpan,
        (double Offset, double Size)? colSpan,
        SplitPaneRegion region,
        double xOrigin,
        double yOrigin,
        bool isPrimary,
        ref TConsumer consumer)
        where TConsumer : struct, ISplitPaneCellLayoutConsumer
    {
        if (colSpan is not { } cols)
            return;

        var rect = new Rect(xOrigin + cols.Offset, yOrigin + rowSpan.Offset, cols.Size, rowSpan.Size);
        var layoutCell = isPrimary ? cell : StripContentForSecondaryMergeQuadrant(cell);

        consumer.AcceptLayout(new SplitPaneCellLayout(layoutCell, rect, rect, region));
    }

    // Excel never repeats a merged cell's text/icon/comment/data-bar in more than one place, but its
    // fill/border must continue into every pane the merge crosses — so the non-anchor quadrant(s) keep
    // Style (for fill/border) while their content is cleared so it isn't drawn twice.
    private static DisplayCell StripContentForSecondaryMergeQuadrant(DisplayCell cell) =>
        cell with
        {
            DisplayText = string.Empty,
            HasComment = false,
            CommentDisplay = null,
            ConditionalIcon = null,
            ConditionalDataBar = null,
        };

    private struct SplitPaneCellLayoutCollector(IReadOnlyList<DisplayCell> cells) : ISplitPaneCellLayoutConsumer
    {
        private readonly IReadOnlyList<DisplayCell> _cells = cells;
        private int _nextCellIndex;
        private int[]? _cellIndexes;
        private Rect[]? _rects;
        private Rect[]? _textClipRects;
        private SplitPaneRegion[]? _regions;
        // Lazily allocated only when a layout's Cell differs from the source cell at its resolved
        // index - i.e. a merged cell crossing a View>Split divider, whose secondary-pane quadrant
        // carries content stripped by StripContentForSecondaryMergeQuadrant. Most cells never need
        // this, so it stays null in the common case.
        private DisplayCell?[]? _cellOverrides;
        private int _count;

        public void AcceptLayout(SplitPaneCellLayout layout)
        {
            EnsureCapacity();
            var index = FindCellIndex(layout.Cell.Row, layout.Cell.Col);
            _cellIndexes![_count] = index;
            _rects![_count] = layout.Rect;
            _textClipRects![_count] = layout.TextClipRect;
            _regions![_count] = layout.Region;
            if (!layout.Cell.Equals(_cells[index]))
                (_cellOverrides ??= new DisplayCell?[_cellIndexes.Length])[_count] = layout.Cell;
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
                    _cellOverrides,
                    _count);

        private void EnsureCapacity()
        {
            if (_cellIndexes is not null)
                return;

            // A merged cell crossing a View>Split divider is now emitted once per pane it touches
            // (up to 4 - one per row-band/column-pane quadrant, since a merge can cross both the
            // horizontal and vertical divider at once), not once overall - so capacity must cover
            // more layouts than source cells.
            var capacity = _cells.Count * 4;
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
        DisplayCell?[]? cellOverrides,
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
                    cellOverrides?[index] ?? cells[cellIndexes[index]],
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

    private readonly struct SplitPaneRowMetricLookup
    {
        private readonly IReadOnlyList<RowMetric> _rows;
        private readonly bool _isSorted;
        private readonly uint _firstRow;
        private readonly uint _lastRow;

        public SplitPaneRowMetricLookup(IReadOnlyList<RowMetric> rows)
        {
            _rows = rows;
            _isSorted = AreRowsSorted(rows);
            _firstRow = rows.Count > 0 ? rows[0].Row : 0;
            _lastRow = rows.Count > 0 ? rows[^1].Row : 0;
        }

        public int Count => _rows.Count;

        public RowMetric this[int index] => _rows[index];

        public bool TryGetValue(uint row, out RowMetric? metric)
        {
            metric = _isSorted
                ? FindSortedRowMetric(_rows, row, _firstRow, _lastRow)
                : FindRowMetric(_rows, row);
            return metric is not null;
        }
    }

    private readonly struct SplitPaneColumnMetricLookup
    {
        private readonly IReadOnlyList<ColMetric> _columns;
        private readonly bool _isSorted;
        private readonly uint _firstColumn;
        private readonly uint _lastColumn;

        public SplitPaneColumnMetricLookup(IReadOnlyList<ColMetric> columns)
        {
            _columns = columns;
            _isSorted = AreColumnsSorted(columns);
            _firstColumn = columns.Count > 0 ? columns[0].Col : 0;
            _lastColumn = columns.Count > 0 ? columns[^1].Col : 0;
        }

        public int Count => _columns.Count;

        public ColMetric this[int index] => _columns[index];

        public bool TryGetValue(uint column, out ColMetric? metric)
        {
            metric = _isSorted
                ? FindSortedColumnMetric(_columns, column, _firstColumn, _lastColumn)
                : FindColumnMetric(_columns, column);
            return metric is not null;
        }
    }

    private static bool AreRowsSorted(IReadOnlyList<RowMetric> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].Row < rows[i - 1].Row)
                return false;
        }

        return true;
    }

    private static bool AreColumnsSorted(IReadOnlyList<ColMetric> columns)
    {
        for (var i = 1; i < columns.Count; i++)
        {
            if (columns[i].Col < columns[i - 1].Col)
                return false;
        }

        return true;
    }

    private static RowMetric? FindSortedRowMetric(
        IReadOnlyList<RowMetric> rows,
        uint row,
        uint firstRow,
        uint lastRow)
    {
        if (rows.Count == 0 || row < firstRow || row > lastRow)
            return null;

        var directIndex = row - firstRow;
        if (directIndex < rows.Count)
        {
            var candidate = rows[(int)directIndex];
            if (candidate.Row == row)
                return candidate;
        }

        var low = 0;
        var high = rows.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var metric = rows[mid];
            if (metric.Row == row)
                return metric;
            if (metric.Row < row)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static ColMetric? FindSortedColumnMetric(
        IReadOnlyList<ColMetric> columns,
        uint column,
        uint firstColumn,
        uint lastColumn)
    {
        if (columns.Count == 0 || column < firstColumn || column > lastColumn)
            return null;

        var directIndex = column - firstColumn;
        if (directIndex < columns.Count)
        {
            var candidate = columns[(int)directIndex];
            if (candidate.Col == column)
                return candidate;
        }

        var low = 0;
        var high = columns.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var metric = columns[mid];
            if (metric.Col == column)
                return metric;
            if (metric.Col < column)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> rows, uint row)
    {
        foreach (var metric in rows)
        {
            if (metric.Row == row)
                return metric;
        }

        return null;
    }

    private static ColMetric? FindColumnMetric(IReadOnlyList<ColMetric> columns, uint column)
    {
        foreach (var metric in columns)
        {
            if (metric.Col == column)
                return metric;
        }

        return null;
    }

    private static bool CanOverflowSplitPaneText(DisplayCell cell, GridRange? merge) =>
        GridView.CanOverflowCellText(cell.Style, cell.RawValue, cell.DisplayText, merge);

    private readonly record struct OccupiedColumnSpan(uint Start, uint End);

    private readonly struct SplitPaneOccupiedCellMap(Dictionary<uint, OccupiedColumnSpans> spansByRow)
    {
        private readonly Dictionary<uint, OccupiedColumnSpans> _spansByRow = spansByRow;

        public bool Contains(uint row, uint col)
        {
            if (!_spansByRow.TryGetValue(row, out var spans))
                return false;

            return spans.Contains(col);
        }
    }

    private static SplitPaneOccupiedCellMap BuildOccupiedCells(
        IReadOnlyList<DisplayCell> cells,
        CellAddress? editingCell)
    {
        var spansByRow = new Dictionary<uint, OccupiedColumnSpans>(EstimateRowSpanCapacity(cells));
        HashSet<uint>? rowsNeedingNormalize = null;
        foreach (var cell in cells)
        {
            if (!GridView.IsOverflowOccupied(cell, editingCell))
                continue;

            var hasRow = spansByRow.TryGetValue(cell.Row, out var spans);
            var needsNormalize = false;
            AddOccupiedColumn(ref spans, cell.Col, ref needsNormalize);
            if (needsNormalize)
                (rowsNeedingNormalize ??= []).Add(cell.Row);

            if (hasRow)
                spansByRow[cell.Row] = spans;
            else
                spansByRow.Add(cell.Row, spans);
        }

        if (rowsNeedingNormalize is not null)
            NormalizeOccupiedColumnSpans(spansByRow, rowsNeedingNormalize);

        return new SplitPaneOccupiedCellMap(spansByRow);
    }

    private static int EstimateRowSpanCapacity(IReadOnlyList<DisplayCell> cells)
    {
        if (cells.Count == 0)
            return 0;

        var rowCount = 1;
        var lastRow = cells[0].Row;
        for (var i = 1; i < cells.Count; i++)
        {
            var row = cells[i].Row;
            if (row == lastRow)
                continue;

            rowCount++;
            lastRow = row;
        }

        return rowCount;
    }

    private static void AddOccupiedColumn(ref OccupiedColumnSpans spans, uint col, ref bool needsNormalize) =>
        spans.Add(col, ref needsNormalize);

    private static void NormalizeOccupiedColumnSpans(
        Dictionary<uint, OccupiedColumnSpans> spansByRow,
        HashSet<uint> rowsNeedingNormalize)
    {
        foreach (var row in rowsNeedingNormalize)
        {
            var spans = spansByRow[row];
            if (spans.Count <= 1)
                continue;

            var normalized = spans;
            normalized.Normalize();
            spansByRow[row] = normalized;
        }
    }

    private struct OccupiedColumnSpans
    {
        private OccupiedColumnSpan _first;
        private OccupiedColumnSpan _second;
        private List<OccupiedColumnSpan>? _overflow;
        private int _count;

        public readonly int Count => _count;

        public void Add(uint col, ref bool needsNormalize)
        {
            if (_count == 0)
            {
                _first = new OccupiedColumnSpan(col, col);
                _count = 1;
                return;
            }

            if (TryAddToExisting(col))
                return;

            var span = new OccupiedColumnSpan(col, col);
            if (_count == 1)
            {
                if (col < _first.Start)
                    needsNormalize = true;

                _second = span;
                _count = 2;
                return;
            }

            if (col < LastSpan.End)
                needsNormalize = true;

            _overflow ??= [];
            _overflow.Add(span);
            _count++;
        }

        public readonly bool Contains(uint col)
        {
            if (_count == 0 || col < _first.Start)
                return false;

            if (col <= _first.End)
                return true;

            if (_count == 1 || col < _second.Start)
                return false;

            if (col <= _second.End)
                return true;

            if (_overflow is null)
                return false;

            foreach (var span in _overflow)
            {
                if (col < span.Start)
                    return false;

                if (col <= span.End)
                    return true;
            }

            return false;
        }

        public void Normalize()
        {
            if (_count == 2 && _overflow is null)
            {
                NormalizePair();
                return;
            }

            if (_overflow is null)
                return;

            _overflow.Add(_first);
            _overflow.Add(_second);
            _overflow.Sort(static (left, right) => left.Start.CompareTo(right.Start));

            var writeIndex = 0;
            for (var readIndex = 1; readIndex < _overflow.Count; readIndex++)
            {
                var current = _overflow[readIndex];
                var merged = _overflow[writeIndex];
                if (CanMerge(merged, current))
                {
                    _overflow[writeIndex] = Merge(merged, current);
                    continue;
                }

                writeIndex++;
                _overflow[writeIndex] = current;
            }

            var keepCount = writeIndex + 1;
            if (keepCount < _overflow.Count)
                _overflow.RemoveRange(keepCount, _overflow.Count - keepCount);

            _first = _overflow[0];
            _count = _overflow.Count;
            if (_count > 1)
                _second = _overflow[1];

            _overflow.RemoveRange(0, Math.Min(2, _overflow.Count));
            if (_overflow.Count == 0)
                _overflow = null;
        }

        private readonly OccupiedColumnSpan LastSpan =>
            _overflow is { Count: > 0 } overflow ? overflow[^1] :
            _count > 1 ? _second :
            _first;

        private bool TryAddToExisting(uint col)
        {
            if (TryAddToSpan(ref _first, col))
                return true;

            if (_count > 1 && TryAddToSpan(ref _second, col))
                return true;

            if (_overflow is null)
                return false;

            for (var i = 0; i < _overflow.Count; i++)
            {
                var span = _overflow[i];
                if (!TryAddToSpan(ref span, col))
                    continue;

                _overflow[i] = span;
                return true;
            }

            return false;
        }

        private void NormalizePair()
        {
            if (_second.Start < _first.Start)
                (_first, _second) = (_second, _first);

            if (!CanMerge(_first, _second))
                return;

            _first = Merge(_first, _second);
            _second = default;
            _count = 1;
        }

        private static bool TryAddToSpan(ref OccupiedColumnSpan span, uint col)
        {
            if (col >= span.Start && col <= span.End)
                return true;

            if (span.End < uint.MaxValue && col == span.End + 1)
            {
                span = new OccupiedColumnSpan(span.Start, col);
                return true;
            }

            if (span.Start > 0 && col == span.Start - 1)
            {
                span = new OccupiedColumnSpan(col, span.End);
                return true;
            }

            return false;
        }

        private static bool CanMerge(OccupiedColumnSpan left, OccupiedColumnSpan right) =>
            right.Start <= left.End ||
            (left.End < uint.MaxValue && right.Start == left.End + 1);

        private static OccupiedColumnSpan Merge(OccupiedColumnSpan left, OccupiedColumnSpan right) =>
            new(left.Start, Math.Max(left.End, right.End));
    }

    private static double SumEmptyOverflowColumnWidths(
        DisplayCell cell,
        SplitPaneColumnMetricLookup columns,
        SplitPaneOccupiedCellMap occupied)
    {
        double width = 0;
        var nextCol = cell.Col + 1;
        while (columns.TryGetValue(nextCol, out var nextMetric) &&
               !occupied.Contains(cell.Row, nextCol))
        {
            width += nextMetric!.Width;
            nextCol++;
        }

        return width;
    }

    // Leftward companion to SumEmptyOverflowColumnWidths: used for Right/Center-aligned text,
    // which (like real Excel) overflows over empty cells to the left of the anchor column.
    private static double SumEmptyOverflowColumnWidthsLeft(
        DisplayCell cell,
        SplitPaneColumnMetricLookup columns,
        SplitPaneOccupiedCellMap occupied)
    {
        double width = 0;
        if (cell.Col <= 1)
            return width;

        var prevCol = cell.Col - 1;
        while (columns.TryGetValue(prevCol, out var prevMetric) &&
               !occupied.Contains(cell.Row, prevCol))
        {
            width += prevMetric!.Width;
            if (prevCol == 1)
                break;
            prevCol--;
        }

        return width;
    }

    // Finds the portion of a merge's column range ([merge.Start.Col, merge.End.Col]) that is visible
    // within a single pane's column lookup, returning that pane-local box's left offset (of its
    // leftmost in-range column) and total width — or null if the pane doesn't show any of those
    // columns at all. Used to render a merge's footprint separately in every pane it crosses.
    // `exclude`, when given, skips any column also present there — used so a secondary (non-anchor)
    // pane's box only covers the part of the merge NOT already shown by the anchor's own pane, even
    // in the degenerate case where two panes' column sets overlap.
    private static (double Offset, double Size)? FindMergeColumnSpan(
        SplitPaneColumnMetricLookup columns,
        GridRange merge,
        SplitPaneColumnMetricLookup? exclude = null)
    {
        double total = 0;
        uint? minCol = null;
        double minOffset = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var metric = columns[i];
            if (metric.Col < merge.Start.Col || metric.Col > merge.End.Col)
                continue;
            if (exclude is { } excludeColumns && excludeColumns.TryGetValue(metric.Col, out _))
                continue;

            total += metric.Width;
            if (minCol is null || metric.Col < minCol.Value)
            {
                minCol = metric.Col;
                minOffset = metric.LeftOffset;
            }
        }

        return minCol is null ? null : (minOffset, total);
    }

    // Row counterpart to FindMergeColumnSpan: the portion of a merge's row range visible within a
    // single pane's row lookup. `exclude`, when given, skips any row also present there — used so a
    // secondary (non-anchor) row band's box only covers the part of the merge NOT already shown by
    // the anchor's own row band, even in the degenerate case where two bands' row sets overlap.
    private static (double Offset, double Size)? FindMergeRowSpan(
        SplitPaneRowMetricLookup rows,
        GridRange merge,
        SplitPaneRowMetricLookup? exclude = null)
    {
        double total = 0;
        uint? minRow = null;
        double minOffset = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var metric = rows[i];
            if (metric.Row < merge.Start.Row || metric.Row > merge.End.Row)
                continue;
            if (exclude is { } excludeRows && excludeRows.TryGetValue(metric.Row, out _))
                continue;

            total += metric.Height;
            if (minRow is null || metric.Row < minRow.Value)
            {
                minRow = metric.Row;
                minOffset = metric.TopOffset;
            }
        }

        return minRow is null ? null : (minOffset, total);
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
