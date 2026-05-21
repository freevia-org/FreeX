using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static class StructuredReferenceResolver
{
    public static GridRange? ResolveDataBodyColumn(
        Workbook? workbook,
        Sheet? currentSheet,
        string tableName,
        string columnNameOrSelector)
        => Resolve(workbook, currentSheet, tableName, columnNameOrSelector);

    public static GridRange? Resolve(
        Workbook? workbook,
        Sheet? currentSheet,
        string tableName,
        string selector)
    {
        var sheets = workbook is not null
            ? workbook.Sheets
            : currentSheet is not null ? [currentSheet] : [];

        foreach (var sheet in sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (!StructuredTableNameMatches(table, tableName))
                    continue;

                if (TryResolveTableSelector(sheet, table, selector) is { } selectedRange)
                    return selectedRange;

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

    private static bool StructuredTableNameMatches(StructuredTableModel table, string name) =>
        string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase);
}
