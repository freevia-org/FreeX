using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B2: F distribution ────────────────────────────────────────────────────

    private static ScalarValue FDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], FDistScalar);
    }

    private static ScalarValue FDistScalar(ScalarValue xValue, ScalarValue d1Value, ScalarValue d2Value, ScalarValue cumulativeValue)
    {
        double d1 = Math.Truncate(ToNumber(d1Value));
        double d2 = Math.Truncate(ToNumber(d2Value));
        bool cum = ToBool(cumulativeValue);
        return FDistScalar(xValue, d1, d2, cum);
    }

    private static ScalarValue FDistScalar(ScalarValue xValue, double d1, double d2, bool cum)
    {
        double x = ToNumber(xValue);
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(FCdf(x, d1, d2)) : NumberResult(FPdf(x, d1, d2));
    }

    private static ScalarValue FDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], FDistRtScalar);
    }

    private static ScalarValue FDistRtScalar(ScalarValue xValue, ScalarValue d1Value, ScalarValue d2Value)
    {
        double d1 = Math.Truncate(ToNumber(d1Value));
        double d2 = Math.Truncate(ToNumber(d2Value));
        return FDistRtScalar(xValue, d1, d2);
    }

    private static ScalarValue FDistRtScalar(ScalarValue xValue, double d1, double d2)
    {
        double x = ToNumber(xValue);
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - FCdf(x, d1, d2));
    }

    private static ScalarValue FInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], FInvScalar);
    }

    private static ScalarValue FInvScalar(ScalarValue probabilityValue, ScalarValue d1Value, ScalarValue d2Value)
    {
        double d1 = Math.Truncate(ToNumber(d1Value));
        double d2 = Math.Truncate(ToNumber(d2Value));
        return FInvScalar(probabilityValue, d1, d2);
    }

    private static ScalarValue FInvScalar(ScalarValue probabilityValue, double d1, double d2)
    {
        double prob = ToNumber(probabilityValue);
        if (d1 < 1 || d2 < 1 || prob < 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(FInv(prob, d1, d2));
    }

    private static ScalarValue FInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], FInvRtScalar);
    }

    private static ScalarValue FInvRtScalar(ScalarValue probabilityValue, ScalarValue d1Value, ScalarValue d2Value)
    {
        double d1 = Math.Truncate(ToNumber(d1Value));
        double d2 = Math.Truncate(ToNumber(d2Value));
        return FInvRtScalar(probabilityValue, d1, d2);
    }

    private static ScalarValue FInvRtScalar(ScalarValue probabilityValue, double d1, double d2)
    {
        double prob = ToNumber(probabilityValue);
        if (d1 < 1 || d2 < 1 || prob <= 0 || prob > 1) return ErrorValue.Num;
        return NumberResult(FInv(1.0 - prob, d1, d2));
    }

    private static ScalarValue FTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (a, b, err) = CollectPair(args[0], args[1]);
        if (err is not null) return err;
        if (a!.Count < 2 || b!.Count < 2) return ErrorValue.DivByZero;
        double m1 = a.Average(), m2 = b.Average();
        double v1 = a.Sum(x => (x - m1) * (x - m1)) / (a.Count - 1);
        double v2 = b.Sum(x => (x - m2) * (x - m2)) / (b.Count - 1);
        if (v2 == 0) return ErrorValue.DivByZero;
        double f = v1 / v2;
        double d1 = a.Count - 1, d2 = b.Count - 1;
        double p1 = FCdf(f, d1, d2);
        return NumberResult(2.0 * Math.Min(p1, 1.0 - p1));
    }

    // ── B2: Chi-squared distribution ──────────────────────────────────────────

    private static ScalarValue ChiSqDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], ChiSqDistScalar);
    }

    private static ScalarValue ChiSqDistScalar(ScalarValue xValue, ScalarValue dfValue, ScalarValue cumulativeValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        bool cum = ToBool(cumulativeValue);
        return ChiSqDistScalar(xValue, df, cum);
    }

    private static ScalarValue ChiSqDistScalar(ScalarValue xValue, double df, bool cum)
    {
        double x = ToNumber(xValue);
        if (df < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(ChiSqCdf(x, df)) : NumberResult(ChiSqPdf(x, df));
    }

    private static ScalarValue ChiSqDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ChiSqDistRtScalar);
    }

    private static ScalarValue ChiSqDistRtScalar(ScalarValue xValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return ChiSqDistRtScalar(xValue, df);
    }

    private static ScalarValue ChiSqDistRtScalar(ScalarValue xValue, double df)
    {
        double x = ToNumber(xValue);
        if (df < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - ChiSqCdf(x, df));
    }

    private static ScalarValue ChiSqInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ChiSqInvScalar);
    }

    private static ScalarValue ChiSqInvScalar(ScalarValue probabilityValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return ChiSqInvScalar(probabilityValue, df);
    }

    private static ScalarValue ChiSqInvScalar(ScalarValue probabilityValue, double df)
    {
        double prob = ToNumber(probabilityValue);
        if (df < 1 || prob < 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(ChiSqInv(prob, df));
    }

    private static ScalarValue ChiSqInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ChiSqInvRtScalar);
    }

    private static ScalarValue ChiSqInvRtScalar(ScalarValue probabilityValue, ScalarValue dfValue)
    {
        double df = Math.Truncate(ToNumber(dfValue));
        return ChiSqInvRtScalar(probabilityValue, df);
    }

    private static ScalarValue ChiSqInvRtScalar(ScalarValue probabilityValue, double df)
    {
        double prob = ToNumber(probabilityValue);
        if (df < 1 || prob <= 0 || prob > 1) return ErrorValue.Num;
        return NumberResult(ChiSqInv(1.0 - prob, df));
    }

    private static ScalarValue ChiSqTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var rv0 = args[0] is RangeValue range0
            ? range0
            : SingleCellArray(args[0]);
        var rv1 = args[1] is RangeValue range1
            ? range1
            : SingleCellArray(args[1]);
        var actualFlat = rv0.Flatten().ToArray();
        var expectedFlat = rv1.Flatten().ToArray();
        if (actualFlat.Length != expectedFlat.Length) return ErrorValue.NA;
        int rows = rv0.RowCount, cols = rv0.ColCount;

        double chiSq = 0;
        int n = actualFlat.Length;
        for (int i = 0; i < n; i++)
        {
            if (actualFlat[i] is not NumberValue av) continue;
            if (expectedFlat[i] is not NumberValue ev) return ErrorValue.Value;
            if (ev.Value == 0) return ErrorValue.DivByZero;
            double diff = av.Value - ev.Value;
            chiSq += diff * diff / ev.Value;
        }

        // df = (rows-1)*(cols-1) for contingency, or (n-1) for one-way
        double df = rows == 1 || cols == 1
            ? n - 1
            : (double)(rows - 1) * (cols - 1);
        if (df < 1) return ErrorValue.NA;
        return NumberResult(1.0 - ChiSqCdf(chiSq, df));
    }
}
