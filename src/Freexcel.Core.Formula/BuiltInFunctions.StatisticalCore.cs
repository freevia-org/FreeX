using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core aggregate, ranking, descriptive-statistical, and regression functions.

    private static ScalarValue Sum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) total += value;
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        return NumberResult(total);
    }

    private static ScalarValue Average(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    total += value;
                    count++;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                count++;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue Min(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (min is null || value < min) min = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (min is null || value < min) min = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (min is null || val < min) min = val;
        }
        return min.HasValue ? NumberResult(min.Value) : new NumberValue(0);
    }

    private static ScalarValue Max(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (max is null || value > max) max = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (max is null || value > max) max = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (max is null || val > max) max = val;
        }
        return max.HasValue ? NumberResult(max.Value) : new NumberValue(0);
    }

    private static ScalarValue Count(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out _, out var refError)) count++;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (TryDirectTextNumber(direct, out _)) count++;
                continue;
            }
            if (arg is NumberValue or BoolValue or DateTimeValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue CountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is not BlankValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue Large(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderByDescending(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderBy(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var range = args[1] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = args.Count > 2 ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        int order  = (int)rawOrder;

        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;

        if (!nums!.Contains(number)) return ErrorValue.NA;

        int rank;
        if (order == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double variance = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(variance));
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
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        int count = range.Flatten().Count(v => v is BlankValue || v is TextValue { Value.Length: 0 });
        return new NumberValue(count);
    }

    private static ScalarValue VarS(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectNumbers(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (int i = start; i < args.Count; i++)
        {
            var a = args[i];
            if (a is ErrorValue e) return (null, e);
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) list.Add(value);
                else if (refError is not null) return (null, refError);
            }
            else if (a is NumberValue nv) list.Add(nv.Value);
            else if (a is BoolValue bv) list.Add(bv.Value ? 1.0 : 0.0);
            else if (a is DateTimeValue dt) list.Add(dt.Value);
            else if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return (null, ErrorValue.Value);
                list.Add(value);
            }
        }
        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbers(RangeValue range)
    {
        var list = new List<double>();
        foreach (var value in range.Flatten())
        {
            if (value is ErrorValue e) return (null, e);
            if (value is NumberValue n) list.Add(n.Value);
            else if (value is DateTimeValue d) list.Add(d.Value);
        }
        return (list, null);
    }

    private static ScalarValue PercentileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
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
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
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
        double rawQuart = ToNumber(args[1]);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return NumberResult(sorted[0]);
        if (quart == 4) return NumberResult(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
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
        double x = ToNumber(args[1]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(v => v).ToList();
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

    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv1 = args[0] is RangeValue range1
            ? range1
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e1) return e1;
        var rv2 = args[1] is RangeValue range2
            ? range2
            : SingleCellArray(args[1]);
        var (xs, xErr) = CollectRangeNumbers(rv1);
        if (xErr is not null) return xErr;
        var (ys, yErr) = CollectRangeNumbers(rv2);
        if (yErr is not null) return yErr;
        if (xs!.Count != ys!.Count) return ErrorValue.NA;
        int n = xs.Count;
        if (n < 2) return ErrorValue.DivByZero;
        double xMean = xs.Average();
        double yMean = ys.Average();
        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean, dy = ys[i] - yMean;
            cov  += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        if (varX == 0 || varY == 0) return ErrorValue.DivByZero;
        return NumberResult(cov / Math.Sqrt(varX * varY));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        var knownY = args[1] is RangeValue knownYRange
            ? knownYRange
            : SingleCellArray(args[1]);
        if (args[2] is ErrorValue e2) return e2;
        var knownX = args[2] is RangeValue knownXRange
            ? knownXRange
            : SingleCellArray(args[2]);
        double x    = ToNumber(args[0]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        var (ys, yErr) = CollectRangeNumbers(knownY);
        if (yErr is not null) return yErr;
        var (xs, xErr) = CollectRangeNumbers(knownX);
        if (xErr is not null) return xErr;
        if (xs!.Count != ys!.Count) return ErrorValue.NA;
        int n = xs.Count;
        if (n < 2) return ErrorValue.DivByZero;
        double xMean = xs.Average();
        double yMean = ys.Average();
        double sXX = 0, sXY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean;
            sXX += dx * dx;
            sXY += dx * (ys[i] - yMean);
        }
        if (sXX == 0) return ErrorValue.DivByZero;
        double b = sXY / sXX;
        double a = yMean - b * xMean;
        return NumberResult(a + b * x);
    }

    private static ScalarValue RankEq(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Rank(args, ctx);

    private static ScalarValue RankAvg(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = args.Count > 2 ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        if (!nums!.Contains(number)) return ErrorValue.NA;
        int betterCount = rawOrder == 0 ? nums.Count(x => x > number) : nums.Count(x => x < number);
        int tieCount = nums.Count(x => x == number);
        return new NumberValue(betterCount + 1 + (tieCount - 1) / 2.0);
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
        double mean = nums.Average();
        return NumberResult(nums.Sum(x => (x - mean) * (x - mean)));
    }
}
