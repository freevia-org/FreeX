using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class PivotTableSlicerTimelineCommandHelpers
{
    internal static (Sheet Sheet, PivotTableModel PivotTable)? FindConnectedPivotTable(Workbook workbook, string pivotTableName)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, pivotTableName, StringComparison.OrdinalIgnoreCase));
            if (pivotTable is not null)
                return (sheet, pivotTable);
        }

        return null;
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

    internal static string SanitizeCacheName(string name, string fallback)
    {
        var chars = name.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
