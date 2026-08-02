using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary arithmetic
    // result (FormulaEvaluator.Operators.cs), mirroring the fix already applied to Sum(),
    // Sumproduct(), Average() and Product(): round the accumulated total the same way SUM
    // would, then round the quotient the same way the '/' operator would. Used for the mean in
    // every VAR/STDEV variant below so e.g. VARP(0.1,0.1,0.1) sees the same rounded mean AVERAGE()
    // would produce (exactly 0.1) instead of raw IEEE-754 summation noise, and its sum-of-squared-
    // deviations comes out exactly 0 like Excel's, not a residual ~1.9e-34.
    private static double RoundedMean(IReadOnlyList<double> nums)
    {
        double total = 0;
        foreach (var n in nums) total += n;
        double roundedTotal = FormulaEvaluator.RoundTo15SignificantDigits(total);
        return FormulaEvaluator.RoundTo15SignificantDigits(roundedTotal / nums.Count);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = RoundedMean(nums);
        double variance = FormulaEvaluator.RoundTo15SignificantDigits(
            nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1));
        return NumberResult(Math.Sqrt(variance));
    }

    private static ScalarValue StdevA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarA(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static ScalarValue StdevPA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarPA(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count == 0) return ErrorValue.Num;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return NumberResult(nums[mid]);
        return NumberResult((nums[mid - 1] + nums[mid]) / 2.0);
    }

    private static ScalarValue Countblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var range = args[0] is RangeValue rv ? rv : SingleCellArray(args[0]);
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is BlankValue || value is TextValue { Value.Length: 0 }) count++;
            }
        }

        return new NumberValue(count);
    }

    private static ScalarValue VarS(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = RoundedMean(list);
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(
            list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1)));
    }

    private static ScalarValue VarA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectAValues(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = RoundedMean(list);
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(
            list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1)));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = RoundedMean(list);
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(
            list.Sum(x => (x - mean) * (x - mean)) / list.Count));
    }

    private static ScalarValue VarPA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectAValues(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = RoundedMean(list);
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(
            list.Sum(x => (x - mean) * (x - mean)) / list.Count));
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }
    private static ScalarValue Devsq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var nums = new List<double>();
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is RangeValue rv)
            {
                var (rangeNums, rangeError) = CollectRangeNumbers(rv);
                if (rangeError is not null) return rangeError;
                nums.AddRange(rangeNums!);
            }
            else if (arg is NumberValue nv) nums.Add(nv.Value);
            else if (arg is DateTimeValue dt) nums.Add(dt.Value);
            else if (arg is BoolValue bv) nums.Add(bv.Value ? 1 : 0);
            else if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                nums.Add(value);
            }
        }
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = RoundedMean(nums);
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(nums.Sum(x => (x - mean) * (x - mean))));
    }
}
