using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ViewportScrollCalculator
{
    public static int NormalizeWheelNotches(int delta)
    {
        if (delta == 0)
            return 0;

        var notches = delta / 120;
        return notches != 0 ? notches : Math.Sign(delta);
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue)
    {
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

    public static uint GetScrollableRowLimit(Sheet? sheet) =>
        CalculateScrollableLimit(CellAddress.MaxRow, sheet?.FrozenRows ?? 0);

    public static uint GetScrollableColumnLimit(Sheet? sheet) =>
        CalculateScrollableLimit(CellAddress.MaxCol, sheet?.FrozenCols ?? 0);

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
        return Math.Max(0, gridWidth - rowHeaderWidth) / effectiveZoom;
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

    private static uint ToVisibleSpan(double visibleSpan)
    {
        return visibleSpan is > 0 and <= uint.MaxValue
            ? Math.Max(1, (uint)Math.Ceiling(visibleSpan))
            : 1;
    }
}
