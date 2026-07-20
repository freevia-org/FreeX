using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static class StructuredReferenceResolver
{
    public static GridRange? ResolveDataBodyColumn(
        Workbook? workbook,
        Sheet? currentSheet,
        string tableName,
        string columnNameOrSelector,
        CellAddress? currentAddress = null)
        => Resolve(workbook, currentSheet, tableName, columnNameOrSelector, currentAddress);

    public static GridRange? Resolve(
        Workbook? workbook,
        Sheet? currentSheet,
        string tableName,
        string selector,
        CellAddress? currentAddress = null)
    {
        var sheets = workbook is not null
            ? workbook.Sheets
            : currentSheet is not null ? [currentSheet] : [];

        foreach (var sheet in sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    if (currentAddress is null || !sheet.Id.Equals(currentAddress.Value.Sheet))
                        continue;
                    // An unqualified [Column] resolves against the table the formula cell belongs to — which
                    // includes the header and totals rows, not just the data body. A totals-row aggregate such
                    // as =SUBTOTAL(109,[Amount]) must still find the Amount data column.
                    if (currentAddress.Value.Row < table.Range.Start.Row || currentAddress.Value.Row > table.Range.End.Row)
                        continue;
                    if (currentAddress.Value.Col < table.Range.Start.Col || currentAddress.Value.Col > table.Range.End.Col)
                        continue;
                }
                else if (!StructuredTableNameMatches(table, tableName))
                {
                    continue;
                }

                // An empty / whitespace selector — e.g. tblName[] — means the entire data body
                // spanning every column (equivalent to [#Data] across all columns). Excel's structured
                // reference spec defines [] as the data body range for the whole table.
                if (string.IsNullOrWhiteSpace(selector))
                    return DataBodyRange(sheet, table, table.Range.Start.Col, table.Range.End.Col);

                if (TryParseCombinedColumnRangeSelector(selector, out var rangeSection, out var rangeStartColumn, out var rangeEndColumn))
                {
                    if (IsThisRowSection(rangeSection))
                    {
                        return ResolveThisRowColumnRange(
                            sheet,
                            table,
                            currentAddress,
                            rangeStartColumn,
                            rangeEndColumn);
                    }

                    return ResolveSectionColumnRange(sheet, table, rangeSection, rangeStartColumn, rangeEndColumn);
                }

                if (TryParseCombinedSelector(selector, out var section, out var columnName))
                {
                    if (IsThisRowSection(section))
                        return ResolveThisRowColumnRange(sheet, table, currentAddress, columnName, columnName);

                    return ResolveSectionColumn(sheet, table, section, columnName);
                }

                // A table column can legitimately be named with text containing a colon (e.g. "Q1:Q2") —
                // that's ordinary header text, not "start:end" range syntax. Real Excel requires the
                // explicit double-bracket form ([[Col1]:[Col2]]) for an actual column range; a bare,
                // un-bracketed "Col1:Col2" selector that exactly matches one existing column's literal
                // name resolves to that column, not to a range from Col1 through Col2.
                var isBareColonSelector = !selector.Contains('[', StringComparison.Ordinal);
                if (!(isBareColonSelector && FindColumnIndex(sheet, table, selector) >= 0) &&
                    TryParseColumnRangeSelector(selector, out var startColumn, out var endColumn))
                    return ResolveSectionColumnRange(sheet, table, "#DATA", startColumn, endColumn);

                if (TryResolveTableSelector(sheet, table, selector) is { } selectedRange)
                    return selectedRange;

                if (IsThisRowSection(selector))
                    return ResolveThisRowColumnRange(
                        sheet,
                        table,
                        currentAddress,
                        FirstColumnNameOrEmpty(sheet, table),
                        LastColumnNameOrEmpty(sheet, table));

                var columnIndex = FindColumnIndex(sheet, table, selector);
                if (columnIndex < 0)
                    return null;

                var col = table.Range.Start.Col + (uint)columnIndex;
                return DataBodyRange(sheet, table, col, col);
            }
        }

        return null;
    }

    public static CellAddress? ResolveCurrentRowColumn(
        Workbook? workbook,
        Sheet? currentSheet,
        CellAddress? currentAddress,
        string? tableName,
        string columnName)
    {
        if (currentAddress is null)
            return null;

        var sheets = workbook is not null
            ? workbook.Sheets
            : currentSheet is not null ? [currentSheet] : [];

        foreach (var sheet in sheets)
        {
            if (!sheet.Id.Equals(currentAddress.Value.Sheet))
                continue;

            foreach (var table in sheet.StructuredTables)
            {
                if (!string.IsNullOrWhiteSpace(tableName) && !StructuredTableNameMatches(table, tableName))
                    continue;
                if (!IsDataBodyRow(table, currentAddress.Value.Row))
                    continue;
                if (currentAddress.Value.Col < table.Range.Start.Col || currentAddress.Value.Col > table.Range.End.Col)
                    continue;

                var columnIndex = FindColumnIndex(sheet, table, columnName);
                if (columnIndex < 0)
                    return null;

                return new CellAddress(
                    sheet.Id,
                    currentAddress.Value.Row,
                    table.Range.Start.Col + (uint)columnIndex);
            }
        }

        return null;
    }

    private static GridRange? TryResolveTableSelector(Sheet sheet, StructuredTableModel table, string selector)
    {
        return selector.Trim().ToUpperInvariant() switch
        {
            "#ALL" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, table.Range.Start.Col),
                new CellAddress(sheet.Id, table.Range.End.Row, table.Range.End.Col)),
            "#HEADERS" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, table.Range.Start.Col),
                new CellAddress(sheet.Id, table.Range.Start.Row, table.Range.End.Col)),
            "#DATA" => DataBodyRange(sheet, table, table.Range.Start.Col, table.Range.End.Col),
            "#TOTALS" when table.TotalsRowShown => new GridRange(
                new CellAddress(sheet.Id, table.Range.End.Row, table.Range.Start.Col),
                new CellAddress(sheet.Id, table.Range.End.Row, table.Range.End.Col)),
            _ => null
        };
    }

    private static GridRange? ResolveSectionColumn(
        Sheet sheet,
        StructuredTableModel table,
        string section,
        string columnName)
    {
        var columnIndex = FindColumnIndex(sheet, table, columnName);
        if (columnIndex < 0)
            return null;

        var col = table.Range.Start.Col + (uint)columnIndex;
        return section.Trim().ToUpperInvariant() switch
        {
            "#ALL" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, col),
                new CellAddress(sheet.Id, table.Range.End.Row, col)),
            "#HEADERS" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, col),
                new CellAddress(sheet.Id, table.Range.Start.Row, col)),
            "#DATA" => DataBodyRange(sheet, table, col, col),
            "#TOTALS" when table.TotalsRowShown => new GridRange(
                new CellAddress(sheet.Id, table.Range.End.Row, col),
                new CellAddress(sheet.Id, table.Range.End.Row, col)),
            _ => null
        };
    }

    private static GridRange? ResolveSectionColumnRange(
        Sheet sheet,
        StructuredTableModel table,
        string section,
        string startColumnName,
        string endColumnName)
    {
        var startColumnIndex = FindColumnIndex(sheet, table, startColumnName);
        var endColumnIndex = FindColumnIndex(sheet, table, endColumnName);
        if (startColumnIndex < 0 || endColumnIndex < 0)
            return null;

        var leftColumnIndex = Math.Min(startColumnIndex, endColumnIndex);
        var rightColumnIndex = Math.Max(startColumnIndex, endColumnIndex);
        var startCol = table.Range.Start.Col + (uint)leftColumnIndex;
        var endCol = table.Range.Start.Col + (uint)rightColumnIndex;

        return section.Trim().ToUpperInvariant() switch
        {
            "#ALL" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, startCol),
                new CellAddress(sheet.Id, table.Range.End.Row, endCol)),
            "#HEADERS" => new GridRange(
                new CellAddress(sheet.Id, table.Range.Start.Row, startCol),
                new CellAddress(sheet.Id, table.Range.Start.Row, endCol)),
            "#DATA" => DataBodyRange(sheet, table, startCol, endCol),
            "#TOTALS" when table.TotalsRowShown => new GridRange(
                new CellAddress(sheet.Id, table.Range.End.Row, startCol),
                new CellAddress(sheet.Id, table.Range.End.Row, endCol)),
            _ => null
        };
    }

    private static GridRange? ResolveThisRowColumnRange(
        Sheet sheet,
        StructuredTableModel table,
        CellAddress? currentAddress,
        string startColumnName,
        string endColumnName)
    {
        if (currentAddress is null || !sheet.Id.Equals(currentAddress.Value.Sheet))
            return null;
        if (!IsDataBodyRow(table, currentAddress.Value.Row))
            return null;
        if (currentAddress.Value.Col < table.Range.Start.Col || currentAddress.Value.Col > table.Range.End.Col)
            return null;

        var startColumnIndex = FindColumnIndex(sheet, table, startColumnName);
        var endColumnIndex = FindColumnIndex(sheet, table, endColumnName);
        if (startColumnIndex < 0 || endColumnIndex < 0)
            return null;

        var leftColumnIndex = Math.Min(startColumnIndex, endColumnIndex);
        var rightColumnIndex = Math.Max(startColumnIndex, endColumnIndex);
        var startCol = table.Range.Start.Col + (uint)leftColumnIndex;
        var endCol = table.Range.Start.Col + (uint)rightColumnIndex;

        return new GridRange(
            new CellAddress(sheet.Id, currentAddress.Value.Row, startCol),
            new CellAddress(sheet.Id, currentAddress.Value.Row, endCol));
    }

    private static bool TryParseCombinedColumnRangeSelector(
        string selector,
        out string section,
        out string startColumnName,
        out string endColumnName)
    {
        section = "";
        startColumnName = "";
        endColumnName = "";

        var parts = ParseCombinedSelectorParts(selector);
        if (parts.Count != 2 || !parts[0].StartsWith('#'))
            return false;
        if (!TryParseColumnRangeSelector(parts[1], out startColumnName, out endColumnName))
            return false;

        section = parts[0];
        return true;
    }

    private static bool TryParseCombinedSelector(string selector, out string section, out string columnName)
    {
        section = "";
        columnName = "";

        var parts = ParseCombinedSelectorParts(selector);
        if (parts.Count != 2 || !parts[0].StartsWith('#'))
            return false;

        section = parts[0];
        columnName = parts[1];
        return !string.IsNullOrWhiteSpace(columnName);
    }

    private static bool TryParseColumnRangeSelector(string selector, out string startColumnName, out string endColumnName)
    {
        startColumnName = "";
        endColumnName = "";

        var cleaned = selector
            .Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal);
        var parts = cleaned.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].StartsWith('#') || parts[1].StartsWith('#'))
            return false;

        startColumnName = parts[0];
        endColumnName = parts[1];
        return !string.IsNullOrWhiteSpace(startColumnName) && !string.IsNullOrWhiteSpace(endColumnName);
    }

    private static List<string> ParseCombinedSelectorParts(string selector)
    {
        var cleaned = selector
            .Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal);
        return cleaned.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string FirstColumnNameOrEmpty(Sheet sheet, StructuredTableModel table) =>
        table.Columns.Count == 0 ? "" : ColumnHeaderText(sheet, table, 0);

    private static string LastColumnNameOrEmpty(Sheet sheet, StructuredTableModel table) =>
        table.Columns.Count == 0 ? "" : ColumnHeaderText(sheet, table, table.Columns.Count - 1);

    private static int FindColumnIndex(Sheet sheet, StructuredTableModel table, string columnName)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(ColumnHeaderText(sheet, table, index), columnName, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    // Resolves the column's EFFECTIVE name for structured-reference matching: the table's header
    // ROW cell text when present (an ordinary EditCellsCommand edit to a header cell — e.g.
    // retyping "Sales" to "Revenue" — updates the sheet cell immediately but nothing currently
    // syncs that back into StructuredTableColumnModel.Name; see R50-io-table-totals-calc-3-1), so
    // structured refs like Table1[Revenue] must match what the user actually sees in the header
    // row rather than the possibly-stale stored column name. Falls back to the stored model name
    // for a headerless table (HeaderRowCount == 0, no header row to read) or a blank header cell.
    private static string ColumnHeaderText(Sheet sheet, StructuredTableModel table, int index)
    {
        var storedName = table.Columns[index].Name;
        if (HeaderRowCount(table) == 0)
            return storedName;

        var headerCol = table.Range.Start.Col + (uint)index;
        return sheet.GetCell(table.Range.Start.Row, headerCol)?.Value is TextValue { Value.Length: > 0 } text
            ? text.Value
            : storedName;
    }

    private static bool IsThisRowSection(string selector) =>
        string.Equals(selector.Trim(), "#THIS ROW", StringComparison.OrdinalIgnoreCase);

    private static GridRange? DataBodyRange(Sheet sheet, StructuredTableModel table, uint startCol, uint endCol)
    {
        var startRow = table.Range.Start.Row + HeaderRowCount(table);
        var endRow = table.TotalsRowShown && table.Range.End.Row > startRow
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        if (startRow > endRow)
            return null;

        return new GridRange(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
    }

    private static bool IsDataBodyRow(StructuredTableModel table, uint row)
    {
        var startRow = table.Range.Start.Row + HeaderRowCount(table);
        var endRow = table.TotalsRowShown && table.Range.End.Row > startRow
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        return row >= startRow && row <= endRow;
    }

    // Excel tables normally have a single header row, but headerRowCount="0" (a headerless table)
    // is a supported, round-tripped feature — clamp to the table's actual row span so a headerless
    // table's very first row is treated as data, not silently swallowed as a phantom header.
    private static uint HeaderRowCount(StructuredTableModel table)
    {
        var rowCount = checked((int)table.Range.RowCount);
        return (uint)Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
    }

    private static bool StructuredTableNameMatches(StructuredTableModel table, string name) =>
        string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase);
}
