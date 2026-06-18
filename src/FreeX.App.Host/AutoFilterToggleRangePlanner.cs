using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class AutoFilterToggleRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange))
            return autoFilterRange;

        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { RowCount: > 1 } currentRegion)
        {
            return currentRegion;
        }

        return selectedRange;
    }
}
