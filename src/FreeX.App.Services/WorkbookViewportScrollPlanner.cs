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

public static class WorkbookViewportScrollPlanner
{
    private const double MinimumScrollValue = 1;

    public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(viewport);

        var visibleRows = CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows);
        var visibleColumns = CountScrollableColumns(viewport.ColMetrics, sheet.FrozenCols);
        return new WorkbookViewportScrollState(
            CreateAxis(
                sheet.ViewTopRow ?? GetScrollableRowStart(sheet),
                sheet.FrozenRows,
                CellAddress.MaxRow,
                visibleRows),
            CreateAxis(
                sheet.ViewLeftCol ?? GetScrollableColumnStart(sheet),
                sheet.FrozenCols,
                CellAddress.MaxCol,
                visibleColumns));
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet sheet,
        double verticalScrollValue,
        double horizontalScrollValue)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return (
            ScrollbarValueToWorksheetIndex(verticalScrollValue, sheet.FrozenRows, CellAddress.MaxRow),
            ScrollbarValueToWorksheetIndex(horizontalScrollValue, sheet.FrozenCols, CellAddress.MaxCol));
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

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)
    {
        visibleSpan = Math.Max(1, visibleSpan);
        return visibleSpan >= absoluteLimit ? 1 : absoluteLimit - visibleSpan + 1;
    }

    private static WorkbookViewportScrollAxis CreateAxis(
        uint worksheetOrigin,
        uint frozenCount,
        uint absoluteLimit,
        uint visibleSpan)
    {
        var scrollableLimit = CalculateScrollableLimit(absoluteLimit, frozenCount);
        var maximum = CalculateMaximumViewportOrigin(scrollableLimit, visibleSpan);
        var value = Math.Clamp(WorksheetIndexToScrollbarValue(worksheetOrigin, frozenCount), 1, maximum);
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

    private static uint CountScrollableRows(IReadOnlyList<RowMetric> rows, uint frozenRows)
    {
        uint count = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Row > frozenRows)
                count++;
        }

        return Math.Max(1, count);
    }

    private static uint CountScrollableColumns(IReadOnlyList<ColMetric> columns, uint frozenColumns)
    {
        uint count = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Col > frozenColumns)
                count++;
        }

        return Math.Max(1, count);
    }

    private static uint GetScrollableRowStart(Sheet sheet) =>
        Math.Min(CellAddress.MaxRow, Math.Max(1, sheet.FrozenRows + 1));

    private static uint GetScrollableColumnStart(Sheet sheet) =>
        Math.Min(CellAddress.MaxCol, Math.Max(1, sheet.FrozenCols + 1));
}
