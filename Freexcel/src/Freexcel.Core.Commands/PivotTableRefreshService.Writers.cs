using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void WriteRowPivot(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = GetPivotBodyStart(pivotTable);
        var rowFields = pivotTable.RowFields.ToList();
        var rowFieldOutputColumns = RowFieldOutputColumnCount(pivotTable);
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col), new TextValue("Row Labels"));
        else
        {
            for (var index = 0; index < rowFields.Count; index++)
                sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        }
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)rowFieldOutputColumns + (uint)index), new TextValue(pivotTable.DataFields[index].Name));

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
                        WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKey, subtotalRows, retainedRows, outputRow);
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
                        WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, subtotalKey, rowsForSubtotal, retainedRows, outputRow);
                        outputRow++;
                    }
                }

                if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
                    subtotalRows.AddRange(group);
            }

            if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(string.Join(" ", group.Key.Values)));
            }
            else
            {
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
            }
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldOutputColumns + (uint)index),
                    DisplayAggregate(
                        group,
                        new PivotDisplayContext(retainedRows, group.ToList(), retainedRows),
                        pivotTable.DataFields[index],
                        pivotTable,
                        headers),
                    pivotTable.DataFields[index]);
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
                    SetPivotValueCell(
                        workbook,
                        sheet,
                        new CellAddress(sheet.Id, outputRow, start.Col + 1 + (uint)index),
                        calculatedValue,
                        pivotTable.DataFields[index]);
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
            WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKey, subtotalRows, retainedRows, outputRow);
            outputRow++;
        }

        if (pivotTable.ShowColumnGrandTotals)
        {
            sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue("Grand Total"));
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldOutputColumns + (uint)index),
                    DisplayAggregate(
                        retainedRows,
                        new PivotDisplayContext(retainedRows, retainedRows, retainedRows),
                        pivotTable.DataFields[index],
                        pivotTable,
                        headers) + calculatedItemTotals[index],
                    pivotTable.DataFields[index]);
        }
    }

    private static void WriteValuesOnlyPivot(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = GetPivotBodyStart(pivotTable);
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(pivotTable.DataFields[index].Name));
            SetPivotValueCell(
                workbook,
                sheet,
                new CellAddress(sheet.Id, start.Row + 1, start.Col + (uint)index),
                DisplayAggregate(
                    rows,
                    new PivotDisplayContext(rows, rows, rows),
                    pivotTable.DataFields[index],
                    pivotTable,
                    headers),
                pivotTable.DataFields[index]);
        }
    }

    private static void WriteColumnOnlyPivot(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var start = GetPivotBodyStart(pivotTable);
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
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        int rowFieldCount,
        PivotKey subtotalKey,
        IReadOnlyList<IReadOnlyList<ScalarValue>> subtotalRows,
        IReadOnlyList<IReadOnlyList<ScalarValue>> grandTotalRows,
        uint outputRow)
    {
        sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{subtotalKey.Values[0]} Total"));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            SetPivotValueCell(
                workbook,
                sheet,
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldCount + (uint)index),
                DisplayAggregate(
                    subtotalRows,
                    new PivotDisplayContext(grandTotalRows, subtotalRows, grandTotalRows),
                    pivotTable.DataFields[index],
                    pivotTable,
                    headers),
                pivotTable.DataFields[index]);
    }

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
        foreach (var rowGroup in rowGroups)
        {
            if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            {
                sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(string.Join(" ", rowGroup.Key.Values)));
            }
            else
            {
                for (var index = 0; index < rowGroup.Key.Values.Count; index++)
                    sheet.SetCell(new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(rowGroup.Key.Values[index]));
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
    private static void WritePageFields(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var pageFields = pivotTable.PageFields.ToList();
        if (pageFields.Count == 0)
            return;

        var start = pivotTable.TargetRange.Start;
        var wrap = Math.Max(0, pivotTable.PageWrap);
        for (var index = 0; index < pageFields.Count; index++)
        {
            var (rowOffset, colPairOffset) = GetPageFieldOffset(index, pageFields.Count, wrap, pivotTable.PageOverThenDown);
            var field = pageFields[index];
            sheet.SetCell(
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset),
                new TextValue(headers[field.SourceFieldIndex]));
            sheet.SetCell(
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset + 1),
                new TextValue(GetPageFieldSelectionText(field)));
        }
    }

    private static (uint RowOffset, uint ColPairOffset) GetPageFieldOffset(
        int index,
        int pageFieldCount,
        int wrap,
        bool overThenDown)
    {
        if (overThenDown)
        {
            var fieldsPerRow = wrap <= 0 ? pageFieldCount : wrap;
            return ((uint)(index / fieldsPerRow), (uint)((index % fieldsPerRow) * 2));
        }

        var rowsPerColumn = wrap <= 0 ? pageFieldCount : wrap;
        return ((uint)(index % rowsPerColumn), (uint)((index / rowsPerColumn) * 2));
    }

    private static string GetPageFieldSelectionText(PivotFieldModel field)
    {
        if (field.SelectedItems is { Count: > 0 })
            return field.SelectedItems.Count == 1 ? field.SelectedItems[0] : "(Multiple Items)";

        return string.IsNullOrWhiteSpace(field.SelectedItem) ? "(All)" : field.SelectedItem;
    }

    private static void ApplyMergedRowLabels(Sheet sheet, PivotTableModel pivotTable)
    {
        if (!pivotTable.MergeAndCenterLabels ||
            pivotTable.ReportLayout == PivotReportLayout.Compact ||
            pivotTable.RowFields.Count <= 1)
        {
            return;
        }

        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var bodyStart = GetPivotBodyStart(pivotTable);
        var rowLabelColumnCount = RowFieldOutputColumnCount(pivotTable);
        if (rowLabelColumnCount <= 1 || materialized.End.Row <= bodyStart.Row + 1)
            return;

        for (var colOffset = 0; colOffset < rowLabelColumnCount - 1; colOffset++)
            MergeRepeatedLabelsInColumn(
                sheet,
                materialized,
                bodyStart.Row + 1,
                bodyStart.Col + (uint)colOffset,
                bodyStart.Col + (uint)rowLabelColumnCount - 1);
    }

    private static void MergeRepeatedLabelsInColumn(
        Sheet sheet,
        GridRange materialized,
        uint firstBodyRow,
        uint labelCol,
        uint lastRowLabelCol)
    {
        uint? spanStart = null;
        string? spanText = null;
        for (var row = firstBodyRow; row <= materialized.End.Row + 1; row++)
        {
            var text = row <= materialized.End.Row ? GetMergeableLabelText(sheet, row, labelCol) : null;
            var suppressedContinuation = text is null &&
                spanStart is not null &&
                row <= materialized.End.Row &&
                HasInnerRowLabelValue(sheet, row, labelCol, lastRowLabelCol);
            if (spanStart is not null &&
                !suppressedContinuation &&
                (!string.Equals(text, spanText, StringComparison.Ordinal) || text is null))
            {
                MergeLabelSpan(sheet, spanStart.Value, row - 1, labelCol);
                spanStart = null;
                spanText = null;
            }

            if (text is not null && spanStart is null)
            {
                spanStart = row;
                spanText = text;
            }
        }
    }

    private static string? GetMergeableLabelText(Sheet sheet, uint row, uint col)
    {
        if (sheet.GetCell(row, col)?.Value is not TextValue text ||
            string.IsNullOrWhiteSpace(text.Value) ||
            IsPivotGrandTotalCaption(text.Value) ||
            IsPivotSubtotalCaption(text.Value))
        {
            return null;
        }

        return text.Value;
    }

    private static bool HasInnerRowLabelValue(Sheet sheet, uint row, uint labelCol, uint lastRowLabelCol)
    {
        for (var col = labelCol + 1; col <= lastRowLabelCol; col++)
        {
            if (GetMergeableLabelText(sheet, row, col) is not null)
                return true;
        }

        return false;
    }

    private static void MergeLabelSpan(Sheet sheet, uint startRow, uint endRow, uint col)
    {
        if (endRow <= startRow)
            return;

        var region = new GridRange(
            new CellAddress(sheet.Id, startRow, col),
            new CellAddress(sheet.Id, endRow, col));
        sheet.AddMergedRegion(region);

        for (var row = startRow + 1; row <= endRow; row++)
            sheet.ClearCell(row, col);
    }
}
