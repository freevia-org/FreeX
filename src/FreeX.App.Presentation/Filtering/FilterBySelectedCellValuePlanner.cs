using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

/// <summary>
/// Excel's cell menu Filter ▸ "Filter by Selected Cell's Value": keep only the rows whose value in
/// the active cell's column equals the active cell's value, turning the AutoFilter on around the
/// active cell first when the sheet has none.
/// </summary>
public static class FilterBySelectedCellValuePlanner
{
    /// <summary>True when the sheet has no AutoFilter yet, so one must be turned on around the active cell.</summary>
    public static bool NeedsAutoFilter(Sheet sheet) =>
        !AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out _);

    /// <summary>
    /// The AutoFilter range, the active cell's column within it and the value to keep, or false when
    /// the active cell lies outside the sheet's AutoFilter.
    /// </summary>
    public static bool TryPlan(
        Sheet sheet,
        CellAddress activeCell,
        out GridRange filterRange,
        out uint columnOffset,
        out IReadOnlyList<string> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        columnOffset = 0;
        allowedValues = [];
        if (!AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out filterRange) ||
            !filterRange.Contains(activeCell))
        {
            return false;
        }

        columnOffset = activeCell.Col - filterRange.Start.Col;
        allowedValues = [FilterValueFormatter.ToText(sheet.GetValue(activeCell))];
        return true;
    }
}
