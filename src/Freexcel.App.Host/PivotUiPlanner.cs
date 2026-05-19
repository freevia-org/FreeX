using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class PivotUiPlanner
{
    public static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex) =>
        sourceFieldIndex >= 0 && sourceFieldIndex < headers.Count
            ? headers[sourceFieldIndex]
            : $"Column {sourceFieldIndex + 1}";

    public static int? FindSourceFieldIndex(IReadOnlyList<string> headers, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], caption, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return null;
    }

    public static int? FindDataFieldIndex(PivotTableModel pivotTable, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            if (string.Equals(pivotTable.DataFields[index].Name, caption, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return null;
    }

    public static int? FindFieldSourceIndex(IReadOnlyList<string> headers, PivotTableModel pivotTable, string caption)
    {
        var sourceIndex = FindSourceFieldIndex(headers, caption);
        if (sourceIndex is not null)
            return sourceIndex;

        return pivotTable.DataFields
            .FirstOrDefault(field => string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase))
            ?.SourceFieldIndex;
    }

    public static PivotTableModel? FindPivotTableForSelection(Sheet sheet, GridRange? selectedRange)
    {
        if (selectedRange is { } range)
        {
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                pivot.TargetRange.Contains(range.Start) || pivot.TargetRange.Overlaps(range));
            if (pivotTable is not null)
                return pivotTable;
        }

        return sheet.PivotTables.FirstOrDefault();
    }

    public static int ChooseDefaultDataField(Sheet sheet, GridRange sourceRange)
    {
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
            {
                if (sheet.GetValue(row, col) is NumberValue or DateTimeValue)
                    return checked((int)(col - sourceRange.Start.Col));
            }
        }

        return checked((int)Math.Min(1, sourceRange.ColCount - 1));
    }

    public static GridRange DefaultTargetRange(Sheet sheet, GridRange sourceRange)
    {
        var start = new CellAddress(
            sheet.Id,
            sourceRange.Start.Row,
            Math.Min(sourceRange.End.Col + 2, CellAddress.MaxCol));
        var end = new CellAddress(
            sheet.Id,
            Math.Min(start.Row + sourceRange.RowCount + 2, CellAddress.MaxRow),
            Math.Min(start.Col + sourceRange.ColCount + 2, CellAddress.MaxCol));
        return new GridRange(start, end);
    }

    public static string GenerateUniquePivotTableName(Sheet sheet)
    {
        for (var index = sheet.PivotTables.Count + 1; index <= 10000; index++)
        {
            var name = $"PivotTable{index}";
            if (sheet.PivotTables.All(pivot => !string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }

        return $"PivotTable{Guid.NewGuid():N}"[..31];
    }

    public static string? ResolvePivotChartFieldButtonCaption(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string fieldButton)
    {
        if (string.Equals(fieldButton, "Values", StringComparison.OrdinalIgnoreCase))
            return pivotTable.DataFields.FirstOrDefault()?.Name;

        if (string.Equals(fieldButton, "Axis Fields", StringComparison.OrdinalIgnoreCase))
        {
            var field = pivotTable.RowFields.Concat(pivotTable.ColumnFields).FirstOrDefault();
            return field is null ? null : FieldCaption(headers, field.SourceFieldIndex);
        }

        var pageField = pivotTable.PageFields.FirstOrDefault();
        if (pageField is not null)
            return FieldCaption(headers, pageField.SourceFieldIndex);

        var axisField = pivotTable.RowFields.Concat(pivotTable.ColumnFields).FirstOrDefault();
        return axisField is null ? pivotTable.DataFields.FirstOrDefault()?.Name : FieldCaption(headers, axisField.SourceFieldIndex);
    }

    public static PivotFieldModel FindExistingPivotField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceFieldIndex)
        ?? new PivotFieldModel(sourceFieldIndex);

    public static List<PivotFieldModel> SetFieldSelectedItems(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex,
        IReadOnlyList<string>? selectedItems) =>
        fields
            .Select(field => field.SourceFieldIndex == sourceFieldIndex
                ? field with
                {
                    SelectedItem = selectedItems is { Count: 1 } ? selectedItems[0] : null,
                    SelectedItems = selectedItems
                }
                : field)
            .ToList();
}
