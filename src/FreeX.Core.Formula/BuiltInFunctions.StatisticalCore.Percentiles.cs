using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue PercentileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => PercentileIncScalar(rv, kValue));
        return PercentileIncScalar(rv, args[1]);
    }

    private static ScalarValue PercentileIncScalar(RangeValue rv, ScalarValue kValue)
    {
        double k = ToNumber(kValue);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!;
        sorted.Sort();
        if (sorted.Count == 0) return ErrorValue.Num;
        double rank = k * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue PercentileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => PercentileExcScalar(rv, kValue));
        return PercentileExcScalar(rv, args[1]);
    }

    private static ScalarValue PercentileExcScalar(RangeValue rv, ScalarValue kValue)
    {
        double k = ToNumber(kValue);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!;
        sorted.Sort();
        int n = sorted.Count;
        if (n == 0) return ErrorValue.Num;
        double rank = k * (n + 1) - 1;
        if (rank < 0 || rank >= n) return ErrorValue.Num;
        int lo = (int)rank;
        if (lo >= n - 1) return NumberResult(sorted[n - 1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue quartRange) return MapUnaryTextRange(quartRange, quartValue => QuartileIncScalar(rv, quartValue));
        return QuartileIncScalar(rv, args[1]);
    }

    private static ScalarValue QuartileIncScalar(RangeValue rv, ScalarValue quartValue)
    {
        double rawQuart = ToNumber(quartValue);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!;
        sorted.Sort();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return NumberResult(sorted[0]);
        if (quart == 4) return NumberResult(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue quartRange) return MapUnaryTextRange(quartRange, quartValue => QuartileExcScalar(rv, quartValue));
        return QuartileExcScalar(rv, args[1]);
    }

    private static ScalarValue QuartileExcScalar(RangeValue rv, ScalarValue quartValue)
    {
        double rawQuart = ToNumber(quartValue);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 1 || quart > 3) return ErrorValue.Num;
        return PercentileExcScalar(rv, new NumberValue(quart / 4.0));
    }

    private static ScalarValue Geomean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        var logSum = 0.0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            logSum += Math.Log(value);
        }
        return NumberResult(Math.Exp(logSum / nums.Count));
    }

    private static ScalarValue Harmean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        double recSum = 0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            recSum += 1.0 / value;
        }
        return NumberResult(nums.Count / recSum);
    }

    private static ScalarValue Avedev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Average(x => Math.Abs(x - mean)));
    }

    private static ScalarValue ModeSngl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;

        var freq = new Dictionary<double, int>();
        var order = new List<double>();
        foreach (var value in nums!)
        {
            if (!freq.ContainsKey(value)) order.Add(value);
            freq[value] = freq.GetValueOrDefault(value) + 1;
        }

        if (freq.Count == 0) return ErrorValue.NA;
        int maxFreq = freq.Values.Max();
        if (maxFreq < 2) return ErrorValue.NA;
        foreach (var key in order)
            if (freq[key] == maxFreq) return NumberResult(key);
        return ErrorValue.NA;
    }

    private static ScalarValue PercentrankInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var sigArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[1], sigArg, (xValue, sigValue) => PercentrankIncScalar(rv, xValue, sigValue));
    }

    private static ScalarValue PercentrankIncScalar(RangeValue rv, ScalarValue xValue, ScalarValue sigValue)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = sigValue is not BlankValue ? ToNumber(sigValue) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!;
        sorted.Sort();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;

        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        double pctRank;
        if (equal > 0)
        {
            pctRank = n == 1 ? 1.0 : (double)below / (n - 1);
        }
        else
        {
            // Excel interpolates between adjacent values when x is not in the array
            // but lies between sorted[0] and sorted[^1]. Find the largest index where
            // sorted[i] < x, then interpolate the rank between i and i+1.
            int lo = below - 1;
            if (lo < 0 || lo >= n - 1) return ErrorValue.NA;
            double lower = sorted[lo];
            double upper = sorted[lo + 1];
            double frac = upper > lower ? (x - lower) / (upper - lower) : 0.0;
            pctRank = ((double)lo + frac) / (n - 1);
        }

        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue PercentrankExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var sigArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[1], sigArg, (xValue, sigValue) => PercentrankExcScalar(rv, xValue, sigValue));
    }

    private static ScalarValue PercentrankExcScalar(RangeValue rv, ScalarValue xValue, ScalarValue sigValue)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = sigValue is not BlankValue ? ToNumber(sigValue) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!;
        sorted.Sort();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;

        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        double pctRank;
        if (equal > 0)
        {
            pctRank = (below + 1.0) / (n + 1.0);
        }
        else
        {
            int lo = below - 1;
            if (lo < 0 || lo >= n - 1) return ErrorValue.NA;
            double lower = sorted[lo];
            double upper = sorted[lo + 1];
            double frac = upper > lower ? (x - lower) / (upper - lower) : 0.0;
            pctRank = (lo + 1.0 + frac) / (n + 1.0);
        }

        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }
}
