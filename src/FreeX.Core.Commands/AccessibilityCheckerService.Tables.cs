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

        // R90-app-accessibility-checker-5-4: walk the sheet's occupied-cell map (bounded to this
        // table's data body) instead of every declared (row, col) pair in the table's full extent.
        // A structured table can span up to the sheet's full row/column limit (e.g. an Excel Table
        // created over an entire column range) while containing only a handful of populated rows,
        // so a direct double loop over dataStartRow..dataEndRow x startCol..endCol can cost millions
        // of GetValue lookups for one table. Mirrors AddLowContrastCellTextIssues (Contrast.cs) and
        // AddHiddenContentIssues (HiddenContent.cs), which already bound their sheet-wide scans the
        // same way.
        var nonBlankRows = new HashSet<uint>();
        var nonBlankCols = new HashSet<int>();
        foreach (var ((row, col), _) in sheet.GetOccupiedCellMap())
        {
            if (row < dataStartRow || row > dataEndRow)
                continue;
            if (col < (uint)startCol || col > (uint)endCol)
                continue;
            if (string.IsNullOrWhiteSpace(ReadHeaderText(sheet, new CellAddress(sheet.Id, row, col))))
                continue;

            nonBlankRows.Add(row);
            nonBlankCols.Add((int)col);
        }

        // R90-app-accessibility-checker-5-4 (follow-up): the detection pass above is already bounded
        // to occupied cells, but walking `dataStartRow..dataEndRow` row-by-row here to test each row
        // against `nonBlankRows` is itself still a full walk of the table's declared extent -- for a
        // table spanning the sheet's entire row range (as in the perf-bounding test above) that is
        // ~1,048,574 loop iterations and one freshly-allocated AccessibilityIssue per blank row.
        // Walk the (small) sorted set of non-blank rows instead and emit one issue per contiguous
        // blank run between them, so cost scales with the number of populated rows, not the table's
        // declared row extent.
        var sortedNonBlankRows = nonBlankRows.Count > 0 ? nonBlankRows.Order().ToList() : [];
        var blankRunStart = dataStartRow;
        foreach (var nonBlankRow in sortedNonBlankRows)
        {
            if (nonBlankRow > blankRunStart)
            {
                AddBlankRowRangeIssue(issues, sheet, startCol, endCol, blankRunStart, nonBlankRow - 1);
            }

            blankRunStart = nonBlankRow + 1;
            if (blankRunStart > dataEndRow)
                break;
        }

        if (blankRunStart <= dataEndRow)
        {
            AddBlankRowRangeIssue(issues, sheet, startCol, endCol, blankRunStart, dataEndRow);
        }

        for (var col = startCol; col <= endCol; col++)
        {
            if (nonBlankCols.Contains(col))
                continue;

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

    /// <summary>
    /// Adds one <see cref="AccessibilityIssueKind.BlankRowOrColumnInTable"/> issue covering a
    /// contiguous run of fully-blank rows [<paramref name="rowStart"/>, <paramref name="rowEnd"/>]
    /// (a single row when the run has length 1).
    /// </summary>
    private static void AddBlankRowRangeIssue(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        int startCol,
        int endCol,
        uint rowStart,
        uint rowEnd)
    {
        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.BlankRowOrColumnInTable,
            sheet.Id,
            sheet.Name,
            FormatRange(new GridRange(
                new CellAddress(sheet.Id, rowStart, (uint)startCol),
                new CellAddress(sheet.Id, rowEnd, (uint)endCol))),
            "Tables should not contain fully blank rows."));
    }

    private static string ReadHeaderText(Sheet sheet, CellAddress headerAddress) =>
        sheet.GetValue(headerAddress) switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeValue dateTime => dateTime.ToDateTime().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string NormalizeHeaderText(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
