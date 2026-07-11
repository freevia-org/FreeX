using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    public sealed record PivotDetailRows(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ScalarValue>> Rows);
    private sealed record DetailRowSelection(IReadOnlyList<string> Keys, bool IsRowGrandTotal, bool IsSubtotal);

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

        var rowSelection = ReadDetailRowSelection(targetSheet, pivotTable, outputRow, firstDataRow, rowFields);
        if (rowSelection is null)
            return new PivotDetailRows(headers, []);

        var columnKeys = ReadDetailColumnKeys(targetSheet, pivotTable, pivotCell, columnFields);
        if (columnKeys is null)
            return new PivotDetailRows(headers, []);

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesFieldSelections(row, pivotTable.PageFields))
            .Where(row => MatchesFieldSelections(row, rowFields))
            .Where(row => MatchesFieldSelections(row, columnFields))
            .Where(row => RowDetailMatches(row, rowFields, rowSelection.Keys, rowSelection.IsRowGrandTotal, rowSelection.IsSubtotal))
            .Where(row => ColumnDetailMatches(row, columnFields, columnKeys))
            .ToList();
        return new PivotDetailRows(headers, rows);
    }

    private static DetailRowSelection? ReadDetailRowSelection(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint outputRow,
        uint firstDataRow,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        if (rowFields.Count == 0)
            return new DetailRowSelection([], IsRowGrandTotal: false, IsSubtotal: false);

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            return ReadCompactDetailRowSelection(sheet, pivotTable, outputRow, firstDataRow, rowFields.Count);

        // A real subtotal row can only exist when WriteSubtotalRow's own gate
        // (pivotTable.ShowSubtotals && rowFields.Count > 1) is satisfied, and even then a genuine
        // subtotal row leaves every row-field column strictly after the caption blank -- it never
        // writes a value into the innermost (last) row-field column, unlike a normal leaf data row,
        // whose last row-field column is always populated (label-repeat suppression never applies
        // to the innermost field; see ShouldSuppressRepeatedRowLabel). So a "<label> Total" caption
        // is only treated as a real subtotal when subtotals are structurally possible AND the row's
        // last row-field column is blank -- otherwise it's a legitimate item whose label happens to
        // end in " Total".
        var lastFieldColumn = pivotTable.TargetRange.Start.Col + (uint)(rowFields.Count - 1);
        var canBeSubtotalRow =
            pivotTable.ShowSubtotals &&
            rowFields.Count > 1 &&
            sheet.GetCell(outputRow, lastFieldColumn)?.Value is null or BlankValue;

        var keys = new List<string>();
        var isRowGrandTotal = false;
        var isSubtotal = false;
        for (var index = 0; index < rowFields.Count; index++)
        {
            var key = ReadDetailRowKey(sheet, pivotTable, outputRow, firstDataRow, index, rowFields.Count);
            if (key is null)
                return null;
            if (IsPivotGrandTotalCaption(pivotTable, key))
            {
                keys.Clear();
                isRowGrandTotal = true;
                break;
            }

            if (canBeSubtotalRow && key.EndsWith(" Total", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(key[..^" Total".Length]);
                isSubtotal = true;
                break;
            }

            keys.Add(key);
        }

        return new DetailRowSelection(keys, isRowGrandTotal, isSubtotal);
    }

    private static DetailRowSelection? ReadCompactDetailRowSelection(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint outputRow,
        uint firstDataRow,
        int rowFieldCount)
    {
        var labelColumn = pivotTable.TargetRange.Start.Col;
        var labelValue = sheet.GetCell(outputRow, labelColumn)?.Value;
        if (labelValue is null)
            return null;

        var label = KeyText(labelValue);
        if (IsPivotGrandTotalCaption(pivotTable, label))
            return new DetailRowSelection([], IsRowGrandTotal: true, IsSubtotal: false);

        // A real subtotal row can only exist when WriteSubtotalRow's own gate (ShowSubtotals with
        // more than one row field, guaranteed true here by the caller) is satisfied -- with
        // subtotals off, a "<label> Total" caption can only be a legitimate item value.
        if (pivotTable.ShowSubtotals && label.EndsWith(" Total", StringComparison.OrdinalIgnoreCase))
            return new DetailRowSelection([label[..^" Total".Length]], IsRowGrandTotal: false, IsSubtotal: true);

        var keys = new List<string> { label };
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        for (var row = outputRow - 1; row >= firstDataRow && keys.Count < rowFieldCount; row--)
        {
            var candidateValue = sheet.GetCell(row, labelColumn)?.Value;
            if (candidateValue is not null &&
                sheet.GetCell(row, firstValueColumn)?.Value is null)
            {
                keys.Insert(0, KeyText(candidateValue));
            }

            if (row == firstDataRow)
                break;
        }

        if (keys.Count == rowFieldCount)
            return new DetailRowSelection(keys, IsRowGrandTotal: false, IsSubtotal: false);

        var combined = SplitCompactCombinedLabel(label, rowFieldCount);
        return combined is null
            ? null
            : new DetailRowSelection(combined, IsRowGrandTotal: false, IsSubtotal: false);
    }

    private static IReadOnlyList<string>? SplitCompactCombinedLabel(string label, int rowFieldCount)
    {
        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == rowFieldCount ? parts : null;
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

        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)RowFieldOutputColumnCount(pivotTable);
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
            if (IsPivotGrandTotalCaption(pivotTable, key))
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
