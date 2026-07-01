using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DataTools;

public static class ForecastSheetSourceRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { } currentRegion &&
            currentRegion.RowCount >= 3 &&
            currentRegion.ColCount == 2)
        {
            return currentRegion;
        }

        return selectedRange;
    }
}
