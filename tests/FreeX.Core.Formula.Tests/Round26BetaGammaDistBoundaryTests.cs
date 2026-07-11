using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R26-statistical-distribution-precision-2: GAMMA.DIST(x,alpha,beta,FALSE) at x=0 with alpha=1
/// (the exponential-density special case) used to return #NUM! instead of 1/beta. The pdf formula
/// computed (alpha-1)*Math.Log(x), and at x=0/alpha=1 that is 0*Math.Log(0) = 0*(-Infinity) = NaN,
/// which NumberResult then turned into #NUM!. Mathematically x^(alpha-1) = 0^0 = 1 at that
/// boundary, so the log-term should contribute 0. Fixed in
/// BuiltInFunctions.StatisticalDistributions.BetaGamma.cs (GammaDistScalar) by special-casing
/// x==0 &amp;&amp; alpha==1 before the log-based computation runs.
///
/// R26-statistical-distribution-precision-3: BETA.DIST(x,alpha,beta,FALSE) at the textbook
/// Beta(1,1)=Uniform(0,1) boundary (x=A with alpha=1, or symmetrically x=B with beta=1) hit the
/// identical 0*Math.Log(0) = NaN pattern in both the (alpha-1)*Math.Log(t) and the
/// (beta-1)*Math.Log(1-t) terms. Fixed in the same file (BetaDistScalar) by special-casing each
/// term independently at its own degenerate boundary.
/// </summary>
public sealed class Round26BetaGammaDistBoundaryTests
{
    private readonly FormulaEvaluator _eval = new();

    private double Calc(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    // ── GAMMA.DIST ──────────────────────────────────────────────────────────

    [Fact]
    public void GammaDist_Pdf_AtZero_WithAlphaOne_ReturnsExponentialDensity()
    {
        // Bug case: GAMMA.DIST(0,1,1,FALSE) is Exponential(rate=1) at 0, density = 1/beta = 1.
        Calc("GAMMA.DIST(0,1,1,FALSE)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void GammaDist_Pdf_AtZero_WithAlphaOneAndOtherBeta_ReturnsOneOverBeta()
    {
        // Same bug, non-trivial beta: density at 0 is 1/beta = 0.2.
        Calc("GAMMA.DIST(0,1,5,FALSE)").Should().BeApproximately(0.2, 1e-12);
    }

    [Fact]
    public void GammaDist_Pdf_AtZero_WithAlphaGreaterThanOne_StillReturnsZero_NoRegression()
    {
        // Sibling boundary case: alpha>1 at x=0 is genuinely 0 density (x^(alpha-1) -> 0), and
        // this already worked before the fix (positive-finite * -Infinity = -Infinity, no NaN).
        Calc("GAMMA.DIST(0,2,1,FALSE)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void GammaDist_Pdf_OrdinaryValue_StillWorks_NoRegression()
    {
        // Already-working ordinary case (x>0): GAMMA.DIST(10,9,1,FALSE) = 10^8 * e^-10 / 8!
        double expected = Math.Pow(10, 8) * Math.Exp(-10) / 40320.0;
        Calc("GAMMA.DIST(10,9,1,FALSE)").Should().BeApproximately(expected, 1e-8);
    }

    // ── BETA.DIST ───────────────────────────────────────────────────────────

    [Fact]
    public void BetaDist_Pdf_AtLowerBound_WithAlphaOne_ReturnsUniformDensity()
    {
        // Bug case: BETA.DIST(0,1,1,FALSE) is Beta(1,1) = Uniform(0,1), density is 1 everywhere,
        // including the left endpoint.
        Calc("BETA.DIST(0,1,1,FALSE)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void BetaDist_Pdf_AtUpperBound_WithBetaOne_ReturnsMirroredDensity_NoRegression()
    {
        // Mirrored bug case called out in the evidence: x=B with beta=1 hits the same
        // 0*Math.Log(0) pattern in the (1-t) term. Beta(2,1) density at t=1 is 2.
        Calc("BETA.DIST(1,2,1,FALSE)").Should().BeApproximately(2.0, 1e-8);
    }

    [Fact]
    public void BetaDist_Pdf_AtInteriorPoint_WithAlphaOne_StillWorks_NoRegression()
    {
        // Already-working case: Beta(1,1)=Uniform(0,1) density is 1 at any interior point too.
        Calc("BETA.DIST(0.5,1,1,FALSE)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void BetaDist_Pdf_OrdinaryValue_StillWorks_NoRegression()
    {
        // Already-working ordinary case: Beta(2,3) pdf at 0.5 = 0.5*0.25/Beta(2,3) = 1.5.
        Calc("BETA.DIST(0.5,2,3,FALSE)").Should().BeApproximately(1.5, 1e-8);
    }
}
