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
