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
                var headerAddress = new CellAddress(sheet.Id, table.Range.Start.Row, (uint)col);
                var headerText = ReadHeaderText(sheet, headerAddress);
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

            AddBlankTableBodyRowAndColumnIssues(issues, sheet, table, startCol, endCol);
        }
    }

    /// <summary>
    /// Flags fully-blank interior rows/columns within a structured table's data body (excluding
    /// the header row(s) and totals row, which are checked separately). Excel's Accessibility
    /// Checker flags these because a screen reader can interpret a fully-blank row/column as the
    /// end of the table.
    /// </summary>
    private static void AddBlankTableBodyRowAndColumnIssues(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        StructuredTableModel table,
        int startCol,
        int endCol)
    {
        var rowCount = (int)table.Range.RowCount;
        var headerRowCount = Math.Clamp(table.HeaderRowCount.GetValueOrDefault(1), 0, rowCount);
        var totalsRowCount = Math.Clamp(table.TotalsRowCount ?? (table.TotalsRowShown ? 1 : 0), 0, rowCount);
        var dataStartRow = table.Range.Start.Row + (uint)headerRowCount;
        var dataEndRow = table.Range.End.Row - (uint)totalsRowCount;
        if (dataStartRow > dataEndRow)
            return;

        for (var row = dataStartRow; row <= dataEndRow; row++)
        {
            var isBlankRow = true;
            for (var col = startCol; col <= endCol && isBlankRow; col++)
            {
                if (!string.IsNullOrWhiteSpace(ReadHeaderText(sheet, new CellAddress(sheet.Id, row, (uint)col))))
                    isBlankRow = false;
            }

            if (isBlankRow)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.BlankRowOrColumnInTable,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(new GridRange(
                        new CellAddress(sheet.Id, row, (uint)startCol),
                        new CellAddress(sheet.Id, row, (uint)endCol))),
                    "Tables should not contain fully blank rows."));
            }
        }

        for (var col = startCol; col <= endCol; col++)
        {
            var isBlankColumn = true;
            for (var row = dataStartRow; row <= dataEndRow && isBlankColumn; row++)
            {
                if (!string.IsNullOrWhiteSpace(ReadHeaderText(sheet, new CellAddress(sheet.Id, row, (uint)col))))
                    isBlankColumn = false;
            }

            if (isBlankColumn)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.BlankRowOrColumnInTable,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(new GridRange(
                        new CellAddress(sheet.Id, dataStartRow, (uint)col),
                        new CellAddress(sheet.Id, dataEndRow, (uint)col))),
                    "Tables should not contain fully blank columns."));
            }
        }
    }

    private static string ReadHeaderText(Sheet sheet, CellAddress headerAddress) =>
        ValueText(sheet.GetValue(headerAddress));

    private static string NormalizeHeaderText(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
