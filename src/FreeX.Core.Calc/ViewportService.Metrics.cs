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

    /// <summary>
    /// <paramref name="frozenRows"/> is the caller's EFFECTIVE frozen-row count -- for
    /// <see cref="GetViewport"/>/<see cref="ComputeRowMetricsSummary"/> this is
    /// <c>request.FrozenRowsOverride ?? sheet.FrozenRows</c>, so a per-view Freeze Panes override
    /// (e.g. <c>WorkbookSession.GetEffectiveFrozenRows</c>) governs the pinned-row band instead of
    /// always falling back to the shared <see cref="Sheet.FrozenRows"/> field.
    /// </summary>
    private static IReadOnlyList<RowMetric> BuildFrozenAwareRowMetrics(Sheet sheet, uint startRow, double availableHeight, uint frozenRows)
    {
        frozenRows = Math.Min(frozenRows, CellAddress.MaxRow);
        if (frozenRows == 0)
        {
            var rows = BuildRowMetrics(sheet, startRow, CellAddress.MaxRow, availableHeight);
            return PrependScrolledPastMergeAnchorRows(sheet, 1, rows);
        }

        var pinnedRows = BuildRowMetrics(sheet, 1, frozenRows, availableHeight);
        var pinnedHeight = SumRowHeights(pinnedRows);
        var remainingHeight = Math.Max(0, availableHeight - pinnedHeight);
        var bodyStart = Math.Max(startRow, frozenRows + 1);
        if (remainingHeight <= 0 || bodyStart > CellAddress.MaxRow)
            return pinnedRows;

        var bodyRows = PrependScrolledPastMergeAnchorRows(
            sheet,
            frozenRows + 1,
            BuildRowMetrics(sheet, bodyStart, CellAddress.MaxRow, remainingHeight));

        return CombineRowsWithOffset(pinnedRows, bodyRows, pinnedHeight);
    }

    /// <summary>
    /// <paramref name="frozenCols"/> is the caller's EFFECTIVE frozen-column count -- see the
    /// remarks on <see cref="BuildFrozenAwareRowMetrics"/>.
    /// </summary>
    private static IReadOnlyList<ColMetric> BuildFrozenAwareColMetrics(Sheet sheet, uint startCol, double availableWidth, uint frozenCols)
    {
        frozenCols = Math.Min(frozenCols, CellAddress.MaxCol);
        if (frozenCols == 0)
        {
            var columns = BuildColMetrics(sheet, startCol, CellAddress.MaxCol, availableWidth);
            return PrependScrolledPastMergeAnchorCols(sheet, 1, columns);
        }

        var pinnedColumns = BuildColMetrics(sheet, 1, frozenCols, availableWidth);
        var pinnedWidth = SumColumnWidths(pinnedColumns);
        var remainingWidth = Math.Max(0, availableWidth - pinnedWidth);
        var bodyStart = Math.Max(startCol, frozenCols + 1);
        if (remainingWidth <= 0 || bodyStart > CellAddress.MaxCol)
            return pinnedColumns;

        var bodyColumns = PrependScrolledPastMergeAnchorCols(
            sheet,
            frozenCols + 1,
            BuildColMetrics(sheet, bodyStart, CellAddress.MaxCol, remainingWidth));

        return CombineColumnsWithOffset(pinnedColumns, bodyColumns, pinnedWidth);
    }

    /// <summary>
    /// When the viewport has scrolled so the visible window's first row (<c>rows[0].Row</c>) is
    /// past a merge's anchor row -- the anchor sits above <paramref name="lookbackFloorRow"/>
    /// (the first row this metrics band is even allowed to represent -- 1 for the unfrozen case,
    /// or one past the last frozen row for the frozen body band, so an anchor already covered by
    /// the frozen band's own metrics is never re-added here) -- but the merge still extends into
    /// the visible window, Excel keeps showing the merge's visible remainder (fill, border, text),
    /// simply clipped at the top of the window exactly like it clips a very tall single row. The
    /// merge's value/style live only on the anchor cell, so the anchor row must stay addressable
    /// (as a zero-height metric, mirroring how <see cref="IsHiddenMergeAnchorRowWithVisibleRemainder"/>
    /// keeps a hidden anchor addressable) for the still-visible remainder to keep drawing instead
    /// of vanishing outright.
    ///
    /// EVERY row between the earliest such anchor and the window's own first row is materialized
    /// as a zero-height metric here too -- not just the anchor row itself. WPF's GridView merge
    /// surface only needs the anchor row present (it sums whichever of the merge's other rows
    /// happen to be in the metrics, tolerating gaps), but Avalonia's grid builder
    /// (MainWindow.BuildSheetGrid's ResolveVisibleMergeAnchor/ResolveVisibleMergeSpan) walks a
    /// merge's remaining extent by literal, CONSECUTIVE sheet-row numbers to size the
    /// anchor's rendered row-span -- a lone anchor entry separated from the window by a gap (e.g.
    /// anchor row 5, window starting row 7, with row 6 entirely absent) breaks that walk on its
    /// very first step and collapses the rendered span back down to just the zero-height anchor
    /// row, leaving the genuinely visible remainder (rows 7-10) unrendered. Filling every
    /// intervening row keeps the sheet-row-number sequence contiguous with the grid-index
    /// sequence so that walk -- and WPF's gap-tolerant summation -- both see a correct span.
    ///
    /// Bounded by <see cref="MaxViewportListCapacityHint"/>: an anchor scrolled an enormous
    /// distance above the window (a colossal merge, or a jump-scroll past a huge banner merge)
    /// would otherwise require materializing hundreds of thousands of placeholder rows just to
    /// bridge the gap. Past that bound this skips prepending entirely for that merge, leaving
    /// both shells at their pre-existing behavior for that one pathological case: WPF's merge
    /// surface simply won't draw the remainder (unfixed, same as before this method existed), and
    /// Avalonia's own substitute-anchor fallback keeps working unaffected, since the true anchor
    /// is never exposed to it in the first place.
    /// </summary>
    private static IReadOnlyList<RowMetric> PrependScrolledPastMergeAnchorRows(
        Sheet sheet, uint lookbackFloorRow, IReadOnlyList<RowMetric> rows)
    {
        if (rows.Count == 0)
            return rows;

        var windowStartRow = rows[0].Row;
        if (windowStartRow <= lookbackFloorRow)
            return rows;

        uint? earliestAnchorRow = null;
        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start.Row < lookbackFloorRow || region.Start.Row >= windowStartRow)
                continue;
            if (region.End.Row < windowStartRow)
                continue;

            if (earliestAnchorRow is null || region.Start.Row < earliestAnchorRow)
                earliestAnchorRow = region.Start.Row;
        }

        if (earliestAnchorRow is not { } anchorRow)
            return rows;

        var gap = (long)windowStartRow - anchorRow;
        if (gap > MaxViewportListCapacityHint)
            return rows;

        var combined = new List<RowMetric>((int)gap + rows.Count);
        for (var r = anchorRow; r < windowStartRow; r++)
            combined.Add(new RowMetric(r, 0, 0));
        combined.AddRange(rows);
        return combined;
    }

    /// <summary>Column counterpart of <see cref="PrependScrolledPastMergeAnchorRows"/>.</summary>
    private static IReadOnlyList<ColMetric> PrependScrolledPastMergeAnchorCols(
        Sheet sheet, uint lookbackFloorCol, IReadOnlyList<ColMetric> columns)
    {
        if (columns.Count == 0)
            return columns;

        var windowStartCol = columns[0].Col;
        if (windowStartCol <= lookbackFloorCol)
            return columns;

        uint? earliestAnchorCol = null;
        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start.Col < lookbackFloorCol || region.Start.Col >= windowStartCol)
                continue;
            if (region.End.Col < windowStartCol)
                continue;

            if (earliestAnchorCol is null || region.Start.Col < earliestAnchorCol)
                earliestAnchorCol = region.Start.Col;
        }

        if (earliestAnchorCol is not { } anchorCol)
            return columns;

        var gap = (long)windowStartCol - anchorCol;
        if (gap > MaxViewportListCapacityHint)
            return columns;

        var combined = new List<ColMetric>((int)gap + columns.Count);
        for (var c = anchorCol; c < windowStartCol; c++)
            combined.Add(new ColMetric(c, 0, 0));
        combined.AddRange(columns);
        return combined;
    }

    /// <summary>
    /// Counts the entries in <paramref name="rows"/> that represent a genuinely on-screen,
    /// scrollable row: past the frozen boundary AND with nonzero height. This intentionally
    /// excludes the zero-height placeholder entries <see cref="PrependScrolledPastMergeAnchorRows"/>
    /// materializes for a merge anchor that has scrolled above the window -- those placeholders
    /// exist purely so the merge's still-visible remainder keeps drawing (see that method's doc
    /// comment) and occupy zero pixels, so they are not actual on-screen rows. A naive count of
    /// every <c>Row &gt; frozenRows</c> entry (regardless of height) would inflate the scrollbar's
    /// ViewportSize/LargeChange and the Page Up/Down jump distance by one row per placeholder
    /// whenever the viewport has scrolled into a tall merge.
    /// </summary>
    public static int CountScrollableRows(IReadOnlyList<RowMetric> rows, uint frozenRows)
    {
        var count = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Row > frozenRows && row.Height > 0)
                count++;
        }

        return count;
    }

    /// <summary>Column counterpart of <see cref="CountScrollableRows"/>.</summary>
    public static int CountScrollableColumns(IReadOnlyList<ColMetric> columns, uint frozenCols)
    {
        var count = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (column.Col > frozenCols && column.Width > 0)
                count++;
        }

        return count;
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

        if (requestedStartRow < ComputeTerminalRowThresholdLowerBound(sheet, maxRow, availableHeight))
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

        if (requestedStartCol < ComputeTerminalColThresholdLowerBound(sheet, maxCol, availableWidth))
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

    /// <summary>
    /// Cheap O(1) LOWER BOUND on <c>BuildTerminalRowMetrics</c>'s <c>terminalThreshold</c> that lets a
    /// caller nowhere near the sheet's bottom skip the expensive bottom-anchored reverse row scan
    /// entirely, instead of paying for the scan and then discarding its result (the pre-existing
    /// <c>requestedStartRow &lt; terminalThreshold</c> check only gated USE of the scan's result, not
    /// whether the scan ran at all).
    ///
    /// However many rows are hidden (manual/filter/group) or custom-height ANYWHERE on the whole
    /// sheet -- <see cref="CanSkipDefaultTerminalRowMetrics"/>'s fast path requires there be NONE of
    /// either -- the reverse scan can never need to walk back further than
    /// <c>hiddenCount + customHeightCount + ceil(availableHeight / DefaultRowHeight)</c> rows from
    /// <paramref name="maxRow"/>: take any window of that many consecutive rows ending at
    /// <paramref name="maxRow"/> and arrange every hidden/custom-height row in the sheet inside it (the
    /// worst possible placement, each contributing zero height) -- the remaining rows are plain
    /// default-height and there are still enough of them to reach <paramref name="availableHeight"/>.
    /// So the real terminal window the full scan would find is never wider than this bound, which
    /// makes <c>maxRow - bound</c> a safe (if not always tight) lower bound on where it starts. A
    /// single stray hidden or resized row elsewhere on a million-row sheet therefore no longer forces
    /// every scroll tick to pay for scanning the whole trailing region -- only genuinely dense
    /// hidden/custom-height counts near the bottom do.
    /// </summary>
    private static uint ComputeTerminalRowThresholdLowerBound(Sheet sheet, uint maxRow, double availableHeight)
    {
        if (sheet.DefaultRowHeight <= 0)
            return 0;

        var defaultRowsNeeded = Math.Ceiling(availableHeight / sheet.DefaultRowHeight);
        if (!double.IsFinite(defaultRowsNeeded) || defaultRowsNeeded < 0)
            return 0;

        long hiddenCount = sheet.HiddenRows.Count;
        hiddenCount += sheet.FilterHiddenRows.Count;
        hiddenCount += sheet.GroupHiddenRows.Count;
        long customCount = sheet.RowHeights.Count;

        var worstCaseSpan = hiddenCount + customCount + (long)defaultRowsNeeded + 1;
        var candidate = (long)maxRow - worstCaseSpan;
        return candidate < 1 ? 0 : (uint)candidate;
    }

    /// <summary>Column counterpart of <see cref="ComputeTerminalRowThresholdLowerBound"/>.</summary>
    private static uint ComputeTerminalColThresholdLowerBound(Sheet sheet, uint maxCol, double availableWidth)
    {
        var defaultWidthPixels = GetDefaultColumnWidthPixels(sheet);
        if (defaultWidthPixels <= 0)
            return 0;

        var defaultColsNeeded = Math.Ceiling(availableWidth / defaultWidthPixels);
        if (!double.IsFinite(defaultColsNeeded) || defaultColsNeeded < 0)
            return 0;

        long hiddenCount = sheet.HiddenCols.Count;
        hiddenCount += sheet.GroupHiddenCols.Count;
        long customCount = sheet.ColumnWidths.Count;

        var worstCaseSpan = hiddenCount + customCount + (long)defaultColsNeeded + 1;
        var candidate = (long)maxCol - worstCaseSpan;
        return candidate < 1 ? 0 : (uint)candidate;
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
