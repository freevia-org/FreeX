using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
                    if (!IsDataBodyRow(table, currentAddress.Value.Row))
                        continue;
                    if (currentAddress.Value.Col < table.Range.Start.Col || currentAddress.Value.Col > table.Range.End.Col)
                        continue;
                }
                else if (!StructuredTableNameMatches(table, tableName))
                {
                    continue;
                }

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

                if (TryParseColumnRangeSelector(selector, out var startColumn, out var endColumn))
                    return ResolveSectionColumnRange(sheet, table, "#DATA", startColumn, endColumn);

                if (TryResolveTableSelector(sheet, table, selector) is { } selectedRange)
                    return selectedRange;

                if (IsThisRowSection(selector))
                    return ResolveThisRowColumnRange(
                        sheet,
                        table,
                        currentAddress,
                        table.Columns.FirstOrDefault()?.Name ?? "",
                        table.Columns.LastOrDefault()?.Name ?? "");

                var columnIndex = table.Columns.FindIndex(column =>
                    string.Equals(column.Name, selector, StringComparison.OrdinalIgnoreCase));
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

                var columnIndex = table.Columns.FindIndex(column =>
                    string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
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
        var columnIndex = table.Columns.FindIndex(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
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
        var startColumnIndex = FindColumnIndex(table, startColumnName);
        var endColumnIndex = FindColumnIndex(table, endColumnName);
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

        var startColumnIndex = FindColumnIndex(table, startColumnName);
        var endColumnIndex = FindColumnIndex(table, endColumnName);
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

    private static int FindColumnIndex(StructuredTableModel table, string columnName) =>
        table.Columns.FindIndex(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));

    private static bool IsThisRowSection(string selector) =>
        string.Equals(selector.Trim(), "#THIS ROW", StringComparison.OrdinalIgnoreCase);

    private static GridRange? DataBodyRange(Sheet sheet, StructuredTableModel table, uint startCol, uint endCol)
    {
        var startRow = table.Range.Start.Row + 1;
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
        var startRow = table.Range.Start.Row + 1;
        var endRow = table.TotalsRowShown && table.Range.End.Row > startRow
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        return row >= startRow && row <= endRow;
    }

    private static bool StructuredTableNameMatches(StructuredTableModel table, string name) =>
        string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase);
}
