using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class PivotTableRefreshService
{
    public sealed record PivotDetailRows(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ScalarValue>> Rows);

    public static void Refresh(Workbook workbook, Sheet targetSheet, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || pivotTable.DataFields.Count == 0)
            return;

        ClearTargetRange(targetSheet, pivotTable.TargetRange);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var columnFields = pivotTable.ColumnFields.ToList();
        if (!pivotTable.RowFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !pivotTable.PageFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !columnFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !pivotTable.DataFields.All(field => IsValidDataField(field, pivotTable, headers.Count)))
        {
            return;
        }

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesPageFilters(row, pivotTable.PageFields))
            .ToList();
        if (pivotTable.RowFields.Count == 0 && columnFields.Count == 0)
            WriteValuesOnlyPivot(targetSheet, pivotTable, headers, rows);
        else if (pivotTable.RowFields.Count == 0)
            WriteColumnOnlyPivot(targetSheet, pivotTable, headers, rows, columnFields);
        else if (columnFields.Count > 0)
            WriteMatrixPivot(targetSheet, pivotTable, headers, rows, columnFields);
        else
            WriteRowPivot(targetSheet, pivotTable, headers, rows);
    }

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
            .Where(row => MatchesPageFilters(row, pivotTable.PageFields))
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

    public static GridRange GetMaterializedOutputRange(Sheet sheet, PivotTableModel pivotTable)
    {
        uint? minRow = null;
        uint? minCol = null;
        uint? maxRow = null;
        uint? maxCol = null;

        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;

            minRow = minRow is null ? row : Math.Min(minRow.Value, row);
            minCol = minCol is null ? col : Math.Min(minCol.Value, col);
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        if (minRow is null || minCol is null || maxRow is null || maxCol is null)
            return new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);

        return new GridRange(
            new CellAddress(sheet.Id, minRow.Value, minCol.Value),
            new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
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

    private static void WriteRowPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = pivotTable.TargetRange.Start;
        var rowFields = pivotTable.RowFields.ToList();
        for (var index = 0; index < rowFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)rowFields.Count + (uint)index), new TextValue(pivotTable.DataFields[index].Name));

        var groups = rows
            .GroupBy(row => new PivotKey(rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .ToList();
        groups = ApplyLabelFilters(groups, pivotTable, rowFields);
        groups = ApplyValueFilters(groups, pivotTable, headers, rowFields);
        groups = ApplySorts(groups, pivotTable, headers, rowFields);
        var retainedRows = groups.SelectMany(group => group).ToList();
        var topSubtotalRows = pivotTable.ShowSubtotals && rowFields.Count > 1 && pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Top
            ? groups
                .GroupBy(group => new PivotKey(group.Key.Values.Take(rowFields.Count - 1).ToArray()))
                .ToDictionary(group => group.Key, group => group.SelectMany(item => item).ToList())
            : [];
        var outputRow = start.Row + 1;
        PivotKey? currentSubtotalKey = null;
        PivotKey? previousRowKey = null;
        var subtotalRows = new List<IReadOnlyList<ScalarValue>>();
        var calculatedItemTotals = new double[pivotTable.DataFields.Count];
        foreach (var group in groups)
        {
            if (pivotTable.ShowSubtotals && rowFields.Count > 1)
            {
                var subtotalKey = new PivotKey(group.Key.Values.Take(rowFields.Count - 1).ToArray());
                if (currentSubtotalKey is not null && !currentSubtotalKey.Equals(subtotalKey))
                {
                    if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
                    {
                        WriteSubtotalRow(sheet, pivotTable, headers, start, rowFields.Count, currentSubtotalKey, subtotalRows, outputRow);
                        outputRow++;
                    }
                    subtotalRows.Clear();
                }

                if (currentSubtotalKey is null || !currentSubtotalKey.Equals(subtotalKey))
                {
                    currentSubtotalKey = subtotalKey;
                    if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Top &&
                        topSubtotalRows.TryGetValue(subtotalKey, out var rowsForSubtotal))
                    {
                        WriteSubtotalRow(sheet, pivotTable, headers, start, rowFields.Count, subtotalKey, rowsForSubtotal, outputRow);
                        outputRow++;
                    }
                }

                if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
                    subtotalRows.AddRange(group);
            }

            for (var index = 0; index < group.Key.Values.Count; index++)
            {
                var suppressRepeat = !pivotTable.RepeatItemLabels &&
                    index < group.Key.Values.Count - 1 &&
                    previousRowKey is not null &&
                    previousRowKey.Values.Count > index &&
                    string.Equals(previousRowKey.Values[index], group.Key.Values[index], StringComparison.CurrentCultureIgnoreCase);
                if (!suppressRepeat)
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(group.Key.Values[index]));
            }
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                sheet.SetCell(
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFields.Count + (uint)index),
                    new NumberValue(Aggregate(group, pivotTable.DataFields[index], pivotTable, headers)));
            previousRowKey = group.Key;
            outputRow++;
            if (pivotTable.BlankLineAfterItems &&
                rowFields.Count > 1 &&
                IsEndOfOuterItem(groups, group, rowFields.Count))
            {
                outputRow++;
            }
        }
        if (rowFields.Count == 1)
        {
            foreach (var calculatedItem in pivotTable.CalculatedItems
                         .Where(item => item.SourceFieldIndex == rowFields[0].SourceFieldIndex)
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(calculatedItem.Name));
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var calculatedValue = EvaluateCalculatedItem(calculatedItem.Formula, groups, pivotTable.DataFields[index], pivotTable, headers);
                    sheet.SetCell(
                        new CellAddress(sheet.Id, outputRow, start.Col + 1 + (uint)index),
                        new NumberValue(calculatedValue));
                    calculatedItemTotals[index] += calculatedValue;
                }

                outputRow++;
            }
        }
        if (pivotTable.ShowSubtotals &&
            rowFields.Count > 1 &&
            pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom &&
            currentSubtotalKey is not null)
        {
            WriteSubtotalRow(sheet, pivotTable, headers, start, rowFields.Count, currentSubtotalKey, subtotalRows, outputRow);
            outputRow++;
        }

        if (pivotTable.ShowColumnGrandTotals)
        {
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                sheet.SetCell(
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFields.Count + (uint)index),
                    new NumberValue(Aggregate(retainedRows, pivotTable.DataFields[index], pivotTable, headers) + calculatedItemTotals[index]));
        }
    }

    private static void WriteValuesOnlyPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = pivotTable.TargetRange.Start;
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(pivotTable.DataFields[index].Name));
            sheet.SetCell(
                new CellAddress(sheet.Id, start.Row + 1, start.Col + (uint)index),
                new NumberValue(Aggregate(rows, pivotTable.DataFields[index], pivotTable, headers)));
        }
    }

    private static void WriteColumnOnlyPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var start = pivotTable.TargetRange.Start;
        var columnKeys = rows
            .Select(row => new PivotKey(columnFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .Distinct()
            .Order(PivotKeyComparer.Instance)
            .ToList();
        columnKeys = ApplyLabelFilters(columnKeys, pivotTable, columnFields);
        columnKeys = ApplyValueFilters(columnKeys, rows, pivotTable, headers, columnFields);
        columnKeys = ApplySorts(columnKeys, rows, pivotTable, headers, columnFields);
        var visibleRows = rows
            .Where(row => columnKeys.Any(columnKey => ColumnKeyMatches(row, columnFields, columnKey)))
            .ToList();
        var singleDataField = pivotTable.DataFields.Count == 1;

        var outputColumn = start.Col;
        foreach (var columnKey in columnKeys)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                WriteColumnHeader(sheet, start.Row, outputColumn, columnKey, dataField, singleDataField);
                outputColumn++;
            }
        }

        if (pivotTable.ShowRowGrandTotals)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                var caption = singleDataField ? "Grand Total" : $"Grand Total {dataField.Name}";
                sheet.SetCell(new CellAddress(sheet.Id, start.Row, outputColumn), new TextValue(caption));
                outputColumn++;
            }
        }

        var outputRow = start.Row + (uint)columnFields.Count;
        outputColumn = start.Col;
        foreach (var columnKey in columnKeys)
        {
            var columnRows = rows.Where(row => ColumnKeyMatches(row, columnFields, columnKey)).ToList();
            foreach (var dataField in pivotTable.DataFields)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(columnRows, dataField, pivotTable, headers)));
                outputColumn++;
            }
        }

        if (pivotTable.ShowRowGrandTotals)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(visibleRows, dataField, pivotTable, headers)));
                outputColumn++;
            }
        }
    }

    private static bool IsEndOfOuterItem(
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        IGrouping<PivotKey, IReadOnlyList<ScalarValue>> group,
        int rowFieldCount)
    {
        var index = -1;
        for (var i = 0; i < groups.Count; i++)
        {
            if (ReferenceEquals(groups[i], group))
            {
                index = i;
                break;
            }
        }
        if (index < 0 || index >= groups.Count - 1)
            return true;
        var currentOuter = group.Key.Values.Take(rowFieldCount - 1);
        var nextOuter = groups[index + 1].Key.Values.Take(rowFieldCount - 1);
        return !currentOuter.SequenceEqual(nextOuter, StringComparer.CurrentCultureIgnoreCase);
    }

    private static void WriteSubtotalRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        int rowFieldCount,
        PivotKey subtotalKey,
        IReadOnlyList<IReadOnlyList<ScalarValue>> subtotalRows,
        uint outputRow)
    {
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{subtotalKey.Values[0]} Total"));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldCount + (uint)index),
                new NumberValue(Aggregate(subtotalRows, pivotTable.DataFields[index], pivotTable, headers)));
    }

    private static void WriteMatrixPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var start = pivotTable.TargetRange.Start;
        var rowFields = pivotTable.RowFields.ToList();
        var rowGroups = rows
            .GroupBy(row => new PivotKey(rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .ToList();
        rowGroups = ApplyLabelFilters(rowGroups, pivotTable, rowFields);
        rowGroups = ApplyValueFilters(rowGroups, pivotTable, headers, rowFields);
        rowGroups = ApplySorts(rowGroups, pivotTable, headers, rowFields);
        var retainedRows = rowGroups.SelectMany(group => group).ToList();
        var columnKeys = retainedRows
            .Select(row => new PivotKey(columnFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .Distinct()
            .Order(PivotKeyComparer.Instance)
            .ToList();
        columnKeys = ApplyLabelFilters(columnKeys, pivotTable, columnFields);
        columnKeys = ApplyValueFilters(columnKeys, retainedRows, pivotTable, headers, columnFields);
        columnKeys = ApplySorts(columnKeys, retainedRows, pivotTable, headers, columnFields);
        var visibleRows = retainedRows
            .Where(row => columnKeys.Any(columnKey => ColumnKeyMatches(row, columnFields, columnKey)))
            .ToList();
        var singleDataField = pivotTable.DataFields.Count == 1;

        for (var index = 0; index < rowFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));

        var valueStartCol = start.Col + (uint)rowFields.Count;
        var outputColumn = valueStartCol;
        foreach (var columnKey in columnKeys)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                WriteColumnHeader(sheet, start.Row, outputColumn, columnKey, dataField, singleDataField);
                outputColumn++;
            }
        }
        if (pivotTable.ShowRowGrandTotals)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                var caption = singleDataField ? "Grand Total" : $"Grand Total {dataField.Name}";
                sheet.SetCell(new CellAddress(sheet.Id, start.Row, outputColumn), new TextValue(caption));
                outputColumn++;
            }
        }

        var outputRow = start.Row + (uint)columnFields.Count;
        foreach (var rowGroup in rowGroups)
        {
            for (var index = 0; index < rowGroup.Key.Values.Count; index++)
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(rowGroup.Key.Values[index]));

            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = rowGroup
                    .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                    .ToList();
                foreach (var dataField in pivotTable.DataFields)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(columnRows, dataField, pivotTable, headers)));
                    outputColumn++;
                }
            }
            if (pivotTable.ShowRowGrandTotals)
            {
                var visibleRowGroupRows = rowGroup
                    .Where(row => columnKeys.Any(columnKey => ColumnKeyMatches(row, columnFields, columnKey)))
                    .ToList();
                foreach (var dataField in pivotTable.DataFields)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(visibleRowGroupRows, dataField, pivotTable, headers)));
                    outputColumn++;
                }
            }
            outputRow++;
        }

        if (pivotTable.ShowColumnGrandTotals)
        {
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = retainedRows
                    .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                    .ToList();
                foreach (var dataField in pivotTable.DataFields)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(columnRows, dataField, pivotTable, headers)));
                    outputColumn++;
                }
            }
            if (pivotTable.ShowRowGrandTotals)
            {
                foreach (var dataField in pivotTable.DataFields)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, outputColumn), new NumberValue(Aggregate(visibleRows, dataField, pivotTable, headers)));
                    outputColumn++;
                }
            }
        }
    }

    private static void WriteColumnHeader(
        Sheet sheet,
        uint startRow,
        uint outputColumn,
        PivotKey columnKey,
        PivotDataFieldModel dataField,
        bool singleDataField)
    {
        for (var level = 0; level < columnKey.Values.Count; level++)
        {
            var caption = columnKey.Values[level];
            if (!singleDataField && level == columnKey.Values.Count - 1)
                caption = $"{caption} {dataField.Name}";
            sheet.SetCell(new CellAddress(sheet.Id, startRow + (uint)level, outputColumn), new TextValue(caption));
        }
    }

    private static bool ColumnKeyMatches(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> columnFields,
        PivotKey columnKey)
    {
        if (columnFields.Count != columnKey.Values.Count)
            return false;

        for (var index = 0; index < columnFields.Count; index++)
        {
            var field = columnFields[index];
            if (!string.Equals(
                    GroupKeyText(row[field.SourceFieldIndex], field),
                    columnKey.Values[index],
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> ReadHeaders(Sheet sheet, GridRange range)
    {
        var headers = new List<string>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var value = sheet.GetCell(range.Start.Row, col)?.Value;
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private static IEnumerable<IReadOnlyList<ScalarValue>> ReadSourceRows(Sheet sheet, GridRange range, int fieldCount)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var values = new List<ScalarValue>(fieldCount);
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                values.Add(sheet.GetCell(row, col)?.Value ?? BlankValue.Instance);
            yield return values;
        }
    }

    private static void ClearTargetRange(Sheet sheet, GridRange targetRange)
    {
        for (var row = targetRange.Start.Row; row <= targetRange.End.Row; row++)
        for (var col = targetRange.Start.Col; col <= targetRange.End.Col; col++)
            sheet.ClearCell(row, col);
    }

    private static bool IsValidField(int index, int fieldCount) => index >= 0 && index < fieldCount;

    private static bool IsValidDataField(PivotDataFieldModel field, PivotTableModel pivotTable, int fieldCount) =>
        IsValidField(field.SourceFieldIndex, fieldCount) ||
        (!string.IsNullOrWhiteSpace(field.CalculatedFieldName) &&
         pivotTable.CalculatedFields.Any(calculated =>
             string.Equals(calculated.Name, field.CalculatedFieldName, StringComparison.OrdinalIgnoreCase)));

    private static bool MatchesPageFilters(IReadOnlyList<ScalarValue> row, IReadOnlyList<PivotFieldModel> pageFields)
    {
        foreach (var field in pageFields)
        {
            var selectedItems = (field.SelectedItems ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item) && !string.Equals(item, "(All)", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selectedItems.Count > 0)
            {
                if (!selectedItems.Contains(GroupKeyText(row[field.SourceFieldIndex], field), StringComparer.CurrentCultureIgnoreCase))
                    return false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.SelectedItem) ||
                string.Equals(field.SelectedItem, "(All)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(GroupKeyText(row[field.SourceFieldIndex], field), field.SelectedItem, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplyValueFilters(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.SourceFieldIndex is not null &&
                !rowFields.Any(field => field.SourceFieldIndex == filter.SourceFieldIndex.Value))
            {
                continue;
            }

            if (filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }
            if ((filter.Kind == PivotValueFilterKind.Top || filter.Kind == PivotValueFilterKind.Bottom) && filter.Count <= 0)
                continue;

            var dataField = pivotTable.DataFields[filter.DataFieldIndex];
            groups = filter.Kind switch
            {
                PivotValueFilterKind.Bottom => groups.OrderBy(group => Aggregate(group, dataField, pivotTable, headers)).Take(filter.Count).ToList(),
                PivotValueFilterKind.Top => groups.OrderByDescending(group => Aggregate(group, dataField, pivotTable, headers)).Take(filter.Count).ToList(),
                PivotValueFilterKind.GreaterThan => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) > (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.GreaterThanOrEqual => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) >= (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.LessThan => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) < (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.LessThanOrEqual => groups.Where(group => Aggregate(group, dataField, pivotTable, headers) <= (filter.ComparisonValue ?? 0)).ToList(),
                PivotValueFilterKind.Equals => groups.Where(group => Math.Abs(Aggregate(group, dataField, pivotTable, headers) - (filter.ComparisonValue ?? 0)) < 0.0000001).ToList(),
                PivotValueFilterKind.DoesNotEqual => groups.Where(group => Math.Abs(Aggregate(group, dataField, pivotTable, headers) - (filter.ComparisonValue ?? 0)) >= 0.0000001).ToList(),
                _ => groups
            };
            groups = groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return groups;
    }

    private static List<PivotKey> ApplyValueFilters(
        List<PivotKey> keys,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var filter in pivotTable.ValueFilters)
        {
            if (filter.SourceFieldIndex is null ||
                !fields.Any(field => field.SourceFieldIndex == filter.SourceFieldIndex.Value))
            {
                continue;
            }

            if (filter.DataFieldIndex < 0 ||
                filter.DataFieldIndex >= pivotTable.DataFields.Count)
            {
                continue;
            }
            if ((filter.Kind == PivotValueFilterKind.Top || filter.Kind == PivotValueFilterKind.Bottom) && filter.Count <= 0)
                continue;

            var dataField = pivotTable.DataFields[filter.DataFieldIndex];
            var aggregates = keys
                .Select(key => new
                {
                    Key = key,
                    Value = Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)
                })
                .ToList();

            keys = filter.Kind switch
            {
                PivotValueFilterKind.Bottom => aggregates.OrderBy(item => item.Value).Take(filter.Count).Select(item => item.Key).ToList(),
                PivotValueFilterKind.Top => aggregates.OrderByDescending(item => item.Value).Take(filter.Count).Select(item => item.Key).ToList(),
                PivotValueFilterKind.GreaterThan => aggregates.Where(item => item.Value > (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.GreaterThanOrEqual => aggregates.Where(item => item.Value >= (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.LessThan => aggregates.Where(item => item.Value < (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.LessThanOrEqual => aggregates.Where(item => item.Value <= (filter.ComparisonValue ?? 0)).Select(item => item.Key).ToList(),
                PivotValueFilterKind.Equals => aggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) < 0.0000001).Select(item => item.Key).ToList(),
                PivotValueFilterKind.DoesNotEqual => aggregates.Where(item => Math.Abs(item.Value - (filter.ComparisonValue ?? 0)) >= 0.0000001).Select(item => item.Key).ToList(),
                _ => keys
            };
            keys = keys.Order(PivotKeyComparer.Instance).ToList();
        }

        return keys;
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplyLabelFilters(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        foreach (var filter in pivotTable.LabelFilters)
        {
            var rowFieldIndex = rowFields.ToList().FindIndex(field => field.SourceFieldIndex == filter.SourceFieldIndex);
            if (rowFieldIndex < 0)
                continue;

            groups = groups
                .Where(group => MatchesLabelFilter(group.Key.Values[rowFieldIndex], filter))
                .ToList();
        }

        return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
    }

    private static List<PivotKey> ApplyLabelFilters(
        List<PivotKey> keys,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> fields)
    {
        foreach (var filter in pivotTable.LabelFilters)
        {
            var fieldIndex = fields.ToList().FindIndex(field => field.SourceFieldIndex == filter.SourceFieldIndex);
            if (fieldIndex < 0)
                continue;

            keys = keys
                .Where(key => MatchesLabelFilter(key.Values[fieldIndex], filter))
                .ToList();
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static bool MatchesLabelFilter(string label, PivotLabelFilterModel filter)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return filter.Kind switch
        {
            PivotLabelFilterKind.Equals => string.Equals(label, filter.Value, comparison),
            PivotLabelFilterKind.DoesNotEqual => !string.Equals(label, filter.Value, comparison),
            PivotLabelFilterKind.BeginsWith => label.StartsWith(filter.Value, comparison),
            PivotLabelFilterKind.EndsWith => label.EndsWith(filter.Value, comparison),
            PivotLabelFilterKind.Contains => label.Contains(filter.Value, comparison),
            PivotLabelFilterKind.DoesNotContain => !label.Contains(filter.Value, comparison),
            _ => true
        };
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> ApplySorts(
        List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        if (pivotTable.Sorts.Count == 0)
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        if (sort.Target == PivotSortTarget.Value &&
            rowFields.Any(field => field.SourceFieldIndex == sort.FieldIndex) &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            return sort.Direction == PivotSortDirection.Descending
                ? groups.OrderByDescending(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList()
                : groups.OrderBy(group => Aggregate(group, dataField, pivotTable, headers)).ThenBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        if (!rowFields.Any(field => field.SourceFieldIndex == sort.FieldIndex))
        {
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
        }

        return sort.Direction == PivotSortDirection.Descending
            ? groups.OrderByDescending(group => group.Key, PivotKeyComparer.Instance).ToList()
            : groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
    }

    private static List<PivotKey> ApplySorts(
        List<PivotKey> keys,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields)
    {
        if (pivotTable.Sorts.Count == 0)
            return keys.Order(PivotKeyComparer.Instance).ToList();

        var sort = pivotTable.Sorts[^1];
        var fieldIndex = fields.ToList().FindIndex(field => field.SourceFieldIndex == sort.FieldIndex);
        if (sort.Target == PivotSortTarget.Label && fieldIndex >= 0)
        {
            return sort.Direction == PivotSortDirection.Descending
                ? keys.OrderByDescending(key => key.Values[fieldIndex], StringComparer.CurrentCultureIgnoreCase).ThenBy(key => key, PivotKeyComparer.Instance).ToList()
                : keys.OrderBy(key => key.Values[fieldIndex], StringComparer.CurrentCultureIgnoreCase).ThenBy(key => key, PivotKeyComparer.Instance).ToList();
        }

        if (sort.Target == PivotSortTarget.Value &&
            fieldIndex >= 0 &&
            sort.DataFieldIndex >= 0 &&
            sort.DataFieldIndex < pivotTable.DataFields.Count)
        {
            var dataField = pivotTable.DataFields[sort.DataFieldIndex];
            return sort.Direction == PivotSortDirection.Descending
                ? keys.OrderByDescending(key => Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)).ThenBy(key => key, PivotKeyComparer.Instance).ToList()
                : keys.OrderBy(key => Aggregate(rows.Where(row => ColumnKeyMatches(row, fields, key)).ToList(), dataField, pivotTable, headers)).ThenBy(key => key, PivotKeyComparer.Instance).ToList();
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static string GroupKeyText(ScalarValue value, PivotFieldModel field) =>
        GroupKeyText(value, field.Grouping, field.GroupStart, field.GroupEnd, field.GroupInterval);

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping) =>
        GroupKeyText(value, grouping, null, null, null);

    private static string GroupKeyText(ScalarValue value, PivotFieldGrouping grouping, double? groupStart, double? groupEnd, double? groupInterval)
    {
        if (grouping == PivotFieldGrouping.None)
            return KeyText(value);

        if (grouping == PivotFieldGrouping.NumberRange)
            return NumberRangeKeyText(value, groupStart ?? 0, groupInterval ?? 1);

        if (value is not DateTimeValue dateValue)
            return KeyText(value);

        var date = dateValue.ToDateTime();
        return grouping switch
        {
            PivotFieldGrouping.Year => date.Year.ToString(CultureInfo.InvariantCulture),
            PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
            PivotFieldGrouping.Month => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => KeyText(value)
        };
    }

    private static string NumberRangeKeyText(ScalarValue value, double start, double interval)
    {
        if (interval <= 0)
            interval = 1;
        var number = Number(value);
        var bucketStart = start + Math.Floor((number - start) / interval) * interval;
        var bucketEnd = bucketStart + interval - 1;
        return $"{bucketStart:0.########}-{bucketEnd:0.########}";
    }

    private static string KeyText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue date => date.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => "(blank)"
        };

    private static double Number(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            BoolValue boolean => boolean.Value ? 1 : 0,
            _ => 0
        };

    private static bool HasNumericValue(ScalarValue value) =>
        value is NumberValue or DateTimeValue or BoolValue;

    private static bool IsNonBlank(ScalarValue value) =>
        value is not BlankValue;

    private static double Aggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var values = rows.Select(row => GetDataFieldValue(row, dataField, pivotTable, headers)).ToList();
        var numericValues = values.Where(HasNumericValue).Select(Number).ToList();
        return dataField.SummaryFunction.Trim().ToLowerInvariant() switch
        {
            "count" => values.Count(IsNonBlank),
            "countnums" or "countNums" => numericValues.Count,
            "average" or "avg" => numericValues.Count == 0 ? 0 : numericValues.Average(),
            "min" => numericValues.Count == 0 ? 0 : numericValues.Min(),
            "max" => numericValues.Count == 0 ? 0 : numericValues.Max(),
            "product" => numericValues.Count == 0 ? 0 : numericValues.Aggregate(1.0, (acc, value) => acc * value),
            _ => numericValues.Sum()
        };
    }

    private static ScalarValue GetDataFieldValue(
        IReadOnlyList<ScalarValue> row,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.SourceFieldIndex >= 0 && dataField.SourceFieldIndex < row.Count)
            return row[dataField.SourceFieldIndex];

        if (!string.IsNullOrWhiteSpace(dataField.CalculatedFieldName))
        {
            var calculated = pivotTable.CalculatedFields.FirstOrDefault(field =>
                string.Equals(field.Name, dataField.CalculatedFieldName, StringComparison.OrdinalIgnoreCase));
            if (calculated is not null)
                return new NumberValue(EvaluateCalculatedField(calculated.Formula, row, headers));
        }

        return BlankValue.Instance;
    }

    private static double EvaluateCalculatedField(
        string formula,
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<string> headers)
    {
        var parser = new CalculatedFieldExpressionParser(formula, name =>
        {
            var index = headers.ToList().FindIndex(header => string.Equals(header, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < row.Count ? Number(row[index]) : 0;
        });
        return parser.Parse();
    }

    private static double EvaluateCalculatedItem(
        string formula,
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var parser = new CalculatedFieldExpressionParser(formula, name =>
        {
            var group = groups.FirstOrDefault(candidate =>
                candidate.Key.Values.Count > 0 &&
                string.Equals(candidate.Key.Values[0], name, StringComparison.CurrentCultureIgnoreCase));
            return group is null ? 0 : Aggregate(group, dataField, pivotTable, headers);
        });
        return parser.Parse();
    }

    private sealed class PivotKey : IEquatable<PivotKey>
    {
        public PivotKey(IReadOnlyList<string> values)
        {
            Values = values;
        }

        public IReadOnlyList<string> Values { get; }

        public bool Equals(PivotKey? other) =>
            other is not null && Values.SequenceEqual(other.Values, StringComparer.CurrentCultureIgnoreCase);

        public override bool Equals(object? obj) =>
            obj is PivotKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in Values)
                hash.Add(value, StringComparer.CurrentCultureIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class PivotKeyComparer : IComparer<PivotKey>
    {
        public static PivotKeyComparer Instance { get; } = new();

        public int Compare(PivotKey? x, PivotKey? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var count = Math.Min(x.Values.Count, y.Values.Count);
            for (var index = 0; index < count; index++)
            {
                var comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.Values[index], y.Values[index]);
                if (comparison != 0)
                    return comparison;
            }

            return x.Values.Count.CompareTo(y.Values.Count);
        }
    }

    private sealed class CalculatedFieldExpressionParser
    {
        private readonly string _text;
        private readonly Func<string, double> _fieldValue;
        private int _position;

        public CalculatedFieldExpressionParser(string text, Func<string, double> fieldValue)
        {
            _text = text ?? "";
            _fieldValue = fieldValue;
        }

        public double Parse()
        {
            var value = ParseAddSubtract();
            SkipWhitespace();
            return value;
        }

        private double ParseAddSubtract()
        {
            var value = ParseMultiplyDivide();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+'))
                    value += ParseMultiplyDivide();
                else if (TryConsume('-'))
                    value -= ParseMultiplyDivide();
                else
                    return value;
            }
        }

        private double ParseMultiplyDivide()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*'))
                    value *= ParseUnary();
                else if (TryConsume('/'))
                {
                    var denominator = ParseUnary();
                    value = Math.Abs(denominator) < double.Epsilon ? 0 : value / denominator;
                }
                else
                    return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume('+'))
                return ParseUnary();
            if (TryConsume('-'))
                return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var value = ParseAddSubtract();
                TryConsume(')');
                return value;
            }

            if (Peek() == '[')
                return _fieldValue(ReadBracketedIdentifier());
            if (char.IsLetter(Peek()) || Peek() == '_')
                return _fieldValue(ReadIdentifier());
            return ReadNumber();
        }

        private string ReadBracketedIdentifier()
        {
            TryConsume('[');
            var start = _position;
            while (_position < _text.Length && _text[_position] != ']')
                _position++;
            var value = _text[start.._position].Trim();
            TryConsume(']');
            return value;
        }

        private string ReadIdentifier()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == ' '))
                _position++;
            return _text[start.._position].Trim();
        }

        private double ReadNumber()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                _position++;
            return double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private bool TryConsume(char ch)
        {
            SkipWhitespace();
            if (Peek() != ch)
                return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }
    }
}
