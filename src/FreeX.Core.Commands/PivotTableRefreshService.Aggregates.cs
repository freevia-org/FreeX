using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static bool HasNumericValue(ScalarValue value) =>
        value is NumberValue or DateTimeValue or BoolValue;

    private static bool IsNonBlank(ScalarValue value) =>
        value is not BlankValue;

    private static double Aggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var summaryFunction = dataField.SummaryFunction.AsSpan().Trim();
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
        if (summaryFunction.Equals("min", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : min;
        if (summaryFunction.Equals("max", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : max;
        if (summaryFunction.Equals("product", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : product;
        if (summaryFunction.Equals("stddev", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddevs", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddev.s", StringComparison.OrdinalIgnoreCase))
            return numericCount < 2 ? 0 : Math.Sqrt(Variance(sumSquaredDeviation, numericCount, sample: true));
        if (summaryFunction.Equals("stddevp", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("stddev.p", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : Math.Sqrt(Variance(sumSquaredDeviation, numericCount, sample: false));
        if (summaryFunction.Equals("var", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("vars", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("var.s", StringComparison.OrdinalIgnoreCase))
            return numericCount < 2 ? 0 : Variance(sumSquaredDeviation, numericCount, sample: true);
        if (summaryFunction.Equals("varp", StringComparison.OrdinalIgnoreCase) ||
            summaryFunction.Equals("var.p", StringComparison.OrdinalIgnoreCase))
            return numericCount == 0 ? 0 : Variance(sumSquaredDeviation, numericCount, sample: false);

        return sum;
    }

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
        IEnumerable<IReadOnlyList<ScalarValue>> ColumnTotalRows);

    private static double DisplayAggregate(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotDisplayContext context,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        var value = Aggregate(rows, dataField, pivotTable, headers);
        if (dataField.ShowValuesAs == PivotShowValuesAs.RunningTotalIn)
            return ReferenceEquals(rows, context.GrandTotalRows)
                ? value
                : RunningTotal(rows, context.GrandTotalRows, dataField, pivotTable, headers);
        if (dataField.ShowValuesAs is PivotShowValuesAs.DifferenceFrom or PivotShowValuesAs.PercentDifferenceFrom)
        {
            var baseValue = BaseItemAggregate(context.GrandTotalRows, dataField, pivotTable, headers);
            var difference = value - baseValue;
            return dataField.ShowValuesAs == PivotShowValuesAs.PercentDifferenceFrom
                ? Math.Abs(baseValue) < 0.0000001 ? 0 : difference / baseValue
                : difference;
        }
        if (dataField.ShowValuesAs is PivotShowValuesAs.RankSmallest or PivotShowValuesAs.RankLargest)
            return RankValue(rows, context.GrandTotalRows, dataField, pivotTable, headers);
        if (dataField.ShowValuesAs == PivotShowValuesAs.Index)
        {
            var grandTotal = Aggregate(context.GrandTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var rowTotal = Aggregate(context.RowTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var columnTotal = Aggregate(context.ColumnTotalRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
            var indexDenominator = rowTotal * columnTotal;
            return Math.Abs(indexDenominator) < 0.0000001 ? 0 : value * grandTotal / indexDenominator;
        }

        var denominatorRows = dataField.ShowValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => context.GrandTotalRows,
            PivotShowValuesAs.PercentOfRowTotal => context.RowTotalRows,
            PivotShowValuesAs.PercentOfColumnTotal => context.ColumnTotalRows,
            PivotShowValuesAs.PercentOfParentRowTotal => context.RowTotalRows,
            PivotShowValuesAs.PercentOfParentColumnTotal => context.ColumnTotalRows,
            PivotShowValuesAs.PercentOfParentTotal => context.GrandTotalRows,
            _ => null
        };
        if (denominatorRows is null)
            return value;

        var denominator = Aggregate(denominatorRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
        return Math.Abs(denominator) < 0.0000001 ? 0 : value / denominator;
    }

    private static double RunningTotal(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        IEnumerable<IReadOnlyList<ScalarValue>> totalRows,
        PivotDataFieldModel dataField,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        if (dataField.BaseFieldIndex is not { } baseFieldIndex || !IsValidField(baseFieldIndex, headers.Count))
            return Aggregate(rows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);

        var currentItem = rows.Select(row => KeyText(row[baseFieldIndex])).FirstOrDefault();
        if (currentItem is null)
            return 0;

        var orderedItems = totalRows
            .Select(row => KeyText(row[baseFieldIndex]))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var currentIndex = orderedItems.FindIndex(item => string.Equals(item, currentItem, StringComparison.CurrentCultureIgnoreCase));
        if (currentIndex < 0)
            return 0;

        var included = new HashSet<string>(orderedItems.Take(currentIndex + 1), StringComparer.CurrentCultureIgnoreCase);
        var runningRows = totalRows.Where(row => included.Contains(KeyText(row[baseFieldIndex])));
        return Aggregate(runningRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
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

        var baseRows = totalRows.Where(row =>
            string.Equals(KeyText(row[baseFieldIndex]), dataField.BaseItem, StringComparison.CurrentCultureIgnoreCase));
        return Aggregate(baseRows, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers);
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

        var currentItem = rows.Select(row => KeyText(row[baseFieldIndex])).FirstOrDefault();
        if (currentItem is null)
            return 0;

        var valuesByItem = totalRows
            .GroupBy(row => KeyText(row[baseFieldIndex]), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => (Item: group.Key, Value: Aggregate(group, dataField with { ShowValuesAs = PivotShowValuesAs.None }, pivotTable, headers)))
            .ToList();
        var ordered = dataField.ShowValuesAs == PivotShowValuesAs.RankLargest
            ? valuesByItem.OrderByDescending(item => item.Value).ThenBy(item => item.Item, StringComparer.CurrentCultureIgnoreCase).ToList()
            : valuesByItem.OrderBy(item => item.Value).ThenBy(item => item.Item, StringComparer.CurrentCultureIgnoreCase).ToList();
        var rank = ordered.FindIndex(item => string.Equals(item.Item, currentItem, StringComparison.CurrentCultureIgnoreCase));
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

        if (!string.IsNullOrWhiteSpace(dataField.CalculatedFieldName))
        {
            var calculated = pivotTable.CalculatedFields.FirstOrDefault(field =>
                string.Equals(field.Name, dataField.CalculatedFieldName, StringComparison.OrdinalIgnoreCase));
            if (calculated is not null)
                return new NumberValue(EvaluateCalculatedField(calculated.Formula, row, headers));
        }

        return BlankValue.Instance;
    }

    private static double EvaluateCalculatedField(
        string formula,
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<string> headers)
    {
        return PivotCalculatedExpressionEvaluator.Evaluate(formula, name =>
        {
            var index = headers.ToList().FindIndex(header => string.Equals(header, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < row.Count ? Number(row[index]) : 0;
        });
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
            var group = groups.FirstOrDefault(candidate =>
                candidate.Key.Values.Count > 0 &&
                string.Equals(candidate.Key.Values[0], name, StringComparison.CurrentCultureIgnoreCase));
            return group is null ? 0 : Aggregate(group, dataField, pivotTable, headers);
        });
    }
}
