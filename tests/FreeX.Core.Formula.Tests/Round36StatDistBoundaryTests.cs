using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round 36 stat-dist-boundary fixes: four PDF/PMF functions hard-coded a boundary result of
/// 0 (or hit a 0*log(0)=NaN pattern that collapsed to #NUM!) at an input that is actually a
/// valid, finite, nonzero value in real Excel. Same bug class as the round-26
/// GAMMA.DIST/BETA.DIST boundary fixes (see Round26BetaGammaDistBoundaryTests.cs).
///
/// R36-formula-statistical-dist-2-1: CHISQ.DIST(0,2,FALSE) hard-coded 0, but chi-square(2) is
/// exactly Exponential(rate=0.5), whose density at 0 equals the rate, 0.5. Fixed in
/// BuiltInFunctions.StatisticalDistributions.Numerical.cs (ChiSqPdf) by only clamping x&lt;0 and
/// special-casing the df=2 (shape=0) boundary to avoid 0*Math.Log(0)=NaN.
///
/// R36-formula-statistical-dist-2-2: F.DIST(0,2,d2,FALSE) hard-coded 0, but with numerator
/// df=2 the density at x=0 is exactly 1 regardless of the denominator df. Fixed in the same
/// file (FPdf) with the analogous d1=2 (shape=0) special case.
///
/// R36-formula-statistical-dist-2-3: NEGBINOM.DIST(0,r,1,FALSE) returned #NUM! because
/// f*Math.Log(1-p) at f=0,p=1 is 0*Math.Log(0)=0*-Infinity=NaN. Mathematically p=1 means every
/// trial succeeds, so 0 failures before the r-th success has probability 1. Fixed in
/// BuiltInFunctions.StatisticalDistributions.Discrete.cs (NegbinomDistScalar) with a p==1 guard
/// mirroring BinomPmf's existing p==0/p==1 guards.
///
/// R36-formula-statistical-dist-2-4: POISSON.DIST(0,0,FALSE) returned #NUM! because
/// x*Math.Log(lambda) at x=0,lambda=0 is 0*-Infinity=NaN. A Poisson process with rate 0 never
/// fires, so P(X=0)=1. Fixed in the same file (PoissonDistScalar) with a lambda==0 guard.
/// </summary>
public sealed class Round36StatDistBoundaryTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    private double Calc(string formula)
    {
        var result = Eval(formula);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    // ── CHISQ.DIST ──────────────────────────────────────────────────────────

    [Fact]
    public void ChiSqDist_Pdf_AtZero_WithDfTwo_ReturnsExponentialRate()
    {
        // Bug case: chi-square(2) == Exponential(0.5); density at 0 is the rate, 0.5.
        Calc("CHISQ.DIST(0,2,FALSE)").Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void ChiSqDist_Pdf_AtZero_WithDfGreaterThanTwo_StillReturnsZero_NoRegression()
    {
        // Sibling boundary: df>2 at x=0 is genuinely 0 density (x^(df/2-1) -> 0).
        Calc("CHISQ.DIST(0,4,FALSE)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void ChiSqDist_Pdf_InteriorValue_StillWorks_NoRegression()
    {
        // Already-working ordinary case (x>0, df=2): pdf = 0.5*e^(-x/2) = 0.5*e^-1.
        double expected = 0.5 * Math.Exp(-1);
        Calc("CHISQ.DIST(2,2,FALSE)").Should().BeApproximately(expected, 1e-10);
    }

    [Fact]
    public void ChiSqDist_Cdf_AtZero_StillZero_NoRegression()
    {
        // Cumulative branch at x=0 is unaffected by the PDF fix.
        Calc("CHISQ.DIST(0,2,TRUE)").Should().BeApproximately(0.0, 1e-12);
    }

    // ── F.DIST ──────────────────────────────────────────────────────────────

    [Fact]
    public void FDist_Pdf_AtZero_WithNumeratorDfTwo_ReturnsOne()
    {
        // Bug case: numerator df=2 gives density exactly 1 at x=0, regardless of d2.
        Calc("F.DIST(0,2,5,FALSE)").Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void FDist_Pdf_AtZero_WithNumeratorDfTwo_DifferentD2_StillReturnsOne_NoRegression()
    {
        Calc("F.DIST(0,2,20,FALSE)").Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void FDist_Pdf_AtZero_WithNumeratorDfGreaterThanTwo_StillReturnsZero_NoRegression()
    {
        // Sibling boundary: d1>2 at x=0 is genuinely 0 density.
        Calc("F.DIST(0,4,5,FALSE)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void FDist_Pdf_InteriorValue_StillWorks_NoRegression()
    {
        // Already-working ordinary case (x>0, d1=2, d2=4): for d1=2 the general F pdf reduces to
        // f(x;2,d2) = d2^(d2/2+1) / (2x+d2)^((d2+2)/2), so f(1;2,4) = 4^3 / 6^3 = 64/216 = 8/27.
        Calc("F.DIST(1,2,4,FALSE)").Should().BeApproximately(8.0 / 27.0, 1e-10);
    }

    // ── NEGBINOM.DIST ───────────────────────────────────────────────────────

    [Fact]
    public void NegBinomDist_Pmf_AtZeroFailures_WithProbabilityOne_ReturnsOne()
    {
        // Bug case: p=1 means every trial succeeds, so 0 failures before the r-th success
        // is certain.
        Calc("NEGBINOM.DIST(0,3,1,FALSE)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void NegBinomDist_Pmf_AtNonzeroFailures_WithProbabilityOne_ReturnsZero_NoRegression()
    {
        // Sibling boundary: p=1 with f>0 already worked pre-fix (finite * -Infinity = -Infinity
        // -> exp -> 0), still must hold post-fix.
        Calc("NEGBINOM.DIST(2,3,1,FALSE)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void NegBinomDist_Pmf_InteriorProbability_StillWorks_NoRegression()
    {
        // Already-working ordinary case: NEGBINOM.DIST(2,1,0.5,FALSE) = C(2,2)*0.5*0.5^2 = 0.125.
        Calc("NEGBINOM.DIST(2,1,0.5,FALSE)").Should().BeApproximately(0.125, 1e-10);
    }

    [Fact]
    public void NegBinomDist_Cdf_WithProbabilityOne_StillWorks_NoRegression()
    {
        // Cumulative branch (uses BetaInc, unaffected by the PMF fix): CDF at f=0,p=1 is 1.
        Calc("NEGBINOM.DIST(0,3,1,TRUE)").Should().BeApproximately(1.0, 1e-10);
    }

    // ── POISSON.DIST ────────────────────────────────────────────────────────

    [Fact]
    public void PoissonDist_Pmf_AtZero_WithLambdaZero_ReturnsOne()
    {
        // Bug case: a Poisson process with rate 0 never fires, so P(X=0)=1.
        Calc("POISSON.DIST(0,0,FALSE)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void PoissonDist_Pmf_AtNonzero_WithLambdaZero_ReturnsZero_NoRegression()
    {
        // Sibling boundary: lambda=0 with x>0 already worked pre-fix, still must hold.
        Calc("POISSON.DIST(1,0,FALSE)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void PoissonDist_Pmf_InteriorValue_StillWorks_NoRegression()
    {
        // Already-working ordinary case: POISSON.DIST(2,3,FALSE) = 3^2*e^-3/2! = 4.5*e^-3.
        double expected = 4.5 * Math.Exp(-3);
        Calc("POISSON.DIST(2,3,FALSE)").Should().BeApproximately(expected, 1e-10);
    }

    [Fact]
    public void PoissonDist_Cdf_WithLambdaZero_StillReturnsOne_NoRegression()
    {
        // The cumulative branch already worked (GammaInc special-cases x==0), unaffected by fix.
        Calc("POISSON.DIST(0,0,TRUE)").Should().BeApproximately(1.0, 1e-12);
    }
}
