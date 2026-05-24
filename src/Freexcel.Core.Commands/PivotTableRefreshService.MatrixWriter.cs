using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void WriteMatrixPivot(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var start = GetPivotBodyStart(pivotTable);
        var rowFields = pivotTable.RowFields.ToList();
        var rowFieldOutputColumns = RowFieldOutputColumnCount(pivotTable);
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

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col), new TextValue("Row Labels"));
        else
        {
            for (var index = 0; index < rowFields.Count; index++)
                sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        }

        var valueStartCol = start.Col + (uint)rowFieldOutputColumns;
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
        PivotKey? previousRowKey = null;
        foreach (var rowGroup in rowGroups)
        {
            if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(string.Join(" ", rowGroup.Key.Values)));
            }
            else
            {
                for (var index = 0; index < rowGroup.Key.Values.Count; index++)
                {
                    var suppressRepeat = ShouldSuppressRepeatedRowLabel(pivotTable, rowGroup.Key, previousRowKey, index);
                    if (!suppressRepeat)
                        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(rowGroup.Key.Values[index]));
                }
            }

            var visibleRowGroupRows = rowGroup
                .Where(row => columnKeys.Any(columnKey => ColumnKeyMatches(row, columnFields, columnKey)))
                .ToList();
            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = rowGroup
                    .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                    .ToList();
                var columnTotalRows = visibleRows
                    .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                    .ToList();
                foreach (var dataField in pivotTable.DataFields)
                {
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        columnRows,
                        new PivotDisplayContext(visibleRows, visibleRowGroupRows, columnTotalRows),
                        dataField,
                        pivotTable,
                        headers),
                        dataField,
                        pivotTable,
                        isEmptyIntersection: columnRows.Count == 0);
                    outputColumn++;
                }
            }
            if (pivotTable.ShowRowGrandTotals)
            {
                foreach (var dataField in pivotTable.DataFields)
                {
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        visibleRowGroupRows,
                        new PivotDisplayContext(visibleRows, visibleRowGroupRows, visibleRows),
                        dataField,
                        pivotTable,
                        headers),
                        dataField);
                    outputColumn++;
                }
            }
            previousRowKey = rowGroup.Key;
            outputRow++;
            if (pivotTable.BlankLineAfterItems &&
                rowFields.Count > 1 &&
                IsEndOfOuterItem(rowGroups, rowGroup, rowFields.Count))
            {
                outputRow++;
            }
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
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        columnRows,
                        new PivotDisplayContext(visibleRows, visibleRows, columnRows),
                        dataField,
                        pivotTable,
                        headers),
                        dataField);
                    outputColumn++;
                }
            }
            if (pivotTable.ShowRowGrandTotals)
            {
                foreach (var dataField in pivotTable.DataFields)
                {
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        visibleRows,
                        new PivotDisplayContext(visibleRows, visibleRows, visibleRows),
                        dataField,
                        pivotTable,
                        headers),
                        dataField);
                    outputColumn++;
                }
            }
        }
    }
}
