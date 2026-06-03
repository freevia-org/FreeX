using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B5: Continuous distributions ──────────────────────────────────────────

    private static ScalarValue ExponDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], ExponDistScalar);
    }

    private static ScalarValue ExponDistScalar(ScalarValue xValue, ScalarValue lambdaValue, ScalarValue cumulativeValue)
    {
        double lambda = ToNumber(lambdaValue);
        bool cum = ToBool(cumulativeValue);
        return ExponDistScalar(xValue, lambda, cum);
    }

    private static ScalarValue ExponDistScalar(ScalarValue xValue, double lambda, bool cum)
    {
        double x = ToNumber(xValue);
        if (x < 0 || lambda <= 0) return ErrorValue.Num;
        return cum
            ? NumberResult(1.0 - Math.Exp(-lambda * x))
            : NumberResult(lambda * Math.Exp(-lambda * x));
    }

    private static ScalarValue WeibullDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], WeibullDistScalar);
    }

    private static ScalarValue WeibullDistScalar(ScalarValue xValue, ScalarValue alphaValue, ScalarValue betaValue, ScalarValue cumulativeValue)
    {
        double alpha = ToNumber(alphaValue), beta = ToNumber(betaValue);
        bool cum = ToBool(cumulativeValue);
        return WeibullDistScalar(xValue, alpha, beta, cum);
    }

    private static ScalarValue WeibullDistScalar(ScalarValue xValue, double alpha, double beta, bool cum)
    {
        double x = ToNumber(xValue);
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(1.0 - Math.Exp(-Math.Pow(x / beta, alpha)));
        return NumberResult((alpha / beta) * Math.Pow(x / beta, alpha - 1) * Math.Exp(-Math.Pow(x / beta, alpha)));
    }
}
