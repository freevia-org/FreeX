using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PivotTableSlicerTimelineCommandHelpers
{
    internal static (Sheet Sheet, PivotTableModel PivotTable)? FindConnectedPivotTable(Workbook workbook, string pivotTableName)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (CommandGuards.TryFindPivotTable(sheet, pivotTableName, out var pivotTable))
                return (sheet, pivotTable);
        }

        return null;
    }

    /// <summary>
    /// R133x-commands-slicer-timeline-multipivot-runtime: resolves every distinct pivot table name a
    /// slicer/timeline connects to, primary name first. A slicer/timeline can drive SEVERAL pivot tables
    /// at once (Excel's "Report Connections") -- <paramref name="connectedNames"/> (populated at load
    /// with every <c>&lt;pivotTable&gt;</c> entry the control's cache carries, see
    /// <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>)
    /// is the authoritative list of ALL connections, while <paramref name="primaryName"/> (<c>SourcePivotTableName</c>)
    /// only ever tracks the first/primary one. Falls back to a single-entry list of just
    /// <paramref name="primaryName"/> when <paramref name="connectedNames"/> is empty -- a freshly
    /// authored control (never loaded from a package) that was only ever connected to one pivot table --
    /// so the common single-pivot case is unaffected.
    /// </summary>
    internal static List<string> ResolveConnectedPivotTableNames(string? primaryName, IReadOnlyList<string> connectedNames)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryName))
            result.Add(primaryName);

        foreach (var name in connectedNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!result.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                result.Add(name);
        }

        return result;
    }

    internal static List<string> ReadPivotHeaders(Sheet sheet, PivotTableModel pivotTable)
    {
        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var value = sheet.GetValue(pivotTable.SourceRange.Start.Row, col);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    internal static void ReplaceSelectedItems(List<PivotFieldModel> fields, int sourceFieldIndex, IReadOnlyList<string> selectedItems)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].SourceFieldIndex != sourceFieldIndex)
                continue;

            fields[index] = fields[index] with
            {
                SelectedItem = selectedItems.Count == 1 ? selectedItems[0] : null,
                SelectedItems = selectedItems.Count == 0 ? null : selectedItems.ToList()
            };
        }
    }

    /// <summary>
    /// A slicer/timeline can be connected to a pivot source field that the user never dragged into
    /// Row/Column/PageFields (Excel still lets you insert a slicer on any source field and it filters
    /// the pivot). <see cref="ReplaceSelectedItems"/> only ever mutates an EXISTING entry in one of
    /// those three lists, so without this it would be a silent no-op for such a field (see H10): the
    /// command reports success and the slicer highlights a selection, but
    /// <c>PivotTableRefreshService</c> only filters rows via <c>MatchesFieldSelections</c> over
    /// Page/Row/ColumnFields, so nothing is actually filtered.
    /// <para>
    /// Ensures <paramref name="sourceFieldIndex"/> is present in one of <paramref name="rowFields"/>,
    /// <paramref name="columnFields"/>, or <paramref name="pageFields"/>; when absent from all three it
    /// is added to <paramref name="pageFields"/> so <c>MatchesFieldSelections</c> picks it up, but
    /// flagged <see cref="PivotFieldModel.IsUnplacedFilterField"/> so the renderer does NOT show a
    /// Filters-area box for it — in real Excel, a slicer/timeline filtering an unplaced field never
    /// adds a visible report-filter row to the table layout, it only narrows the rows/columns that are
    /// already there.
    /// </para>
    /// </summary>
    internal static void EnsureFieldInLayout(
        List<PivotFieldModel> rowFields,
        List<PivotFieldModel> columnFields,
        List<PivotFieldModel> pageFields,
        int sourceFieldIndex)
    {
        if (rowFields.Any(field => field.SourceFieldIndex == sourceFieldIndex) ||
            columnFields.Any(field => field.SourceFieldIndex == sourceFieldIndex) ||
            pageFields.Any(field => field.SourceFieldIndex == sourceFieldIndex))
        {
            return;
        }

        pageFields.Add(new PivotFieldModel(sourceFieldIndex, IsUnplacedFilterField: true));
    }

    internal static string SanitizeCacheName(string name, string fallback)
    {
        var chars = name.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
