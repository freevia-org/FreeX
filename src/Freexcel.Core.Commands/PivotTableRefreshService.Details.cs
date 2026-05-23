using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    public sealed record PivotDetailRows(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ScalarValue>> Rows);

    public static PivotDetailRows ExtractDetailRows(
        Workbook workbook,
        Sheet targetSheet,
        PivotTableModel pivotTable,
        CellAddress pivotCell)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || !pivotTable.TargetRange.Contains(pivotCell))
            return new PivotDetailRows([], []);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var outputRow = pivotCell.Row;
        var columnFields = pivotTable.ColumnFields.ToList();
        var firstDataRow = pivotTable.TargetRange.Start.Row + (uint)Math.Max(1, columnFields.Count);
        if (outputRow < firstDataRow)
            return new PivotDetailRows(headers, []);

        var rowFields = pivotTable.RowFields.ToList();
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        if (pivotCell.Col < firstValueColumn)
            return new PivotDetailRows(headers, []);

        var keys = new List<string>();
        var isRowGrandTotal = false;
        var isSubtotal = false;
        for (var index = 0; index < rowFields.Count; index++)
        {
            var key = ReadDetailRowKey(targetSheet, pivotTable, outputRow, firstDataRow, index, rowFields.Count);
            if (key is null)
                return new PivotDetailRows(headers, []);
            if (string.Equals(key, "Grand Total", StringComparison.OrdinalIgnoreCase))
            {
                keys.Clear();
                isRowGrandTotal = true;
                break;
            }

            if (key.EndsWith(" Total", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(key[..^" Total".Length]);
                isSubtotal = true;
                break;
            }

            keys.Add(key);
        }

        var columnKeys = ReadDetailColumnKeys(targetSheet, pivotTable, pivotCell, columnFields);
        if (columnKeys is null)
            return new PivotDetailRows(headers, []);

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesFieldSelections(row, pivotTable.PageFields))
            .Where(row => MatchesFieldSelections(row, rowFields))
            .Where(row => MatchesFieldSelections(row, columnFields))
            .Where(row => RowDetailMatches(row, rowFields, keys, isRowGrandTotal, isSubtotal))
            .Where(row => ColumnDetailMatches(row, columnFields, columnKeys))
            .ToList();
        return new PivotDetailRows(headers, rows);
    }

    private static string? ReadDetailRowKey(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint outputRow,
        uint firstDataRow,
        int fieldIndex,
        int rowFieldCount)
    {
        var column = pivotTable.TargetRange.Start.Col + (uint)fieldIndex;
        var value = sheet.GetCell(outputRow, column)?.Value;
        if (value is not null)
            return KeyText(value);

        if (pivotTable.RepeatItemLabels || fieldIndex >= rowFieldCount - 1)
            return null;

        for (var row = outputRow - 1; row >= firstDataRow; row--)
        {
            value = sheet.GetCell(row, column)?.Value;
            if (value is not null)
                return KeyText(value);
            if (row == firstDataRow)
                break;
        }

        return null;
    }

    private static bool RowDetailMatches(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<string> rowKeys,
        bool isRowGrandTotal,
        bool isSubtotal)
    {
        if (isRowGrandTotal)
            return true;

        var sourceKeys = rowFields
            .Select(field => GroupKeyText(row[field.SourceFieldIndex], field))
            .ToList();
        return isSubtotal
            ? sourceKeys.Take(rowKeys.Count).SequenceEqual(rowKeys, StringComparer.CurrentCultureIgnoreCase)
            : sourceKeys.SequenceEqual(rowKeys, StringComparer.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<string>? ReadDetailColumnKeys(
        Sheet sheet,
        PivotTableModel pivotTable,
        CellAddress pivotCell,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        if (columnFields.Count == 0)
            return [];

        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)pivotTable.RowFields.Count;
        if (pivotCell.Col < firstValueColumn)
            return null;

        if (pivotTable.ShowRowGrandTotals)
        {
            var dataFieldWidth = Math.Max(1, pivotTable.DataFields.Count);
            var valueOffset = pivotCell.Col - firstValueColumn;
            var materialized = GetMaterializedOutputRange(sheet, pivotTable);
            var grandTotalStart = materialized.End.Col >= (uint)dataFieldWidth - 1
                ? materialized.End.Col - (uint)dataFieldWidth + 1
                : materialized.End.Col;
            if (valueOffset >= 0 && pivotCell.Col >= grandTotalStart)
                return [];
        }

        var keys = new List<string>();
        for (var level = 0; level < columnFields.Count; level++)
        {
            var value = sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, pivotCell.Col)?.Value;
            if (value is null)
                return null;
            var key = KeyText(value);
            if (string.Equals(key, "Grand Total", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("Grand Total ", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            keys.Add(RemoveDataFieldCaptionSuffix(key, pivotTable.DataFields));
        }

        return keys;
    }

    private static string RemoveDataFieldCaptionSuffix(string key, IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        foreach (var dataField in dataFields)
        {
            var suffix = $" {dataField.Name}";
            if (key.EndsWith(suffix, StringComparison.CurrentCultureIgnoreCase))
                return key[..^suffix.Length];
        }

        return key;
    }

    private static bool ColumnDetailMatches(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<string> columnKeys)
    {
        if (columnKeys.Count == 0)
            return true;
        if (columnFields.Count != columnKeys.Count)
            return false;

        for (var index = 0; index < columnFields.Count; index++)
        {
            var field = columnFields[index];
            if (!string.Equals(
                    GroupKeyText(row[field.SourceFieldIndex], field),
                    columnKeys[index],
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
