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
        var rowsByColumnKey = BuildColumnRowsByKey(retainedRows, columnFields);
        var columnKeys = BuildColumnKeys(workbook, pivotTable, retainedRows, columnFields, rowsByColumnKey);
        columnKeys = ApplyLabelFilters(columnKeys, pivotTable, columnFields);
        var columnAggregateCache = CreateColumnAggregateCacheIfNeeded(rowsByColumnKey, pivotTable, headers, columnFields);
        columnKeys = ApplyValueFilters(columnKeys, rowsByColumnKey, pivotTable, headers, columnFields, columnAggregateCache);
        columnKeys = ApplySorts(columnKeys, rowsByColumnKey, pivotTable, headers, columnFields, columnAggregateCache);
        var visibleRows = RowsForColumnKeys(rowsByColumnKey, columnKeys, retainedRows);
        var visibleRowsByColumnKey = BuildColumnRowsByKey(visibleRows, columnFields);
        var singleDataField = pivotTable.DataFields.Count == 1;

        // Build prefix-row lookup maps for the row hierarchy (for % of Parent Row Total).
        // prefixRowsByLevel[k] maps (row-prefix of length k+1) → source rows.
        var rowSubtotalLevelCount = rowFields.Count - 1;
        var rowPrefixRowsByLevel = new Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[rowSubtotalLevelCount];
        for (var level = 0; level < rowSubtotalLevelCount; level++)
        {
            var prefixLen = level + 1;
            rowPrefixRowsByLevel[level] = rowGroups
                .GroupBy(group => new PivotKey(group.Key.Values.Take(prefixLen).ToArray()))
                .ToDictionary(group => group.Key, group => group.SelectMany(item => item).ToList());
        }

        // Build column-prefix lookup: for % of Parent Column Total with nested column fields.
        // colPrefixRowsByLevel[k] maps (column-prefix of length k+1) → source rows (over all retained rows).
        var colSubtotalLevelCount = columnFields.Count - 1;
        var colPrefixRowsByLevelAll = new Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[colSubtotalLevelCount];
        for (var level = 0; level < colSubtotalLevelCount; level++)
        {
            var prefixLen = level + 1;
            colPrefixRowsByLevelAll[level] = rowGroups
                .SelectMany(g => g)
                .GroupBy(row => new PivotKey(columnFields.Take(prefixLen).Select(f => GroupKeyText(row[f.SourceFieldIndex], f)).ToArray()))
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col), new TextValue("Row Labels"));
        else
        {
            for (var index = 0; index < rowFields.Count; index++)
                SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
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
                SetPivotCell(
                    sheet,
                    new CellAddress(sheet.Id, start.Row, outputColumn),
                    new TextValue(GrandTotalCaption(pivotTable, dataField, singleDataField)));
                outputColumn++;
            }
        }

        var outputRow = start.Row + (uint)columnFields.Count;
        PivotKey? previousRowKey = null;
        var writeSubtotals = pivotTable.ShowSubtotals && rowFields.Count > 1;
        var writeBottomSubtotals = writeSubtotals && pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom;
        var writeTopSubtotals = writeSubtotals && pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Top;

        // subtotalLevelCount = rowFields.Count - 1; level 0 = outermost, level N-2 = innermost subtotaled
        var subtotalLevelCount = rowSubtotalLevelCount;

        // Build top subtotal row lookups for all levels (reuse precomputed rowPrefixRowsByLevel)
        var topSubtotalRowsByLevel = new Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[subtotalLevelCount];
        if (writeTopSubtotals)
        {
            for (var level = 0; level < subtotalLevelCount; level++)
                topSubtotalRowsByLevel[level] = rowPrefixRowsByLevel[level];
        }
        else
        {
            for (var level = 0; level < subtotalLevelCount; level++)
                topSubtotalRowsByLevel[level] = [];
        }

        // Per-level state for bottom subtotals
        var currentSubtotalKeys = new PivotKey?[subtotalLevelCount];
        var subtotalRowSets = new List<IReadOnlyList<ScalarValue>>[subtotalLevelCount];
        for (var level = 0; level < subtotalLevelCount; level++)
            subtotalRowSets[level] = [];

        foreach (var rowGroup in rowGroups)
        {
            var rowGroupRows = rowGroup.ToList();
            if (writeSubtotals)
            {
                if (writeBottomSubtotals)
                {
                    // Find the outermost level that changed
                    var breakLevel = subtotalLevelCount; // sentinel: no break
                    for (var level = 0; level < subtotalLevelCount; level++)
                    {
                        var prefixLen = level + 1;
                        var newKey = new PivotKey(rowGroup.Key.Values.Take(prefixLen).ToArray());
                        if (currentSubtotalKeys[level] is not null && !currentSubtotalKeys[level]!.Equals(newKey))
                        {
                            breakLevel = level;
                            break;
                        }
                    }

                    if (breakLevel < subtotalLevelCount)
                    {
                        // Flush subtotals innermost first, then blank line if needed (on outermost break)
                        for (var level = subtotalLevelCount - 1; level >= breakLevel; level--)
                        {
                            if (currentSubtotalKeys[level] is not null)
                            {
                                var subtotalParentRows = ComputeParentPrefixRows(currentSubtotalKeys[level]!, rowPrefixRowsByLevel, retainedRows);
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
                                    visibleRowsByColumnKey,
                                    colPrefixRowsByLevelAll,
                                    currentSubtotalKeys[level]!,
                                    subtotalRowSets[level],
                                    subtotalParentRows,
                                    outputRow);
                                outputRow++;
                            }
                        }
                        // Blank line after the outermost (level 0) subtotal group flush
                        if (pivotTable.BlankLineAfterItems && breakLevel == 0)
                            outputRow++;
                        // Reset broken levels
                        for (var level = breakLevel; level < subtotalLevelCount; level++)
                        {
                            currentSubtotalKeys[level] = null;
                            subtotalRowSets[level] = [];
                        }
                    }

                    // Initialize/accumulate into all levels
                    for (var level = 0; level < subtotalLevelCount; level++)
                    {
                        var prefixLen = level + 1;
                        currentSubtotalKeys[level] ??= new PivotKey(rowGroup.Key.Values.Take(prefixLen).ToArray());
                        subtotalRowSets[level].AddRange(rowGroupRows);
                    }
                }
                else // Top placement
                {
                    for (var level = 0; level < subtotalLevelCount; level++)
                    {
                        var prefixLen = level + 1;
                        var newKey = new PivotKey(rowGroup.Key.Values.Take(prefixLen).ToArray());
                        if (currentSubtotalKeys[level] is null || !currentSubtotalKeys[level]!.Equals(newKey))
                        {
                            currentSubtotalKeys[level] = newKey;
                            if (topSubtotalRowsByLevel[level].TryGetValue(newKey, out var rowsForSubtotal))
                            {
                                var subtotalParentRows = ComputeParentPrefixRows(newKey, rowPrefixRowsByLevel, retainedRows);
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
                                    visibleRowsByColumnKey,
                                    colPrefixRowsByLevelAll,
                                    newKey,
                                    rowsForSubtotal,
                                    subtotalParentRows,
                                    outputRow);
                                outputRow++;
                            }
                        }
                    }
                }
            }

            if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1)
            {
                SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(string.Join(" ", rowGroup.Key.Values)));
            }
            else
            {
                for (var index = 0; index < rowGroup.Key.Values.Count; index++)
                {
                    var suppressRepeat = ShouldSuppressRepeatedRowLabel(pivotTable, rowGroup.Key, previousRowKey, index);
                    if (!suppressRepeat)
                        SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(rowGroup.Key.Values[index]));
                }
            }

            var rowGroupRowsByColumnKey = BuildColumnRowsByKey(rowGroupRows, columnFields);
            var visibleRowGroupRows = RowsForColumnKeys(rowGroupRowsByColumnKey, columnKeys, rowGroupRows);

            // Parent row rows: rows matching parent row prefix, unrestricted by column (will be
            // further restricted to each column key inside the loop below).
            var parentRowPrefixRows = ComputeParentPrefixRows(rowGroup.Key, rowPrefixRowsByLevel, retainedRows);
            var parentRowPrefixRowsByColumnKey = BuildColumnRowsByKey(parentRowPrefixRows, columnFields);

            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = RowsForColumnKey(rowGroupRowsByColumnKey, columnKey);
                var columnTotalRows = RowsForColumnKey(visibleRowsByColumnKey, columnKey);

                // Parent row denominator: parent prefix rows restricted to same column key.
                var parentRowRows = RowsForColumnKey(parentRowPrefixRowsByColumnKey, columnKey);

                // Parent column denominator: rows in this row group matching parent column prefix.
                var parentColRows = ComputeParentColumnRows(columnKey, colPrefixRowsByLevelAll, visibleRowGroupRows, columnFields, rowGroupRows);

                foreach (var dataField in pivotTable.DataFields)
                {
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        columnRows,
                        new PivotDisplayContext(visibleRows, visibleRowGroupRows, columnTotalRows,
                            ParentRowRows: parentRowRows,
                            ParentColumnRows: parentColRows),
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
                !writeBottomSubtotals &&
                rowFields.Count > 1 &&
                IsEndOfOuterItem(rowGroups, rowGroup, rowFields.Count))
            {
                outputRow++;
            }
        }

        // Flush remaining bottom subtotals after the last group (innermost first)
        if (writeBottomSubtotals)
        {
            for (var level = subtotalLevelCount - 1; level >= 0; level--)
            {
                if (currentSubtotalKeys[level] is not null)
                {
                    var subtotalParentRows = ComputeParentPrefixRows(currentSubtotalKeys[level]!, rowPrefixRowsByLevel, retainedRows);
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
                        visibleRowsByColumnKey,
                        colPrefixRowsByLevelAll,
                        currentSubtotalKeys[level]!,
                        subtotalRowSets[level],
                        subtotalParentRows,
                        outputRow);
                    outputRow++;
                }
            }
            if (pivotTable.BlankLineAfterItems)
                outputRow++;
        }

        if (pivotTable.ShowColumnGrandTotals)
        {
            SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(GrandTotalCaption(pivotTable)));
            outputColumn = valueStartCol;
            foreach (var columnKey in columnKeys)
            {
                var columnRows = RowsForColumnKey(rowsByColumnKey, columnKey);
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
        PivotColumnRowMap visibleRowsByColumnKey,
        Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[] colPrefixRowsByLevelAll,
        PivotKey subtotalKey,
        IReadOnlyList<IReadOnlyList<ScalarValue>> subtotalRows,
        IEnumerable<IReadOnlyList<ScalarValue>>? parentRowRows,
        uint outputRow)
    {
        var captionItem = subtotalKey.Values.Count == 0
            ? ""
            : subtotalKey.Values[^1];
        SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{captionItem} Total"));

        var subtotalRowsByColumnKey = BuildColumnRowsByKey(subtotalRows, columnFields);
        var visibleSubtotalRows = RowsForColumnKeys(subtotalRowsByColumnKey, columnKeys, subtotalRows);

        // Parent row rows restricted per column key (for % of Parent Row Total)
        var parentRowPrefixRowsByColumnKey = parentRowRows is not null
            ? BuildColumnRowsByKey(parentRowRows, columnFields)
            : null;

        var outputColumn = valueStartCol;
        foreach (var columnKey in columnKeys)
        {
            var subtotalColumnRows = RowsForColumnKey(subtotalRowsByColumnKey, columnKey);
            var columnTotalRows = RowsForColumnKey(visibleRowsByColumnKey, columnKey);

            // Parent row denominator restricted to this column key
            IReadOnlyList<IReadOnlyList<ScalarValue>>? parentRowColRows = parentRowPrefixRowsByColumnKey is not null
                ? RowsForColumnKey(parentRowPrefixRowsByColumnKey, columnKey)
                : null;

            // Parent column denominator: subtotal rows restricted to parent column prefix
            var parentColRows = ComputeParentColumnRows(columnKey, colPrefixRowsByLevelAll, visibleSubtotalRows, columnFields, subtotalRows);

            foreach (var dataField in pivotTable.DataFields)
            {
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, outputColumn),
                    DisplayAggregate(
                        subtotalColumnRows,
                        new PivotDisplayContext(visibleRows, visibleSubtotalRows, columnTotalRows,
                            ParentRowRows: parentRowColRows,
                            ParentColumnRows: parentColRows),
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
            // Row grand total of the subtotal row: parent is grand total (default fallback)
            foreach (var dataField in pivotTable.DataFields)
            {
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, outputColumn),
                    DisplayAggregate(
                        visibleSubtotalRows,
                        new PivotDisplayContext(visibleRows, visibleSubtotalRows, visibleRows,
                            ParentRowRows: parentRowRows),
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
