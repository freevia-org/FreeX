using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ViewportScrollCalculator
{
    public static int NormalizeWheelNotches(int delta) =>
        WorkbookViewportScrollPlanner.NormalizeWheelNotches(delta);

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        WorkbookViewportScrollPlanner.CalculateViewportOrigin(sheet, verticalScrollValue, horizontalScrollValue);

    public static uint ScrollbarValueToWorksheetIndex(
        double scrollbarValue,
        uint frozenCount,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.ScrollbarValueToWorksheetIndex(scrollbarValue, frozenCount, absoluteLimit);

    public static uint WorksheetIndexToScrollbarValue(uint worksheetIndex, uint frozenCount) =>
        WorkbookViewportScrollPlanner.WorksheetIndexToScrollbarValue(worksheetIndex, frozenCount);

    public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount) =>
        WorkbookViewportScrollPlanner.CalculateScrollableLimit(absoluteLimit, frozenCount);

    public static uint GetScrollableRowLimit(Sheet? sheet) =>
        WorkbookViewportScrollPlanner.GetScrollableRowLimit(sheet);

    public static uint GetScrollableColumnLimit(Sheet? sheet) =>
        WorkbookViewportScrollPlanner.GetScrollableColumnLimit(sheet);

    /// <summary>
    /// Per-window Freeze Panes counterpart of <see cref="GetScrollableRowLimit(Sheet?)"/>: resolves
    /// against an explicit frozen-row count instead of reading <c>Sheet.FrozenRows</c> off the shared
    /// Sheet (R89-freeze-split-per-window-1), so a caller holding this window's own effective frozen
    /// count (<c>MainWindow.GetEffectiveViewState</c>) gets a scrollable-row limit reflecting THIS
    /// window's Freeze Panes state rather than a sibling "New Window"'s. Delegates to the same pure
    /// <see cref="CalculateScrollableLimit"/> the Sheet-based overload above uses.
    /// </summary>
    public static uint GetScrollableRowLimit(uint frozenRows) =>
        WorkbookViewportScrollPlanner.CalculateScrollableLimit(CellAddress.MaxRow, frozenRows);

    /// <summary>Per-window counterpart of <see cref="GetScrollableColumnLimit(Sheet?)"/> -- see remarks on <see cref="GetScrollableRowLimit(uint)"/>.</summary>
    public static uint GetScrollableColumnLimit(uint frozenCols) =>
        WorkbookViewportScrollPlanner.CalculateScrollableLimit(CellAddress.MaxCol, frozenCols);

    /// <summary>
    /// Per-window Freeze Panes counterpart of <see cref="CalculateViewportOrigin(Sheet?, double, double)"/>:
    /// resolves the scrollbar-to-worksheet-index mapping against explicit frozen-row/frozen-column
    /// counts instead of the shared Sheet's (R89-freeze-split-per-window-1).
    /// </summary>
    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        uint frozenRows,
        uint frozenCols,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        (
            ScrollbarValueToWorksheetIndex(verticalScrollValue, frozenRows, CellAddress.MaxRow),
            ScrollbarValueToWorksheetIndex(horizontalScrollValue, frozenCols, CellAddress.MaxCol)
        );

    public static uint ClampViewportOrigin(double rawValue, uint absoluteLimit, uint visibleSpan) =>
        WorkbookViewportScrollPlanner.ClampViewportOrigin(rawValue, absoluteLimit, visibleSpan);

    public static double CalculateViewportAvailableWidth(
        double gridWidth,
        double rowHeaderWidth,
        double zoomLevel) =>
        WorkbookViewportScrollPlanner.CalculateViewportAvailableWidth(gridWidth, rowHeaderWidth, zoomLevel);

    public static uint CalculateOpenedWorksheetScrollValue(
        uint? savedTopLeftIndex,
        uint fallbackIndex,
        uint absoluteLimit,
        uint frozenCount = 0) =>
        WorkbookViewportScrollPlanner.CalculateOpenedWorksheetScrollValue(
            savedTopLeftIndex,
            fallbackIndex,
            absoluteLimit,
            frozenCount);

    public static WorkbookViewportCellRevealPlan PlanCellReveal(
        ViewportModel viewport,
        Sheet? sheet,
        CellAddress target,
        double currentVerticalMaximum,
        double currentHorizontalMaximum) =>
        WorkbookViewportScrollPlanner.PlanCellReveal(
            viewport,
            sheet,
            target,
            currentVerticalMaximum,
            currentHorizontalMaximum);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit,
        uint visibleSpan) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit,
            visibleSpan);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(targetIndex, firstVisibleIndex, lastVisibleIndex);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForKeyboardReveal(
            currentMaximum,
            desiredScrollValue,
            absoluteLimit);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForKeyboardReveal(currentMaximum, desiredScrollValue);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateWheelScroll(
        double currentValue,
        double currentMaximum,
        int wheelNotches,
        double stepPerNotch,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateWheelScroll(
            currentValue,
            currentMaximum,
            wheelNotches,
            stepPerNotch,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateDragAutoScroll(
        double currentValue,
        double currentMaximum,
        int direction,
        double step,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
            currentValue,
            currentMaximum,
            direction,
            step,
            visibleSpan,
            absoluteLimit);

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan) =>
        WorkbookViewportScrollPlanner.CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);

    public static uint CalculateScrollbarMaximumForUsedRange(
        uint usedMax,
        uint visibleSpan,
        uint currentScrollValue,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForUsedRange(
            usedMax,
            visibleSpan,
            currentScrollValue,
            absoluteLimit);
}
