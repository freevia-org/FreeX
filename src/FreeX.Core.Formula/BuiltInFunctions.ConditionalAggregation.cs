using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Conditional aggregation functions.

    private static ScalarValue Sumif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue sumRangeError) return sumRangeError;
        if (args.Count > 2 && args[2] is not RangeValue) return ErrorValue.Value;
        RangeValue? sumRange = args.Count > 2 ? (RangeValue)args[2] : null;
        var criteriaMatcher = CompileCriteria(criteria);

        double total = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (criteriaMatcher.Matches(CellAtFlatIndex(rangeArg, i)))
            {
                var sv = sumRange is not null
                    ? CellAtRelativeOffsetOrContext(sumRange, i / rangeArg.ColCount, i % rangeArg.ColCount, ctx)
                    : CellAtFlatIndex(rangeArg, i);
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) total += value;
                else if (sv is BlankValue) { /* skip */ }
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        var criteriaMatcher = CompileCriteria(criteria);

        return new NumberValue(CountMatchingCells(rangeArg, criteriaMatcher));
    }

    private static ScalarValue Averageif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue avgRangeError) return avgRangeError;
        if (args.Count > 2 && args[2] is not RangeValue) return ErrorValue.Value;
        RangeValue? avgRange = args.Count > 2 ? (RangeValue)args[2] : null;
        var criteriaMatcher = CompileCriteria(criteria);

        double total = 0;
        int count = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (criteriaMatcher.Matches(CellAtFlatIndex(rangeArg, i)))
            {
                var sv = avgRange is not null
                    ? CellAtRelativeOffsetOrContext(avgRange, i / rangeArg.ColCount, i % rangeArg.ColCount, ctx)
                    : CellAtFlatIndex(rangeArg, i);
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    private static ScalarValue Sumifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue sumRangeError) return sumRangeError;
        if (args[0] is not RangeValue sumRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, sumRange, out var criteriaSet) is { } pairError)
            return pairError;

        double total = 0;
        for (int r = 0; r < sumRange.RowCount; r++)
        {
            for (int c = 0; c < sumRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var sumValue = sumRange.Cells[r, c];
                if (sumValue is ErrorValue e) return e;
                if (TryCellNumber(sumValue, out double value)) total += value;
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        if (TryCreateConditionalCriteriaSet(args, 0, pairCount, null, out var criteriaSet) is { } pairError)
            return pairError;

        int count = 0;
        var shapeRange = criteriaSet.ShapeRange;
        for (int r = 0; r < shapeRange.RowCount; r++)
        {
            for (int c = 0; c < shapeRange.ColCount; c++)
            {
                if (criteriaSet.Includes(r, c)) count++;
            }
        }
        return new NumberValue(count);
    }

    private static ScalarValue Averageifs2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue avgRangeError) return avgRangeError;
        if (args[0] is not RangeValue avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        int pairCount = (args.Count - 1) / 2;
        if (TryCreateConditionalCriteriaSet(args, 1, pairCount, avgRange, out var criteriaSet) is { } pairError)
            return pairError;

        double total = 0;
        int count = 0;
        for (int r = 0; r < avgRange.RowCount; r++)
        {
            for (int c = 0; c < avgRange.ColCount; c++)
            {
                if (!criteriaSet.Includes(r, c)) continue;

                var avgValue = avgRange.Cells[r, c];
                if (avgValue is ErrorValue e) return e;
                if (TryCellNumber(avgValue, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    private readonly struct ConditionalCriteriaPair
    {
        public ConditionalCriteriaPair(RangeValue range, CriteriaMatcher criteria)
        {
            Range = range;
            Criteria = criteria;
        }

        public RangeValue Range { get; }
        public CriteriaMatcher Criteria { get; }
    }

    private readonly struct ConditionalCriteriaSet
    {
        private readonly ConditionalCriteriaPair[] _pairs;

        public ConditionalCriteriaSet(RangeValue shapeRange, ConditionalCriteriaPair[] pairs)
        {
            ShapeRange = shapeRange;
            _pairs = pairs;
        }

        public RangeValue ShapeRange { get; }

        public bool Includes(int row, int col)
        {
            for (int i = 0; i < _pairs.Length; i++)
            {
                var pair = _pairs[i];
                if (!pair.Criteria.Matches(pair.Range.Cells[row, col]))
                    return false;
            }

            return true;
        }
    }

    private static ErrorValue? TryCreateConditionalCriteriaSet(
        IReadOnlyList<ScalarValue> args,
        int firstCriteriaRangeIndex,
        int pairCount,
        RangeValue? requiredShape,
        out ConditionalCriteriaSet criteriaSet)
    {
        criteriaSet = default;
        var pairs = new ConditionalCriteriaPair[pairCount];
        var shapeRange = requiredShape;

        for (int p = 0; p < pairCount; p++)
        {
            int rangeIndex = firstCriteriaRangeIndex + p * 2;
            int criteriaIndex = rangeIndex + 1;

            if (args[rangeIndex] is ErrorValue rangeError) return rangeError;
            if (args[rangeIndex] is not RangeValue criteriaRange) return ErrorValue.Value;

            shapeRange ??= criteriaRange;
            if (!SameShape(shapeRange, criteriaRange)) return ErrorValue.Value;

            var criteria = args[criteriaIndex];
            if (criteria is ErrorValue criteriaError) return criteriaError;
            pairs[p] = new ConditionalCriteriaPair(criteriaRange, CompileCriteria(criteria));
        }

        criteriaSet = new ConditionalCriteriaSet(shapeRange!, pairs);
        return null;
    }

    private static int CountMatchingCells(RangeValue range, CriteriaMatcher criteria)
    {
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                if (criteria.Matches(range.Cells[r, c]))
                    count++;
        return count;
    }

    private static int FlatCount(RangeValue range) => range.RowCount * range.ColCount;

    private static ScalarValue CellAtFlatIndex(RangeValue range, int index)
    {
        int row = index / range.ColCount;
        int col = index - (row * range.ColCount);
        return range.Cells[row, col];
    }

    private static ScalarValue CellAtRelativeOffsetOrContext(RangeValue range, int rowOffset, int colOffset, IEvalContext ctx)
    {
        if (rowOffset < range.RowCount && colOffset < range.ColCount)
            return range.Cells[rowOffset, colOffset];

        var row = range.StartRow + (uint)rowOffset;
        var col = range.StartCol + (uint)colOffset;
        return !string.IsNullOrEmpty(range.SheetName)
            ? ctx.GetCellValue(range.SheetName, row, col)
            : ctx.GetCellValue(row, col);
    }
}
