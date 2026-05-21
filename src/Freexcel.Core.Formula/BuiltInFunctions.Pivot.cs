using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue GetPivotData(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0)
            return ErrorValue.Value;
        if (ctx.CurrentSheet is null || ctx.CurrentWorkbook is null)
            return ErrorValue.Ref;
        if (args[0] is ErrorValue dataFieldError)
            return dataFieldError;
        if (args[1] is ErrorValue pivotRefError)
            return pivotRefError;
        if (args[1] is not RangeValue { RowCount: 1, ColCount: 1 } pivotReference)
            return ErrorValue.Ref;

        var dataFieldCaption = PivotText(args[0]);
        if (string.IsNullOrWhiteSpace(dataFieldCaption))
            return ErrorValue.Value;

        var filters = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        for (var index = 2; index < args.Count; index += 2)
        {
            if (args[index] is ErrorValue fieldError)
                return fieldError;
            if (args[index + 1] is ErrorValue itemError)
                return itemError;
            var fieldName = PivotText(args[index]);
            var itemName = PivotText(args[index + 1]);
            if (string.IsNullOrWhiteSpace(fieldName))
                return ErrorValue.Value;
            if (filters.TryGetValue(fieldName, out var existingItem) &&
                !string.Equals(existingItem, itemName, StringComparison.CurrentCultureIgnoreCase))
            {
                return ErrorValue.Ref;
            }
            filters[fieldName] = itemName;
        }

        var locatedPivot = FindPivotTableForReference(ctx, pivotReference);
        if (locatedPivot is null)
            return ErrorValue.Ref;
        var (pivotSheet, pivotTable) = locatedPivot.Value;

        var headers = ReadPivotSourceHeaders(ctx.CurrentWorkbook, pivotTable);
        var dataFieldIndex = pivotTable.DataFields.FindIndex(field =>
            string.Equals(field.Name, dataFieldCaption, StringComparison.CurrentCultureIgnoreCase));
        if (dataFieldIndex < 0)
            return ErrorValue.Ref;
        if (!GetPivotDataFilterFieldsAreVisible(pivotTable, headers, filters))
            return ErrorValue.Ref;
        if (!PageFieldFiltersMatch(pivotTable, headers, filters))
            return ErrorValue.Ref;

        var materialized = GetPivotMaterializedRange(pivotSheet, pivotTable);
        var headerRows = (uint)Math.Max(1, pivotTable.ColumnFields.Count);
        var firstDataRow = pivotTable.TargetRange.Start.Row + headerRows;
        var outputRow = ResolveGetPivotDataRow(pivotSheet, pivotTable, headers, filters, firstDataRow, materialized.End.Row);
        if (outputRow is null)
            return ErrorValue.Ref;

        var outputColumn = ResolveGetPivotDataColumn(pivotSheet, pivotTable, headers, filters, dataFieldIndex, materialized.End.Col);
        if (outputColumn is null)
            return ErrorValue.Ref;

        return pivotSheet.GetCell(outputRow.Value, outputColumn.Value)?.Value ?? ErrorValue.Ref;
    }

    private static bool GetPivotDataFilterFieldsAreVisible(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        var visibleFields = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .Select(field => PivotHeader(headers, field.SourceFieldIndex))
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return filters.Keys.All(visibleFields.Contains);
    }

    private static bool PageFieldFiltersMatch(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        foreach (var pageField in pivotTable.PageFields)
        {
            var header = PivotHeader(headers, pageField.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            if (!string.IsNullOrWhiteSpace(pageField.SelectedItem))
                return string.Equals(pageField.SelectedItem, expected, StringComparison.CurrentCultureIgnoreCase);

            if (pageField.SelectedItems is { Count: > 0 } selectedItems)
                return selectedItems.Contains(expected, StringComparer.CurrentCultureIgnoreCase);
        }

        return true;
    }

    private static (Sheet Sheet, PivotTableModel PivotTable)? FindPivotTableForReference(
        IEvalContext ctx,
        RangeValue reference)
    {
        var row = reference.StartRow;
        var col = reference.StartCol;
        if (!string.IsNullOrWhiteSpace(reference.SheetName))
        {
            var sheet = ctx.CurrentWorkbook?.GetSheet(reference.SheetName);
            var address = sheet is null ? (CellAddress?)null : new CellAddress(sheet.Id, row, col);
            var pivot = address is null
                ? null
                : sheet!.PivotTables.FirstOrDefault(item => item.TargetRange.Contains(address.Value));
            return pivot is null ? null : (sheet!, pivot);
        }

        if (ctx.CurrentSheet is not null)
        {
            var currentAddress = new CellAddress(ctx.CurrentSheet.Id, row, col);
            var currentPivot = ctx.CurrentSheet.PivotTables.FirstOrDefault(pivot => pivot.TargetRange.Contains(currentAddress));
            if (currentPivot is not null)
                return (ctx.CurrentSheet, currentPivot);
        }

        if (ctx.CurrentWorkbook is null)
            return null;

        foreach (var sheet in ctx.CurrentWorkbook.Sheets)
        {
            var address = new CellAddress(sheet.Id, row, col);
            var pivot = sheet.PivotTables.FirstOrDefault(item => item.TargetRange.Contains(address));
            if (pivot is not null)
                return (sheet, pivot);
        }

        return null;
    }

    private static uint? ResolveGetPivotDataRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint lastRow)
    {
        var rowFields = pivotTable.RowFields.ToList();
        if (rowFields.Count == 0)
            return firstDataRow <= lastRow ? firstDataRow : null;

        var requestedRowFieldCount = rowFields.Count(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex)));
        if (requestedRowFieldCount == 0)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (IsPivotGrandTotalText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
                    return row;
            }
        }

        if (requestedRowFieldCount > 0 && requestedRowFieldCount < rowFields.Count)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (TryReadPivotSubtotalCaption(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value, out var subtotalItem) &&
                    PivotSubtotalMatches(sheet, pivotTable, headers, filters, firstDataRow, row, subtotalItem, requestedRowFieldCount))
                {
                    return row;
                }
            }
        }

        for (var row = firstDataRow; row <= lastRow; row++)
        {
            if (IsPivotGrandTotalText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
            {
                if (!rowFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
                    return row;
                continue;
            }

            if (TryCompactPivotRowMatch(sheet, pivotTable, headers, filters, row, rowFields, requestedRowFieldCount))
                return row;

            var matches = true;
            for (var index = 0; index < rowFields.Count; index++)
            {
                var header = PivotHeader(headers, rowFields[index].SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var actual = ReadPivotRowItem(sheet, pivotTable, row, firstDataRow, index, rowFields.Count);
                if (!string.Equals(actual, expected, StringComparison.CurrentCultureIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return row;
        }

        return null;
    }

    private static bool TryCompactPivotRowMatch(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint row,
        IReadOnlyList<PivotFieldModel> rowFields,
        int requestedRowFieldCount)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact || rowFields.Count <= 1)
            return false;
        if (requestedRowFieldCount != rowFields.Count)
            return false;

        var expectedParts = new List<string>(rowFields.Count);
        foreach (var field in rowFields)
        {
            var header = PivotHeader(headers, field.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                return false;
            expectedParts.Add(expected);
        }

        var actual = PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);
        var expectedCaption = string.Join(" ", expectedParts);
        return string.Equals(actual, expectedCaption, StringComparison.CurrentCultureIgnoreCase);
    }

    private static uint? ResolveGetPivotDataColumn(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        int dataFieldIndex,
        uint lastColumn)
    {
        var rowFieldColumns = PivotRowFieldOutputColumnCount(pivotTable);
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)rowFieldColumns;
        if (pivotTable.ColumnFields.Count == 0)
            return firstValueColumn + (uint)dataFieldIndex <= lastColumn ? firstValueColumn + (uint)dataFieldIndex : null;

        if (!pivotTable.ColumnFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
        {
            for (var col = firstValueColumn; col <= lastColumn; col++)
            {
                var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
                if (columnDataFieldIndex != dataFieldIndex)
                    continue;
                for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
                {
                    if (IsPivotGrandTotalText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value))
                        return col;
                }
            }
        }

        for (var col = firstValueColumn; col <= lastColumn; col++)
        {
            var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
            if (columnDataFieldIndex != dataFieldIndex)
                continue;

            var matches = true;
            for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
            {
                var field = pivotTable.ColumnFields[level];
                var header = PivotHeader(headers, field.SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var caption = PivotText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value);
                if (pivotTable.DataFields.Count > 1 && level == pivotTable.ColumnFields.Count - 1)
                {
                    var dataFieldName = pivotTable.DataFields[dataFieldIndex].Name;
                    if (caption.EndsWith(dataFieldName, StringComparison.CurrentCultureIgnoreCase))
                        caption = caption[..^dataFieldName.Length].TrimEnd();
                }

                if (!string.Equals(caption, expected, StringComparison.CurrentCultureIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return col;
        }

        return null;
    }

    private static bool PivotSubtotalMatches(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint subtotalRow,
        string subtotalItem,
        int requestedRowFieldCount)
    {
        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var header = PivotHeader(headers, pivotTable.RowFields[index].SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            string? actual = null;
            if (index == 0)
                actual = subtotalItem;
            else if (index < requestedRowFieldCount)
                actual = ReadPivotRowItem(sheet, pivotTable, subtotalRow - 1, firstDataRow, index, pivotTable.RowFields.Count);

            if (!string.Equals(actual, expected, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    private static string? ReadPivotRowItem(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint firstDataRow,
        int fieldIndex,
        int rowFieldCount)
    {
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFieldCount > 1)
            return PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);

        var col = pivotTable.TargetRange.Start.Col + (uint)fieldIndex;
        for (var current = row; current >= firstDataRow; current--)
        {
            var value = sheet.GetCell(current, col)?.Value;
            if (value is not null)
                return PivotText(value);
            if (current == firstDataRow)
                break;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadPivotSourceHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return [];
        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            headers.Add(PivotText(sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value));
        return headers;
    }

    private static string PivotHeader(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : "";

    private static int PivotRowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static GridRange GetPivotMaterializedRange(Sheet sheet, PivotTableModel pivotTable)
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

    private static bool IsPivotGrandTotalText(ScalarValue? value) =>
        value is TextValue text && text.Value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase);

    private static bool TryReadPivotSubtotalCaption(ScalarValue? value, out string item)
    {
        item = "";
        if (value is not TextValue text ||
            !text.Value.EndsWith(" Total", StringComparison.CurrentCultureIgnoreCase) ||
            text.Value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        item = text.Value[..^" Total".Length];
        return item.Length > 0;
    }

    private static string PivotText(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        DirectTextLiteralValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        DateTimeValue date => date.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        ReferencedScalarValue referenced => PivotText(referenced.Value),
        _ => value.ToString() ?? ""
    };

}
