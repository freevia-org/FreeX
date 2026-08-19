using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Portable resolution of worksheet AutoFilter ranges.
/// </summary>
public static class AutoFilterRangeResolver
{
    /// <summary>
    /// Resolves the explicit worksheet-level <c>&lt;autoFilter&gt;</c> range on <paramref name="sheet"/>.
    /// </summary>
    public static bool TryGetWorksheetAutoFilterRange(Sheet sheet, out GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        range = default;

        if (sheet.AutoFilter is { Reference: { } reference } &&
            !string.IsNullOrWhiteSpace(reference))
        {
            return TryParseRange(reference, sheet.Id, out range);
        }

        return false;
    }

    /// <summary>
    /// Resolves the AutoFilter range to render or target in UI: a worksheet-level range first,
    /// otherwise the first structured table with AutoFilter enabled.
    /// </summary>
    public static bool TryGetEffectiveAutoFilterRange(Sheet sheet, out GridRange range)
    {
        if (TryGetWorksheetAutoFilterRange(sheet, out range))
            return true;

        // Excel structured tables carry their AutoFilter inside the table definition rather than as a
        // worksheet <autoFilter>; surface the first filtered table's range so the header still shows
        // filter-arrow buttons exactly as Excel renders them.
        foreach (var table in sheet.StructuredTables)
        {
            if (!table.HasAutoFilter)
                continue;

            var tableRange = table.Range;
            if (tableRange.Start.Sheet != sheet.Id ||
                tableRange.End.Row < tableRange.Start.Row ||
                tableRange.End.Col < tableRange.Start.Col)
            {
                continue;
            }

            range = tableRange;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convenience overload returning the worksheet-level range, or <c>null</c> when none is active.
    /// </summary>
    public static GridRange? TryGetWorksheetAutoFilterRange(Sheet sheet) =>
        TryGetWorksheetAutoFilterRange(sheet, out var range) ? range : null;

    /// <summary>
    /// Convenience overload returning the effective UI range, or <c>null</c> when no AutoFilter is active.
    /// </summary>
    public static GridRange? TryGetEffectiveAutoFilterRange(Sheet sheet) =>
        TryGetEffectiveAutoFilterRange(sheet, out var range) ? range : null;

    /// <summary>
    /// R104-app-presentation-autofilter-totalsrow-1: public choke point exposing
    /// <see cref="StructuredTableEditEffects.GetFilterableLastRow"/> (internal to this assembly) to
    /// UI-facing dropdown planners in FreeX.App.Presentation. When <paramref name="range"/> is exactly
    /// a structured table's <c>Range</c> (the shape <see cref="TryGetEffectiveAutoFilterRange(Sheet, out GridRange)"/>
    /// hands back for a table's header-cell filter dropdown) and that table's Totals Row is shown,
    /// <c>range.End.Row</c> IS the Totals Row itself -- so the checklist/kind-detection/color-list
    /// builders that enumerate the filterable data set must stop one row short of it, exactly like the
    /// interactive filter-apply commands (FilterCommand, TopBottomFilterCommand, AverageFilterCommand,
    /// FilterConditionCommand) already do via this same bound.
    /// </summary>
    public static uint GetFilterableLastRow(Sheet sheet, GridRange range) =>
        StructuredTableEditEffects.GetFilterableLastRow(sheet, range);

    /// <summary>
    /// table-semantics-F1: the START-bound counterpart to <see cref="GetFilterableLastRow"/>, exposing
    /// <see cref="FilterHiddenRowUpdater.GetFilterableFirstRow"/> to the same UI-facing dropdown
    /// planners. A structured table loaded with <c>headerRowCount="0"</c> has no header row at all, so
    /// <c>range.Start.Row</c> is itself a data row.
    /// </summary>
    /// <remarks>
    /// The two bounds have to move together. Using the header-aware end with a header-naive start is
    /// the asymmetry that produced this whole defect family: every member already called
    /// GetFilterableLastRow while still hardcoding <c>Start.Row + 1</c>, so a headerless table lost its
    /// first row -- from the applied filter, and here from the dropdown checklist itself, where the
    /// value cannot even be seen to be selected.
    /// </remarks>
    public static uint GetFilterableFirstRow(Sheet sheet, GridRange range) =>
        FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range);

    private static bool TryParseRange(string reference, SheetId sheetId, out GridRange range)
    {
        try
        {
            range = GridRange.Parse(reference, sheetId);
            return true;
        }
        catch (FormatException)
        {
            range = default;
            return false;
        }
        catch (ArgumentException)
        {
            range = default;
            return false;
        }
    }
}
