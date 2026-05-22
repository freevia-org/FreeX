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

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> sumFlat = sumRange is not null ? sumRange.Flatten() : rangeFlat;

        double total = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < sumFlat.Count ? sumFlat[i] : BlankValue.Instance;
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
        foreach (var v in rangeArg.Flatten())
            if (MatchesCriteria(v, criteria))
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

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> avgFlat = avgRange is not null ? avgRange.Flatten() : rangeFlat;

        double total = 0;
        int count = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < avgFlat.Count ? avgFlat[i] : BlankValue.Instance;
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
        var sumFlat = sumRange.Flatten();
        int len = sumFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(sumRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (sumFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(sumFlat[i], out double value)) total += value;
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        RangeValue? firstRange = null;
        for (int p = 0; p < pairCount; p++)
        {
            if (args[p * 2] is ErrorValue rangeError) return rangeError;
            if (args[p * 2] is not RangeValue cr) return ErrorValue.Value;
            firstRange ??= cr;
            if (!SameShape(firstRange, cr)) return ErrorValue.Value;
            if (args[p * 2 + 1] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[p * 2 + 1]);
        }
        int len = pairs[0].Flat.Count;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include) count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue Averageifs2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue avgRangeError) return avgRangeError;
        if (args[0] is not RangeValue avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var avgFlat = avgRange.Flatten();
        int len = avgFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(avgRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (avgFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(avgFlat[i], out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }
}
