using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B4: Discrete distributions ────────────────────────────────────────────

    /// <summary>Log of binomial coefficient C(n,k).</summary>
    private static double LogBinom(int n, int k)
    {
        if (k < 0 || k > n) return double.NegativeInfinity;
        return LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
    }

    /// <summary>Binomial PMF P(X=k | n, p).</summary>
    private static double BinomPmf(int k, int n, double p)
    {
        // Guard the p=0/p=1 boundaries: 0*log(0) is NaN, but the PMF is well-defined
        // (degenerate) there — Excel returns 1 at the only attainable k, 0 elsewhere.
        if (p == 0) return k == 0 ? 1.0 : 0.0;
        if (p == 1) return k == n ? 1.0 : 0.0;
        return Math.Exp(LogBinom(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));
    }

    /// <summary>Binomial CDF P(X &lt;= k | n, p) via regularised incomplete beta.</summary>
    private static double BinomCdf(int k, int n, double p)
    {
        if (k < 0) return 0;
        if (k >= n) return 1;
        // CDF = I_{1-p}(n-k, k+1)
        return BetaInc(n - k, k + 1, 1.0 - p);
    }

    private static ScalarValue BinomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], BinomDistScalar);
    }

    private static ScalarValue BinomDistScalar(ScalarValue kValue, ScalarValue nValue, ScalarValue probabilityValue, ScalarValue cumulativeValue)
    {
        int n = (int)Math.Truncate(ToNumber(nValue));
        double p = ToNumber(probabilityValue);
        bool cum = ToBool(cumulativeValue);
        return BinomDistScalar(kValue, n, p, cum);
    }

    private static ScalarValue BinomDistScalar(ScalarValue kValue, int n, double p, bool cum)
    {
        int k = (int)Math.Truncate(ToNumber(kValue));
        if (k < 0 || n < 0 || k > n || p < 0 || p > 1) return ErrorValue.Num;
        return cum ? NumberResult(BinomCdf(k, n, p)) : NumberResult(BinomPmf(k, n, p));
    }

    private static ScalarValue BinomDistRange(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var upperArg = args.Count >= 4 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], upperArg], values => BinomDistRangeScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue BinomDistRangeScalar(ScalarValue trialsValue, ScalarValue probabilityValue, ScalarValue successStartValue, ScalarValue successEndValue)
    {
        int n = (int)Math.Truncate(ToNumber(trialsValue));
        double p = ToNumber(probabilityValue);
        int k1 = (int)Math.Truncate(ToNumber(successStartValue));
        int k2 = successEndValue is BlankValue ? k1 : (int)Math.Truncate(ToNumber(successEndValue));
        if (n < 0 || p < 0 || p > 1 || k1 < 0 || k2 < k1 || k2 > n) return ErrorValue.Num;
        double sum = 0;
        for (int k = k1; k <= k2; k++) sum += BinomPmf(k, n, p);
        return NumberResult(sum);
    }

    private static ScalarValue BinomInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], BinomInvScalar);
    }

    private static ScalarValue BinomInvScalar(ScalarValue nValue, ScalarValue probabilityValue, ScalarValue alphaValue)
    {
        int n = (int)Math.Truncate(ToNumber(nValue));
        double p = ToNumber(probabilityValue);
        return BinomInvScalar(n, p, alphaValue);
    }

    private static ScalarValue BinomInvScalar(int n, double p, ScalarValue alphaValue)
    {
        double alpha = ToNumber(alphaValue);
        if (n < 0 || p < 0 || p > 1 || alpha < 0 || alpha > 1) return ErrorValue.Num;
        double cumP = 0;
        for (int k = 0; k <= n; k++)
        {
            cumP += BinomPmf(k, n, p);
            if (cumP >= alpha) return new NumberValue(k);
        }
        return new NumberValue(n);
    }

    private static ScalarValue NegbinomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], NegbinomDistScalar);
    }

    private static ScalarValue NegbinomDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], (failures, successes, probability) => NegbinomDistScalar(failures, successes, probability, new BoolValue(false)));
    }

    private static ScalarValue NegbinomDistScalar(ScalarValue failuresValue, ScalarValue successesValue, ScalarValue probabilityValue, ScalarValue cumulativeValue)
    {
        int r = (int)Math.Truncate(ToNumber(successesValue));
        double p = ToNumber(probabilityValue);
        bool cum = ToBool(cumulativeValue);
        return NegbinomDistScalar(failuresValue, r, p, cum);
    }

    private static ScalarValue NegbinomDistScalar(ScalarValue failuresValue, int r, double p, bool cum)
    {
        int f = (int)Math.Truncate(ToNumber(failuresValue));
        if (f < 0 || r < 1 || p <= 0 || p > 1) return ErrorValue.Num;

        if (!cum)
        {
            // PMF: C(f+r-1, f) * p^r * (1-p)^f
            double pmf = Math.Exp(LogBinom(f + r - 1, f) + r * Math.Log(p) + f * Math.Log(1 - p));
            return NumberResult(pmf);
        }
        // CDF = I_p(r, f+1)
        return NumberResult(BetaInc(r, f + 1, p));
    }

    private static ScalarValue PoissonDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], PoissonDistScalar);
    }

    private static ScalarValue PoissonDistScalar(ScalarValue xValue, ScalarValue lambdaValue, ScalarValue cumulativeValue)
    {
        double lambda = ToNumber(lambdaValue);
        bool cum = ToBool(cumulativeValue);
        return PoissonDistScalar(xValue, lambda, cum);
    }

    private static ScalarValue PoissonDistScalar(ScalarValue xValue, double lambda, bool cum)
    {
        int x = (int)Math.Truncate(ToNumber(xValue));
        if (x < 0 || lambda < 0) return ErrorValue.Num;
        if (!cum)
        {
            // PMF: lambda^x * e^(-lambda) / x!
            double pmf = Math.Exp(x * Math.Log(lambda) - lambda - LogGamma(x + 1));
            return NumberResult(pmf);
        }
        // CDF = 1 - GammaInc(x+1, lambda) via regularised upper gamma = e^{-lambda} sum_{k=0}^{x} lambda^k / k!
        // = 1 - GammaInc(x+1, lambda)
        return NumberResult(1.0 - GammaInc(x + 1, lambda));
    }

    private static ScalarValue HypergeomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        if (args[4] is ErrorValue e4) return e4;
        return MapScalarArgs(args, values => HypergeomDistScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue HypergeomDistCompat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapScalarArgs(args, values => HypergeomDistScalar(values[0], values[1], values[2], values[3], new BoolValue(false)));
    }

    private static ScalarValue HypergeomDistScalar(ScalarValue sampleSuccessesValue, ScalarValue sampleSizeValue, ScalarValue populationSuccessesValue, ScalarValue populationSizeValue, ScalarValue cumulativeValue)
    {
        int n = (int)Math.Truncate(ToNumber(sampleSizeValue));
        int M = (int)Math.Truncate(ToNumber(populationSuccessesValue));
        int N = (int)Math.Truncate(ToNumber(populationSizeValue));
        bool cum = ToBool(cumulativeValue);
        return HypergeomDistScalar(sampleSuccessesValue, n, M, N, cum);
    }

    private static ScalarValue HypergeomDistScalar(ScalarValue sampleSuccessesValue, int n, int M, int N, bool cum)
    {
        int s = (int)Math.Truncate(ToNumber(sampleSuccessesValue));
        int sMin = Math.Max(0, n + M - N);
        if (s < 0 || n < 0 || M < 0 || N <= 0 || s > n || s > M || n > N || M > N || s < sMin) return ErrorValue.Num;

        if (!cum)
        {
            double pmf = Math.Exp(LogBinom(M, s) + LogBinom(N - M, n - s) - LogBinom(N, n));
            return NumberResult(pmf);
        }
        double cdf = 0;
        for (int k = Math.Max(0, n - (N - M)); k <= Math.Min(n, M) && k <= s; k++)
            cdf += Math.Exp(LogBinom(M, k) + LogBinom(N - M, n - k) - LogBinom(N, n));
        return NumberResult(cdf);
    }
}
