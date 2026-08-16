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
        // Excel's Compact form always shows the fixed "Row Labels" caption above the row-label
        // column, whether there is one row field or several — it is not conditioned on field count.
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 0)
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
        var rowCalculatedItems = CalculatedItemsForFields(pivotTable, rowFields);

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

        // Excel includes a row field's calculated items in every subtotal of its enclosing
        // fields, not just the grand total - a calculated item is itself a value of its field,
        // and Excel's field subtotal sums ALL of a field's items, real and calculated. Without
        // this, a subtotal only summed the raw group rows and could disagree with its own
        // grand total (which does add calculatedItemTotals below) by exactly the calculated
        // items' contribution. Precomputed once here, rather than accumulated during the main
        // loop below, because Top-placement subtotals are written before their child
        // rows/calculated items are ever visited by that loop.
        var subtotalCalculatedItemTotals = new Dictionary<PivotKey, double[]>[subtotalLevelCount];
        for (var level = 0; level < subtotalLevelCount; level++)
            subtotalCalculatedItemTotals[level] = [];

        if (pivotTable.ShowSubtotals && subtotalLevelCount > 0 && rowCalculatedItems.Count > 0)
        {
            var calculatedItemGroupKeys = groups.Select(group => group.Key).ToList();
            var calculatedItemContext = new PivotDisplayContext(retainedRows, retainedRows, retainedRows);
            foreach (var (calculatedItem, fieldPosition) in rowCalculatedItems)
            {
                if (fieldPosition == 0)
                    continue; // no enclosing subtotal level exists above the outermost field

                var parentPrefixes = groups
                    .Select(group => new PivotKey(group.Key.Values.Take(fieldPosition).ToArray()))
                    .Distinct();

                foreach (var parentPrefix in parentPrefixes)
                {
                    var values = new double[pivotTable.DataFields.Count];
                    for (var index = 0; index < pivotTable.DataFields.Count; index++)
                        values[index] = EvaluateCalculatedItemForField(
                            calculatedItem.Formula,
                            calculatedItemGroupKeys,
                            key => groups.Where(group => group.Key.Equals(key)).SelectMany(group => group),
                            fieldPosition,
                            parentPrefix.Values,
                            pivotTable.DataFields[index],
                            pivotTable,
                            headers,
                            context: calculatedItemContext);

                    // The calculated item belongs inside every subtotal level strictly
                    // shallower than its own field position - those subtotals collapse over
                    // deeper fields (including this one), so they must add the item's
                    // contribution too, in addition to the raw rows they already sum.
                    for (var level = 0; level < Math.Min(fieldPosition, subtotalLevelCount); level++)
                    {
                        var key = new PivotKey(parentPrefix.Values.Take(level + 1).ToArray());
                        if (!subtotalCalculatedItemTotals[level].TryGetValue(key, out var existing))
                        {
                            existing = new double[pivotTable.DataFields.Count];
                            subtotalCalculatedItemTotals[level][key] = existing;
                        }
                        for (var index = 0; index < pivotTable.DataFields.Count; index++)
                            existing[index] += values[index];
                    }
                }
            }
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
                                subtotalCalculatedItemTotals[level].TryGetValue(currentSubtotalKeys[level]!, out var calculatedItemAddend);
                                WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKeys[level]!, subtotalRowSets[level], retainedRows, subtotalParentRows, outputRow, calculatedItemAddend);
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
                                subtotalCalculatedItemTotals[level].TryGetValue(newKey, out var calculatedItemAddend);
                                WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, newKey, rowsForSubtotal, retainedRows, subtotalParentRows, outputRow, calculatedItemAddend);
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

            foreach (var (calculatedItem, fieldPosition) in rowCalculatedItems)
            {
                if (!IsEndOfCalculatedItemParent(groups, group, fieldPosition))
                    continue;

                var parentPrefix = group.Key.Values.Take(fieldPosition).ToArray();
                WriteRowCalculatedItem(
                    workbook,
                    sheet,
                    pivotTable,
                    headers,
                    start,
                    rowFieldOutputColumns,
                    groups,
                    calculatedItem,
                    fieldPosition,
                    parentPrefix,
                    outputRow,
                    compactRowIndentLevels,
                    indentStep,
                    calculatedItemTotals,
                    retainedRows);
                outputRow++;
            }

            if (pivotTable.BlankLineAfterItems &&
                rowFields.Count > 1 &&
                IsEndOfOuterItem(groups, group, rowFields.Count))
            {
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
                    subtotalCalculatedItemTotals[level].TryGetValue(currentSubtotalKeys[level]!, out var calculatedItemAddend);
                    WriteSubtotalRow(workbook, sheet, pivotTable, headers, start, rowFieldOutputColumns, currentSubtotalKeys[level]!, subtotalRowSets[level], retainedRows, subtotalParentRows, outputRow, calculatedItemAddend);
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
        var columnCalculatedItems = columnFields.Count == 1
            ? CalculatedItemsForFields(pivotTable, columnFields)
            : [];

        var outputColumn = start.Col;
        foreach (var columnKey in columnKeys)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                WriteColumnHeader(sheet, start.Row, outputColumn, columnKey, dataField, singleDataField);
                outputColumn++;
            }
        }
        foreach (var (calculatedItem, _) in columnCalculatedItems)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                WriteColumnHeader(sheet, start.Row, outputColumn, new PivotKey([calculatedItem.Name]), dataField, singleDataField);
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
        var calculatedItemTotals = new double[pivotTable.DataFields.Count];
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
        // Column-only pivot: a single value axis, so grand/row/column total all reduce to
        // the same visible-rows set (matches the DisplayAggregate context built for the
        // ordinary column cells just above in this method).
        var columnCalculatedItemContext = new PivotDisplayContext(visibleRows, visibleRows, visibleRows);
        foreach (var (calculatedItem, fieldPosition) in columnCalculatedItems)
        {
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
            {
                var calculatedValue = EvaluateCalculatedItemForField(
                    calculatedItem.Formula,
                    columnKeys,
                    key => RowsForColumnKey(rowsByColumnKey, key),
                    fieldPosition,
                    [],
                    pivotTable.DataFields[index],
                    pivotTable,
                    headers,
                    context: columnCalculatedItemContext);
                SetPivotValueCell(
                    workbook,
                    sheet,
                    new CellAddress(sheet.Id, outputRow, outputColumn),
                    calculatedValue,
                    pivotTable.DataFields[index],
                    pivotTable);
                calculatedItemTotals[index] += calculatedValue;
                outputColumn++;
            }
        }

        if (pivotTable.ShowRowGrandTotals)
        {
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
            {
                var dataField = pivotTable.DataFields[index];
                SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                    visibleRows,
                    new PivotDisplayContext(visibleRows, visibleRows, visibleRows),
                    dataField,
                    pivotTable,
                    headers) + calculatedItemTotals[index],
                    dataField,
                    pivotTable);
                outputColumn++;
            }
        }
    }

    private static List<(PivotCalculatedItemModel Item, int FieldPosition)> CalculatedItemsForFields(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> fields)
    {
        var calculatedItems = new List<(PivotCalculatedItemModel Item, int FieldPosition)>();
        foreach (var calculatedItem in pivotTable.CalculatedItems)
        {
            for (var fieldPosition = 0; fieldPosition < fields.Count; fieldPosition++)
            {
                if (calculatedItem.SourceFieldIndex != fields[fieldPosition].SourceFieldIndex)
                    continue;

                calculatedItems.Add((calculatedItem, fieldPosition));
                break;
            }
        }

        return calculatedItems
            .OrderByDescending(item => item.FieldPosition)
            .ThenBy(item => item.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void WriteRowCalculatedItem(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        int rowFieldOutputColumns,
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotCalculatedItemModel calculatedItem,
        int fieldPosition,
        IReadOnlyList<string> parentPrefix,
        uint outputRow,
        Dictionary<uint, int>? compactRowIndentLevels,
        int indentStep,
        double[] calculatedItemTotals,
        IReadOnlyList<IReadOnlyList<ScalarValue>> retainedRows)
    {
        if (compactRowIndentLevels is not null)
        {
            SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col), new TextValue(calculatedItem.Name));
            compactRowIndentLevels[outputRow] = fieldPosition * indentStep;
        }
        else
        {
            for (var index = 0; index < parentPrefix.Count; index++)
                SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)index), new TextValue(parentPrefix[index]));

            SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)fieldPosition), new TextValue(calculatedItem.Name));
        }

        var groupKeys = groups.Select(group => group.Key).ToList();
        // Row-only pivot: a single value axis, so grand/row/column total all reduce to the
        // same retained-rows set (matches the DisplayAggregate context built for the
        // ordinary row cells just above in WriteRowPivot).
        var context = new PivotDisplayContext(retainedRows, retainedRows, retainedRows);
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            var calculatedValue = EvaluateCalculatedItemForField(
                calculatedItem.Formula,
                groupKeys,
                key => groups.Where(group => group.Key.Equals(key)).SelectMany(group => group),
                fieldPosition,
                parentPrefix,
                pivotTable.DataFields[index],
                pivotTable,
                headers,
                context: context);
            SetPivotValueCell(
                workbook,
                sheet,
                new CellAddress(sheet.Id, outputRow, start.Col + (uint)rowFieldOutputColumns + (uint)index),
                calculatedValue,
                pivotTable.DataFields[index],
                pivotTable);
            calculatedItemTotals[index] += calculatedValue;
        }
    }

    private static double EvaluateCalculatedItemForField(
        string formula,
        IReadOnlyList<PivotKey> keys,
        Func<PivotKey, IEnumerable<IReadOnlyList<ScalarValue>>> rowsForKey,
        int fieldPosition,
        IReadOnlyList<string> parentPrefix,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<string>? suffix = null,
        PivotDisplayContext? context = null)
    {
        suffix ??= [];
        var rawValue = PivotCalculatedExpressionEvaluator.Evaluate(formula, name =>
        {
            var rows = keys
                .Where(key => CalculatedItemKeyMatches(key, fieldPosition, parentPrefix, name, suffix))
                .SelectMany(rowsForKey);
            return AggregateDouble(rows, dataField, pivotTable, headers);
        });

        // R87-calc-pivot-aggregation-5-3: apply the data field's Show Values As setting
        // (% of grand/row/column/parent total) to the calculated item's own combined
        // value, so it doesn't display a raw aggregate in a column where every sibling
        // cell shows a transformed value. See ApplyPercentOfTotalToCalculatedValue.
        return context is null ? rawValue : ApplyPercentOfTotalToCalculatedValue(rawValue, context, dataField, pivotTable, headers);
    }

    private static bool CalculatedItemKeyMatches(
        PivotKey key,
        int fieldPosition,
        IReadOnlyList<string> parentPrefix,
        string itemName,
        IReadOnlyList<string> suffix)
    {
        if (key.Values.Count < fieldPosition + 1 + suffix.Count || parentPrefix.Count != fieldPosition)
            return false;

        for (var index = 0; index < parentPrefix.Count; index++)
        {
            if (!string.Equals(key.Values[index], parentPrefix[index], StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        if (!string.Equals(key.Values[fieldPosition], itemName, StringComparison.CurrentCultureIgnoreCase))
            return false;

        for (var index = 0; index < suffix.Count; index++)
        {
            var suffixIndex = fieldPosition + 1 + index;
            if (!string.Equals(key.Values[suffixIndex], suffix[index], StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsEndOfCalculatedItemParent(
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        IGrouping<PivotKey, IReadOnlyList<ScalarValue>> group,
        int fieldPosition)
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

        var currentParent = group.Key.Values.Take(fieldPosition);
        var nextParent = groups[index + 1].Key.Values.Take(fieldPosition);
        return !currentParent.SequenceEqual(nextParent, StringComparer.CurrentCultureIgnoreCase);
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
        uint outputRow,
        IReadOnlyList<double>? calculatedItemAddend = null)
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
                    headers) + (calculatedItemAddend is null ? 0 : calculatedItemAddend[index]),
                pivotTable.DataFields[index],
                pivotTable,
                isEmptyIntersection: subtotalRows.Count == 0 && calculatedItemAddend is null);
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
