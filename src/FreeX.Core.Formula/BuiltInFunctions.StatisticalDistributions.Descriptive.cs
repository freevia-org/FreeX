using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B3: Descriptive statistics ────────────────────────────────────────────

    private static ScalarValue Skew(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 3) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / (n - 1);
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m3 = nums.Sum(x => Math.Pow((x - mean) / s, 3));
        return NumberResult(m3 * n / ((n - 1.0) * (n - 2.0)));
    }

    private static ScalarValue SkewP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 1) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / n;
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m3 = nums.Sum(x => Math.Pow((x - mean) / s, 3));
        return NumberResult(m3 / n);
    }

    private static ScalarValue Kurt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 4) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / (n - 1);
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m4 = nums.Sum(x => Math.Pow((x - mean) / s, 4));
        double kurtosis = (double)n * (n + 1) / ((n - 1.0) * (n - 2) * (n - 3)) * m4
                          - 3.0 * (n - 1) * (n - 1) / ((n - 2.0) * (n - 3));
        return NumberResult(kurtosis);
    }

    private static ScalarValue Frequency(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;

        // Collect data values — allow scalar or range
        // (TryCellNumber coerces DateTimeValue to its serial number too, matching every other
        // numeric aggregate — a hand-rolled `is NumberValue` check would silently drop date cells.)
        // An error cell anywhere in data_array or bins_array propagates (Excel: the whole result
        // becomes that error) rather than being silently dropped like a blank/text cell — mirrors
        // CountRangeNumbers (BuiltInFunctions.StatisticalCore.Helpers.cs).
        var dataList = new List<double>();
        if (args[0] is RangeValue rvd)
        {
            foreach (var v in rvd.Flatten())
            {
                if (v is ErrorValue dve) return dve;
                if (TryCellNumber(v, out double dv)) dataList.Add(dv);
            }
        }
        else if (TryCellNumber(args[0], out double dva)) dataList.Add(dva);

        // Collect bins (sorted)
        var binsList = new List<double>();
        if (args[1] is RangeValue rvb)
        {
            foreach (var v in rvb.Flatten())
            {
                if (v is ErrorValue bve) return bve;
                if (TryCellNumber(v, out double bv)) binsList.Add(bv);
            }
        }
        else if (TryCellNumber(args[1], out double bva)) binsList.Add(bva);

        // Excel processes bins_array positionally, in the order the user supplied it — it does
        // NOT sort bins_array before binning. Supplying an unsorted bins_array yields different
        // (and, per Microsoft's own guidance, order-dependent) results, matched here intentionally.
        int binsCount = binsList.Count;
        int[] counts = new int[binsCount + 1];
        foreach (double d in dataList)
        {
            bool placed = false;
            for (int i = 0; i < binsCount; i++)
            {
                if (d <= binsList[i]) { counts[i]++; placed = true; break; }
            }
            if (!placed) counts[binsCount]++;
        }

        var result = new ScalarValue[binsCount + 1, 1];
        for (int i = 0; i <= binsCount; i++) result[i, 0] = new NumberValue(counts[i]);
        return new RangeValue(result);
    }

    private static ScalarValue ConfidenceNorm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], ConfidenceNormScalar);
    }

    private static ScalarValue ConfidenceNormScalar(ScalarValue alphaValue, ScalarValue stdevValue, ScalarValue sizeValue)
    {
        return ConfidenceNormScalar(alphaValue, ToNumber(stdevValue), ToNumber(sizeValue));
    }

    private static ScalarValue ConfidenceNormScalar(ScalarValue alphaValue, double stdev, double size)
    {
        double alpha = ToNumber(alphaValue);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 1) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        return NumberResult(NormSInv(1.0 - alpha / 2.0) * stdev / Math.Sqrt(n));
    }

    private static ScalarValue ConfidenceT(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], ConfidenceTScalar);
    }

    private static ScalarValue ConfidenceTScalar(ScalarValue alphaValue, ScalarValue stdevValue, ScalarValue sizeValue)
    {
        return ConfidenceTScalar(alphaValue, ToNumber(stdevValue), ToNumber(sizeValue));
    }

    private static ScalarValue ConfidenceTScalar(ScalarValue alphaValue, double stdev, double size)
    {
        double alpha = ToNumber(alphaValue);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 2) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        double df = n - 1;
        return NumberResult(TInv(1.0 - alpha / 2.0, df) * stdev / Math.Sqrt(n));
    }
}
