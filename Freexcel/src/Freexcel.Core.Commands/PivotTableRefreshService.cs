using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
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
            .Where(row => MatchesFieldSelections(row, pivotTable.PageFields))
            .Where(row => MatchesFieldSelections(row, rowFields))
            .Where(row => MatchesFieldSelections(row, columnFields))
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

    private static void ApplyPivotTableStyle(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var palette = PivotStylePaletteResolver.Resolve(pivotTable.StyleName);
        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = palette.HeaderFont,
            FillColor = palette.HeaderFill,
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var subtotalStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = palette.SubtotalFill,
            BorderTop = new CellBorder(BorderStyle.Thin, palette.Border),
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var grandTotalStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = palette.GrandTotalFill,
            FontColor = palette.GrandTotalFont,
            BorderTop = new CellBorder(BorderStyle.Thin, palette.Border),
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var stripeStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = palette.StripeFill
        });

        var headerEndRow = pivotTable.TargetRange.Start.Row + (uint)Math.Max(1, pivotTable.ColumnFields.Count) - 1;
        var subtotalRows = new HashSet<uint>();
        var grandTotalRows = new HashSet<uint>();
        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            if (sheet.GetCell(row, col)?.Value is not TextValue text)
                continue;
            if (IsPivotGrandTotalCaption(text.Value))
                grandTotalRows.Add(row);
            else if (IsPivotSubtotalCaption(text.Value))
                subtotalRows.Add(row);
        }

        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            var cell = sheet.GetCell(row, col);
            if (cell is null)
                continue;

            if (row <= headerEndRow)
            {
                if (ShouldApplyPivotHeaderStyle(pivotTable, col))
                    ApplyPivotVisualStyle(workbook, cell, headerStyle);
                continue;
            }

            if (grandTotalRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, grandTotalStyle);
                continue;
            }

            if (subtotalRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, subtotalStyle);
                continue;
            }

            var bodyRowIndex = row - headerEndRow - 1;
            var bodyColIndex = col - materialized.Start.Col;
            if (pivotTable.ShowRowStripes && bodyRowIndex % 2 == 0)
                ApplyPivotVisualStyle(workbook, cell, stripeStyle);
            if (pivotTable.ShowColumnStripes && bodyColIndex % 2 == 1)
                ApplyPivotVisualStyle(workbook, cell, stripeStyle);
        }
    }

    private static void ApplyPivotVisualStyle(Workbook workbook, Cell cell, StyleId visualStyleId)
    {
        var numberFormat = workbook.GetStyle(cell.StyleId).NumberFormat;
        if (numberFormat == CellStyle.Default.NumberFormat)
        {
            cell.StyleId = visualStyleId;
            return;
        }

        var style = workbook.GetStyle(visualStyleId);
        style.NumberFormat = numberFormat;
        cell.StyleId = workbook.RegisterStyle(style);
    }

    private static bool ShouldApplyPivotHeaderStyle(PivotTableModel pivotTable, uint col)
    {
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        return col < firstValueColumn
            ? pivotTable.ShowRowHeaders
            : pivotTable.ShowColumnHeaders;
    }

    private static bool IsPivotGrandTotalCaption(string value) =>
        string.Equals(value, "Grand Total", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Grand Total ", StringComparison.OrdinalIgnoreCase);

    private static bool IsPivotSubtotalCaption(string value) =>
        value.EndsWith(" Total", StringComparison.OrdinalIgnoreCase);

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

    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

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

    private static void SetPivotValueCell(
        Workbook workbook,
        Sheet sheet,
        CellAddress address,
        double value,
        PivotDataFieldModel dataField,
        PivotTableModel? pivotTable = null,
        bool isEmptyIntersection = false)
    {
        if (isEmptyIntersection && !string.IsNullOrWhiteSpace(pivotTable?.EmptyValueText))
        {
            sheet.SetCell(address, new TextValue(pivotTable.EmptyValueText));
            return;
        }

        var cell = Cell.FromValue(new NumberValue(value));
        if (TryResolveNumberFormat(workbook, dataField, out var formatCode) &&
            formatCode != CellStyle.Default.NumberFormat)
        {
            var style = CellStyle.Default.Clone();
            style.NumberFormat = formatCode;
            cell.StyleId = workbook.RegisterStyle(style);
        }

        sheet.SetCell(address, cell);
    }

    private static bool TryResolveNumberFormat(Workbook workbook, PivotDataFieldModel dataField, out string formatCode)
    {
        if (!string.IsNullOrWhiteSpace(dataField.NumberFormatCode))
        {
            formatCode = dataField.NumberFormatCode;
            return true;
        }

        if (dataField.NumberFormatId is >= 164 and var numberFormatId &&
            workbook.NumberFormatCatalog.TryGetValue(numberFormatId, out var catalogFormatCode) &&
            !string.IsNullOrWhiteSpace(catalogFormatCode))
        {
            formatCode = catalogFormatCode;
            return true;
        }

        return TryResolveBuiltInNumberFormat(dataField.NumberFormatId, out formatCode);
    }

    private static bool TryResolveBuiltInNumberFormat(int? numberFormatId, out string formatCode)
    {
        formatCode = numberFormatId switch
        {
            null or 0 => "General",
            1 => "0",
            2 => "0.00",
            3 => "#,##0",
            4 => "#,##0.00",
            5 => "$#,##0;($#,##0)",
            6 => "$#,##0;[Red]($#,##0)",
            7 => "$#,##0.00;($#,##0.00)",
            8 => "$#,##0.00;[Red]($#,##0.00)",
            9 => "0%",
            10 => "0.00%",
            11 => "0.00E+00",
            12 => "# ?/?",
            13 => "# ??/??",
            14 => "m/d/yyyy",
            15 => "d-mmm-yy",
            16 => "d-mmm",
            17 => "mmm-yy",
            18 => "h:mm AM/PM",
            19 => "h:mm:ss AM/PM",
            20 => "h:mm",
            21 => "h:mm:ss",
            22 => "m/d/yyyy h:mm",
            37 => "#,##0;(#,##0)",
            38 => "#,##0;[Red](#,##0)",
            39 => "#,##0.00;(#,##0.00)",
            40 => "#,##0.00;[Red](#,##0.00)",
            41 => "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)",
            42 => "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)",
            43 => "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
            44 => "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)",
            45 => "mm:ss",
            46 => "[h]:mm:ss",
            47 => "mm:ss.0",
            48 => "##0.0E+0",
            49 => "@",
            _ => ""
        };
        return formatCode.Length > 0;
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
}
