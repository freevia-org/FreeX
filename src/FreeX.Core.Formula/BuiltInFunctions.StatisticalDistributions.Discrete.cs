using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── B4: Discrete distributions ────────────────────────────────────────────

    // A (int) cast on a double outside Int32's representable range SATURATES to Int32.MaxValue
    // (or MinValue) in .NET rather than throwing — so a legitimately huge trials/population-size
    // argument (e.g. 2.2 billion: an ordinary finite double, and BINOM.DIST/NEGBINOM.DIST/
    // HYPGEOM.DIST document no upper bound on it) would otherwise be silently substituted with a
    // wildly different, smaller value and produce a confidently wrong numeric result instead of
    // erroring. Any magnitude that doesn't fit in an int falls outside what this int-based
    // implementation can compute, so it must yield #NUM! rather than a corrupted answer.
    private static bool TryTruncateToInt32(double value, out int result)
    {
        result = 0;
        if (!double.IsFinite(value)) return false;
        double truncated = Math.Truncate(value);
        if (truncated < int.MinValue || truncated > int.MaxValue) return false;
        result = (int)truncated;
        return true;
    }

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
        if (!TryTruncateToInt32(ToNumber(nValue), out int n)) return ErrorValue.Num;
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
        if (!TryTruncateToInt32(ToNumber(trialsValue), out int n)) return ErrorValue.Num;
        double p = ToNumber(probabilityValue);
        int k1 = (int)Math.Truncate(ToNumber(successStartValue));
        int k2 = successEndValue is BlankValue ? k1 : (int)Math.Truncate(ToNumber(successEndValue));
        if (n < 0 || p < 0 || p > 1 || k1 < 0 || k2 < k1 || k2 > n) return ErrorValue.Num;
        // Sum of the range = CDF(k2) - CDF(k1-1); BinomCdf is O(1) (closed-form via BetaInc)
        // so this avoids an O(range) term-by-term walk for wide [k1,k2] windows. BinomCdf
        // already treats any k < 0 as a 0 lower tail, so k1=0 (=> k1-1=-1) needs no extra guard.
        return NumberResult(BinomCdf(k2, n, p) - BinomCdf(k1 - 1, n, p));
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
        if (!TryTruncateToInt32(ToNumber(nValue), out int n)) return ErrorValue.Num;
        double p = ToNumber(probabilityValue);
        return BinomInvScalar(n, p, alphaValue);
    }

    private static ScalarValue BinomInvScalar(int n, double p, ScalarValue alphaValue)
    {
        double alpha = ToNumber(alphaValue);
        if (n < 0 || p < 0 || p > 1 || alpha < 0 || alpha > 1) return ErrorValue.Num;
        // Find the smallest k in [0,n] with BinomCdf(k,n,p) >= alpha via binary search.
        // BinomCdf is monotone non-decreasing in k (it's a cumulative distribution), so this
        // lands on exactly the same k the old term-by-term accumulation would have stopped
        // at, but in O(log n) closed-form BinomCdf calls instead of an O(n) PMF walk — the
        // difference between instant and ~1e9 iterations when n is in the billions.
        int lo = 0, hi = n;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (BinomCdf(mid, n, p) >= alpha) hi = mid;
            else lo = mid + 1;
        }
        return new NumberValue(lo);
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
        if (!TryTruncateToInt32(ToNumber(successesValue), out int r)) return ErrorValue.Num;
        double p = ToNumber(probabilityValue);
        bool cum = ToBool(cumulativeValue);
        return NegbinomDistScalar(failuresValue, r, p, cum);
    }

    private static ScalarValue NegbinomDistScalar(ScalarValue failuresValue, int r, double p, bool cum)
    {
        if (!TryTruncateToInt32(ToNumber(failuresValue), out int f)) return ErrorValue.Num;
        if (f < 0 || r < 1 || p <= 0 || p > 1) return ErrorValue.Num;

        if (!cum)
        {
            // PMF: C(f+r-1, f) * p^r * (1-p)^f
            // Guard the p=1 boundary: f*log(1-p) is 0*log(0) = NaN at f=0, but the PMF is
            // well-defined (degenerate) there — every trial succeeds, so the r-th success
            // is certain on the very first trial, giving 0 failures with probability 1.
            double pmf = p == 1
                ? (f == 0 ? 1.0 : 0.0)
                : Math.Exp(LogBinom(f + r - 1, f) + r * Math.Log(p) + f * Math.Log(1 - p));
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
            // Guard the lambda=0 boundary: x*log(0) is 0*-Infinity = NaN at x=0, but the
            // distribution is degenerate at 0 there (a Poisson process with rate 0 never
            // fires) — Excel returns 1 at x=0, 0 elsewhere.
            double pmf = lambda == 0
                ? (x == 0 ? 1.0 : 0.0)
                : Math.Exp(x * Math.Log(lambda) - lambda - LogGamma(x + 1));
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
        if (!TryTruncateToInt32(ToNumber(sampleSizeValue), out int n)) return ErrorValue.Num;
        if (!TryTruncateToInt32(ToNumber(populationSuccessesValue), out int M)) return ErrorValue.Num;
        if (!TryTruncateToInt32(ToNumber(populationSizeValue), out int N)) return ErrorValue.Num;
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

        int kMin = Math.Max(0, n - (N - M));
        int kMax = Math.Min(Math.Min(n, M), s);
        long span = (long)kMax - kMin + 1;
        if (span > MaxHypergeomCdfTerms)
        {
            // Term-by-term summation over the actual support [kMin, kMax] would take up to
            // ~O(min(n,M,N)) iterations, which freezes the calc thread for astronomically
            // large population/sample sizes. Beyond the safety limit, fall back to the
            // continuity-corrected normal approximation to the hypergeometric CDF (the
            // hypergeometric converges to normal as N grows, same as Excel is consistent
            // with for its own huge-parameter behavior).
            double mean = (double)n * M / N;
            double variance = mean * (1.0 - (double)M / N) * (N - n) / (double)(N - 1);
            double z = variance > 0
                ? (s + 0.5 - mean) / Math.Sqrt(variance)
                : (s >= mean ? double.PositiveInfinity : double.NegativeInfinity);
            return NumberResult(Math.Clamp(NormSCdf(z), 0.0, 1.0));
        }

        double cdf = 0;
        for (int k = kMin; k <= kMax; k++)
            cdf += Math.Exp(LogBinom(M, k) + LogBinom(N - M, n - k) - LogBinom(N, n));
        return NumberResult(cdf);
    }

    // Beyond this many terms, HYPGEOM.DIST's cumulative branch switches from an exact
    // term-by-term PMF sum to a normal approximation — see HypergeomDistScalar above.
    private const int MaxHypergeomCdfTerms = 1_000_000;
}
