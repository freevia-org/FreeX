using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Resolves the Excel-style Forecast Sheet source from the current selection for every renderer.
/// A single cell inside a valid two-column current region expands to that region; explicit ranges
/// remain authoritative and are validated by <see cref="ForecastSheetPlanner"/>.
/// </summary>
public static class ForecastSheetSourceRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { } currentRegion &&
            currentRegion.RowCount >= ForecastSheetPlanner.MinimumSourceRowCount &&
            currentRegion.ColCount == ForecastSheetPlanner.RequiredColumnCount)
        {
            return currentRegion;
        }

        return selectedRange;
    }
}
