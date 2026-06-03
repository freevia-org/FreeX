using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B1: Normal distribution ───────────────────────────────────────────────

    private static ScalarValue NormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], NormDistScalar);
    }

    private static ScalarValue NormDistScalar(ScalarValue xValue, ScalarValue meanValue, ScalarValue stdevValue, ScalarValue cumulativeValue)
    {
        double mean = ToNumber(meanValue), stdev = ToNumber(stdevValue);
        bool cum = ToBool(cumulativeValue);
        return NormDistScalar(xValue, mean, stdev, cum);
    }

    private static ScalarValue NormDistScalar(ScalarValue xValue, double mean, double stdev, bool cum)
    {
        double x = ToNumber(xValue);
        if (stdev <= 0) return ErrorValue.Num;
        double z = (x - mean) / stdev;
        return cum ? NumberResult(NormSCdf(z)) : NumberResult(NormSPdf(z) / stdev);
    }

    private static ScalarValue NormInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], NormInvScalar);
    }

    private static ScalarValue NormInvScalar(ScalarValue probabilityValue, ScalarValue meanValue, ScalarValue stdevValue)
    {
        double mean = ToNumber(meanValue), stdev = ToNumber(stdevValue);
        return NormInvScalar(probabilityValue, mean, stdev);
    }

    private static ScalarValue NormInvScalar(ScalarValue probabilityValue, double mean, double stdev)
    {
        double prob = ToNumber(probabilityValue);
        if (stdev <= 0 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(NormSInv(prob) * stdev + mean);
    }

    private static ScalarValue NormSDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], NormSDistScalar);
    }

    private static ScalarValue NormSDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => NormSDistScalar(value, cum: true));
        return NormSDistScalar(args[0], cum: true);
    }

    private static ScalarValue NormSDistScalar(ScalarValue zValue, ScalarValue cumulativeValue)
    {
        bool cum = ToBool(cumulativeValue);
        return NormSDistScalar(zValue, cum);
    }

    private static ScalarValue NormSDistScalar(ScalarValue zValue, bool cum)
    {
        double z = ToNumber(zValue);
        return cum ? NumberResult(NormSCdf(z)) : NumberResult(NormSPdf(z));
    }

    private static ScalarValue NormSInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, NormSInvScalar);
        return NormSInvScalar(args[0]);
    }

    private static ScalarValue NormSInvScalar(ScalarValue probabilityValue)
    {
        double prob = ToNumber(probabilityValue);
        if (prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(NormSInv(prob));
    }

    private static ScalarValue Phi(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, PhiScalar);
        return PhiScalar(args[0]);
    }

    private static ScalarValue PhiScalar(ScalarValue xValue)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        return NumberResult(NormSPdf(x));
    }

    private static ScalarValue Gauss(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, GaussScalar);
        return GaussScalar(args[0]);
    }

    private static ScalarValue GaussScalar(ScalarValue zValue)
    {
        double z = ToNumber(zValue);
        if (!double.IsFinite(z)) return ErrorValue.Num;
        return NumberResult(NormSCdf(z) - 0.5);
    }

    private static ScalarValue Standardize(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], StandardizeScalar);
    }

    private static ScalarValue StandardizeScalar(ScalarValue xValue, ScalarValue meanValue, ScalarValue stdevValue)
    {
        double mean = ToNumber(meanValue), stdev = ToNumber(stdevValue);
        return StandardizeScalar(xValue, mean, stdev);
    }

    private static ScalarValue StandardizeScalar(ScalarValue xValue, double mean, double stdev)
    {
        double x = ToNumber(xValue);
        if (stdev <= 0) return ErrorValue.Num;
        return NumberResult((x - mean) / stdev);
    }


    private static ScalarValue ZTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var (nums, err) = args[0] is RangeValue range
            ? CollectRangeNumbers(range)
            : CollectNumbers([args[0]]);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.NA;

        double hypothesizedMean = ToNumber(args[1]);
        double sigma;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            sigma = ToNumber(args[2]);
            if (sigma <= 0 || !double.IsFinite(sigma)) return ErrorValue.Num;
        }
        else
        {
            if (nums.Count < 2) return ErrorValue.DivByZero;
            double sampleMean = nums.Average();
            double variance = nums.Sum(value => (value - sampleMean) * (value - sampleMean)) / (nums.Count - 1);
            sigma = Math.Sqrt(variance);
            if (sigma == 0) return ErrorValue.DivByZero;
        }

        double z = (nums.Average() - hypothesizedMean) / (sigma / Math.Sqrt(nums.Count));
        return NumberResult(1.0 - NormSCdf(z));
    }

    private static ScalarValue LognormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], LognormDistScalar);
    }

    private static ScalarValue LognormDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], (x, mean, stdev) => LognormDistScalar(x, mean, stdev, new BoolValue(true)));
    }

    private static ScalarValue LognormDistScalar(ScalarValue xValue, ScalarValue meanValue, ScalarValue stdevValue, ScalarValue cumulativeValue)
    {
        double mean = ToNumber(meanValue), stdev = ToNumber(stdevValue);
        bool cum = ToBool(cumulativeValue);
        return LognormDistScalar(xValue, mean, stdev, cum);
    }

    private static ScalarValue LognormDistScalar(ScalarValue xValue, double mean, double stdev, bool cum)
    {
        double x = ToNumber(xValue);
        if (x <= 0 || stdev <= 0) return ErrorValue.Num;
        double z = (Math.Log(x) - mean) / stdev;
        if (cum) return NumberResult(NormSCdf(z));
        return NumberResult(NormSPdf(z) / (x * stdev));
    }

    private static ScalarValue LognormInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], LognormInvScalar);
    }

    private static ScalarValue LognormInvScalar(ScalarValue probabilityValue, ScalarValue meanValue, ScalarValue stdevValue)
    {
        double mean = ToNumber(meanValue), stdev = ToNumber(stdevValue);
        return LognormInvScalar(probabilityValue, mean, stdev);
    }

    private static ScalarValue LognormInvScalar(ScalarValue probabilityValue, double mean, double stdev)
    {
        double prob = ToNumber(probabilityValue);
        if (prob <= 0 || prob >= 1 || stdev <= 0) return ErrorValue.Num;
        return NumberResult(Math.Exp(NormSInv(prob) * stdev + mean));
    }
}
