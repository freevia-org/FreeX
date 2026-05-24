using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Phase B — Statistical Distribution Functions
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Numerical primitives ─────────────────────────────────────────────────

    /// <summary>Complementary error function using a Chebyshev approximation.</summary>
    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 2.0 / (2.0 + z);
        double ty = 4.0 * t - 2.0;
        double d = 0.0;
        double dd = 0.0;
        ReadOnlySpan<double> coefficients =
        [
            -1.3026537197817094,
             0.64196979235649026,
             0.019476473204185836,
            -0.009561514786808631,
            -0.000946595344482036,
             0.000366839497852761,
             0.000042523324806907,
            -0.000020278578112534,
            -0.000001624290004647,
             0.00000130365583558,
             0.000000015626441722,
            -0.000000085238095915,
             0.000000006529054439,
             0.000000005059343495,
            -0.000000000991364156,
            -0.000000000227365122,
             0.000000000096467911,
             0.000000000002394038,
            -0.000000000006886027,
             0.000000000000894487,
             0.000000000000313092,
            -0.000000000000112708,
             0.000000000000000381,
             0.000000000000007106,
            -0.000000000000001523,
            -0.000000000000000094,
             0.000000000000000121,
            -0.000000000000000028
        ];

        for (int j = coefficients.Length - 1; j > 0; j--)
        {
            double previous = d;
            d = ty * d - dd + coefficients[j];
            dd = previous;
        }

        double result = t * Math.Exp(-z * z + 0.5 * (coefficients[0] + ty * d) - dd);
        return x >= 0.0 ? result : 2.0 - result;
    }

    /// <summary>Error function used by normal distribution helpers.</summary>
    private static double Erf(double x)
        => x >= 0.0 ? 1.0 - Erfc(x) : Erfc(-x) - 1.0;

    private static double NormSCdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    private static double NormSPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

    /// <summary>Inverse standard-normal CDF (Acklam rational approximation with CDF refinement).</summary>
    private static double NormSInv(double p)
    {
        if (p <= 0 || p >= 1) throw new FormulaEvalException("#NUM!", "probability out of range");
        if (p == 0.5) return 0.0;

        const double plow = 0.02425;
        const double phigh = 1.0 - plow;
        double x;

        if (p < plow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            x = (((((-0.007784894002430293 * q - 0.3223964580411365) * q - 2.400758277161838) * q - 2.549732539343734) * q + 4.374664141464968) * q + 2.938163982698783) /
                ((((0.007784695709041462 * q + 0.3224671290700398) * q + 2.445134137142996) * q + 3.754408661907416) * q + 1.0);
        }
        else if (p <= phigh)
        {
            double q = p - 0.5;
            double r = q * q;
            x = (((((-39.69683028665376 * r + 220.9460984245205) * r - 275.9285104469687) * r + 138.3577518672690) * r - 30.66479806614716) * r + 2.506628277459239) * q /
                (((((-54.47609879822406 * r + 161.5858368580409) * r - 155.6989798598866) * r + 66.80131188771972) * r - 13.28068155288572) * r + 1.0);
        }
        else
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            x = -(((((-0.007784894002430293 * q - 0.3223964580411365) * q - 2.400758277161838) * q - 2.549732539343734) * q + 4.374664141464968) * q + 2.938163982698783) /
                ((((0.007784695709041462 * q + 0.3224671290700398) * q + 2.445134137142996) * q + 3.754408661907416) * q + 1.0);
        }

        for (int i = 0; i < 2; i++)
        {
            double pdf = NormSPdf(x);
            if (pdf == 0 || !double.IsFinite(pdf)) break;
            x -= (NormSCdf(x) - p) / pdf;
        }

        return x;
    }

    /// <summary>Lanczos approximation for ln(Gamma(x)), x > 0.</summary>
    private static double LogGamma(double x)
    {
        double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                       -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>Gamma function value via exp(LogGamma). Handles negative non-integer x via reflection.</summary>
    private static double GammaValue(double x)
    {
        if (x <= 0)
        {
            // Reflection: Gamma(x)*Gamma(1-x) = pi/sin(pi*x)
            if (x == Math.Floor(x)) return double.NaN; // pole
            return Math.PI / (Math.Sin(Math.PI * x) * GammaValue(1.0 - x));
        }
        return Math.Exp(LogGamma(x));
    }

    /// <summary>Regularised incomplete gamma P(a, x) using series (x &lt; a+1) or CF (x >= a+1).</summary>
    private static double GammaInc(double a, double x)
    {
        if (x < 0 || a <= 0) return double.NaN;
        if (x == 0) return 0;
        return x < a + 1.0 ? GammaIncSeries(a, x) : 1.0 - GammaIncCf(a, x);
    }

    private static double GammaIncSeries(double a, double x)
    {
        double ap = a, del = 1.0 / a, sum = del;
        for (int n = 1; n <= 300; n++)
        {
            ap++; del *= x / ap; sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * 1e-12) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
    }

    private static double GammaIncCf(double a, double x)
    {
        double b = x + 1.0 - a, c = 1.0 / 1e-30, d = 1.0 / b, h = d;
        if (Math.Abs(d) < 1e-30) d = 1e-30;
        for (int i = 1; i <= 300; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = an * d + b; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = b + an / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1.0 / d; double del2 = d * c; h *= del2;
            if (Math.Abs(del2 - 1.0) < 1e-12) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>Inverse of GammaInc(a, x) = p via Newton refinement.</summary>
    private static double GammaInv(double p, double a)
    {
        if (p <= 0) return 0;
        if (p >= 1) return double.PositiveInfinity;
        // Initial guess via normal approximation
        double x = a * Math.Pow(NormSInv(p) / Math.Sqrt(9 * a) + 1 - 1.0 / (9 * a), 3);
        if (x <= 0) x = 0.01;
        for (int i = 0; i < 200; i++)
        {
            double f = GammaInc(a, x) - p;
            double df = Math.Exp((a - 1) * Math.Log(x) - x - LogGamma(a));
            if (df == 0) break;
            double dx = f / df;
            x -= dx;
            if (x <= 0) x = 1e-10;
            if (Math.Abs(dx) < x * 1e-10) break;
        }
        return x;
    }

    /// <summary>Regularised incomplete beta I_x(a, b).</summary>
    private static double BetaInc(double a, double b, double x)
    {
        if (x < 0 || x > 1) return double.NaN;
        if (x == 0) return 0;
        if (x == 1) return 1;
        // Use symmetry when x > (a+1)/(a+b+2) for better CF convergence
        if (x > (a + 1) / (a + b + 2))
            return 1.0 - BetaInc(b, a, 1.0 - x);
        double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        double front = Math.Exp(Math.Log(x) * a + Math.Log(1 - x) * b - lbeta) / a;
        return front * BetaCf(a, b, x);
    }

    private static double BetaCf(double a, double b, double x)
    {
        const int maxIter = 300; const double eps = 3e-12;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-30) d = 1e-30;
        d = 1 / d; double h = d;
        for (int m = 1; m <= maxIter; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d; double del = d * c; h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }

    /// <summary>Inverse regularised incomplete beta via Newton's method.</summary>
    private static double BetaInv(double p, double a, double b)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        double x = a / (a + b); // initial guess: mean of beta
        for (int i = 0; i < 200; i++)
        {
            double f = BetaInc(a, b, x) - p;
            double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
            double df = Math.Exp((a - 1) * Math.Log(x) + (b - 1) * Math.Log(1 - x) - lbeta);
            if (df == 0) break;
            double dx = f / df;
            x -= dx;
            x = Math.Clamp(x, 1e-10, 1.0 - 1e-10);
            if (Math.Abs(dx) < 1e-10) break;
        }
        return x;
    }

    /// <summary>Student-t CDF using regularised incomplete beta.</summary>
    private static double TCdf(double t, double df)
    {
        double x = df / (df + t * t);
        double tail = 0.5 * BetaInc(df / 2.0, 0.5, x);
        return t >= 0 ? 1.0 - tail : tail;
    }

    private static double TPdf(double t, double df)
        => Math.Exp(LogGamma((df + 1) / 2.0) - LogGamma(df / 2.0))
           / (Math.Sqrt(df * Math.PI) * Math.Pow(1 + t * t / df, (df + 1) / 2.0));

    /// <summary>Inverse t-distribution CDF via bisection.</summary>
    private static double TInv(double p, double df)
    {
        if (p <= 0 || p >= 1) throw new FormulaEvalException("#NUM!", "p out of range");
        double lo = -1e9, hi = 1e9;
        for (int i = 0; i < 300; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (TCdf(mid, df) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-10) break;
        }
        return (lo + hi) / 2.0;
    }

    /// <summary>F-distribution CDF.</summary>
    private static double FCdf(double x, double d1, double d2)
    {
        if (x <= 0) return 0;
        double t = d1 * x / (d1 * x + d2);
        return BetaInc(d1 / 2.0, d2 / 2.0, t);
    }

    private static double FPdf(double x, double d1, double d2)
    {
        if (x <= 0) return 0;
        double lbeta = LogGamma(d1 / 2.0) + LogGamma(d2 / 2.0) - LogGamma((d1 + d2) / 2.0);
        return Math.Exp((d1 / 2.0) * Math.Log(d1) + (d2 / 2.0) * Math.Log(d2)
                        + (d1 / 2.0 - 1) * Math.Log(x)
                        - ((d1 + d2) / 2.0) * Math.Log(d1 * x + d2) - lbeta);
    }

    /// <summary>Inverse F-distribution CDF via bisection.</summary>
    private static double FInv(double p, double d1, double d2)
    {
        if (p <= 0) return 0;
        if (p >= 1) throw new FormulaEvalException("#NUM!", "p >= 1");
        double lo = 0, hi = 1e9;
        for (int i = 0; i < 300; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (FCdf(mid, d1, d2) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-9) break;
        }
        return (lo + hi) / 2.0;
    }

    /// <summary>Chi-squared CDF (special case of Gamma).</summary>
    private static double ChiSqCdf(double x, double df) => x <= 0 ? 0.0 : GammaInc(df / 2.0, x / 2.0);

    private static double ChiSqPdf(double x, double df)
    {
        if (x <= 0) return 0;
        return Math.Exp((df / 2.0 - 1) * Math.Log(x) - x / 2.0 - (df / 2.0) * Math.Log(2) - LogGamma(df / 2.0));
    }

    private static double ChiSqInv(double p, double df) => 2.0 * GammaInv(p, df / 2.0);

    // ── Helper: collect two parallel arrays from two args (range or scalar) ─

    private static (List<double>? A, List<double>? B, ErrorValue? Err)
        CollectPair(ScalarValue argA, ScalarValue argB)
    {
        var (a, ea) = argA is RangeValue rva ? CollectRangeNumbers(rva) : CollectNumbers(new[] { argA });
        if (ea is not null) return (null, null, ea);
        var (b, eb) = argB is RangeValue rvb ? CollectRangeNumbers(rvb) : CollectNumbers(new[] { argB });
        if (eb is not null) return (null, null, eb);
        return (a, b, null);
    }

    // ── B1: Normal distribution ───────────────────────────────────────────────

    private static ScalarValue NormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => NormDistScalar(value, mean, stdev, cum));
        return NormDistScalar(args[0], mean, stdev, cum);
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
        double mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => NormInvScalar(value, mean, stdev));
        return NormInvScalar(args[0], mean, stdev);
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
        bool cum = ToBool(args[1]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => NormSDistScalar(value, cum));
        return NormSDistScalar(args[0], cum);
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

    private static ScalarValue Standardize(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => StandardizeScalar(value, mean, stdev));
        return StandardizeScalar(args[0], mean, stdev);
    }

    private static ScalarValue StandardizeScalar(ScalarValue xValue, double mean, double stdev)
    {
        double x = ToNumber(xValue);
        if (stdev <= 0) return ErrorValue.Num;
        return NumberResult((x - mean) / stdev);
    }

    // ── B2: T distribution ────────────────────────────────────────────────────

    private static ScalarValue TDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double df = Math.Truncate(ToNumber(args[1]));
        bool cum = ToBool(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TDistScalar(value, df, cum));
        return TDistScalar(args[0], df, cum);
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
        double df = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TDistRtScalar(value, df));
        return TDistRtScalar(args[0], df);
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
        double df = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TDist2TScalar(value, df));
        return TDist2TScalar(args[0], df);
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
        double df = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TInvScalar(value, df));
        return TInvScalar(args[0], df);
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
        double df = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TInv2TScalar(value, df));
        return TInv2TScalar(args[0], df);
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

    // ── B2: F distribution ────────────────────────────────────────────────────

    private static ScalarValue FDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        bool cum = ToBool(args[3]);
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(FCdf(x, d1, d2)) : NumberResult(FPdf(x, d1, d2));
    }

    private static ScalarValue FDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - FCdf(x, d1, d2));
    }

    private static ScalarValue FInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(FInv(prob, d1, d2));
    }

    private static ScalarValue FInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
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
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        bool cum = ToBool(args[2]);
        if (df < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(ChiSqCdf(x, df)) : NumberResult(ChiSqPdf(x, df));
    }

    private static ScalarValue ChiSqDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - ChiSqCdf(x, df));
    }

    private static ScalarValue ChiSqInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || prob < 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(ChiSqInv(prob, df));
    }

    private static ScalarValue ChiSqInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
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
        var dataList = new List<double>();
        if (args[0] is RangeValue rvd)
        {
            foreach (var v in rvd.Flatten())
                if (v is NumberValue nv) dataList.Add(nv.Value);
        }
        else if (args[0] is NumberValue nva) dataList.Add(nva.Value);

        // Collect bins (sorted)
        var binsList = new List<double>();
        if (args[1] is RangeValue rvb)
        {
            foreach (var v in rvb.Flatten())
                if (v is NumberValue nv) binsList.Add(nv.Value);
        }
        else if (args[1] is NumberValue nvb) binsList.Add(nvb.Value);

        binsList.Sort();
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
        double alpha = ToNumber(args[0]), stdev = ToNumber(args[1]), size = ToNumber(args[2]);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 1) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        return NumberResult(NormSInv(1.0 - alpha / 2.0) * stdev / Math.Sqrt(n));
    }

    private static ScalarValue ConfidenceT(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double alpha = ToNumber(args[0]), stdev = ToNumber(args[1]), size = ToNumber(args[2]);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 2) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        double df = n - 1;
        return NumberResult(TInv(1.0 - alpha / 2.0, df) * stdev / Math.Sqrt(n));
    }

    // ── B4: Discrete distributions ────────────────────────────────────────────

    /// <summary>Log of binomial coefficient C(n,k).</summary>
    private static double LogBinom(int n, int k)
    {
        if (k < 0 || k > n) return double.NegativeInfinity;
        return LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
    }

    /// <summary>Binomial PMF P(X=k | n, p).</summary>
    private static double BinomPmf(int k, int n, double p)
        => Math.Exp(LogBinom(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));

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
        int k = (int)Math.Truncate(ToNumber(args[0]));
        int n = (int)Math.Truncate(ToNumber(args[1]));
        double p = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (k < 0 || n < 0 || k > n || p < 0 || p > 1) return ErrorValue.Num;
        return cum ? NumberResult(BinomCdf(k, n, p)) : NumberResult(BinomPmf(k, n, p));
    }

    private static ScalarValue BinomDistRange(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        int n = (int)Math.Truncate(ToNumber(args[0]));
        double p = ToNumber(args[1]);
        int k1 = (int)Math.Truncate(ToNumber(args[2]));
        int k2 = args.Count >= 4 && args[3] is not BlankValue ? (int)Math.Truncate(ToNumber(args[3])) : k1;
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
        int n = (int)Math.Truncate(ToNumber(args[0]));
        double p = ToNumber(args[1]);
        double alpha = ToNumber(args[2]);
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
        int f = (int)Math.Truncate(ToNumber(args[0]));
        int r = (int)Math.Truncate(ToNumber(args[1]));
        double p = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
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
        double lambda = ToNumber(args[1]);
        bool cum = ToBool(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => PoissonDistScalar(value, lambda, cum));
        return PoissonDistScalar(args[0], lambda, cum);
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
        int s = (int)Math.Truncate(ToNumber(args[0]));   // sample successes
        int n = (int)Math.Truncate(ToNumber(args[1]));   // sample size
        int M = (int)Math.Truncate(ToNumber(args[2]));   // population successes
        int N = (int)Math.Truncate(ToNumber(args[3]));   // population size
        bool cum = ToBool(args[4]);
        if (s < 0 || n < 0 || M < 0 || N <= 0 || s > n || s > M || n > N || M > N) return ErrorValue.Num;

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

    // ── B5: Continuous distributions ──────────────────────────────────────────

    private static ScalarValue ExponDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double lambda = ToNumber(args[1]);
        bool cum = ToBool(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => ExponDistScalar(value, lambda, cum));
        return ExponDistScalar(args[0], lambda, cum);
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
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => WeibullDistScalar(value, alpha, beta, cum));
        return WeibullDistScalar(args[0], alpha, beta, cum);
    }

    private static ScalarValue WeibullDistScalar(ScalarValue xValue, double alpha, double beta, bool cum)
    {
        double x = ToNumber(xValue);
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(1.0 - Math.Exp(-Math.Pow(x / beta, alpha)));
        return NumberResult((alpha / beta) * Math.Pow(x / beta, alpha - 1) * Math.Exp(-Math.Pow(x / beta, alpha)));
    }

    private static ScalarValue GammaDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => GammaDistScalar(value, alpha, beta, cum));
        return GammaDistScalar(args[0], alpha, beta, cum);
    }

    private static ScalarValue GammaDistScalar(ScalarValue xValue, double alpha, double beta, bool cum)
    {
        double x = ToNumber(xValue);
        // Excel: beta is scale (theta), so mean = alpha*beta
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(GammaInc(alpha, x / beta));
        double pdf = Math.Exp((alpha - 1) * Math.Log(x) - x / beta - alpha * Math.Log(beta) - LogGamma(alpha));
        return NumberResult(pdf);
    }

    private static ScalarValue GammaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => GammaInvScalar(value, alpha, beta));
        return GammaInvScalar(args[0], alpha, beta);
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
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        double A = args.Count >= 5 && args[4] is not BlankValue ? ToNumber(args[4]) : 0.0;
        double B = args.Count >= 6 && args[5] is not BlankValue ? ToNumber(args[5]) : 1.0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => BetaDistScalar(value, alpha, beta, cum, A, B));
        return BetaDistScalar(args[0], alpha, beta, cum, A, B);
    }

    private static ScalarValue BetaDistScalar(ScalarValue xValue, double alpha, double beta, bool cum, double A, double B)
    {
        double x = ToNumber(xValue);
        if (alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        if (x < A || x > B) return ErrorValue.Num;
        double t = (x - A) / (B - A);
        if (cum) return NumberResult(BetaInc(alpha, beta, t));
        double lbeta = LogGamma(alpha) + LogGamma(beta) - LogGamma(alpha + beta);
        double pdf = Math.Exp((alpha - 1) * Math.Log(t) + (beta - 1) * Math.Log(1 - t) - lbeta) / (B - A);
        return NumberResult(pdf);
    }

    private static ScalarValue BetaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        double A = args.Count >= 4 && args[3] is not BlankValue ? ToNumber(args[3]) : 0.0;
        double B = args.Count >= 5 && args[4] is not BlankValue ? ToNumber(args[4]) : 1.0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => BetaInvScalar(value, alpha, beta, A, B));
        return BetaInvScalar(args[0], alpha, beta, A, B);
    }

    private static ScalarValue BetaInvScalar(ScalarValue probabilityValue, double alpha, double beta, double A, double B)
    {
        double prob = ToNumber(probabilityValue);
        if (prob < 0 || prob > 1 || alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        return NumberResult(BetaInv(prob, alpha, beta) * (B - A) + A);
    }

    private static ScalarValue LognormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => LognormDistScalar(value, mean, stdev, cum));
        return LognormDistScalar(args[0], mean, stdev, cum);
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
        double mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => LognormInvScalar(value, mean, stdev));
        return LognormInvScalar(args[0], mean, stdev);
    }

    private static ScalarValue LognormInvScalar(ScalarValue probabilityValue, double mean, double stdev)
    {
        double prob = ToNumber(probabilityValue);
        if (prob <= 0 || prob >= 1 || stdev <= 0) return ErrorValue.Num;
        return NumberResult(Math.Exp(NormSInv(prob) * stdev + mean));
    }

}
