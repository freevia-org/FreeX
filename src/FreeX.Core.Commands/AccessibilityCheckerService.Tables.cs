using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
    private static void AddStructuredTableIssues(List<AccessibilityIssue> issues, Sheet sheet)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.HeaderRowCount.GetValueOrDefault(1) <= 0)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.TableMissingHeaderRow,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(table.Range),
                    "Tables should include a header row."));
                continue;
            }

            var seenHeaderTexts = new Dictionary<string, CellAddress>(StringComparer.OrdinalIgnoreCase);
            var startCol = (int)table.Range.Start.Col;
            var endCol = (int)table.Range.End.Col;
            for (var col = startCol; col <= endCol; col++)
            {
                var columnOffset = col - startCol;
                var columnName = columnOffset < table.Columns.Count ? table.Columns[columnOffset].Name : null;
                var headerAddress = new CellAddress(sheet.Id, table.Range.Start.Row, (uint)col);
                var headerText = ReadHeaderText(sheet, headerAddress, columnName);
                if (string.IsNullOrWhiteSpace(headerText))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableMissingHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should not be blank."));
                    continue;
                }

                if (AccessibilityTextRules.IsDefaultTableHeaderText(headerText))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableDefaultHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should describe the column contents."));
                    continue;
                }

                var normalizedHeaderText = NormalizeHeaderText(headerText);
                if (seenHeaderTexts.TryGetValue(normalizedHeaderText, out _))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableDuplicateHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should be unique."));
                    continue;
                }

                seenHeaderTexts[normalizedHeaderText] = headerAddress;
            }
        }
    }

    private static string? ReadHeaderText(Sheet sheet, CellAddress headerAddress, string? columnName)
    {
        if (sheet.GetCell(headerAddress) is { } cell)
            return ValueText(cell.Value);

        return columnName;
    }

    private static string NormalizeHeaderText(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
