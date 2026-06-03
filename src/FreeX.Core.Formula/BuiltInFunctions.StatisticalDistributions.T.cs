using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B2: T distribution ────────────────────────────────────────────────────

    private static ScalarValue TDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], TDistScalar);
    }

    private static ScalarValue TDistScalar(ScalarValue xValue, ScalarValue dfValue, ScalarValue cumulativeValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        bool cum = ToBool(cumulativeValue);
        return TDistScalar(xValue, df, cum);
    }

    private static ScalarValue TDistScalar(ScalarValue xValue, double df, bool cum)
    {
        double x = ToNumber(xValue);
        if (df < 1) return ErrorValue.Num;
        return cum ? NumberResult(TCdf(x, df)) : NumberResult(TPdf(x, df));
    }

    private static ScalarValue TDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], TDistRtScalar);
    }

    private static ScalarValue TDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], (xValue, dfValue, tailsValue) =>
        {
            var tails = (int)Math.Truncate(ToNumber(tailsValue));
            return tails switch
            {
                1 => TDistRtScalar(xValue, dfValue),
                2 => TDist2TScalar(xValue, dfValue),
                _ => ErrorValue.Num
            };
        });
    }

    private static ScalarValue TDistRtScalar(ScalarValue xValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return TDistRtScalar(xValue, df);
    }

    private static ScalarValue TDistRtScalar(ScalarValue xValue, double df)
    {
        double x = ToNumber(xValue);
        if (df < 1) return ErrorValue.Num;
        if (x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - TCdf(x, df));
    }

    private static ScalarValue TDist2T(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], TDist2TScalar);
    }

    private static ScalarValue TDist2TScalar(ScalarValue xValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return TDist2TScalar(xValue, df);
    }

    private static ScalarValue TDist2TScalar(ScalarValue xValue, double df)
    {
        double x = ToNumber(xValue);
        if (df < 1) return ErrorValue.Num;
        if (x < 0) return ErrorValue.Num;
        return NumberResult(2.0 * (1.0 - TCdf(x, df)));
    }

    private static ScalarValue TInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], TInvScalar);
    }

    private static ScalarValue TInvScalar(ScalarValue probabilityValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return TInvScalar(probabilityValue, df);
    }

    private static ScalarValue TInvScalar(ScalarValue probabilityValue, double df)
    {
        double prob = ToNumber(probabilityValue);
        if (df < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(TInv(prob, df));
    }

    private static ScalarValue TInv2TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], TInv2TScalar);
    }

    private static ScalarValue TInv2TScalar(ScalarValue probabilityValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return TInv2TScalar(probabilityValue, df);
    }

    private static ScalarValue TInv2TScalar(ScalarValue probabilityValue, double df)
    {
        double prob = ToNumber(probabilityValue);
        if (df < 1 || prob <= 0 || prob > 1) return ErrorValue.Num;
        // T.INV.2T(p, df) returns the positive t s.t. P(|T| > t) = p
        // i.e. the one-tail area is p/2, so we solve TCdf(-t) = p/2
        return NumberResult(TInv(1.0 - prob / 2.0, df));
    }

    private static ScalarValue TTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        var (a, b, err) = CollectPair(args[0], args[1]);
        if (err is not null) return err;
        int tails = (int)Math.Truncate(ToNumber(args[2]));
        int type = (int)Math.Truncate(ToNumber(args[3]));
        if (tails < 1 || tails > 2 || type < 1 || type > 3) return ErrorValue.Num;
        if (a!.Count == 0 || b!.Count == 0) return ErrorValue.NA;

        double t, df;
        if (type == 1) // paired
        {
            if (a.Count != b.Count) return ErrorValue.NA;
            int n = a.Count;
            double[] diffs = new double[n];
            for (int i = 0; i < n; i++) diffs[i] = a[i] - b[i];
            double meanD = diffs.Average();
            double s2 = diffs.Sum(d => (d - meanD) * (d - meanD)) / (n - 1);
            if (s2 == 0) return ErrorValue.DivByZero;
            t = meanD / Math.Sqrt(s2 / n);
            df = n - 1;
        }
        else if (type == 2) // equal variances
        {
            int n1 = a.Count, n2 = b.Count;
            double m1 = a.Average(), m2 = b.Average();
            double s1 = a.Sum(x => (x - m1) * (x - m1));
            double s2 = b.Sum(x => (x - m2) * (x - m2));
            double sp2 = (s1 + s2) / (n1 + n2 - 2);
            if (sp2 == 0) return ErrorValue.DivByZero;
            t = (m1 - m2) / Math.Sqrt(sp2 * (1.0 / n1 + 1.0 / n2));
            df = n1 + n2 - 2;
        }
        else // unequal variances (Welch)
        {
            int n1 = a.Count, n2 = b.Count;
            double m1 = a.Average(), m2 = b.Average();
            double v1 = a.Sum(x => (x - m1) * (x - m1)) / (n1 - 1);
            double v2 = b.Sum(x => (x - m2) * (x - m2)) / (n2 - 1);
            double se2 = v1 / n1 + v2 / n2;
            if (se2 == 0) return ErrorValue.DivByZero;
            t = (m1 - m2) / Math.Sqrt(se2);
            double v1n = v1 / n1, v2n = v2 / n2;
            df = (v1n + v2n) * (v1n + v2n) / (v1n * v1n / (n1 - 1) + v2n * v2n / (n2 - 1));
        }

        double p = tails == 1 ? 1.0 - TCdf(Math.Abs(t), df) : 2.0 * (1.0 - TCdf(Math.Abs(t), df));
        return NumberResult(p);
    }
}
