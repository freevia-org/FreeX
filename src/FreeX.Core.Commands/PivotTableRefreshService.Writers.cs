using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col), new TextValue("Row Labels"));
        else
        {
            for (var index = 0; index < rowFields.Count; index++)
                SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        }
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col + (uint)rowFieldOutputColumns + (uint)index), new TextValue(pivotTable.DataFields[index].Name));

        var groups = BuildRowGroups(workbook, pivotTable, rows, rowFields);
        groups = ApplyLabelFilters(groups, pivotTable, rowFields);
        groups = ApplyValueFilters(groups, pivotTable, headers, rowFields);
        groups = ApplySorts(groups, pivotTable, headers, rowFields);
        var retainedRows = groups.SelectMany(group => group).ToList();

        // subtotalLevelCount = rowFields.Count - 1; level 0 = outermost (R0), level N-2 = innermost subtotaled (second-to-last field)
        var subtotalLevelCount = rowFields.Count - 1;

        // Build prefix-row lookup for ALL levels (level k → prefix key of length k+1 → rows).
        // Used for both subtotal placement and parent-row denominator computation.
        var prefixRowsByLevel = new Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[subtotalLevelCount];
        for (var level = 0; level < subtotalLevelCount; level++)
        {
            var prefixLen = level + 1;
            prefixRowsByLevel[level] = groups
                .GroupBy(group => new PivotKey(group.Key.Values.Take(prefixLen).ToArray()))
                .ToDictionary(group => group.Key, group => group.SelectMany(item => item).ToList());
        }

        // Build top subtotal row lookups for all levels (level k → prefix key of length k+1)
        var topSubtotalRowsByLevel = new Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[subtotalLevelCount];
        if (pivotTable.ShowSubtotals && rowFields.Count > 1 && pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Top)
        {
            for (var level = 0; level < subtotalLevelCount; level++)
                topSubtotalRowsByLevel[level] = prefixRowsByLevel[level];
        }
        else
        {
            for (var level = 0; level < subtotalLevelCount; level++)
                topSubtotalRowsByLevel[level] = [];
        }

        var outputRow = start.Row + 1;
        // Per-level state for bottom subtotals: current prefix key and accumulated rows
        var currentSubtotalKeys = new PivotKey?[subtotalLevelCount];
        var subtotalRowSets = new List<IReadOnlyList<ScalarValue>>[subtotalLevelCount];
        for (var level = 0; level < subtotalLevelCount; level++)
            subtotalRowSets[level] = [];
        PivotKey? previousRowKey = null;
        var calculatedItemTotals = new double[pivotTable.DataFields.Count];

        // For compact multi-row-field layout: track per-row indent levels so the
        // post-processing style pass can apply per-level indentation.
        var isCompactMultiRow = pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 1;
        var indentStep = isCompactMultiRow ? Math.Max(1, pivotTable.CompactRowLabelIndent) : 0;
        Dictionary<uint, int>? compactRowIndentLevels = null;
        if (isCompactMultiRow)
        {
            compactRowIndentLevels = [];
            if (CurrentRenderFootprint.Value is { } fp)
                fp.CompactRowIndentLevels = compactRowIndentLevels;
        }

        // Track which non-leaf levels have already been emitted for compact layout,
        // so we only emit a header row when that level's value changes.
        var compactLastKey = (PivotKey?)null;

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            if (pivotTable.ShowSubtotals && rowFields.Count > 1)
            {
                if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
                {
                    // Find the outermost level that changed (smallest k where prefix[k] changed)
                    var breakLevel = subtotalLevelCount; // sentinel: no break
                    for (var level = 0; level < subtotalLevelCount; level++)
                    {
                        var prefixLen = level + 1;
                        var newKey = new PivotKey(group.Key.Values.Take(prefixLen).ToArray());
                        if (currentSubtotalKeys[level] is not null && !currentSubtotalKeys[level]!.Equals(newKey))
                        {
                            breakLevel = level;
                            break;
                        }
                    }

                    if (breakLevel < subtotalLevelCount)
                    {
                        // Flush subtotals from innermost (N-2) down to breakLevel (inclusive), innermost first
                        for (var level = subtotalLevelCount - 1; level >= breakLevel; level--)
                        {
                            if (currentSubtotalKeys[level] is not null)
                            {
                                var subtotalParentRows = ComputeParentPrefixRows(currentSubtotalKeys[level]!, prefixRowsByLevel, retainedRows);
                                WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKeys[level]!, subtotalRowSets[level], retainedRows, subtotalParentRows, outputRow);
                                if (compactRowIndentLevels is not null)
                                    compactRowIndentLevels[outputRow] = level * indentStep;
                                outputRow++;
                            }
                        }
                        // Reset accumulators for all broken levels
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
                        currentSubtotalKeys[level] ??= new PivotKey(group.Key.Values.Take(prefixLen).ToArray());
                        subtotalRowSets[level].AddRange(groupRows);
                    }
                }
                else // Top placement
                {
                    // Emit top subtotals for each level that is newly entered (outermost to innermost)
                    for (var level = 0; level < subtotalLevelCount; level++)
                    {
                        var prefixLen = level + 1;
                        var newKey = new PivotKey(group.Key.Values.Take(prefixLen).ToArray());
                        if (currentSubtotalKeys[level] is null || !currentSubtotalKeys[level]!.Equals(newKey))
                        {
                            currentSubtotalKeys[level] = newKey;
                            if (topSubtotalRowsByLevel[level].TryGetValue(newKey, out var rowsForSubtotal))
                            {
                                var subtotalParentRows = ComputeParentPrefixRows(newKey, prefixRowsByLevel, retainedRows);
                                WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, newKey, rowsForSubtotal, retainedRows, subtotalParentRows, outputRow);
                                if (compactRowIndentLevels is not null)
                                    compactRowIndentLevels[outputRow] = level * indentStep;
                                outputRow++;
                            }
                        }
                    }
                }
            }

            if (isCompactMultiRow)
            {
                // Excel compact layout: emit a separate header row for each non-leaf level
                // that changed relative to the previous group key.
                var leafIndex = rowFields.Count - 1;
                var firstChanged = 0;
                if (compactLastKey is not null)
                {
                    firstChanged = leafIndex; // default: only the leaf changed
                    for (var k = 0; k < leafIndex; k++)
                    {
                        if (!string.Equals(group.Key.Values[k], compactLastKey.Values[k], StringComparison.CurrentCultureIgnoreCase))
                        {
                            firstChanged = k;
                            break;
                        }
                    }
                }

                // Emit header rows for each non-leaf level [firstChanged .. leafIndex-1] that changed
                for (var k = firstChanged; k < leafIndex; k++)
                {
                    SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(group.Key.Values[k]));
                    compactRowIndentLevels![outputRow] = k * indentStep;
                    outputRow++;
                }

                // Emit the leaf row (with data values below)
                SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(group.Key.Values[leafIndex]));
                compactRowIndentLevels![outputRow] = leafIndex * indentStep;
                compactLastKey = group.Key;
            }
            else
            {
                for (var index = 0; index < group.Key.Values.Count; index++)
                {
                    var suppressRepeat = ShouldSuppressRepeatedRowLabel(pivotTable, group.Key, previousRowKey, index);
                    if (!suppressRepeat)
                        SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(group.Key.Values[index]));
                }
            }
            // Compute parent-row denominator rows: rows matching the parent prefix (key minus last value).
            // If this is the outermost row key (length 1), the parent is the grand total (retainedRows).
            var parentRowRows = ComputeParentPrefixRows(group.Key, prefixRowsByLevel, retainedRows);

            for (var index = 0; index < pivotTable.DataFields.Count; index++)
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldOutputColumns + (uint)index),
                    DisplayAggregate(
                        groupRows,
                        new PivotDisplayContext(retainedRows, groupRows, retainedRows,
                            ParentRowRows: parentRowRows),
                        pivotTable.DataFields[index],
                    pivotTable,
                    headers),
                    pivotTable.DataFields[index],
                    pivotTable,
                    isEmptyIntersection: groupRows.Count == 0);
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
                SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(calculatedItem.Name));
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var calculatedValue = EvaluateCalculatedItem(calculatedItem.Formula, groups, pivotTable.DataFields[index], pivotTable, headers);
                    SetPivotValueCell(
                        workbook,
                        sheet,
                        new CellAddress(sheet.Id, outputRow, start.Col + 1 + (uint)index),
                        calculatedValue,
                        pivotTable.DataFields[index],
                        pivotTable);
                    calculatedItemTotals[index] += calculatedValue;
                }

                outputRow++;
            }
        }
        // Flush remaining bottom subtotals (innermost to outermost) after the last group
        if (pivotTable.ShowSubtotals &&
            rowFields.Count > 1 &&
            pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
        {
            for (var level = subtotalLevelCount - 1; level >= 0; level--)
            {
                if (currentSubtotalKeys[level] is not null)
                {
                    var subtotalParentRows = ComputeParentPrefixRows(currentSubtotalKeys[level]!, prefixRowsByLevel, retainedRows);
                    WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKeys[level]!, subtotalRowSets[level], retainedRows, subtotalParentRows, outputRow);
                    if (compactRowIndentLevels is not null)
                        compactRowIndentLevels[outputRow] = level * indentStep;
                    outputRow++;
                }
            }
        }

        if (pivotTable.ShowColumnGrandTotals)
        {
            SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(GrandTotalCaption(pivotTable)));
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
                    pivotTable.DataFields[index],
                    pivotTable);
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
            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(pivotTable.DataFields[index].Name));
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
                pivotTable.DataFields[index],
                pivotTable);
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
        var rowsByColumnKey = BuildColumnRowsByKey(rows, columnFields);
        var columnKeys = BuildColumnKeys(workbook, pivotTable, rows, columnFields, rowsByColumnKey);
        columnKeys = ApplyLabelFilters(columnKeys, pivotTable, columnFields);
        var columnAggregateCache = CreateColumnAggregateCacheIfNeeded(rowsByColumnKey, pivotTable, headers, columnFields);
        columnKeys = ApplyValueFilters(columnKeys, rowsByColumnKey, pivotTable, headers, columnFields, columnAggregateCache);
        columnKeys = ApplySorts(columnKeys, rowsByColumnKey, pivotTable, headers, columnFields, columnAggregateCache);
        var visibleRows = RowsForColumnKeys(rowsByColumnKey, columnKeys, rows);
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
                SetPivotCell(
                    sheet,
                    new CellAddress(sheet.Id, start.Row, outputColumn),
                    new TextValue(GrandTotalCaption(pivotTable, dataField, singleDataField)));
                outputColumn++;
            }
        }

        var outputRow = start.Row + (uint)columnFields.Count;
        outputColumn = start.Col;
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
        IEnumerable<IReadOnlyList<ScalarValue>>? parentRowRows,
        uint outputRow)
    {
        var captionItem = subtotalKey.Values.Count == 0
            ? ""
            : subtotalKey.Values[^1];
        SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue($"{captionItem} Total"));
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
            SetPivotValueCell(
                workbook,
                sheet,
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldCount + (uint)index),
                DisplayAggregate(
                    subtotalRows,
                    new PivotDisplayContext(grandTotalRows, subtotalRows, grandTotalRows,
                        ParentRowRows: parentRowRows),
                    pivotTable.DataFields[index],
                    pivotTable,
                    headers),
                pivotTable.DataFields[index],
                pivotTable,
                isEmptyIntersection: subtotalRows.Count == 0);
    }

    private static bool ShouldSuppressRepeatedRowLabel(
        PivotTableModel pivotTable,
        PivotKey currentRowKey,
        PivotKey? previousRowKey,
        int index) =>
        !pivotTable.RepeatItemLabels &&
        index < currentRowKey.Values.Count - 1 &&
        previousRowKey is not null &&
        previousRowKey.Values.Count > index &&
        string.Equals(previousRowKey.Values[index], currentRowKey.Values[index], StringComparison.CurrentCultureIgnoreCase);

}
