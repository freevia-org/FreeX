using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class PivotTableRefreshService
{
    public static void Refresh(Workbook workbook, Sheet targetSheet, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || pivotTable.RowFields.Count == 0 || pivotTable.DataFields.Count == 0)
            return;

        ClearTargetRange(targetSheet, pivotTable.TargetRange);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var rowField = pivotTable.RowFields[0].SourceFieldIndex;
        var columnField = pivotTable.ColumnFields.FirstOrDefault()?.SourceFieldIndex;
        var dataField = pivotTable.DataFields[0];
        if (!IsValidField(rowField, headers.Count) ||
            (columnField.HasValue && !IsValidField(columnField.Value, headers.Count)) ||
            !IsValidField(dataField.SourceFieldIndex, headers.Count))
        {
            return;
        }

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count).ToList();
        if (columnField.HasValue)
            WriteMatrixPivot(targetSheet, pivotTable, headers[rowField], rows, rowField, columnField.Value, dataField.SourceFieldIndex);
        else
            WriteRowPivot(targetSheet, pivotTable, headers[rowField], dataField.Name, rows, rowField, dataField.SourceFieldIndex);
    }

    private static void WriteRowPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        string rowHeader,
        string dataHeader,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        int rowField,
        int dataField)
    {
        var start = pivotTable.TargetRange.Start;
        sheet.SetCell(start, new TextValue(rowHeader));
        sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + 1), new TextValue(dataHeader));

        var groups = rows
            .GroupBy(row => KeyText(row[rowField]))
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var outputRow = start.Row + 1;
        foreach (var group in groups)
        {
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(group.Key));
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + 1), new NumberValue(group.Sum(row => Number(row[dataField]))));
            outputRow++;
        }

        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + 1), new NumberValue(rows.Sum(row => Number(row[dataField]))));
    }

    private static void WriteMatrixPivot(
        Sheet sheet,
        PivotTableModel pivotTable,
        string rowHeader,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        int rowField,
        int columnField,
        int dataField)
    {
        var start = pivotTable.TargetRange.Start;
        var rowKeys = rows.Select(row => KeyText(row[rowField])).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var columnKeys = rows.Select(row => KeyText(row[columnField])).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

        sheet.SetCell(start, new TextValue(rowHeader));
        for (var index = 0; index < columnKeys.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index + 1), new TextValue(columnKeys[index]));
        sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)columnKeys.Count + 1), new TextValue("Grand Total"));

        var outputRow = start.Row + 1;
        foreach (var rowKey in rowKeys)
        {
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(rowKey));
            double rowTotal = 0;
            for (var index = 0; index < columnKeys.Count; index++)
            {
                var columnKey = columnKeys[index];
                var value = rows
                    .Where(row => string.Equals(KeyText(row[rowField]), rowKey, StringComparison.CurrentCultureIgnoreCase) &&
                                  string.Equals(KeyText(row[columnField]), columnKey, StringComparison.CurrentCultureIgnoreCase))
                    .Sum(row => Number(row[dataField]));
                rowTotal += value;
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index + 1), new NumberValue(value));
            }

            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)columnKeys.Count + 1), new NumberValue(rowTotal));
            outputRow++;
        }

        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
        for (var index = 0; index < columnKeys.Count; index++)
        {
            var columnKey = columnKeys[index];
            var value = rows
                .Where(row => string.Equals(KeyText(row[columnField]), columnKey, StringComparison.CurrentCultureIgnoreCase))
                .Sum(row => Number(row[dataField]));
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index + 1), new NumberValue(value));
        }
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)columnKeys.Count + 1), new NumberValue(rows.Sum(row => Number(row[dataField]))));
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
}
