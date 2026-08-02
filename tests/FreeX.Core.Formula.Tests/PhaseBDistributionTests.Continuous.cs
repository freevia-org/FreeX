using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseBDistributionTests
{
    [Fact]
    public void GammaAndLognormalFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 1.0), (2, 1, 2.0));

        AssertColumnApproximately(Eval("GAMMA(A1:A2)", xValues), 1.0, 1.0);
        AssertColumnApproximately(Eval("GAMMALN(A1:A2)", xValues), 0.0, 0.0);
        AssertColumnApproximately(Eval("GAMMA.DIST(A1:A2,1,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("LOGNORM.DIST(A1:A2,0,1,TRUE)", xValues), 0.5, NormSCdfForTest(Math.Log(2.0)));

        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 1.0 - Math.Exp(-2.0)));
        AssertColumnApproximately(Eval("GAMMA.INV(A1:A2,1,1)", probabilities), Math.Log(2.0), 2.0);
        AssertColumnApproximately(Eval("LOGNORM.INV(A1:A2,0,1)", MakeSheet((1, 1, 0.5), (2, 1, 0.8413447460685429))), 1.0, Math.E);
    }

    [Fact]
    public void GammaLognormalAndWeibullFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 1.0), (2, 1, 2.0),
            (1, 2, 1.0), (2, 2, 2.0),
            (1, 3, 1.0), (2, 3, 3.0),
            (1, 4, 0.0), (2, 4, 1.0),
            (1, 5, 0.5), (2, 5, 0.75),
            (1, 6, 0.0), (2, 6, 0.5),
            (1, 7, 1.0), (2, 7, 1.5));

        AssertColumnApproximately(Eval("WEIBULL.DIST(A1:A2,B1:B2,C1:C2,D1:D2)", sheet), Calc("WEIBULL.DIST(1,1,1,FALSE)"), Calc("WEIBULL.DIST(2,2,3,TRUE)"));
        AssertColumnApproximately(Eval("GAMMA.DIST(A1:A2,B1:B2,C1:C2,D1:D2)", sheet), Calc("GAMMA.DIST(1,1,1,FALSE)"), Calc("GAMMA.DIST(2,2,3,TRUE)"));
        AssertColumnApproximately(Eval("GAMMA.INV(E1:E2,B1:B2,C1:C2)", sheet), Calc("GAMMA.INV(0.5,1,1)"), Calc("GAMMA.INV(0.75,2,3)"));
        AssertColumnApproximately(Eval("LOGNORM.DIST(A1:A2,F1:F2,G1:G2,D1:D2)", sheet), Calc("LOGNORM.DIST(1,0,1,FALSE)"), Calc("LOGNORM.DIST(2,0.5,1.5,TRUE)"));
        AssertColumnApproximately(Eval("LOGNORM.INV(E1:E2,F1:F2,G1:G2)", sheet), Calc("LOGNORM.INV(0.5,0,1)"), Calc("LOGNORM.INV(0.75,0.5,1.5)"));

        Eval("WEIBULL.DIST(A1:A2,B1:B3,1,TRUE)", sheet).Should().Be(ErrorValue.Value);
        Eval("GAMMA.DIST(A1:A2,B1:B3,1,TRUE)", sheet).Should().Be(ErrorValue.Value);
        Eval("LOGNORM.DIST(A1:A2,F1:F3,1,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void BetaDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 0.25), (2, 1, 0.5));

        AssertColumnApproximately(Eval("BETA.DIST(A1:A2,1,1,TRUE)", xValues), 0.25, 0.5);
        AssertColumnApproximately(Eval("BETA.DIST(A1:A2,1,1,FALSE)", xValues), 1.0, 1.0);

        var probabilities = MakeSheet((1, 1, 0.25), (2, 1, 0.5));
        AssertColumnApproximately(Eval("BETA.INV(A1:A2,1,1)", probabilities), 0.25, 0.5);
    }

    [Fact]
    public void BetaDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 0.25), (2, 1, 0.5),
            (1, 2, 1.0), (2, 2, 2.0),
            (1, 3, 1.0), (2, 3, 3.0),
            (1, 4, 1.0), (2, 4, 0.0),
            (1, 5, 0.0), (2, 5, 0.0),
            (1, 6, 1.0), (2, 6, 2.0));

        AssertColumnApproximately(Eval("BETA.DIST(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2,F1:F2)", sheet), Calc("BETA.DIST(0.25,1,1,TRUE,0,1)"), Calc("BETA.DIST(0.5,2,3,FALSE,0,2)"));
        AssertColumnApproximately(Eval("BETA.INV(A1:A2,B1:B2,C1:C2,E1:E2,F1:F2)", sheet), Calc("BETA.INV(0.25,1,1,0,1)"), Calc("BETA.INV(0.5,2,3,0,2)"));

        Eval("BETA.DIST(A1:A2,B1:B3,1,TRUE)", sheet).Should().Be(ErrorValue.Value);
        Eval("BETA.INV(A1:A2,B1:B3,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SimpleDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 1.0), (2, 1, 2.0));

        AssertColumnApproximately(Eval("EXPON.DIST(A1:A2,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("WEIBULL.DIST(A1:A2,1,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("POISSON.DIST(A1:A2,2,FALSE)", xValues), 2.0 * Math.Exp(-2.0), 2.0 * Math.Exp(-2.0));
    }

    [Fact]
    public void ExponDist_Cdf_KnownCase()
    {
        // EXPON.DIST(0.2,10,TRUE) = 1 - e^(-10*0.2) = 1 - e^-2
        double result = Calc("EXPON.DIST(0.2,10,TRUE)");
        result.Should().BeApproximately(1.0 - Math.Exp(-2.0), 1e-10);
    }

    [Fact]
    public void ExponDist_Pdf_KnownCase()
    {
        // EXPON.DIST(0.2,10,FALSE) = 10 * e^(-10*0.2)
        double result = Calc("EXPON.DIST(0.2,10,FALSE)");
        result.Should().BeApproximately(10.0 * Math.Exp(-2.0), 1e-10);
    }

    // ── WEIBULL.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void WeibullDist_Cdf_KnownCase()
    {
        // WEIBULL.DIST(105,20,100,TRUE)
        double result = Calc("WEIBULL.DIST(105,20,100,TRUE)");
        result.Should().BeApproximately(0.929581185, 1e-6);
    }

    // ── GAMMA.DIST / GAMMA.INV ────────────────────────────────────────────────

    [Fact]
    public void GammaDist_Pdf_KnownCase()
    {
        // GAMMA.DIST(10,9,1,FALSE): x^8 * e^-10 / Gamma(9) = 10^8 * e^-10 / 8!
        double expected = Math.Pow(10, 8) * Math.Exp(-10) / 40320.0;
        double result = Calc("GAMMA.DIST(10,9,1,FALSE)");
        result.Should().BeApproximately(expected, 1e-8);
    }

    [Fact]
    public void GammaInv_RoundTrip()
    {
        double x = Calc("GAMMA.INV(0.9,9,1)");
        double p = Calc($"GAMMA.DIST({x.ToString("R")},9,1,TRUE)");
        p.Should().BeApproximately(0.9, 1e-6);
    }

    [Fact]
    public void Gamma_PositiveInteger_ReturnsFactorialMinusOne()
        => Calc("GAMMA(5)").Should().BeApproximately(24.0, 1e-12);

    // ── GAMMALN ───────────────────────────────────────────────────────────────

    [Fact]
    public void GammaLn_At1_Returns0()
        => Calc("GAMMALN(1)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void GammaLn_At0Point5_ReturnsHalfLogPi()
        => Calc("GAMMALN(0.5)").Should().BeApproximately(0.5723649429, 1e-6);

    // ── BETA.DIST / BETA.INV ──────────────────────────────────────────────────

    [Fact]
    public void BetaDist_Cdf_MonotonicAndBounded()
    {
        // CDF should be strictly increasing: F(0.2) < F(0.5) < F(0.8)
        double p02 = Calc("BETA.DIST(0.2,8,10,TRUE)");
        double p05 = Calc("BETA.DIST(0.5,8,10,TRUE)");
        double p08 = Calc("BETA.DIST(0.8,8,10,TRUE)");
        p02.Should().BeInRange(0, 1);
        p02.Should().BeLessThan(p05);
        p05.Should().BeLessThan(p08);
        p08.Should().BeLessThan(1.0);
    }

    [Fact]
    public void BetaDist_Cdf_WithBounds()
    {
        // BETA.DIST(2,8,10,TRUE,1,3) maps x to (x-1)/(3-1)=0.5 → I_0.5(8,10)
        double result = Calc("BETA.DIST(2,8,10,TRUE,1,3)");
        double unbounded = Calc("BETA.DIST(0.5,8,10,TRUE)");
        result.Should().BeApproximately(unbounded, 1e-8);
    }

    [Fact]
    public void BetaInv_RoundTrip()
    {
        double x = Calc("BETA.INV(0.7,8,10)");
        double p = Calc($"BETA.DIST({x.ToString("R")},8,10,TRUE)");
        p.Should().BeApproximately(0.7, 1e-6);
    }

    // ── LOGNORM.DIST / LOGNORM.INV ────────────────────────────────────────────

    [Fact]
    public void LognormDist_Cdf_KnownCase()
    {
        // LOGNORM.DIST(4,3.5,1.2,TRUE)
        double result = Calc("LOGNORM.DIST(4,3.5,1.2,TRUE)");
        result.Should().BeApproximately(0.0390835, 1e-4);
    }

    [Fact]
    public void LognormInv_RoundTrip()
    {
        double x = Calc("LOGNORM.INV(0.039,3.5,1.2)");
        double p = Calc($"LOGNORM.DIST({x.ToString("R")},3.5,1.2,TRUE)");
        p.Should().BeApproximately(0.039, 1e-4);
    }

    // ── T.TEST round-trip check ──────────────────────────────────────────────
}
