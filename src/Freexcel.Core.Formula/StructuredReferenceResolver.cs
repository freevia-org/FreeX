using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static class StructuredReferenceResolver
{
    public static GridRange? ResolveDataBodyColumn(
        Workbook? workbook,
        Sheet? currentSheet,
        string tableName,
        string columnName)
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

                var columnIndex = table.Columns.FindIndex(column =>
                    string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
                if (columnIndex < 0)
                    return null;

                var startRow = table.Range.Start.Row + 1;
                var endRow = table.TotalsRowShown && table.Range.End.Row > startRow
                    ? table.Range.End.Row - 1
                    : table.Range.End.Row;
                if (startRow > endRow)
                    return null;

                var col = table.Range.Start.Col + (uint)columnIndex;
                return new GridRange(
                    new CellAddress(sheet.Id, startRow, col),
                    new CellAddress(sheet.Id, endRow, col));
            }
        }

        return null;
    }

    private static bool StructuredTableNameMatches(StructuredTableModel table, string name) =>
        string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase);
}
