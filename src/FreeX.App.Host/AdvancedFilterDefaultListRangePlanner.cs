using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class AdvancedFilterDefaultListRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { } currentRegion &&
            currentRegion.RowCount > 1)
        {
            return currentRegion;
        }

        return selectedRange;
    }
}
