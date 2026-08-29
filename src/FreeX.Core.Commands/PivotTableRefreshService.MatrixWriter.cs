using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    // A column slot is either a Leaf (a full-length column key) or a Subtotal
    // (an outer-prefix key that represents a subtotal column for the outer group).
    // Subtotal slots are only emitted when ShowSubtotals && columnFields.Count > 1.
    private abstract class ColumnSlot
    {
        private ColumnSlot() { }

        public sealed class Leaf(PivotKey key) : ColumnSlot
        {
            public PivotKey Key { get; } = key;
        }

        public sealed class Subtotal(PivotKey prefixKey) : ColumnSlot
        {
            // The prefix key identifies this outer group (e.g. ["Q1"] for a Quarter subtotal).
            public PivotKey PrefixKey { get; } = prefixKey;
        }

        public sealed class CalculatedItem(
            PivotKey key,
            PivotCalculatedItemModel item,
            int fieldPosition,
            IReadOnlyList<string> parentPrefix,
            IReadOnlyList<string> suffix) : ColumnSlot
        {
            public PivotKey Key { get; } = key;

            public PivotCalculatedItemModel Item { get; } = item;

            public int FieldPosition { get; } = fieldPosition;

            public IReadOnlyList<string> ParentPrefix { get; } = parentPrefix;

            public IReadOnlyList<string> Suffix { get; } = suffix;
        }
    }

    // Builds the ordered list of column slots from the sorted leaf column keys.
    // When subtotals are suppressed (single field or ShowSubtotals=false) this returns
    // exactly one Leaf slot per key — identical to the prior raw-columnKeys loop.
    private static List<ColumnSlot> BuildColumnSlots(
        IReadOnlyList<PivotKey> columnKeys,
        IReadOnlyList<PivotFieldModel> columnFields,
        bool emitSubtotals,
        IReadOnlyList<(PivotCalculatedItemModel Item, int FieldPosition)> calculatedItems)
    {
        var slots = new List<ColumnSlot>(columnKeys.Count);
        if (!emitSubtotals || columnFields.Count <= 1)
        {
            for (var index = 0; index < columnKeys.Count; index++)
            {
                slots.Add(new ColumnSlot.Leaf(columnKeys[index]));
                AppendCalculatedColumnItemSlots(slots, columnKeys, index, calculatedItems);
            }
            return slots;
        }

        // Walk leaf keys and emit subtotal slots after the last leaf of each outer group,
        // for every outer level (outermost first, innermost subtotal last within each group).
        // E.g. for Q1/Retail, Q1/Wholesale, Q2/Retail, Q2/Wholesale:
        //   Q1/Retail, Q1/Wholesale, [Q1 Total], Q2/Retail, Q2/Wholesale, [Q2 Total]
        var subtotalLevelCount = columnFields.Count - 1; // levels 0..subtotalLevelCount-1

        for (var i = 0; i < columnKeys.Count; i++)
        {
            slots.Add(new ColumnSlot.Leaf(columnKeys[i]));
            AppendCalculatedColumnItemSlots(slots, columnKeys, i, calculatedItems);

            // After emitting this leaf, check each outer level (outermost first):
            // if the NEXT leaf has a different prefix at this level (or there is no next leaf),
            // emit a subtotal for that prefix.
            for (var level = 0; level < subtotalLevelCount; level++)
            {
                var prefixLen = level + 1;
                var currentPrefix = new PivotKey(columnKeys[i].Values.Take(prefixLen).ToArray());

                var nextHasDifferentPrefix = i + 1 >= columnKeys.Count ||
                    !new PivotKey(columnKeys[i + 1].Values.Take(prefixLen).ToArray()).Equals(currentPrefix);

                if (nextHasDifferentPrefix)
                    slots.Add(new ColumnSlot.Subtotal(currentPrefix));
            }
        }

        return slots;
    }

    private static void AppendCalculatedColumnItemSlots(
        List<ColumnSlot> slots,
        IReadOnlyList<PivotKey> columnKeys,
        int columnKeyIndex,
        IReadOnlyList<(PivotCalculatedItemModel Item, int FieldPosition)> calculatedItems)
    {
        if (columnKeyIndex < 0 || columnKeyIndex >= columnKeys.Count)
            return;

        var columnKey = columnKeys[columnKeyIndex];
        foreach (var (calculatedItem, fieldPosition) in calculatedItems)
        {
            if (!IsEndOfCalculatedColumnItemParent(columnKeys, columnKeyIndex, fieldPosition))
                continue;

            var parentPrefix = columnKey.Values.Take(fieldPosition).ToArray();
            foreach (var suffix in CalculatedColumnItemSuffixes(columnKeys, fieldPosition, parentPrefix))
            {
                var calculatedKey = new PivotKey(parentPrefix
                    .Concat([calculatedItem.Name])
                    .Concat(suffix)
                    .ToArray());
                slots.Add(new ColumnSlot.CalculatedItem(calculatedKey, calculatedItem, fieldPosition, parentPrefix, suffix));
            }
        }
    }

    private static List<string[]> CalculatedColumnItemSuffixes(
        IReadOnlyList<PivotKey> columnKeys,
        int fieldPosition,
        IReadOnlyList<string> parentPrefix)
    {
        var suffixes = new List<string[]>();
        foreach (var columnKey in columnKeys)
        {
            if (!ColumnKeyHasPrefix(columnKey, parentPrefix))
                continue;

            var suffix = columnKey.Values.Skip(fieldPosition + 1).ToArray();
            if (!suffixes.Any(existing => existing.SequenceEqual(suffix, StringComparer.CurrentCultureIgnoreCase)))
                suffixes.Add(suffix);
        }

        return suffixes
            .OrderBy(suffix => new PivotKey(suffix), PivotKeyComparer.Instance)
            .ToList();
    }

    private static bool ColumnKeyHasPrefix(PivotKey columnKey, IReadOnlyList<string> parentPrefix)
    {
        if (columnKey.Values.Count < parentPrefix.Count)
            return false;

        for (var index = 0; index < parentPrefix.Count; index++)
        {
            if (!string.Equals(columnKey.Values[index], parentPrefix[index], StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsEndOfCalculatedColumnItemParent(
        IReadOnlyList<PivotKey> columnKeys,
        int columnKeyIndex,
        int fieldPosition)
    {
        if (columnKeyIndex < 0 || columnKeyIndex >= columnKeys.Count - 1)
            return true;

        var currentParent = columnKeys[columnKeyIndex].Values.Take(fieldPosition);
        var nextParent = columnKeys[columnKeyIndex + 1].Values.Take(fieldPosition);
        return !currentParent.SequenceEqual(nextParent, StringComparer.CurrentCultureIgnoreCase);
    }

    // Returns the source rows for a slot in a given row-group context.
    // For Leaf slots this delegates to RowsForColumnKey.
    // For Subtotal slots this returns all rows whose column prefix matches.
    private static IReadOnlyList<IReadOnlyList<ScalarValue>> RowsForSlot(
        ColumnSlot slot,
        PivotColumnRowMap rowsByColumnKey,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotKey> visibleColumnKeys)
    {
        if (slot is ColumnSlot.Leaf leaf)
            return RowsForColumnKey(rowsByColumnKey, leaf.Key);

        if (slot is ColumnSlot.Subtotal sub)
        {
            // Collect all rows whose column key starts with the subtotal prefix AND whose
            // full column key is still visible (i.e. survived any Label/Value filter on a
            // nested column field). Without the visibility check, a subtotal/grand-total slot
            // would keep summing rows that belong to an inner column item the filter just
            // hid, even though that item no longer appears anywhere on the sheet.
            var prefixLen = sub.PrefixKey.Values.Count;
            var visibleKeySet = new HashSet<PivotKey>(visibleColumnKeys);
            var result = new List<IReadOnlyList<ScalarValue>>();
            foreach (var (key, rows) in rowsByColumnKey.RowsByKey)
            {
                if (key.Values.Count >= prefixLen &&
                    new PivotKey(key.Values.Take(prefixLen).ToArray()).Equals(sub.PrefixKey) &&
                    visibleKeySet.Contains(key))
                {
                    result.AddRange(rows);
                }
            }
            return result;
        }

        return Array.Empty<IReadOnlyList<ScalarValue>>();
    }

    // Returns the "column-total" rows for a slot (used as ColumnTotalRows in PivotDisplayContext).
    // For Leaf slots this is the rows under that leaf across all row groups (from visibleRowsByColumnKey).
    // For Subtotal slots this is all visible rows whose column prefix matches.
    private static IReadOnlyList<IReadOnlyList<ScalarValue>> ColumnTotalRowsForSlot(
        ColumnSlot slot,
        PivotColumnRowMap visibleRowsByColumnKey,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        if (slot is ColumnSlot.Leaf leaf)
            return RowsForColumnKey(visibleRowsByColumnKey, leaf.Key);

        if (slot is ColumnSlot.Subtotal sub)
        {
            var prefixLen = sub.PrefixKey.Values.Count;
            var result = new List<IReadOnlyList<ScalarValue>>();
            foreach (var (key, rows) in visibleRowsByColumnKey.RowsByKey)
            {
                if (key.Values.Count >= prefixLen &&
                    new PivotKey(key.Values.Take(prefixLen).ToArray()).Equals(sub.PrefixKey))
                {
                    result.AddRange(rows);
                }
            }
            return result;
        }

        return Array.Empty<IReadOnlyList<ScalarValue>>();
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
        var columnCalculatedItems = CalculatedItemsForFields(pivotTable, columnFields);

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

        // Build the ordered column slot list.  Subtotal slots are emitted only when
        // ShowSubtotals is on AND there are 2+ column fields.
        var emitColumnSubtotals = pivotTable.ShowSubtotals && columnFields.Count > 1;
        var columnSlots = BuildColumnSlots(columnKeys, columnFields, emitColumnSubtotals, columnCalculatedItems);

        // Excel's Compact form always shows the fixed "Row Labels" caption above the row-label
        // column, whether there is one row field or several — it is not conditioned on field count.
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFields.Count > 0)
            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col), new TextValue("Row Labels"));
        else
        {
            for (var index = 0; index < rowFields.Count; index++)
                SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row, start.Col + (uint)index), new TextValue(headers[rowFields[index].SourceFieldIndex]));
        }

        // Site 1: header row loop — route through slot list.
        var valueStartCol = start.Col + (uint)rowFieldOutputColumns;
        var outputColumn = valueStartCol;
        foreach (var slot in columnSlots)
        {
            foreach (var dataField in pivotTable.DataFields)
            {
                if (slot is ColumnSlot.Leaf leaf)
                {
                    WriteColumnHeader(sheet, start.Row, outputColumn, leaf.Key, dataField, singleDataField);
                }
                else if (slot is ColumnSlot.Subtotal sub)
                {
                    // Write the subtotal column header: emit the outer item text on the outer
                    // level row and "{outer} Total" on the innermost header row.
                    var prefixLen = sub.PrefixKey.Values.Count;
                    for (var level = 0; level < columnFields.Count; level++)
                    {
                        if (level < prefixLen)
                        {
                            var caption = sub.PrefixKey.Values[level];
                            if (level == prefixLen - 1)
                                caption = $"{caption} Total";
                            SetPivotCell(sheet, new CellAddress(sheet.Id, start.Row + (uint)level, outputColumn), new TextValue(caption));
                        }
                        // Rows below the prefix level are left blank in a subtotal column header.
                    }
                }
                else if (slot is ColumnSlot.CalculatedItem calculated)
                {
                    WriteColumnHeader(sheet, start.Row, outputColumn, calculated.Key, dataField, singleDataField);
                }
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
                                    columnSlots,
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
                                    columnSlots,
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
            else if (pivotTable.ReportLayout == PivotReportLayout.Outline && rowFields.Count > 1)
            {
                // Excel's Outline form gives every outer row field its own header row, in that
                // field's own column, with only the innermost field sharing the data row --
                // unlike Tabular form, where every row field's value sits on the same row as
                // the data. Emit a header row for each level (outermost first) whose value
                // changed since the previous row group, then the leaf level on the data row.
                var leafIndex = rowGroup.Key.Values.Count - 1;
                var firstChangedLevel = 0;
                if (previousRowKey is not null)
                {
                    firstChangedLevel = leafIndex; // default: only the leaf item changed
                    for (var level = 0; level < leafIndex; level++)
                    {
                        if (previousRowKey.Values.Count <= level ||
                            !string.Equals(rowGroup.Key.Values[level], previousRowKey.Values[level], StringComparison.CurrentCultureIgnoreCase))
                        {
                            firstChangedLevel = level;
                            break;
                        }
                    }
                }
                for (var level = firstChangedLevel; level < leafIndex; level++)
                {
                    SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)level), new TextValue(rowGroup.Key.Values[level]));
                    outputRow++;
                }
                SetPivotCell(sheet, new CellAddress(sheet.Id, outputRow, start.Col + (uint)leafIndex), new TextValue(rowGroup.Key.Values[leafIndex]));
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

            // Site 2: data row value loop — route through slot list.
            outputColumn = valueStartCol;
            var rowCalculatedItemTotals = new double[pivotTable.DataFields.Count];
            foreach (var slot in columnSlots)
            {
                if (slot is ColumnSlot.CalculatedItem calculated)
                {
                    for (var index = 0; index < pivotTable.DataFields.Count; index++)
                    {
                        var calculatedValue = EvaluateCalculatedColumnItemSlot(
                            calculated,
                            columnKeys,
                            rowGroupRowsByColumnKey,
                            pivotTable.DataFields[index],
                            pivotTable,
                            headers);
                        SetPivotValueCell(
                            workbook,
                            sheet,
                            new CellAddress(sheet.Id, outputRow, outputColumn),
                            calculatedValue,
                            pivotTable.DataFields[index],
                            pivotTable);
                        rowCalculatedItemTotals[index] += calculatedValue;
                        outputColumn++;
                    }

                    continue;
                }

                // Rows in this row group that fall under this slot.
                var columnRows = RowsForSlot(slot, rowGroupRowsByColumnKey, columnFields, columnKeys);
                // Column-total rows across all row groups for this slot.
                var columnTotalRows = ColumnTotalRowsForSlot(slot, visibleRowsByColumnKey, columnFields);

                // Parent row denominator: parent prefix rows restricted to this slot.
                IReadOnlyList<IReadOnlyList<ScalarValue>> parentRowRows;
                if (slot is ColumnSlot.Leaf leaf)
                    parentRowRows = RowsForColumnKey(parentRowPrefixRowsByColumnKey, leaf.Key);
                else
                    parentRowRows = RowsForSlot(slot, BuildColumnRowsByKey(parentRowPrefixRows, columnFields), columnFields, columnKeys);

                // Parent column denominator: rows in this row group matching parent column prefix.
                // For a Subtotal slot the parent column is one level up from the prefix.
                IEnumerable<IReadOnlyList<ScalarValue>>? parentColRows;
                if (slot is ColumnSlot.Leaf leafForParent)
                    parentColRows = ComputeParentColumnRows(leafForParent.Key, colPrefixRowsByLevelAll, visibleRowGroupRows, columnFields, rowGroupRows);
                else
                    parentColRows = null; // subtotal column: no parent column (falls back to row total)

                foreach (var dataField in pivotTable.DataFields)
                {
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        columnRows,
                        new PivotDisplayContext(visibleRows, visibleRowGroupRows, columnTotalRows,
                            ParentRowRows: parentRowRows,
                            ParentColumnRows: parentColRows,
                            RunningTotalScopeRows: columnTotalRows),
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
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var dataField = pivotTable.DataFields[index];
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        visibleRowGroupRows,
                        new PivotDisplayContext(visibleRows, visibleRowGroupRows, visibleRows),
                        dataField,
                        pivotTable,
                        headers) + rowCalculatedItemTotals[index],
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
                (rowFields.Count == 1 || IsEndOfOuterItem(rowGroups, rowGroup, rowFields.Count)))
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
                        columnSlots,
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
            // Site 4: grand-total row loop — route through slot list.
            outputColumn = valueStartCol;
            var grandRowCalculatedItemTotals = new double[pivotTable.DataFields.Count];
            foreach (var slot in columnSlots)
            {
                if (slot is ColumnSlot.CalculatedItem calculated)
                {
                    for (var index = 0; index < pivotTable.DataFields.Count; index++)
                    {
                        var calculatedValue = EvaluateCalculatedColumnItemSlot(
                            calculated,
                            columnKeys,
                            rowsByColumnKey,
                            pivotTable.DataFields[index],
                            pivotTable,
                            headers);
                        SetPivotValueCell(
                            workbook,
                            sheet,
                            new CellAddress(sheet.Id, outputRow, outputColumn),
                            calculatedValue,
                            pivotTable.DataFields[index],
                            pivotTable);
                        grandRowCalculatedItemTotals[index] += calculatedValue;
                        outputColumn++;
                    }

                    continue;
                }

                // Use rows from ALL retained rows (not filtered by row group) for the grand-total row.
                var columnRows = RowsForSlot(slot, rowsByColumnKey, columnFields, columnKeys);
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
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var dataField = pivotTable.DataFields[index];
                    SetPivotValueCell(workbook, sheet, new CellAddress(sheet.Id, outputRow, outputColumn), DisplayAggregate(
                        visibleRows,
                        new PivotDisplayContext(visibleRows, visibleRows, visibleRows),
                        dataField,
                        pivotTable,
                        headers) + grandRowCalculatedItemTotals[index],
                        dataField,
                        pivotTable);
                    outputColumn++;
                }
            }
        }
    }

    private static double EvaluateCalculatedColumnItemSlot(
        ColumnSlot.CalculatedItem calculated,
        IReadOnlyList<PivotKey> columnKeys,
        PivotColumnRowMap rowsByColumnKey,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers) =>
        EvaluateCalculatedItemForField(
            calculated.Item.Formula,
            columnKeys,
            key => RowsForColumnKey(rowsByColumnKey, key),
            calculated.FieldPosition,
            calculated.ParentPrefix,
            dataField,
            pivotTable,
            headers,
            calculated.Suffix);

    private static void WriteMatrixSubtotalRow(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        uint valueStartCol,
        IReadOnlyList<ColumnSlot> columnSlots,
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
        var leafColumnKeys = columnSlots
            .OfType<ColumnSlot.Leaf>()
            .Select(s => s.Key)
            .ToList();
        var visibleSubtotalRows = RowsForColumnKeys(subtotalRowsByColumnKey, leafColumnKeys, subtotalRows);

        // Parent row rows restricted per column key (for % of Parent Row Total)
        var parentRowPrefixRowsByColumnKey = parentRowRows is not null
            ? BuildColumnRowsByKey(parentRowRows, columnFields)
            : null;

        // Site 3: subtotal row value loop — route through slot list.
        var outputColumn = valueStartCol;
        var subtotalCalculatedItemTotals = new double[pivotTable.DataFields.Count];
        foreach (var slot in columnSlots)
        {
            if (slot is ColumnSlot.CalculatedItem calculated)
            {
                for (var index = 0; index < pivotTable.DataFields.Count; index++)
                {
                    var calculatedValue = EvaluateCalculatedColumnItemSlot(
                        calculated,
                        leafColumnKeys,
                        subtotalRowsByColumnKey,
                        pivotTable.DataFields[index],
                        pivotTable,
                        headers);
                    SetPivotValueCell(
                        workbook,
                        sheet,
                        new CellAddress(sheet.Id, outputRow, outputColumn),
                        calculatedValue,
                        pivotTable.DataFields[index],
                        pivotTable);
                    subtotalCalculatedItemTotals[index] += calculatedValue;
                    outputColumn++;
                }

                continue;
            }

            var subtotalColumnRows = RowsForSlot(slot, subtotalRowsByColumnKey, columnFields, leafColumnKeys);
            var columnTotalRows = ColumnTotalRowsForSlot(slot, visibleRowsByColumnKey, columnFields);

            // Parent row denominator restricted to this slot
            IReadOnlyList<IReadOnlyList<ScalarValue>>? parentRowColRows = null;
            if (parentRowPrefixRowsByColumnKey is not null)
            {
                if (slot is ColumnSlot.Leaf leafSlot)
                    parentRowColRows = RowsForColumnKey(parentRowPrefixRowsByColumnKey, leafSlot.Key);
                else
                    parentRowColRows = RowsForSlot(slot, parentRowPrefixRowsByColumnKey, columnFields, leafColumnKeys);
            }

            // Parent column denominator: subtotal rows restricted to parent column prefix.
            // Only meaningful for leaf slots (subtotal slots themselves have no parent column subtotal).
            IEnumerable<IReadOnlyList<ScalarValue>>? parentColRows = null;
            if (slot is ColumnSlot.Leaf leafForParent)
                parentColRows = ComputeParentColumnRows(leafForParent.Key, colPrefixRowsByLevelAll, visibleSubtotalRows, columnFields, subtotalRows);

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
                            ParentColumnRows: parentColRows,
                            RunningTotalScopeRows: columnTotalRows),
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
            for (var index = 0; index < pivotTable.DataFields.Count; index++)
            {
                var dataField = pivotTable.DataFields[index];
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
                        headers) + subtotalCalculatedItemTotals[index],
                    dataField,
                    pivotTable,
                    isEmptyIntersection: visibleSubtotalRows.Count == 0);
                outputColumn++;
            }
        }
    }
}
