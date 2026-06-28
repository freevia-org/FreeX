using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DataTools;

public static class DataListCommandRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.RowCount != 1 || selectedRange.ColCount != 1)
            return selectedRange;

        var currentRegion = SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start);
        return currentRegion is { RowCount: > 1 }
            ? currentRegion.Value
            : selectedRange;
    }
}
