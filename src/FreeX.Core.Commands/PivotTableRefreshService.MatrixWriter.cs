using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        var rowGroups = BuildRowGroups(workbook, pivotTable, rows, rowFields);
        rowGroups = ApplyLabelFilters(rowGroups, pivotTable, rowFields);
        rowGroups = ApplyValueFilters(rowGroups, pivotTable, headers, rowFields);
        rowGroups = ApplySorts(rowGroups, pivotTable, headers, rowFields);
        var retainedRows = rowGroups.SelectMany(group => group).ToList();
        var columnKeys = BuildColumnKeys(workbook, pivotTable, retainedRows, columnFields);
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
        PivotKey? currentSubtotalKey = null;
        var subtotalRows = new List<IReadOnlyList<ScalarValue>>();
        var writeCompactBottomSubtotals = pivotTable.ReportLayout == PivotReportLayout.Compact &&
            pivotTable.ShowSubtotals &&
            rowFields.Count > 1 &&
            pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom;
        var writeCompactTopSubtotals = pivotTable.ReportLayout == PivotReportLayout.Compact &&
            pivotTable.ShowSubtotals &&
            rowFields.Count > 1 &&
            pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Top;
        var topSubtotalRows = writeCompactTopSubtotals
            ? rowGroups
                .GroupBy(group => new PivotKey(group.Key.Values.Take(rowFields.Count - 1).ToArray()))
                .ToDictionary(group => group.Key, group => group.SelectMany(item => item).ToList())
            : [];
        foreach (var rowGroup in rowGroups)
        {
            var rowGroupRows = rowGroup.ToList();
            if (writeCompactBottomSubtotals || writeCompactTopSubtotals)
            {
                var subtotalKey = new PivotKey(rowGroup.Key.Values.Take(rowFields.Count - 1).ToArray());
                if (writeCompactBottomSubtotals && currentSubtotalKey is not null && !currentSubtotalKey.Equals(subtotalKey))
                {
                    WriteMatrixSubtotalRow(
                        workbook,
                        sheet,
                        pivotTable,
                        headers,
                        start,
                        valueStartCol,
                        columnKeys,
                        columnFields,
                        visibleRows,
                        currentSubtotalKey,
                        subtotalRows,
                        outputRow);
                    outputRow++;
                    if (pivotTable.BlankLineAfterItems)
                        outputRow++;
                    subtotalRows.Clear();
                }

                if (writeCompactBottomSubtotals)
                {
                    currentSubtotalKey = subtotalKey;
                    subtotalRows.AddRange(rowGroupRows);
                }
                else if (currentSubtotalKey is null || !currentSubtotalKey.Equals(subtotalKey))
                {
                    currentSubtotalKey = subtotalKey;
                    if (topSubtotalRows.TryGetValue(subtotalKey, out var rowsForSubtotal))
                    {
                        WriteMatrixSubtotalRow(
                            workbook,
                            sheet,
                            pivotTable,
                            headers,
                            start,
                            valueStartCol,
                            columnKeys,
                            columnFields,
                            visibleRows,
                            subtotalKey,
                            rowsForSubtotal,
                            outputRow);
                        outputRow++;
                    }
                }
            }

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
                        dataField,
                        pivotTable,
                        isEmptyIntersection: visibleRowGroupRows.Count == 0);
                    outputColumn++;
                }
            }
            previousRowKey = rowGroup.Key;
            outputRow++;
            if (pivotTable.BlankLineAfterItems &&
                !writeCompactBottomSubtotals &&
                rowFields.Count > 1 &&
                IsEndOfOuterItem(rowGroups, rowGroup, rowFields.Count))
            {
                outputRow++;
            }
        }

        if (writeCompactBottomSubtotals && currentSubtotalKey is not null)
        {
            WriteMatrixSubtotalRow(
                workbook,
                sheet,
                pivotTable,
                headers,
                start,
                valueStartCol,
                columnKeys,
                columnFields,
                visibleRows,
                currentSubtotalKey,
                subtotalRows,
                outputRow);
            outputRow++;
            if (pivotTable.BlankLineAfterItems)
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
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        columnRows,
                        new PivotDisplayContext(visibleRows, visibleRows, columnRows),
                        dataField,
                        pivotTable,
                        headers),
                        dataField,
                        pivotTable);
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
                        dataField,
                        pivotTable);
                    outputColumn++;
                }
            }
        }
    }

    private static void WriteMatrixSubtotalRow(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        uint valueStartCol,
        IReadOnlyList<PivotKey> columnKeys,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<IReadOnlyList<ScalarValue>> visibleRows,
        PivotKey subtotalKey,
        IReadOnlyList<IReadOnlyList<ScalarValue>> subtotalRows,
        uint outputRow)
    {
        var captionItem = subtotalKey.Values.Count == 0
            ? ""
            : subtotalKey.Values[^1];
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{captionItem} Total"));

        var visibleSubtotalRows = subtotalRows
            .Where(row => columnKeys.Any(columnKey => ColumnKeyMatches(row, columnFields, columnKey)))
            .ToList();
        var outputColumn = valueStartCol;
        foreach (var columnKey in columnKeys)
        {
            var subtotalColumnRows = subtotalRows
                .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                .ToList();
            var columnTotalRows = visibleRows
                .Where(row => ColumnKeyMatches(row, columnFields, columnKey))
                .ToList();
            foreach (var dataField in pivotTable.DataFields)
            {
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, outputColumn),
                    DisplayAggregate(
                        subtotalColumnRows,
                        new PivotDisplayContext(visibleRows, visibleSubtotalRows, columnTotalRows),
                        dataField,
                        pivotTable,
                        headers),
                    dataField,
                    pivotTable,
                    isEmptyIntersection: subtotalColumnRows.Count == 0);
                outputColumn++;
            }
        }

        if (pivotTable.ShowRowGrandTotals)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, outputColumn),
                    DisplayAggregate(
                        visibleSubtotalRows,
                        new PivotDisplayContext(visibleRows, visibleSubtotalRows, visibleRows),
                        dataField,
                        pivotTable,
                        headers),
                    dataField,
                    pivotTable,
                    isEmptyIntersection: visibleSubtotalRows.Count == 0);
                outputColumn++;
            }
        }
    }
}
