using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue GammaDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], GammaDistScalar);
    }

    private static ScalarValue GammaDistScalar(ScalarValue xValue, ScalarValue alphaValue, ScalarValue betaValue, ScalarValue cumulativeValue)
    {
        double alpha = ToNumber(alphaValue), beta = ToNumber(betaValue);
        bool cum = ToBool(cumulativeValue);
        return GammaDistScalar(xValue, alpha, beta, cum);
    }

    private static ScalarValue GammaDistScalar(ScalarValue xValue, double alpha, double beta, bool cum)
    {
        double x = ToNumber(xValue);
        // Excel: beta is scale (theta), so mean = alpha*beta
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(GammaInc(alpha, x / beta));
        // x==0 with alpha==1 is the exponential-density special case: x^(alpha-1) == 0^0 == 1,
        // not the 0 * Math.Log(0) == 0 * -Infinity == NaN that a literal log-term evaluation produces.
        double logXTerm = x == 0 && alpha == 1 ? 0.0 : (alpha - 1) * Math.Log(x);
        double pdf = Math.Exp(logXTerm - x / beta - alpha * Math.Log(beta) - LogGamma(alpha));
        return NumberResult(pdf);
    }

    private static ScalarValue GammaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], GammaInvScalar);
    }

    private static ScalarValue GammaInvScalar(ScalarValue probabilityValue, ScalarValue alphaValue, ScalarValue betaValue)
    {
        double alpha = ToNumber(alphaValue), beta = ToNumber(betaValue);
        return GammaInvScalar(probabilityValue, alpha, beta);
    }

    private static ScalarValue GammaInvScalar(ScalarValue probabilityValue, double alpha, double beta)
    {
        double prob = ToNumber(probabilityValue);
        if (prob < 0 || prob >= 1 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        return NumberResult(GammaInv(prob, alpha) * beta);
    }

    private static ScalarValue GammaLnFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, GammaLnScalar);
        return GammaLnScalar(args[0]);
    }

    private static ScalarValue GammaLnScalar(ScalarValue xValue)
    {
        double x = ToNumber(xValue);
        if (x <= 0) return ErrorValue.Num;
        return NumberResult(LogGamma(x));
    }

    private static ScalarValue GammaFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, GammaScalar);
        return GammaScalar(args[0]);
    }

    private static ScalarValue GammaScalar(ScalarValue xValue)
    {
        double x = ToNumber(xValue);
        if (x == 0 || (x < 0 && x == Math.Floor(x))) return ErrorValue.Num;
        double g = GammaValue(x);
        return double.IsFinite(g) ? NumberResult(g) : ErrorValue.Num;
    }

    private static ScalarValue BetaDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var lowerArg = args.Count >= 5 ? args[4] : BlankValue.Instance;
        var upperArg = args.Count >= 6 ? args[5] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], lowerArg, upperArg], values => BetaDistScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue BetaDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var lowerArg = args.Count >= 4 ? args[3] : BlankValue.Instance;
        var upperArg = args.Count >= 5 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], lowerArg, upperArg], values => BetaDistScalar(values[0], values[1], values[2], new BoolValue(true), values[3], values[4]));
    }

    private static ScalarValue BetaDistScalar(ScalarValue xValue, ScalarValue alphaValue, ScalarValue betaValue, ScalarValue cumulativeValue, ScalarValue lowerValue, ScalarValue upperValue)
    {
        double alpha = ToNumber(alphaValue), beta = ToNumber(betaValue);
        bool cum = ToBool(cumulativeValue);
        double A = lowerValue is BlankValue ? 0.0 : ToNumber(lowerValue);
        double B = upperValue is BlankValue ? 1.0 : ToNumber(upperValue);
        return BetaDistScalar(xValue, alpha, beta, cum, A, B);
    }

    private static ScalarValue BetaDistScalar(ScalarValue xValue, double alpha, double beta, bool cum, double A, double B)
    {
        double x = ToNumber(xValue);
        if (alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        if (x < A || x > B) return ErrorValue.Num;
        double t = (x - A) / (B - A);
        if (cum) return NumberResult(BetaInc(alpha, beta, t));
        double lbeta = LogGamma(alpha) + LogGamma(beta) - LogGamma(alpha + beta);
        // t==0 with alpha==1 (t^0==1) and t==1 with beta==1 ((1-t)^0==1) are the Uniform(0,1)-style
        // boundary special cases: a literal log-term evaluation hits 0 * Math.Log(0) == NaN instead.
        double logTTerm = t == 0 && alpha == 1 ? 0.0 : (alpha - 1) * Math.Log(t);
        double log1MinusTTerm = t == 1 && beta == 1 ? 0.0 : (beta - 1) * Math.Log(1 - t);
        double pdf = Math.Exp(logTTerm + log1MinusTTerm - lbeta) / (B - A);
        return NumberResult(pdf);
    }

    private static ScalarValue BetaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var lowerArg = args.Count >= 4 ? args[3] : BlankValue.Instance;
        var upperArg = args.Count >= 5 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], lowerArg, upperArg], values => BetaInvScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue BetaInvScalar(ScalarValue probabilityValue, ScalarValue alphaValue, ScalarValue betaValue, ScalarValue lowerValue, ScalarValue upperValue)
    {
        double alpha = ToNumber(alphaValue), beta = ToNumber(betaValue);
        double A = lowerValue is BlankValue ? 0.0 : ToNumber(lowerValue);
        double B = upperValue is BlankValue ? 1.0 : ToNumber(upperValue);
        return BetaInvScalar(probabilityValue, alpha, beta, A, B);
    }

    private static ScalarValue BetaInvScalar(ScalarValue probabilityValue, double alpha, double beta, double A, double B)
    {
        double prob = ToNumber(probabilityValue);
        if (prob < 0 || prob > 1 || alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        return NumberResult(BetaInv(prob, alpha, beta) * (B - A) + A);
    }
}
