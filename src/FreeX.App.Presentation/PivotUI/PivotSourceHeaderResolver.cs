using System.Linq;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Resolves a pivot table's source field names (headers).
/// <para>
/// Field captions and header dropdowns are normally derived from the header row of the pivot's
/// <see cref="PivotTableModel.SourceRange"/>. Pivots loaded from xlsx that draw on a pivot cache
/// (rather than a direct worksheet range) arrive with an empty <c>SourceRange</c>, so reading the
/// source header row yields blanks and the UI falls back to generic <c>Column N</c> captions with
/// no header dropdowns. In that case the pivot cache still carries the real field names, so we use
/// them instead (Issue 123).
/// </para>
/// </summary>
public static class PivotSourceHeaderResolver
{
    /// <summary>
    /// Returns the field headers for <paramref name="pivotTable"/>, preferring the headers read from
    /// its source range and falling back to the pivot cache's field names when the source range did
    /// not resolve to a usable header row.
    /// </summary>
    public static List<string> Resolve(Workbook workbook, PivotTableModel pivotTable, List<string> sourceHeaders)
    {
        var cacheFields = workbook.PivotCaches
            .FirstOrDefault(cache => cache.CacheId == pivotTable.CacheId)?.Fields;
        if (cacheFields is null || cacheFields.Count == 0)
            return sourceHeaders;

        // Use the source-range headers when they actually resolved (enough columns and at least one
        // non-generic caption); otherwise the source range was empty/unresolved — use the cache names.
        var sourceUsable = sourceHeaders.Count >= cacheFields.Count &&
                           sourceHeaders.Where((header, index) => !IsGenericCaption(header, index)).Any();
        if (sourceUsable)
            return sourceHeaders;

        return cacheFields
            .Select((field, index) => string.IsNullOrWhiteSpace(field.Name) ? $"Column {index + 1}" : field.Name)
            .ToList();
    }

    private static bool IsGenericCaption(string caption, int index) =>
        string.IsNullOrWhiteSpace(caption) ||
        string.Equals(caption, $"Column {index + 1}", StringComparison.Ordinal) ||
        string.Equals(caption, $"Field{index + 1}", StringComparison.Ordinal);
}
