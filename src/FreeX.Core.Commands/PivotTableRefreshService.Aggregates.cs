using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static bool HasNumericValue(ScalarValue value) =>
        value is NumberValue or DateTimeValue or BoolValue;

    private static bool IsNonBlank(ScalarValue value) =>
        value is not BlankValue;

    // FIX 2: Returns null when the group has rows but no numeric values for
    // min/max/product/stddev/var (Excel shows a blank cell in those cases).
    // Returns 0 (not null) for sum/count/average/countnums (Excel convention).
    private static double? Aggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var summaryFunction = dataField.SummaryFunction.AsSpan().Trim();

        // FIX 1: Calculated fields are evaluated once per group using SUM of each
        // constituent source field over the group rows, not per-row evaluation.
        if (!string.IsNullOrWhiteSpace(dataField.CalculatedFieldName))
        {
            var calculated = FindCalculatedField(pivotTable, dataField.CalculatedFieldName);
            if (calculated is not null)
                return EvaluateCalculatedFieldOnGroup(calculated.Formula, rows, pivotTable, headers);
        }

        if (summaryFunction.Equals("sum", StringComparison.OrdinalIgnoreCase))
            return SumAggregate(rows, dataField, pivotTable, headers);

        var nonBlankCount = 0;
        var numericCount = 0;
        var sum = 0d;
        var mean = 0d;
        var sumSquaredDeviation = 0d;
        var min = 0d;
        var max = 0d;
        var product = 1d;

        foreach (var row in rows)
        {
            var value = GetDataFieldValue(row, dataField, pivotTable, headers);
            if (IsNonBlank(value))
                nonBlankCount++;

            if (!HasNumericValue(value))
                continue;

            var numeric = Number(value);
            numericCount++;
            sum += numeric;
            product *= numeric;
            if (numericCount == 1)
            {
                min = numeric;
                max = numeric;
            }
            else
            {
                min = Math.Min(min, numeric);
                max = Math.Max(max, numeric);
            }

            var delta = numeric - mean;
            mean += delta / numericCount;
            sumSquaredDeviation += delta * (numeric - mean);
        }

        if (summaryFunction.Equals("count", StringComparison.OrdinalIgnoreCase))
            return nonBlankCount;
        if (summaryFunction.Equals("countnums", StringComparison.OrdinalIgnoreCase))
            return numericCount;
        if (summaryFunction.Equals("average", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("avg", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : sum / numericCount;
        // FIX 2: Return null (blank) instead of 0 when no numeric values for min/max/product/stddev/var
        if (summaryFunction.Equals("min", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? null : min;
        if (summaryFunction.Equals("max", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? null : max;
        if (summaryFunction.Equals("product", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? null : product;
        if (summaryFunction.Equals("stddev", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddevs", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddev.s", StringComparison.OrdinalIgnoreCase))
            return numericCount < 2 ? (numericCount == 0 ? null : (double?)0) : Math.Sqrt(Variance(sumSquaredDeviation, numericCount, sample: true));
        if (summaryFunction.Equals("stddevp", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddev.p", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? null : Math.Sqrt(Variance(sumSquaredDeviation, numericCount, sample: false));
        if (summaryFunction.Equals("var", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("vars", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("var.s", StringComparison.OrdinalIgnoreCase))
            return numericCount < 2 ? (numericCount == 0 ? null : (double?)0) : Variance(sumSquaredDeviation, numericCount, sample: true);
        if (summaryFunction.Equals("varp", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("var.p", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? null : Variance(sumSquaredDeviation, numericCount, sample: false);

        return sum;
    }

    // Internal helper: returns Aggregate as a non-null double (null → 0) for use in
    // denominator calculations and other internal numeric contexts.
    private static double AggregateDouble(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers) =>
        Aggregate(rows, dataField, pivotTable, headers) ?? 0;

    private static double SumAggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var sum = 0d;
        foreach (var row in rows)
        {
            var value = GetDataFieldValue(row, dataField, pivotTable, headers);
            if (HasNumericValue(value))
                sum += Number(value);
        }

        return sum;
    }

    private static double Variance(double sumSquaredDeviation, int count, bool sample)
    {
        return sumSquaredDeviation / (sample ? count - 1 : count);
    }

    private sealed record PivotDisplayContext(
        IEnumerable<IReadOnlyList<ScalarValue>> GrandTotalRows,
        IEnumerable<IReadOnlyList<ScalarValue>> RowTotalRows,
        IEnumerable<IReadOnlyList<ScalarValue>> ColumnTotalRows,
        // Immediate-parent denominators for the "% of Parent Row/Column" modes.
        // null means fall back: parent-row -> grand total in the same column,
        // parent-column -> the full row total in the same row.
        IEnumerable<IReadOnlyList<ScalarValue>>? ParentRowRows = null,
        IEnumerable<IReadOnlyList<ScalarValue>>? ParentColumnRows = null,
        // R15-pivot-tables-deep-2: scope for "Running Total In" / "Difference From" base-item
        // lookups. In a matrix pivot (row + column fields) this must be the CURRENT COLUMN's
        // rows (across all row groups), not the whole-grid GrandTotalRows, so each column
        // accumulates/bases its own running total instead of sharing one grid-wide total.
        // null means fall back to GrandTotalRows (correct for single-axis pivots, where the
        // two are already equivalent).
        IEnumerable<IReadOnlyList<ScalarValue>>? RunningTotalScopeRows = null);

    // Returns double? — null means "write a blank cell" (FIX 2 propagation).
    private static double? DisplayAggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDisplayContext context,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var value = Aggregate(rows, dataField, pivotTable, headers);
        // R15-pivot-tables-deep-2: base the running total / difference-from lookups on the
        // current column's own rows (RunningTotalScopeRows) when the writer supplied one
        // (matrix pivots); fall back to GrandTotalRows for single-axis pivots where the two
        // sets are already equivalent.
        var runningTotalScopeRows = context.RunningTotalScopeRows ?? context.GrandTotalRows;
        if (dataField.ShowValuesAs == PivotShowValuesAs.RunningTotalIn)
            return ReferenceEquals(rows, runningTotalScopeRows)
                ? value
                : RunningTotal(rows, runningTotalScopeRows, dataField, pivotTable, headers);
        if (dataField.ShowValuesAs is PivotShowValuesAs.DifferenceFrom or PivotShowValuesAs.PercentDifferenceFrom)
        {
            var baseValue = BaseItemAggregate(runningTotalScopeRows, dataField, pivotTable, headers);
            var numericValue = value ?? 0;
            var difference = numericValue - baseValue;
            return dataField.ShowValuesAs == PivotShowValuesAs.PercentDifferenceFrom
                ? Math.Abs(baseValue) < 0.0000001 ? 0 : difference / baseValue
                : difference;
        }
        if (dataField.ShowValuesAs is PivotShowValuesAs.RankSmallest or PivotShowValuesAs.RankLargest)
            return RankValue(rows, context.GrandTotalRows, dataField, pivotTable, headers);
        if (dataField.ShowValuesAs == PivotShowValuesAs.Index)
        {
            var grandTotal = AggregateDouble(context.GrandTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var rowTotal = AggregateDouble(context.RowTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var columnTotal = AggregateDouble(context.ColumnTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var indexDenominator = rowTotal * columnTotal;
            var numericValue = value ?? 0;
            return Math.Abs(indexDenominator) < 0.0000001 ? 0 : numericValue * grandTotal / indexDenominator;
        }

        // For the three "% of Parent" modes, use the immediate-parent denominator rows
        // that were pre-computed by the writer (null = fall back to grand/row total).
        IEnumerable<IReadOnlyList<ScalarValue>>? denominatorRows = dataField.ShowValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => context.GrandTotalRows,
            PivotShowValuesAs.PercentOfRowTotal => context.RowTotalRows,
            PivotShowValuesAs.PercentOfColumnTotal => context.ColumnTotalRows,
            PivotShowValuesAs.PercentOfParentRowTotal => context.ParentRowRows ?? context.GrandTotalRows,
            PivotShowValuesAs.PercentOfParentColumnTotal => context.ParentColumnRows ?? context.RowTotalRows,
            PivotShowValuesAs.PercentOfParentTotal => PercentOfParentTotalRows(rows, context, dataField, pivotTable, headers),
            _ => null
        };
        if (denominatorRows is null)
            return value;

        var denominator = AggregateDouble(denominatorRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
        var numVal = value ?? 0;
        return Math.Abs(denominator) < 0.0000001 ? 0 : numVal / denominator;
    }

    private static IEnumerable<IReadOnlyList<ScalarValue>> PercentOfParentTotalRows(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDisplayContext context,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.BaseFieldIndex is not { } baseFieldIndex ||
            !IsValidField(baseFieldIndex, headers.Count))
        {
            return context.GrandTotalRows;
        }

        var rowFieldPosition = FindPivotFieldPosition(pivotTable.RowFields, baseFieldIndex);
        if (rowFieldPosition >= 0)
        {
            return RowsForSharedAxisPrefix(
                rows,
                context.ColumnTotalRows,
                pivotTable.RowFields,
                rowFieldPosition) ?? rows;
        }

        var columnFieldPosition = FindPivotFieldPosition(pivotTable.ColumnFields, baseFieldIndex);
        if (columnFieldPosition >= 0)
        {
            return RowsForSharedAxisPrefix(
                rows,
                context.RowTotalRows,
                pivotTable.ColumnFields,
                columnFieldPosition) ?? rows;
        }

        return context.GrandTotalRows;
    }

    private static IReadOnlyList<IReadOnlyList<ScalarValue>>? RowsForSharedAxisPrefix(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        IEnumerable<IReadOnlyList<ScalarValue>> candidateRows,
        IReadOnlyList<PivotFieldModel> axisFields,
        int fieldPosition)
    {
        var rowList = rows is IReadOnlyList<IReadOnlyList<ScalarValue>> list ? list : rows.ToList();
        if (rowList.Count == 0)
            return [];

        var prefix = SharedAxisPrefix(rowList, axisFields, fieldPosition);
        if (prefix is null)
            return null;

        return candidateRows
            .Where(row => MatchesAxisPrefix(row, axisFields, prefix))
            .ToList();
    }

    private static IReadOnlyList<string>? SharedAxisPrefix(
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> axisFields,
        int fieldPosition)
    {
        var first = rows[0];
        var prefix = axisFields
            .Take(fieldPosition + 1)
            .Select(field => GroupKeyText(first[field.SourceFieldIndex], field))
            .ToList();

        foreach (var row in rows.Skip(1))
        {
            if (!MatchesAxisPrefix(row, axisFields, prefix))
                return null;
        }

        return prefix;
    }

    private static bool MatchesAxisPrefix(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> axisFields,
        IReadOnlyList<string> prefix)
    {
        if (prefix.Count > axisFields.Count)
            return false;

        for (var index = 0; index < prefix.Count; index++)
        {
            var field = axisFields[index];
            if (!string.Equals(
                    GroupKeyText(row[field.SourceFieldIndex], field),
                    prefix[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int FindPivotFieldPosition(IEnumerable<PivotFieldModel> fields, int sourceFieldIndex)
    {
        var index = 0;
        foreach (var field in fields)
        {
            if (field.SourceFieldIndex == sourceFieldIndex)
                return index;
            index++;
        }

        return -1;
    }

    private static double RunningTotal(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        IEnumerable<IReadOnlyList<ScalarValue>> totalRows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.BaseFieldIndex is not { } baseFieldIndex || !IsValidField(baseFieldIndex, headers.Count))
            return AggregateDouble(rows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);

        // R15-pivot-tables-deep-1: identify items by their DISPLAYED group text (so a
        // date/number-grouped base field accumulates over the displayed buckets) and order
        // them with the same numeric-aware comparer used for row/column display
        // (PivotKeyComparer), not plain lexicographic OrdinalIgnoreCase. Otherwise numeric
        // base fields like 1,2,3,10,11,20 accumulate in the wrong order (e.g. "10" would only
        // include {"1","10"} instead of {1,2,3,10}).
        var baseField = FindFieldModel(pivotTable, baseFieldIndex);
        string ItemKey(IReadOnlyList<ScalarValue> row) =>
            baseField is null ? KeyText(row[baseFieldIndex]) : GroupKeyText(row[baseFieldIndex], baseField);

        var currentItem = FirstBaseFieldItem(rows, ItemKey);
        if (currentItem is null)
            return 0;

        var orderedItems = totalRows
            .Select(ItemKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(Comparer<string>.Create(PivotKeyComparer.CompareKeyText))
            .ToList();
        var currentIndex = FindOrdinalIgnoreCaseIndex(orderedItems, currentItem);
        if (currentIndex < 0)
            return 0;

        var included = new HashSet<string>(orderedItems.Take(currentIndex + 1), StringComparer.OrdinalIgnoreCase);
        var runningRows = totalRows.Where(row => included.Contains(ItemKey(row)));
        return AggregateDouble(runningRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
    }

    // Finds the PivotFieldModel (row/column/page) for a source field index, so its display
    // grouping (date/number buckets) can be applied via GroupKeyText.
    private static PivotFieldModel? FindFieldModel(PivotTableModel pivotTable, int sourceFieldIndex)
    {
        foreach (var field in pivotTable.RowFields)
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        foreach (var field in pivotTable.ColumnFields)
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        foreach (var field in pivotTable.PageFields)
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        return null;
    }

    private static double BaseItemAggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> totalRows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.BaseFieldIndex is not { } baseFieldIndex ||
            !IsValidField(baseFieldIndex, headers.Count) ||
            string.IsNullOrWhiteSpace(dataField.BaseItem))
        {
            return 0;
        }

        // FIX 3: Use OrdinalIgnoreCase for item identity comparisons
        var baseRows = totalRows.Where(row =>
            string.Equals(KeyText(row[baseFieldIndex]), dataField.BaseItem, StringComparison.OrdinalIgnoreCase));
        return AggregateDouble(baseRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
    }

    private static double RankValue(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        IEnumerable<IReadOnlyList<ScalarValue>> totalRows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.BaseFieldIndex is not { } baseFieldIndex || !IsValidField(baseFieldIndex, headers.Count))
            return 0;

        var currentItem = FirstBaseFieldItem(rows, baseFieldIndex);
        if (currentItem is null)
            return 0;

        // FIX 3: Use OrdinalIgnoreCase for item identity comparisons
        var valuesByItem = totalRows
            .GroupBy(row => KeyText(row[baseFieldIndex]), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Item: group.Key, Value: AggregateDouble(group, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers)))
            .ToList();
        var ordered = dataField.ShowValuesAs == PivotShowValuesAs.RankLargest
            ? valuesByItem.OrderByDescending(item => item.Value).ThenBy(item => item.Item, StringComparer.OrdinalIgnoreCase).ToList()
            : valuesByItem.OrderBy(item => item.Value).ThenBy(item => item.Item, StringComparer.OrdinalIgnoreCase).ToList();
        var rank = FindRankedItemIndex(ordered, currentItem);
        return rank < 0 ? 0 : rank + 1;
    }

    private static ScalarValue GetDataFieldValue(
        IReadOnlyList<ScalarValue> row,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.SourceFieldIndex >= 0 && dataField.SourceFieldIndex < row.Count)
            return row[dataField.SourceFieldIndex];

        // Note: calculated field per-row evaluation is only used internally now;
        // group aggregation goes through EvaluateCalculatedFieldOnGroup in Aggregate.
        return BlankValue.Instance;
    }

    private static string? FirstBaseFieldItem(IEnumerable<IReadOnlyList<ScalarValue>> rows, int baseFieldIndex)
    {
        foreach (var row in rows)
            return KeyText(row[baseFieldIndex]);

        return null;
    }

    private static string? FirstBaseFieldItem(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        Func<IReadOnlyList<ScalarValue>, string> itemKey)
    {
        foreach (var row in rows)
            return itemKey(row);

        return null;
    }

    private static PivotCalculatedFieldModel? FindCalculatedField(PivotTableModel pivotTable, string fieldName)
    {
        foreach (var field in pivotTable.CalculatedFields)
        {
            if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                return field;
        }

        return null;
    }

    // FIX 1: Evaluate a calculated field formula for a group of rows, using the SUM
    // of each referenced source field across the group (Excel semantics).
    // Referenced fields that are themselves calculated fields are resolved recursively
    // (cycle guard via visitedNames).
    private static double EvaluateCalculatedFieldOnGroup(
        string formula,
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var rowList = rows is IReadOnlyList<IReadOnlyList<ScalarValue>> list ? list : rows.ToList();
        return EvaluateCalculatedFieldOnGroupCore(formula, rowList, pivotTable, headers, visitedNames: null);
    }

    private static double EvaluateCalculatedFieldOnGroupCore(
        string formula,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        HashSet<string>? visitedNames)
    {
        return PivotCalculatedExpressionEvaluator.Evaluate(formula, name =>
        {
            // Check if this name refers to another calculated field (recursive resolution)
            var nestedCalc = FindCalculatedField(pivotTable, name);
            if (nestedCalc is not null)
            {
                // Guard against infinite recursion / cycles
                if (visitedNames is not null && visitedNames.Contains(nestedCalc.Name, StringComparer.OrdinalIgnoreCase))
                    return 0;
                var visited = visitedNames is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { nestedCalc.Name }
                    : new HashSet<string>(visitedNames, StringComparer.OrdinalIgnoreCase) { nestedCalc.Name };
                return EvaluateCalculatedFieldOnGroupCore(nestedCalc.Formula, rows, pivotTable, headers, visited);
            }

            // Otherwise resolve as source column SUM
            var index = FindOrdinalIgnoreCaseIndex(headers, name);
            if (index < 0)
                return 0;
            var sum = 0d;
            foreach (var row in rows)
            {
                if (index < row.Count && HasNumericValue(row[index]))
                    sum += Number(row[index]);
            }
            return sum;
        });
    }

    private static int FindRankedItemIndex(IReadOnlyList<(string Item, double Value)> items, string value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index].Item, value, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    // Returns the rows matching the parent prefix (key minus its last value).
    // If the key has only one value (outermost level), the parent is the grand total so
    // grandTotalRows is returned. prefixRowsByLevel[level k] maps prefix keys of length k+1.
    private static IEnumerable<IReadOnlyList<ScalarValue>> ComputeParentPrefixRows(
        PivotKey key,
        Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[] prefixRowsByLevel,
        IReadOnlyList<IReadOnlyList<ScalarValue>> grandTotalRows)
    {
        var depth = key.Values.Count; // e.g. 2 for ["East","Q1"]
        if (depth <= 1)
            return grandTotalRows; // outermost → parent is grand total

        // parent prefix length = depth - 1  → sits at prefixRowsByLevel[depth-2]
        var parentLevel = depth - 2;
        if (parentLevel < 0 || parentLevel >= prefixRowsByLevel.Length)
            return grandTotalRows;

        var parentKey = new PivotKey(key.Values.Take(depth - 1).ToArray());
        return prefixRowsByLevel[parentLevel].TryGetValue(parentKey, out var parentRows)
            ? parentRows
            : grandTotalRows;
    }

    // Returns the rows matching the parent column prefix (column key minus its last value),
    // restricted to the current row group's rows. Used for % of Parent Column Total.
    // If the column key has only one level, the parent is all rows in the same row group.
    private static IEnumerable<IReadOnlyList<ScalarValue>>? ComputeParentColumnRows(
        PivotKey columnKey,
        Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>>[] colPrefixRowsByLevelAll,
        IEnumerable<IReadOnlyList<ScalarValue>> rowGroupRows,
        IReadOnlyList<PivotFieldModel> columnFields,
        IEnumerable<IReadOnlyList<ScalarValue>> allSubtotalRows)
    {
        if (colPrefixRowsByLevelAll.Length == 0)
            return null; // single column level — parent column = row total, use fallback

        var colDepth = columnKey.Values.Count;
        if (colDepth <= 1)
            return null; // outermost column → parent is row total (null = use fallback RowTotalRows)

        // parent column prefix length = colDepth - 1 → sits at colPrefixRowsByLevelAll[colDepth-2]
        var parentColLevel = colDepth - 2;
        if (parentColLevel < 0 || parentColLevel >= colPrefixRowsByLevelAll.Length)
            return null;

        var parentColKey = new PivotKey(columnKey.Values.Take(colDepth - 1).ToArray());
        if (!colPrefixRowsByLevelAll[parentColLevel].TryGetValue(parentColKey, out var parentColAllRows))
            return null;

        // Restrict the global parent-column rows to just the current row group's rows
        var parentColRowSet = new HashSet<IReadOnlyList<ScalarValue>>(parentColAllRows);
        return allSubtotalRows.Where(row => parentColRowSet.Contains(row));
    }

    private static int FindOrdinalIgnoreCaseIndex(IReadOnlyList<string> items, string value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index], value, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static double EvaluateCalculatedItem(
        string formula,
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        return PivotCalculatedExpressionEvaluator.Evaluate(formula, name =>
        {
            var group = FindCalculatedItemGroup(groups, name);
            return group is null ? 0 : AggregateDouble(group, dataField, pivotTable, headers);
        });
    }

    private static IGrouping<PivotKey, IReadOnlyList<ScalarValue>>? FindCalculatedItemGroup(
        IReadOnlyList<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> groups,
        string itemName)
    {
        foreach (var candidate in groups)
        {
            // FIX 3: Use OrdinalIgnoreCase for item identity comparisons
            if (candidate.Key.Values.Count > 0 &&
                string.Equals(candidate.Key.Values[0], itemName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
