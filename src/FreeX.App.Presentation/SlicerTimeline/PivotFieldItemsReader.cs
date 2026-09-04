using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Enumerates the distinct, sorted field items for a resolved PivotTable source-field column.
/// This is the shared enumeration core: same comparer, same sentinel, same sort order across the
/// WPF host and the cross-platform shells. Callers supply the already-resolved source-field index
/// and a value formatter that converts each <see cref="ScalarValue"/> to a display string.
/// </summary>
public static class PivotFieldItemsReader
{
    private const string BlankItem = "(blank)";

    /// <summary>
    /// Returns the distinct, case-insensitively sorted field items for the column at
    /// <paramref name="sourceFieldIndex"/> within <paramref name="pivotTable"/>'s source range
    /// (header row excluded). Blank or whitespace-only formatted values are mapped to the sentinel
    /// string <c>(blank)</c>. Deduplication and sort are both current-culture, case-insensitive.
    /// </summary>
    /// <param name="sheet">The sheet that holds the pivot source data.</param>
    /// <param name="pivotTable">The pivot table whose source range to enumerate.</param>
    /// <param name="sourceFieldIndex">Zero-based column index within the source range.</param>
    /// <param name="formatValue">
    /// Converts a raw <see cref="ScalarValue"/> to its display string. The caller is responsible
    /// for any trimming needed before the blank-sentinel check — this method passes the formatter
    /// result directly to <see cref="string.IsNullOrWhiteSpace"/> without further processing.
    /// </param>
    public static IReadOnlyList<string> ReadItems(
        Sheet sheet,
        PivotTableModel pivotTable,
        int sourceFieldIndex,
        Func<ScalarValue?, string> formatValue)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(formatValue);

        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            var text = formatValue(sheet.GetValue(row, sourceColumn));
            values.Add(string.IsNullOrWhiteSpace(text) ? BlankItem : text);
        }

        return values.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
