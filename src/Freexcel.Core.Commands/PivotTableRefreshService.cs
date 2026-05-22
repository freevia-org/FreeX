using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
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
            .Where(row => MatchesFieldSelections(row, pivotTable.PageFields))
            .Where(row => MatchesFieldSelections(row, pivotTable.RowFields))
            .Where(row => MatchesFieldSelections(row, columnFields))
            .ToList();
        if (pivotTable.RowFields.Count == 0 && columnFields.Count == 0)
            WriteValuesOnlyPivot(workbook, targetSheet, pivotTable, headers, rows);
        else if (pivotTable.RowFields.Count == 0)
            WriteColumnOnlyPivot(workbook, targetSheet, pivotTable, headers, rows, columnFields);
        else if (columnFields.Count > 0)
            WriteMatrixPivot(workbook, targetSheet, pivotTable, headers, rows, columnFields);
        else
            WriteRowPivot(workbook, targetSheet, pivotTable, headers, rows);

        ApplyPivotTableStyle(workbook, targetSheet, pivotTable);
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


    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static void WriteRowPivot(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
    {
        var start = pivotTable.TargetRange.Start;
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
        var start = pivotTable.TargetRange.Start;
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
        var start = pivotTable.TargetRange.Start;
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
}
