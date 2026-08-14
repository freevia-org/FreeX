using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public readonly record struct WorkbookViewportScrollAxis(
    double Minimum,
    double Maximum,
    double Value,
    double ViewportSize,
    double SmallChange,
    double LargeChange,
    bool IsEnabled);

public readonly record struct WorkbookViewportScrollState(
    WorkbookViewportScrollAxis Vertical,
    WorkbookViewportScrollAxis Horizontal);

public readonly record struct WorkbookViewportCellRevealAxisPlan(
    bool ShouldScroll,
    double Maximum,
    double Value);

public readonly record struct WorkbookViewportCellRevealPlan(
    WorkbookViewportCellRevealAxisPlan Vertical,
    WorkbookViewportCellRevealAxisPlan Horizontal,
    uint? BottomLeftTopRow = null,
    uint? TopRightLeftCol = null);

public static class WorkbookViewportScrollPlanner
{
    private const double MinimumScrollValue = 1;
    public const int DefaultWheelScrollLinesPerNotch = 3;
    public const int MaximumWheelScrollLinesPerNotch = 100;

    /// <summary>
    /// Converts the platform wheel-lines setting into a worksheet row/column step. The Windows
    /// page-scroll sentinel (-1) uses the visible page size; invalid settings use the historic
    /// three-line default, and every result is clamped to a practical range.
    /// </summary>
    public static int NormalizeWheelScrollStep(int wheelScrollLines, double visibleSpan)
    {
        if (wheelScrollLines == -1)
        {
            var pageSize = double.IsFinite(visibleSpan)
                ? Math.Max(1, Math.Round(visibleSpan))
                : DefaultWheelScrollLinesPerNotch;
            return (int)Math.Clamp(pageSize, 1, MaximumWheelScrollLinesPerNotch);
        }

        if (wheelScrollLines <= 0)
            return DefaultWheelScrollLinesPerNotch;

        return Math.Clamp(wheelScrollLines, 1, MaximumWheelScrollLinesPerNotch);
    }

    public static int NormalizeWheelNotches(int delta)
    {
        return NormalizeWheelNotches(delta, unitsPerNotch: 120);
    }

    /// <summary>
    /// Normalizes an Avalonia pointer-wheel delta. Avalonia reports pointer deltas in logical
    /// notch units, while WPF reports the same gesture in 120-unit mouse-wheel ticks. Preserve
    /// the magnitude so high-resolution Linux devices that coalesce several notches into one event
    /// pan by the same number of worksheet rows or columns as the WPF route.
    /// </summary>
    public static int NormalizePointerWheelNotches(double delta)
    {
        return NormalizeWheelNotches(delta, unitsPerNotch: 1);
    }

    private static int NormalizeWheelNotches(double delta, double unitsPerNotch)
    {
        if (!double.IsFinite(delta) || delta == 0)
            return 0;

        var wholeNotches = Math.Truncate(delta / unitsPerNotch);
        if (wholeNotches > int.MaxValue)
            return int.MaxValue;
        if (wholeNotches < -int.MaxValue)
            return -int.MaxValue;

        var notches = (int)wholeNotches;
        return notches != 0 ? notches : Math.Sign(delta);
    }

