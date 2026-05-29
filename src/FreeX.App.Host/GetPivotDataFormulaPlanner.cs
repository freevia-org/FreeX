using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record GetPivotDataFormulaPlan(string FunctionCall, string Formula);

public static class GetPivotDataFormulaPlanner
{
    public static GetPivotDataFormulaPlan? Create(
        Workbook workbook,
        Sheet formulaSheet,
        Sheet pivotSheet,
        CellAddress selectedCell)
    {
        var pivotTable = pivotSheet.PivotTables.FirstOrDefault(pivot => pivot.TargetRange.Contains(selectedCell));
        if (pivotTable is null)
            return null;

        var headers = ReadPivotSourceHeaders(workbook, pivotTable);
        if (headers.Count == 0 || pivotTable.DataFields.Count == 0)
            return null;

        var materialized = GetPivotMaterializedRange(pivotSheet, pivotTable);
        var headerRows = (uint)Math.Max(1, pivotTable.ColumnFields.Count);
        var firstDataRow = pivotTable.TargetRange.Start.Row + headerRows;
        var rowFieldColumns = PivotRowFieldOutputColumnCount(pivotTable);
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)rowFieldColumns;
        if (selectedCell.Row < firstDataRow ||
            selectedCell.Col < firstValueColumn ||
            selectedCell.Row > materialized.End.Row ||
            selectedCell.Col > materialized.End.Col)
        {
            return null;
        }

        var dataFieldIndex = (int)((selectedCell.Col - firstValueColumn) % (uint)pivotTable.DataFields.Count);
        if (dataFieldIndex < 0 || dataFieldIndex >= pivotTable.DataFields.Count)
            return null;

        var dataField = pivotTable.DataFields[dataFieldIndex];
        var arguments = new List<string>
        {
            QuoteFormulaText(dataField.Name),
            FormatPivotReference(formulaSheet, pivotSheet, pivotTable.TargetRange.Start)
        };

        AddRowFilters(arguments, pivotSheet, pivotTable, headers, selectedCell.Row, firstDataRow);
        AddColumnFilters(arguments, pivotSheet, pivotTable, headers, selectedCell.Col, dataField.Name);
        AddPageFilters(arguments, pivotTable, headers);

        var functionCall = $"GETPIVOTDATA({string.Join(",", arguments)})";
        return new GetPivotDataFormulaPlan(functionCall, "=" + functionCall);
    }

    private static void AddRowFilters(
        List<string> arguments,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        uint selectedRow,
        uint firstDataRow)
    {
        if (pivotTable.RowFields.Count == 0)
            return;

        var firstLabel = PivotText(sheet.GetCell(selectedRow, pivotTable.TargetRange.Start.Col)?.Value);
        if (IsGrandTotal(firstLabel))
            return;

        if (TryReadSubtotalCaption(firstLabel, out var subtotalItem))
        {
            AddFieldItem(arguments, PivotHeader(headers, pivotTable.RowFields[0].SourceFieldIndex), subtotalItem);
            return;
        }

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1)
            return;

        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var header = PivotHeader(headers, pivotTable.RowFields[index].SourceFieldIndex);
            var item = ReadRepeatedRowItem(sheet, pivotTable, selectedRow, firstDataRow, index);
            AddFieldItem(arguments, header, item);
        }
    }

    private static void AddColumnFilters(
        List<string> arguments,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        uint selectedColumn,
        string dataFieldName)
    {
        for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
        {
            var item = PivotText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, selectedColumn)?.Value);
            if (pivotTable.DataFields.Count > 1 && level == pivotTable.ColumnFields.Count - 1)
                item = TrimDataFieldSuffix(item, dataFieldName);

            if (IsGrandTotal(item))
                continue;

            var header = PivotHeader(headers, pivotTable.ColumnFields[level].SourceFieldIndex);
            AddFieldItem(arguments, header, item);
        }
    }

    private static void AddPageFilters(
        List<string> arguments,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        foreach (var pageField in pivotTable.PageFields)
        {
            var item = pageField.SelectedItem;
            if (string.IsNullOrWhiteSpace(item) && pageField.SelectedItems is { Count: 1 })
                item = pageField.SelectedItems[0];

            AddFieldItem(arguments, PivotHeader(headers, pageField.SourceFieldIndex), item);
        }
    }

    private static void AddFieldItem(List<string> arguments, string field, string? item)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(item))
            return;

        arguments.Add(QuoteFormulaText(field));
        arguments.Add(QuoteFormulaText(item));
    }

    private static string? ReadRepeatedRowItem(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint selectedRow,
        uint firstDataRow,
        int rowFieldIndex)
    {
        var col = pivotTable.TargetRange.Start.Col + (uint)rowFieldIndex;
        for (var row = selectedRow; row >= firstDataRow; row--)
        {
            var item = PivotText(sheet.GetCell(row, col)?.Value);
            if (!string.IsNullOrWhiteSpace(item))
                return item;
            if (row == firstDataRow)
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

    private static GridRange GetPivotMaterializedRange(Sheet sheet, PivotTableModel pivotTable)
    {
        uint? maxRow = null;
        uint? maxCol = null;
        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        return maxRow is null || maxCol is null
            ? new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start)
            : new GridRange(pivotTable.TargetRange.Start, new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
    }

    private static string FormatPivotReference(Sheet formulaSheet, Sheet pivotSheet, CellAddress pivotReference)
    {
        var reference = SpreadsheetDisplayFormatter.FormatCellReference(pivotReference, useR1C1ReferenceStyle: false);
        return formulaSheet.Id == pivotSheet.Id
            ? reference
            : $"{PivotUiPlanner.QuoteSheetNameForReference(pivotSheet.Name)}!{reference}";
    }

    private static string QuoteFormulaText(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string PivotHeader(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : "";

    private static int PivotRowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static string PivotText(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        DateTimeValue date => date.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => value.ToString() ?? ""
    };

    private static bool IsGrandTotal(string value) =>
        value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase);

    private static bool TryReadSubtotalCaption(string value, out string item)
    {
        item = "";
        if (!value.EndsWith(" Total", StringComparison.CurrentCultureIgnoreCase) ||
            value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        item = value[..^" Total".Length];
        return item.Length > 0;
    }

    private static string TrimDataFieldSuffix(string item, string dataFieldName) =>
        item.EndsWith(dataFieldName, StringComparison.CurrentCultureIgnoreCase)
            ? item[..^dataFieldName.Length].TrimEnd()
            : item;
}
