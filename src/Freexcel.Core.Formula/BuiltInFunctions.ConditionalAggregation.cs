using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
        RangeValue? sumRange = args.Count > 2 ? args[2] as RangeValue : null;

        double total = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (MatchesCriteria(CellAtFlatIndex(rangeArg, i), criteria))
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

        int count = 0;
        for (int r = 0; r < rangeArg.RowCount; r++)
            for (int c = 0; c < rangeArg.ColCount; c++)
                if (MatchesCriteria(rangeArg.Cells[r, c], criteria))
                    count++;
        return new NumberValue(count);
    }

    private static ScalarValue Averageif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue avgRangeError) return avgRangeError;
        RangeValue? avgRange = args.Count > 2 ? args[2] as RangeValue : null;

        double total = 0;
        int count = 0;
        int len = FlatCount(rangeArg);
        for (int i = 0; i < len; i++)
        {
            if (MatchesCriteria(CellAtFlatIndex(rangeArg, i), criteria))
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
        var pairs = new (RangeValue Range, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(sumRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr, args[2 + p * 2]);
        }
        double total = 0;
        for (int r = 0; r < sumRange.RowCount; r++)
        {
            for (int c = 0; c < sumRange.ColCount; c++)
            {
                bool include = true;
                foreach (var (criteriaRange, pairCriteria) in pairs)
                {
                    if (!MatchesCriteria(criteriaRange.Cells[r, c], pairCriteria))
                        { include = false; break; }
                }
                if (include)
                {
                    var sumValue = sumRange.Cells[r, c];
                    if (sumValue is ErrorValue e) return e;
                    if (TryCellNumber(sumValue, out double value)) total += value;
                }
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        var pairs = new (RangeValue Range, ScalarValue Criteria)[pairCount];
        RangeValue? firstRange = null;
        for (int p = 0; p < pairCount; p++)
        {
            if (args[p * 2] is ErrorValue rangeError) return rangeError;
            if (args[p * 2] is not RangeValue cr) return ErrorValue.Value;
            firstRange ??= cr;
            if (!SameShape(firstRange, cr)) return ErrorValue.Value;
            if (args[p * 2 + 1] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr, args[p * 2 + 1]);
        }
        int count = 0;
        for (int r = 0; r < firstRange!.RowCount; r++)
        {
            for (int c = 0; c < firstRange.ColCount; c++)
            {
                bool include = true;
                foreach (var (criteriaRange, criteria) in pairs)
                {
                    if (!MatchesCriteria(criteriaRange.Cells[r, c], criteria))
                        { include = false; break; }
                }
                if (include) count++;
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
        var pairs = new (RangeValue Range, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(avgRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr, args[2 + p * 2]);
        }
        double total = 0;
        int count = 0;
        for (int r = 0; r < avgRange.RowCount; r++)
        {
            for (int c = 0; c < avgRange.ColCount; c++)
            {
                bool include = true;
                foreach (var (criteriaRange, pairCriteria) in pairs)
                {
                    if (!MatchesCriteria(criteriaRange.Cells[r, c], pairCriteria))
                        { include = false; break; }
                }
                if (include)
                {
                    var avgValue = avgRange.Cells[r, c];
                    if (avgValue is ErrorValue e) return e;
                    if (TryCellNumber(avgValue, out double value)) { total += value; count++; }
                }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
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
