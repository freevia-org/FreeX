using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free reader of a filter column's values. Formats each cell with the SAME canonical text the Core
/// <c>FilterCommand</c> matches against (<see cref="FilterValueFormatter.ToText"/>), so the checklist the
/// dropdown shows and the values it sends to the filter agree exactly. Portable (Core model only) and
/// unit testable.
/// </summary>
internal static class AutoFilterColumnValueReader
{
    /// <summary>
    /// The canonical filter text for a cell value — delegates to Core's <see cref="FilterValueFormatter.ToText"/>:
    /// text as-is, numbers invariant, bools TRUE/FALSE, dates yyyy-MM-dd, blanks empty, errors as code.
    /// </summary>
    public static string ToFilterText(ScalarValue value) => FilterValueFormatter.ToText(value);

    /// <summary>
    /// The distinct filter-text values in the data rows of the column at <paramref name="columnOffset"/>
    /// within <paramref name="range"/> (the header row is excluded), in first-seen order.
    /// </summary>
    public static IReadOnlyList<string> DistinctColumnValues(Sheet sheet, GridRange range, uint columnOffset)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var col = range.Start.Col + columnOffset;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var text = ToFilterText(sheet.GetValue(row, col));
            if (seen.Add(text))
                values.Add(text);
        }

        return values;
    }
}
