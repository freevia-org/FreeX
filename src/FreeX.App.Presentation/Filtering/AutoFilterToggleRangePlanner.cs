using FreeX.App.Presentation.AutoFilter;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterToggleRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (AutoFilterRangeResolver.TryGetAutoFilterRange(sheet, out var autoFilterRange))
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
