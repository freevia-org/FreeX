using FreeX.Core.Model;

namespace FreeX.App.Presentation.AutoFilter;

/// <summary>
/// Portable resolution of a sheet's effective AutoFilter range. A worksheet-level
/// <c>&lt;autoFilter&gt;</c> reference takes precedence (an explicit AutoFilter applied to a range);
/// otherwise the first structured table whose <see cref="StructuredTableModel.HasAutoFilter"/> is set
/// supplies the range so its header still shows filter-arrow buttons exactly as Excel renders them.
/// Core-model only (no UI deps) so both the WPF host and the Avalonia port share one implementation.
/// </summary>
public static class AutoFilterRangeResolver
{
    /// <summary>
    /// Resolves the AutoFilter range on <paramref name="sheet"/>. Returns <c>true</c> with
    /// <paramref name="range"/> set when an AutoFilter is active; otherwise <c>false</c>.
    /// </summary>
    public static bool TryGetAutoFilterRange(Sheet sheet, out GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        range = default;

        // A worksheet-level <autoFilter> takes precedence (an explicit AutoFilter applied to a range).
        if (sheet.AutoFilter is { Reference: { } reference } &&
            !string.IsNullOrWhiteSpace(reference))
        {
            try
            {
                range = GridRange.Parse(reference, sheet.Id);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

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
    /// Convenience overload returning the resolved range, or <c>null</c> when no AutoFilter is active.
    /// </summary>
    public static GridRange? TryGetAutoFilterRange(Sheet sheet) =>
        TryGetAutoFilterRange(sheet, out var range) ? range : null;
}