    /// <summary>
    /// Counts the genuinely on-screen scrollable rows in <paramref name="viewport"/>, floored at 1.
    /// Single neutral owner for what both renderers previously kept as private
    /// <c>CountScrollableRows</c> copies (WPF MainWindow.Viewport.cs, Avalonia MainWindow.cs).
    ///
    /// Takes an explicit frozen-row count -- THIS window's effective Freeze Panes state
    /// (R89-freeze-split-per-window-1) -- rather than a Sheet, so callers pass
    /// viewState.FrozenRows instead of ever falling back to the shared Sheet.FrozenRows.
    ///
    /// Delegates to the guarded FreeX.Core.Calc.ViewportService.CountScrollableRows (R110), which
    /// excludes the zero-height RowMetric placeholders PrependScrolledPastMergeAnchorRows inserts
    /// for a merge anchor that has scrolled above the window. A naive `row.Row > frozenRows` count
    /// counted those placeholders too, inflating both the scrollbar's ViewportSize/LargeChange and
    /// the Page Up/Down jump distance by one row per placeholder whenever the viewport had
    /// scrolled into a tall merge -- real Excel's Page Up/Down always jumps by exactly one
    /// screenful of genuinely on-screen rows.
    /// </summary>
    public static int CountVisibleScrollableRows(ViewportModel viewport, uint frozenRows)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return Math.Max(1, ViewportService.CountScrollableRows(viewport.RowMetrics, frozenRows));
    }

    /// <summary>Column counterpart of <see cref="CountVisibleScrollableRows(ViewportModel, uint)"/>.</summary>
    public static int CountVisibleScrollableColumns(ViewportModel viewport, uint frozenColumns)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return Math.Max(1, ViewportService.CountScrollableColumns(viewport.ColMetrics, frozenColumns));
    }

    /// <summary>
    /// Sheet-resolved convenience overload for callers whose Freeze Panes state is not per-window
    /// (the Avalonia host). A null sheet resolves to zero frozen rows.
    /// </summary>
    public static int CountVisibleScrollableRows(ViewportModel viewport, Sheet? sheet) =>
        CountVisibleScrollableRows(viewport, sheet?.FrozenRows ?? 0);

    /// <summary>Column counterpart of <see cref="CountVisibleScrollableRows(ViewportModel, Sheet?)"/>.</summary>
    public static int CountVisibleScrollableColumns(ViewportModel viewport, Sheet? sheet) =>
        CountVisibleScrollableColumns(viewport, sheet?.FrozenCols ?? 0);

    public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(viewport);

        var visibleRows = (uint)CountVisibleScrollableRows(viewport, sheet.FrozenRows);
        var visibleColumns = (uint)CountVisibleScrollableColumns(viewport, sheet.FrozenCols);
        var (usedMaxRow, usedMaxCol) = CalculateUsedRangeExtents(sheet);
        return new WorkbookViewportScrollState(
            CreateAxis(
                sheet.ViewTopRow ?? GetScrollableRowStart(sheet),
                sheet.FrozenRows,
                CellAddress.MaxRow,
                visibleRows,
                usedMaxRow),
            CreateAxis(
                sheet.ViewLeftCol ?? GetScrollableColumnStart(sheet),
                sheet.FrozenCols,
                CellAddress.MaxCol,
                visibleColumns,
                usedMaxCol));
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue)
    {
        if (sheet is not null)
        {
            return (
                ScrollbarValueToWorksheetIndex(verticalScrollValue, sheet.FrozenRows, CellAddress.MaxRow),
                ScrollbarValueToWorksheetIndex(horizontalScrollValue, sheet.FrozenCols, CellAddress.MaxCol));
        }

        var frozenRows = sheet?.FrozenRows ?? 0;
        var frozenCols = sheet?.FrozenCols ?? 0;
        return (
            ScrollbarValueToWorksheetIndex(verticalScrollValue, frozenRows, CellAddress.MaxRow),
            ScrollbarValueToWorksheetIndex(horizontalScrollValue, frozenCols, CellAddress.MaxCol));
    }

    public static uint ScrollbarValueToWorksheetIndex(
        double scrollbarValue,
        uint frozenCount,
        uint absoluteLimit)
    {
        var scrollValue = scrollbarValue is > 0 and <= uint.MaxValue
            ? (uint)Math.Ceiling(scrollbarValue)
            : 1;
        var origin = frozenCount > 0
            ? (ulong)frozenCount + scrollValue
            : scrollValue;
        return (uint)Math.Clamp(origin, 1UL, absoluteLimit);
    }

    public static uint WorksheetIndexToScrollbarValue(uint worksheetIndex, uint frozenCount)
    {
        if (frozenCount == 0)
            return Math.Max(1, worksheetIndex);

        return worksheetIndex > frozenCount
            ? worksheetIndex - frozenCount
            : 1;
    }

    public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount)
    {
        if (absoluteLimit <= 1)
            return 1;

        return Math.Max(1, absoluteLimit - Math.Min(frozenCount, absoluteLimit - 1));
    }

    /// <summary>
    /// Keeps the content at a viewport origin anchored across a row or column insertion/deletion.
    /// A null result means the edit is below/right of the origin or has no structural delta.
    /// </summary>
    public static uint? PlanStructuralEditOriginShift(
        uint currentOrigin,
        uint editIndex,
        int delta,
        uint absoluteLimit)
    {
        if (delta == 0 || editIndex > currentOrigin)
            return null;

        return (uint)Math.Clamp((long)currentOrigin + delta, 1, absoluteLimit);
    }

    public static uint GetScrollableRowLimit(Sheet? sheet) =>
        CalculateScrollableLimit(CellAddress.MaxRow, sheet?.FrozenRows ?? 0);

    public static uint GetScrollableRowLimit(uint frozenRows) =>
        CalculateScrollableLimit(CellAddress.MaxRow, frozenRows);

    public static uint GetScrollableColumnLimit(Sheet? sheet) =>
        CalculateScrollableLimit(CellAddress.MaxCol, sheet?.FrozenCols ?? 0);

    public static uint GetScrollableColumnLimit(uint frozenColumns) =>
        CalculateScrollableLimit(CellAddress.MaxCol, frozenColumns);

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        uint frozenRows,
        uint frozenColumns,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        (
            ScrollbarValueToWorksheetIndex(verticalScrollValue, frozenRows, CellAddress.MaxRow),
            ScrollbarValueToWorksheetIndex(horizontalScrollValue, frozenColumns, CellAddress.MaxCol)
        );

    public static uint ClampViewportOrigin(double rawValue, uint absoluteLimit, uint visibleSpan)
    {
        var value = rawValue is > 0 and <= uint.MaxValue ? (uint)Math.Ceiling(rawValue) : 1;
        return Math.Clamp(value, 1, CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan));
    }

    public static double CalculateViewportAvailableWidth(
        double gridWidth,
        double rowHeaderWidth,
        double zoomLevel)
    {
        var effectiveZoom = zoomLevel > 0 ? zoomLevel : 1.0;

        // gridWidth (e.g. SheetGrid.ActualWidth) is the WPF physical/unscaled layout size of the
        // container -- it is NOT affected by the zoom RenderTransform. rowHeaderWidth, however, is
        // a logical (already-unscaled) pixel width used everywhere else under the RenderTransform
        // (row/column offsets, hit-testing, etc). To combine them correctly we must first divide
        // the physical container width by zoom to get it into the same logical space, and only
        // then subtract the logical row-header width. Subtracting first and dividing afterwards
        // (the previous behavior) is only correct at zoom = 100%.
        return Math.Max(0, gridWidth / effectiveZoom - rowHeaderWidth);
    }

    /// <summary>
    /// Same dimensional fix as <see cref="CalculateViewportAvailableWidth"/>, for the vertical
    /// axis: gridHeight is the physical/unscaled container height, colHeaderHeight is a logical
    /// (already-unscaled) pixel height, so gridHeight must be divided by zoom before subtracting
    /// the logical header height.
    /// </summary>
    public static double CalculateViewportAvailableHeight(
        double gridHeight,
        double colHeaderHeight,
        double zoomLevel)
    {
        var effectiveZoom = zoomLevel > 0 ? zoomLevel : 1.0;
        return Math.Max(0, gridHeight / effectiveZoom - colHeaderHeight);
    }

    public static uint CalculateOpenedWorksheetScrollValue(
        uint? savedTopLeftIndex,
        uint fallbackIndex,
        uint absoluteLimit,
        uint frozenCount = 0)
    {
        var worksheetIndex = Math.Clamp(savedTopLeftIndex ?? fallbackIndex, 1, absoluteLimit);
        return WorksheetIndexToScrollbarValue(worksheetIndex, frozenCount);
    }

    public static WorkbookViewportCellRevealPlan PlanCellReveal(
        ViewportModel viewport,
        Sheet? sheet,
        CellAddress target,
        double currentVerticalMaximum,
        double currentHorizontalMaximum)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        // Per-window Freeze Panes (R93 keyboard-nav-scroll-reveal-1): the shared Sheet.FrozenRows/
        // FrozenCols reflect only whichever window last set them, so a keyboard-nav reveal in a
        // window whose Freeze Panes differ from a sibling window's must NOT read those fields.
        // viewport.FrozenPanes already carries this call's effective per-view frozen counts --
        // WorkbookSession.BuildViewport bakes GetEffectiveFrozenRows()/GetEffectiveFrozenCols()
        // into it via ViewportRequest's FrozenRowsOverride/FrozenColsOverride (see
        // WorkbookSession's _viewFrozenRowsOverrides remarks) -- exactly like every other viewport
        // consumer already does (GridView.Rendering.cs's frozenColsRight/Left,
        // GridView.Rendering.Headers.cs's RenderFreezeDivider, Avalonia's
        // AddFreezePaneDividerOverlay). A null FrozenPanes means this call's effective frozen
        // counts are genuinely (0, 0) -- it is NOT a signal to fall back to the shared Sheet.
        var frozenRows = viewport.FrozenPanes?.Rows ?? 0;
        var frozenColumns = viewport.FrozenPanes?.Cols ?? 0;

        // Window > Split (viewport.SplitPanes) is distinct from Freeze Panes: the split's top/left
        // panes are pinned (never scroll) and the bottom-left/top-right panes can be scrolled
        // *independently* of the main scrollbars via the host's own per-pane offsets (see
        // MainWindow.Viewport.cs's _splitPaneViewportOffsets / TryScrollIndependentSplitPane).
        // SetSplitPanesCommand always zeroes FrozenRows/FrozenCols, so a plain frozen-pane-shaped
        // reveal here would only ever move the main (bottom-right) scrollbars -- it can never reach
        // a cell that's out of view in an independently-scrolled bottom-left or top-right pane.
        var splitPanes = viewport.SplitPanes;
        var splitRow = splitPanes?.Row;
        var splitColumn = splitPanes?.Column;
        var isFourWaySplit = splitRow.HasValue && splitColumn.HasValue;
        var targetIsInPinnedTopRows = splitRow is { } sr && target.Row < sr;
        var targetIsInPinnedLeftColumns = splitColumn is { } sc && target.Col < sc;

        uint? bottomLeftTopRow = null;
        WorkbookViewportCellRevealAxisPlan verticalPlan;
        if (targetIsInPinnedTopRows)
        {
            // Always fully shown in the pinned top pane(s), regardless of the bottom panes' scroll.
            verticalPlan = new WorkbookViewportCellRevealAxisPlan(false, currentVerticalMaximum, 0);
        }
        else if (isFourWaySplit && targetIsInPinnedLeftColumns)
        {
            var bottomLeftRows = splitPanes!.BottomLeftRows ?? viewport.RowMetrics;
            bottomLeftTopRow = PlanSplitPaneOffsetReveal(
                target.Row,
                CellAddress.MaxRow,
                GetScrollableRowWindow(bottomLeftRows, frozenCount: 0, target.Row));
            verticalPlan = new WorkbookViewportCellRevealAxisPlan(false, currentVerticalMaximum, 0);
        }
        else
        {
            verticalPlan = PlanCellRevealAxis(
                target.Row,
                frozenRows,
                CellAddress.MaxRow,
                currentVerticalMaximum,
                GetScrollableRowWindow(viewport.RowMetrics, frozenRows, target.Row));
        }

        uint? topRightLeftCol = null;
        WorkbookViewportCellRevealAxisPlan horizontalPlan;
        if (targetIsInPinnedLeftColumns)
        {
            horizontalPlan = new WorkbookViewportCellRevealAxisPlan(false, currentHorizontalMaximum, 0);
        }
        else if (isFourWaySplit && targetIsInPinnedTopRows)
        {
            var topRightColumns = splitPanes!.TopRightColumns ?? viewport.ColMetrics;
            topRightLeftCol = PlanSplitPaneOffsetReveal(
                target.Col,
                CellAddress.MaxCol,
                GetScrollableColumnWindow(topRightColumns, frozenCount: 0, target.Col));
            horizontalPlan = new WorkbookViewportCellRevealAxisPlan(false, currentHorizontalMaximum, 0);
        }
        else
        {
            horizontalPlan = PlanCellRevealAxis(
                target.Col,
                frozenColumns,
                CellAddress.MaxCol,
                currentHorizontalMaximum,
                GetScrollableColumnWindow(viewport.ColMetrics, frozenColumns, target.Col));
        }

        return new WorkbookViewportCellRevealPlan(verticalPlan, horizontalPlan, bottomLeftTopRow, topRightLeftCol);
    }

    private static uint? PlanSplitPaneOffsetReveal(
        uint targetIndex,
        uint absoluteLimit,
        ScrollableMetricWindow window)
    {
        if (window.Count == 0 || window.ContainsTarget)
            return null;

        return CalculateScrollValueToRevealCell(
            targetIndex,
            window.First,
            window.Last,
            absoluteLimit,
            (uint)window.Count);
    }

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit)
    {
        var visibleSpan = Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
        return CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit,
            visibleSpan);
    }

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit,
        uint visibleSpan)
    {
        var maxOrigin = CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);
        if (targetIndex < firstVisibleIndex)
            return Math.Clamp(targetIndex, 1, maxOrigin);
        if (targetIndex > lastVisibleIndex)
            return Math.Clamp(targetIndex - (lastVisibleIndex - firstVisibleIndex), 1, maxOrigin);
        return Math.Clamp(firstVisibleIndex, 1, maxOrigin);
    }

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex) =>
        CalculateScrollValueToRevealCell(targetIndex, firstVisibleIndex, lastVisibleIndex, CellAddress.MaxRow);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue,
        uint absoluteLimit)
    {
        return Math.Min(absoluteLimit, Math.Max(currentMaximum, desiredScrollValue));
    }

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue) =>
        CalculateScrollbarMaximumForKeyboardReveal(currentMaximum, desiredScrollValue, CellAddress.MaxRow);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        uint absoluteLimit)
    {
        return CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            visibleSpan: 1,
            absoluteLimit);
    }

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        double visibleSpan,
        uint absoluteLimit)
    {
        var maxOrigin = CalculateMaximumViewportOrigin(absoluteLimit, ToVisibleSpan(visibleSpan));
        if (currentValue < currentMaximum || currentMaximum >= maxOrigin)
            return (currentMaximum, currentValue);

        var step = Math.Max(1, smallChange);
        var maximum = Math.Min(maxOrigin, currentMaximum + step);
        var value = Math.Min(maximum, currentValue + step);
        return (maximum, value);
    }

    public static (double Maximum, double Value) CalculateWheelScroll(
        double currentValue,
        double currentMaximum,
        int wheelNotches,
        double stepPerNotch,
        double visibleSpan,
        uint absoluteLimit)
    {
        var step = Math.Max(1, stepPerNotch);
        var desired = currentValue - wheelNotches * step;
        var maxOrigin = CalculateMaximumViewportOrigin(absoluteLimit, ToVisibleSpan(visibleSpan));
        var maximum = Math.Min(maxOrigin, Math.Max(currentMaximum, desired));
        var value = Math.Clamp(desired, 1, maximum);
        return (maximum, value);
    }

    public static (double Maximum, double Value) CalculateDragAutoScroll(
        double currentValue,
        double currentMaximum,
        int direction,
        double step,
        double visibleSpan,
        uint absoluteLimit)
    {
        if (direction == 0)
            return (currentMaximum, currentValue);

        var effectiveStep = Math.Max(1, step);
        var desired = currentValue + Math.Sign(direction) * effectiveStep;
        var maxOrigin = CalculateMaximumViewportOrigin(absoluteLimit, ToVisibleSpan(visibleSpan));
        var maximum = Math.Min(maxOrigin, Math.Max(currentMaximum, desired));
        var value = Math.Clamp(desired, 1, maximum);
        return (maximum, value);
    }

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)
    {
        visibleSpan = Math.Max(1, visibleSpan);
        return visibleSpan >= absoluteLimit ? 1 : absoluteLimit - visibleSpan + 1;
    }

    public static uint CalculateScrollbarMaximumForUsedRange(
        uint usedMax,
        uint visibleSpan,
        uint currentScrollValue,
        uint absoluteLimit)
    {
        var maxOrigin = CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);
        return Math.Min(maxOrigin, Math.Max(Math.Max(usedMax, visibleSpan), currentScrollValue));
    }

    public static (uint UsedMaxRow, uint UsedMaxCol) CalculateUsedRangeExtents(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var usedRange = sheet.GetUsedRange();
        return usedRange is null
            ? (1u, 1u)
            : (usedRange.Value.End.Row, usedRange.Value.End.Col);
    }

    private static WorkbookViewportCellRevealAxisPlan PlanCellRevealAxis(
        uint targetIndex,
        uint frozenCount,
        uint absoluteLimit,
        double currentMaximum,
        ScrollableMetricWindow window)
    {
        if (targetIndex <= frozenCount || window.Count == 0 || window.ContainsTarget)
            return new WorkbookViewportCellRevealAxisPlan(
                ShouldScroll: false,
                Maximum: currentMaximum,
                Value: 0);

        var scrollableLimit = CalculateScrollableLimit(absoluteLimit, frozenCount);
        var scrollValue = CalculateScrollValueToRevealCell(
            WorksheetIndexToScrollbarValue(targetIndex, frozenCount),
            WorksheetIndexToScrollbarValue(window.First, frozenCount),
            WorksheetIndexToScrollbarValue(window.Last, frozenCount),
            scrollableLimit,
            (uint)window.Count);
        var maximum = CalculateScrollbarMaximumForKeyboardReveal(
            currentMaximum,
            scrollValue,
            scrollableLimit);
        return new WorkbookViewportCellRevealAxisPlan(
            ShouldScroll: true,
            Maximum: maximum,
            Value: scrollValue);
    }

    private static WorkbookViewportScrollAxis CreateAxis(
        uint worksheetOrigin,
        uint frozenCount,
        uint absoluteLimit,
        uint visibleSpan,
        uint usedMax)
    {
        var scrollableLimit = CalculateScrollableLimit(absoluteLimit, frozenCount);
        var value = WorksheetIndexToScrollbarValue(worksheetOrigin, frozenCount);
        var usedMaxScrollValue = WorksheetIndexToScrollbarValue(usedMax, frozenCount);
        var maximum = CalculateScrollbarMaximumForUsedRange(usedMaxScrollValue, visibleSpan, value, scrollableLimit);
        value = Math.Clamp(value, 1, maximum);
        var largeChange = Math.Max(1, visibleSpan - 1);
        return new WorkbookViewportScrollAxis(
            MinimumScrollValue,
            maximum,
            value,
            Math.Max(1, visibleSpan),
            SmallChange: 1,
            LargeChange: largeChange,
            IsEnabled: maximum > MinimumScrollValue);
    }

    /// <summary>
    /// The sheet's current scrollable row origin: its persisted <see cref="Sheet.ViewTopRow"/>, or
    /// the first row past the frozen pane when the sheet has never been scrolled.
    /// </summary>
    public static uint GetViewportRowOrigin(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return sheet.ViewTopRow ?? GetScrollableRowStart(sheet);
    }

    /// <summary>Column counterpart of <see cref="GetViewportRowOrigin"/>.</summary>
    public static uint GetViewportColumnOrigin(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return sheet.ViewLeftCol ?? GetScrollableColumnStart(sheet);
    }

    private static uint GetScrollableRowStart(Sheet sheet) =>
        Math.Min(CellAddress.MaxRow, Math.Max(1, sheet.FrozenRows + 1));

    private static uint GetScrollableColumnStart(Sheet sheet) =>
        Math.Min(CellAddress.MaxCol, Math.Max(1, sheet.FrozenCols + 1));

    private static ScrollableMetricWindow GetScrollableRowWindow(
        IReadOnlyList<RowMetric> rowMetrics,
        uint frozenCount,
        uint targetRow)
    {
        var result = new ScrollableMetricWindow();
        foreach (var metric in rowMetrics)
        {
            if (metric.Row <= frozenCount)
                continue;

            result = result.Include(metric.Row, metric.Row == targetRow);
        }

        return result;
    }

    private static ScrollableMetricWindow GetScrollableColumnWindow(
        IReadOnlyList<ColMetric> colMetrics,
        uint frozenCount,
        uint targetColumn)
    {
        var result = new ScrollableMetricWindow();
        foreach (var metric in colMetrics)
        {
            if (metric.Col <= frozenCount)
                continue;

            result = result.Include(metric.Col, metric.Col == targetColumn);
        }

        return result;
    }

    private readonly record struct ScrollableMetricWindow(uint First, uint Last, int Count, bool ContainsTarget)
    {
        public ScrollableMetricWindow Include(uint index, bool isTarget) =>
            Count == 0
                ? new ScrollableMetricWindow(index, index, 1, isTarget)
                : this with
                {
                    Last = index,
                    Count = Count + 1,
                    ContainsTarget = ContainsTarget || isTarget
                };
    }

    private static uint ToVisibleSpan(double visibleSpan)
    {
        return visibleSpan is > 0 and <= uint.MaxValue
            ? Math.Max(1, (uint)Math.Ceiling(visibleSpan))
            : 1;
    }
}
