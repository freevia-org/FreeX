using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static bool IsRowHidden(Sheet sheet, uint row) =>
        sheet.IsRowEffectivelyHidden(row);

    /// <summary>
    /// True when <paramref name="row"/> is hidden but is the anchor (top-left) row of a merged
    /// region that still has at least one other visible row. Excel simply collapses a hidden row
    /// inside a taller merged block to zero height rather than hiding the whole merge, so the
    /// anchor row must stay addressable (as a zero-height metric) for the merge's value/style --
    /// which live on the anchor cell -- to still be surfaced at the still-visible remainder.
    /// </summary>
    private static bool IsHiddenMergeAnchorRowWithVisibleRemainder(Sheet sheet, uint row)
    {
        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start.Row != row) continue;

            for (var r = region.Start.Row; r <= region.End.Row; r++)
            {
                if (!IsRowHidden(sheet, r)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="col"/> is hidden but is the anchor (top-left) column of a merged
    /// region that still has at least one other visible column. Mirrors
    /// <see cref="IsHiddenMergeAnchorRowWithVisibleRemainder"/> for horizontal merges.
    /// </summary>
    private static bool IsHiddenMergeAnchorColWithVisibleRemainder(Sheet sheet, uint col)
    {
        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start.Col != col) continue;

            for (var c = region.Start.Col; c <= region.End.Col; c++)
            {
                if (!sheet.IsColEffectivelyHidden(c)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when (<paramref name="row"/>, <paramref name="col"/>) is exactly the anchor cell of a
    /// merged region whose hidden anchor row/column still has a visible remainder (see
    /// <see cref="IsHiddenMergeAnchorRowWithVisibleRemainder"/> and
    /// <see cref="IsHiddenMergeAnchorColWithVisibleRemainder"/>). The merge's value/style live only
    /// on this one anchor cell, so cell-enumeration must expose ONLY this cell for a hidden anchor
    /// row/column -- never any other, unrelated cell that merely happens to share the hidden row or
    /// column (e.g. an unrelated cell in column A of a hidden row that has nothing to do with a
    /// merge anchored in column B).
    /// </summary>
    private static bool IsExposedHiddenMergeAnchorCell(Sheet sheet, uint row, uint col, bool rowHidden, bool colHidden)
    {
        if (!rowHidden && !colHidden)
            return false;

        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start.Row != row || region.Start.Col != col) continue;

            if (rowHidden && region.End.Row > region.Start.Row)
            {
                for (var r = region.Start.Row; r <= region.End.Row; r++)
                {
                    if (!IsRowHidden(sheet, r)) return true;
                }
            }

            if (colHidden && region.End.Col > region.Start.Col)
            {
                for (var c = region.Start.Col; c <= region.End.Col; c++)
                {
                    if (!sheet.IsColEffectivelyHidden(c)) return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<RowMetric> BuildFrozenAwareRowMetrics(Sheet sheet, uint startRow, double availableHeight)
    {
        var frozenRows = Math.Min(sheet.FrozenRows, CellAddress.MaxRow);
        if (frozenRows == 0)
            return BuildRowMetrics(sheet, startRow, CellAddress.MaxRow, availableHeight);

        var pinnedRows = BuildRowMetrics(sheet, 1, frozenRows, availableHeight);
        var pinnedHeight = SumRowHeights(pinnedRows);
        var remainingHeight = Math.Max(0, availableHeight - pinnedHeight);
        var bodyStart = Math.Max(startRow, frozenRows + 1);
        if (remainingHeight <= 0 || bodyStart > CellAddress.MaxRow)
            return pinnedRows;

        return CombineRowsWithOffset(
            pinnedRows,
            BuildRowMetrics(sheet, bodyStart, CellAddress.MaxRow, remainingHeight),
            pinnedHeight);
    }

    private static IReadOnlyList<ColMetric> BuildFrozenAwareColMetrics(Sheet sheet, uint startCol, double availableWidth)
    {
        var frozenCols = Math.Min(sheet.FrozenCols, CellAddress.MaxCol);
        if (frozenCols == 0)
            return BuildColMetrics(sheet, startCol, CellAddress.MaxCol, availableWidth);

        var pinnedColumns = BuildColMetrics(sheet, 1, frozenCols, availableWidth);
        var pinnedWidth = SumColumnWidths(pinnedColumns);
        var remainingWidth = Math.Max(0, availableWidth - pinnedWidth);
        var bodyStart = Math.Max(startCol, frozenCols + 1);
        if (remainingWidth <= 0 || bodyStart > CellAddress.MaxCol)
            return pinnedColumns;

        return CombineColumnsWithOffset(
            pinnedColumns,
            BuildColMetrics(sheet, bodyStart, CellAddress.MaxCol, remainingWidth),
            pinnedWidth);
    }

    private static double SumRowHeights(IReadOnlyList<RowMetric> rows)
    {
        double height = 0;
        for (var i = 0; i < rows.Count; i++)
            height += rows[i].Height;

        return height;
    }

    private static double SumColumnWidths(IReadOnlyList<ColMetric> columns)
    {
        double width = 0;
        for (var i = 0; i < columns.Count; i++)
            width += columns[i].Width;

        return width;
    }

    private static List<RowMetric> CombineRowsWithOffset(
        IReadOnlyList<RowMetric> pinnedRows,
        IReadOnlyList<RowMetric> bodyRows,
        double bodyTopOffset)
    {
        var combined = new List<RowMetric>(pinnedRows.Count + bodyRows.Count);
        combined.AddRange(pinnedRows);
        for (var i = 0; i < bodyRows.Count; i++)
        {
            var row = bodyRows[i];
            combined.Add(row with { TopOffset = row.TopOffset + bodyTopOffset });
        }

        return combined;
    }

    private static List<ColMetric> CombineColumnsWithOffset(
        IReadOnlyList<ColMetric> pinnedColumns,
        IReadOnlyList<ColMetric> bodyColumns,
        double bodyLeftOffset)
    {
        var combined = new List<ColMetric>(pinnedColumns.Count + bodyColumns.Count);
        combined.AddRange(pinnedColumns);
        for (var i = 0; i < bodyColumns.Count; i++)
        {
            var column = bodyColumns[i];
            combined.Add(column with { LeftOffset = column.LeftOffset + bodyLeftOffset });
        }

        return combined;
    }

    private static IReadOnlyList<RowMetric> BuildRowMetrics(Sheet sheet, uint startRow, uint endRow, double availableHeight)
    {
        if (startRow < 1 || endRow < startRow)
            return [];

        var maxRow = Math.Min(endRow, CellAddress.MaxRow);
        var terminalRows = BuildTerminalRowMetrics(sheet, startRow, maxRow, availableHeight);
        if (terminalRows is not null)
            return terminalRows;

        if (TryCreateDefaultRowMetrics(sheet, startRow, maxRow, availableHeight) is { } defaultRows)
            return defaultRows;

        var rowMetrics = new List<RowMetric>(EstimateMetricCapacity(sheet.DefaultRowHeight, availableHeight));
        double topOffset = 0;
        for (uint row = startRow; row <= maxRow; row++)
        {
            if (IsRowHidden(sheet, row))
            {
                if (IsHiddenMergeAnchorRowWithVisibleRemainder(sheet, row))
                    rowMetrics.Add(new RowMetric(row, 0, topOffset));

                continue;
            }

            double height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
            rowMetrics.Add(new RowMetric(row, height, topOffset));
            topOffset += height;
            if (topOffset > availableHeight) break;
        }

        return rowMetrics;
    }

    private static IReadOnlyList<ColMetric> BuildColMetrics(Sheet sheet, uint startCol, uint endCol, double availableWidth)
    {
        if (startCol < 1 || endCol < startCol)
            return [];

        var maxCol = Math.Min(endCol, CellAddress.MaxCol);
        var terminalColumns = BuildTerminalColMetrics(sheet, startCol, maxCol, availableWidth);
        if (terminalColumns is not null)
            return terminalColumns;

        var defaultColumnWidth = GetDefaultColumnWidthPixels(sheet);
        if (TryCreateDefaultColMetrics(sheet, startCol, maxCol, availableWidth, defaultColumnWidth) is { } defaultColumns)
            return defaultColumns;

        var colMetrics = new List<ColMetric>(EstimateMetricCapacity(defaultColumnWidth, availableWidth));
        double leftOffset = 0;
        for (uint col = startCol; col <= maxCol; col++)
        {
            if (sheet.IsColEffectivelyHidden(col))
            {
                if (IsHiddenMergeAnchorColWithVisibleRemainder(sheet, col))
                    colMetrics.Add(new ColMetric(col, 0, leftOffset));

                continue;
            }

            double width = GetColumnWidthPixels(sheet, col);
            colMetrics.Add(new ColMetric(col, width, leftOffset));
            leftOffset += width;
            if (leftOffset > availableWidth) break;
        }

        return colMetrics;
    }

    private static IReadOnlyList<RowMetric>? TryCreateDefaultRowMetrics(
        Sheet sheet,
        uint startRow,
        uint endRow,
        double availableHeight)
    {
        if (sheet.RowHeights.Count != 0 ||
            sheet.HiddenRows.Count != 0 ||
            sheet.FilterHiddenRows.Count != 0 ||
            sheet.GroupHiddenRows.Count != 0 ||
            sheet.DefaultRowHeight <= 0)
        {
            return null;
        }

        var count = CalculateDefaultMetricCount(startRow, endRow, availableHeight, sheet.DefaultRowHeight);
        return count == 0 ? [] : new DefaultRowMetricList(startRow, count, sheet.DefaultRowHeight);
    }

    private static IReadOnlyList<ColMetric>? TryCreateDefaultColMetrics(
        Sheet sheet,
        uint startCol,
        uint endCol,
        double availableWidth,
        double defaultColumnWidth)
    {
        if (sheet.ColumnWidths.Count != 0 ||
            sheet.HiddenCols.Count != 0 ||
            sheet.GroupHiddenCols.Count != 0)
        {
            return null;
        }

        var count = CalculateDefaultMetricCount(startCol, endCol, availableWidth, defaultColumnWidth);
        return count == 0 ? [] : new DefaultColMetricList(startCol, count, defaultColumnWidth);
    }

    private static int CalculateDefaultMetricCount(
        uint start,
        uint end,
        double availableExtent,
        double defaultExtent)
    {
        if (start < 1 || end < start)
            return 0;

        var maxCount = (long)end - start + 1;
        if (availableExtent <= 0)
            return 1;

        if (!double.IsFinite(availableExtent))
            return (int)maxCount;

        var estimate = Math.Floor(availableExtent / defaultExtent) + 1;
        if (!double.IsFinite(estimate) || estimate >= maxCount)
            return (int)maxCount;

        var visibleCount = (long)estimate;
        if (visibleCount < 1)
            visibleCount = 1;
        if (visibleCount > maxCount)
            visibleCount = maxCount;

        return (int)visibleCount;
    }

    private static int EstimateMetricCapacity(double defaultExtent, double availableExtent)
    {
        if (availableExtent <= 0 || defaultExtent <= 0)
            return 0;

        var estimate = Math.Ceiling(availableExtent / defaultExtent) + 1;
        if (!double.IsFinite(estimate) || estimate >= MaxViewportListCapacityHint)
            return MaxViewportListCapacityHint;

        return estimate <= 0 ? 0 : (int)estimate;
    }

    private static List<RowMetric>? BuildTerminalRowMetrics(
        Sheet sheet,
        uint requestedStartRow,
        uint maxRow,
        double availableHeight)
    {
        if (availableHeight <= 0 || maxRow < CellAddress.MaxRow)
            return null;

        if (CanSkipDefaultTerminalRowMetrics(sheet, requestedStartRow, availableHeight))
            return null;

        var rows = new List<(uint Row, double Height)>();
        double totalHeight = 0;
        for (uint row = maxRow; row >= 1; row--)
        {
            if (!IsRowHidden(sheet, row))
            {
                var height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
                rows.Add((row, height));
                totalHeight += height;
                if (totalHeight >= availableHeight)
                    break;
            }

            if (row == 1)
                break;
        }

        rows.Reverse();
        if (rows.Count == 0)
            return null;

        var firstTerminalRow = rows[0].Row;
        var terminalThreshold = firstTerminalRow > 1 ? firstTerminalRow - 1 : 1;
        if (requestedStartRow < terminalThreshold)
            return null;

        var metrics = new List<RowMetric>(rows.Count);
        var topOffset = availableHeight - totalHeight;
        foreach (var (row, height) in rows)
        {
            metrics.Add(new RowMetric(row, height, topOffset));
            topOffset += height;
        }

        return metrics;
    }

    private static bool CanSkipDefaultTerminalRowMetrics(Sheet sheet, uint requestedStartRow, double availableHeight)
    {
        if (sheet.RowHeights.Count != 0
            || sheet.HiddenRows.Count != 0
            || sheet.FilterHiddenRows.Count != 0
            || sheet.GroupHiddenRows.Count != 0
            || sheet.DefaultRowHeight <= 0)
        {
            return false;
        }

        var visibleRowCount = Math.Ceiling(availableHeight / sheet.DefaultRowHeight);
        if (!double.IsFinite(visibleRowCount) || visibleRowCount <= 0 || visibleRowCount >= CellAddress.MaxRow)
            return false;

        var firstTerminalRow = CellAddress.MaxRow - (uint)visibleRowCount + 1;
        var terminalThreshold = firstTerminalRow > 1 ? firstTerminalRow - 1 : 1;
        return requestedStartRow < terminalThreshold;
    }

    private static List<ColMetric>? BuildTerminalColMetrics(
        Sheet sheet,
        uint requestedStartCol,
        uint maxCol,
        double availableWidth)
    {
        if (availableWidth <= 0 || maxCol < CellAddress.MaxCol)
            return null;

        if (CanSkipDefaultTerminalColMetrics(sheet, requestedStartCol, availableWidth))
            return null;

        var columns = new List<(uint Col, double Width)>();
        double totalWidth = 0;
        for (uint col = maxCol; col >= 1; col--)
        {
            if (!sheet.IsColEffectivelyHidden(col))
            {
                var width = GetColumnWidthPixels(sheet, col);
                columns.Add((col, width));
                totalWidth += width;
                if (totalWidth >= availableWidth)
                    break;
            }

            if (col == 1)
                break;
        }

        columns.Reverse();
        if (columns.Count == 0)
            return null;

        var firstTerminalColumn = columns[0].Col;
        var terminalThreshold = firstTerminalColumn > 1 ? firstTerminalColumn - 1 : 1;
        if (requestedStartCol < terminalThreshold)
            return null;

        var metrics = new List<ColMetric>(columns.Count);
        var leftOffset = availableWidth - totalWidth;
        foreach (var (col, width) in columns)
        {
            metrics.Add(new ColMetric(col, width, leftOffset));
            leftOffset += width;
        }

        return metrics;
    }

    private static bool CanSkipDefaultTerminalColMetrics(Sheet sheet, uint requestedStartCol, double availableWidth)
    {
        var defaultWidthPixels = GetDefaultColumnWidthPixels(sheet);
        if (sheet.ColumnWidths.Count != 0
            || sheet.HiddenCols.Count != 0
            || sheet.GroupHiddenCols.Count != 0
            || defaultWidthPixels <= 0)
        {
            return false;
        }

        var visibleColumnCount = Math.Ceiling(availableWidth / defaultWidthPixels);
        if (!double.IsFinite(visibleColumnCount) || visibleColumnCount <= 0 || visibleColumnCount >= CellAddress.MaxCol)
            return false;

        var firstTerminalColumn = CellAddress.MaxCol - (uint)visibleColumnCount + 1;
        var terminalThreshold = firstTerminalColumn > 1 ? firstTerminalColumn - 1 : 1;
        return requestedStartCol < terminalThreshold;
    }

    private sealed class DefaultRowMetricList : IReadOnlyList<RowMetric>
    {
        private readonly uint _startRow;
        private readonly int _count;
        private readonly double _height;

        public DefaultRowMetricList(uint startRow, int count, double height)
        {
            _startRow = startRow;
            _count = count;
            _height = height;
        }

        public int Count => _count;

        public RowMetric this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return new RowMetric(_startRow + (uint)index, _height, index * _height);
            }
        }

        public IEnumerator<RowMetric> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DefaultColMetricList : IReadOnlyList<ColMetric>
    {
        private readonly uint _startCol;
        private readonly int _count;
        private readonly double _width;

        public DefaultColMetricList(uint startCol, int count, double width)
        {
            _startCol = startCol;
            _count = count;
            _width = width;
        }

        public int Count => _count;

        public ColMetric this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return new ColMetric(_startCol + (uint)index, _width, index * _width);
            }
        }

        public IEnumerator<ColMetric> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
