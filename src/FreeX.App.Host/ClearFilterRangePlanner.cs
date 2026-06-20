using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class ClearFilterRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange))
            return autoFilterRange;

        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { } currentRegion &&
            currentRegion.RowCount > 1)
        {
            return currentRegion;
        }

        return selectedRange;
    }

    public static bool HasActiveFilter(Sheet sheet, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (sheet.FilterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return false;

        if ((uint)sheet.FilterHiddenRows.Count < range.RowCount)
        {
            foreach (var row in sheet.FilterHiddenRows)
            {
                if (row >= firstDataRow && row <= lastDataRow)
                    return true;
            }

            return false;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.FilterHiddenRows.Contains(row))
                return true;
        }

        return false;
    }
}
