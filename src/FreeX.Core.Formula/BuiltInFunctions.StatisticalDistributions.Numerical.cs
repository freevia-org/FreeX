using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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
        // Heavy-tailed low-df distributions can have quantiles far beyond +-1e9 (e.g. T.INV
        // near 0 or 1 for df=1). Expand whichever side doesn't yet bracket the target so the
        // bisection below finds the real root instead of clamping to the initial window edge.
        for (int i = 0; i < 1100 && TCdf(lo, df) >= p; i++)
        {
            hi = lo;
            lo *= 2.0;
            if (double.IsInfinity(lo)) break;
        }
        for (int i = 0; i < 1100 && TCdf(hi, df) <= p; i++)
        {
            lo = hi;
            hi *= 2.0;
            if (double.IsInfinity(hi)) break;
        }
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
        if (x < 0) return 0;
        double lbeta = LogGamma(d1 / 2.0) + LogGamma(d2 / 2.0) - LogGamma((d1 + d2) / 2.0);
        double shape = d1 / 2.0 - 1;
        if (x == 0 && shape == 0)
        {
            // d1=2: x^(d1/2-1) == x^0 == 1 by convention, but evaluating Math.Log(0) would
            // otherwise multiply 0 * -Infinity = NaN. The true limit at x=0 is exactly 1
            // (independent of d2), so drop the (now-zero) shape*log(x) term instead of
            // computing it.
            return Math.Exp((d1 / 2.0) * Math.Log(d1) + (d2 / 2.0) * Math.Log(d2)
                            - ((d1 + d2) / 2.0) * Math.Log(d2) - lbeta);
        }
        return Math.Exp((d1 / 2.0) * Math.Log(d1) + (d2 / 2.0) * Math.Log(d2)
                        + shape * Math.Log(x)
                        - ((d1 + d2) / 2.0) * Math.Log(d1 * x + d2) - lbeta);
    }

    /// <summary>Inverse F-distribution CDF via bisection.</summary>
    private static double FInv(double p, double d1, double d2)
    {
        if (p <= 0) return 0;
        if (p >= 1) throw new FormulaEvalException("#NUM!", "p >= 1");
        double lo = 0, hi = 1e9;
        // F-distributions with a small denominator df have a heavy right tail (decays like
        // x^(-d2/2)), so the initial window can undershoot for p close to 1. Expand hi until
        // it brackets the target rather than silently clamping the bisection to 1e9.
        for (int i = 0; i < 1100 && FCdf(hi, d1, d2) <= p; i++)
        {
            lo = hi;
            hi *= 2.0;
            if (double.IsInfinity(hi)) break;
        }
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
        if (x < 0) return 0;
        double shape = df / 2.0 - 1;
        if (x == 0 && shape == 0)
        {
            // df=2: chi-square(2) is exactly Exponential(rate=0.5), whose density at 0 is
            // the rate itself (0.5), not 0. x^(df/2-1) == x^0 == 1 by convention, but
            // evaluating Math.Log(0) would otherwise multiply 0 * -Infinity = NaN, so drop
            // the (now-zero) shape*log(x) term instead of computing it.
            return Math.Exp(-(df / 2.0) * Math.Log(2) - LogGamma(df / 2.0));
        }
        return Math.Exp(shape * Math.Log(x) - x / 2.0 - (df / 2.0) * Math.Log(2) - LogGamma(df / 2.0));
    }

    private static double ChiSqInv(double p, double df) => 2.0 * GammaInv(p, df / 2.0);
}
