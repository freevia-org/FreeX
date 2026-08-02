using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseBDistributionTests
{
    [Fact]
    public void TDist_CumulativeAt0_Returns0Point5()
        => Calc("T.DIST(0,10,TRUE)").Should().BeApproximately(0.5, 1e-10);

    [Fact]
    public void TDist_RightTailAt0_Returns0Point5()
        => Calc("T.DIST.RT(0,10)").Should().BeApproximately(0.5, 1e-10);

    [Fact]
    public void TDist_TwoTailSymmetry()
        => Calc("T.DIST.2T(1,10)").Should().BeApproximately(2.0 * (1.0 - Calc("T.DIST(1,10,TRUE)")), 1e-10);

    [Fact]
    public void TDist_NegativeX_ReturnsNum()
        => CalcError("T.DIST.2T(-1,10)").Should().Be("#NUM!");

    // ── T.INV ────────────────────────────────────────────────────────────────

    [Fact]
    public void TDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var sheet = MakeSheet((1, 1, 0.0), (2, 1, 1.0));

        AssertColumnApproximately(Eval("T.DIST(A1:A2,10,TRUE)", sheet), Calc("T.DIST(0,10,TRUE)"), Calc("T.DIST(1,10,TRUE)"));
        AssertColumnApproximately(Eval("T.DIST.RT(A1:A2,10)", sheet), Calc("T.DIST.RT(0,10)"), Calc("T.DIST.RT(1,10)"));
        AssertColumnApproximately(Eval("T.DIST.2T(A1:A2,10)", sheet), Calc("T.DIST.2T(0,10)"), Calc("T.DIST.2T(1,10)"));
        AssertColumnApproximately(Eval("T.INV(A1:A2,10)", MakeSheet((1, 1, 0.5), (2, 1, 0.75))), Calc("T.INV(0.5,10)"), Calc("T.INV(0.75,10)"));
        AssertColumnApproximately(Eval("T.INV.2T(A1:A2,10)", MakeSheet((1, 1, 0.5), (2, 1, 0.25))), Calc("T.INV.2T(0.5,10)"), Calc("T.INV.2T(0.25,10)"));
    }

    [Fact]
    public void TDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 0.0), (2, 1, 1.0),
            (1, 2, 10.0), (2, 2, 5.0),
            (1, 3, 1.0), (2, 3, 0.0),
            (3, 2, 8.0));

        AssertColumnApproximately(Eval("T.DIST(A1:A2,B1:B2,C1:C2)", sheet), Calc("T.DIST(0,10,TRUE)"), Calc("T.DIST(1,5,FALSE)"));
        AssertColumnApproximately(Eval("T.DIST.RT(A1:A2,B1:B2)", sheet), Calc("T.DIST.RT(0,10)"), Calc("T.DIST.RT(1,5)"));
        AssertColumnApproximately(Eval("T.DIST.2T(A1:A2,B1:B2)", sheet), Calc("T.DIST.2T(0,10)"), Calc("T.DIST.2T(1,5)"));

        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 0.75), (1, 2, 10.0), (2, 2, 5.0));
        AssertColumnApproximately(Eval("T.INV(A1:A2,B1:B2)", probabilities), Calc("T.INV(0.5,10)"), Calc("T.INV(0.75,5)"));
        AssertColumnApproximately(Eval("T.INV.2T(A1:A2,B1:B2)", probabilities), Calc("T.INV.2T(0.5,10)"), Calc("T.INV.2T(0.75,5)"));

        Eval("T.DIST(A1:A2,B1:B3,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void TInv_At0Point5_Returns0()
        => Calc("T.INV(0.5,10)").Should().BeApproximately(0.0, 1e-5);

    [Fact]
    public void TInv2T_At0Point05_10df_ReturnsApprox2Point228()
        => Calc("T.INV.2T(0.05,10)").Should().BeApproximately(2.228138852, 1e-4);

    // ── F.DIST ───────────────────────────────────────────────────────────────

    [Fact]
    public void FDist_CumulativeAt0_Returns0()
        => Calc("F.DIST(0,5,10,TRUE)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void FDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 0.5), (2, 1, 2.0));
        var probabilities = MakeSheet((1, 1, 0.25), (2, 1, 0.75));
        var rightTailProbabilities = MakeSheet((1, 1, 0.25), (2, 1, 0.05));

        AssertColumnApproximately(Eval("F.DIST(A1:A2,5,10,TRUE)", xValues), Calc("F.DIST(0.5,5,10,TRUE)"), Calc("F.DIST(2,5,10,TRUE)"));
        AssertColumnApproximately(Eval("F.DIST.RT(A1:A2,5,10)", xValues), Calc("F.DIST.RT(0.5,5,10)"), Calc("F.DIST.RT(2,5,10)"));
        AssertColumnApproximately(Eval("F.INV(A1:A2,5,10)", probabilities), Calc("F.INV(0.25,5,10)"), Calc("F.INV(0.75,5,10)"));
        AssertColumnApproximately(Eval("F.INV.RT(A1:A2,5,10)", rightTailProbabilities), Calc("F.INV.RT(0.25,5,10)"), Calc("F.INV.RT(0.05,5,10)"));
    }

    [Fact]
    public void FDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 0.5), (2, 1, 2.0),
            (1, 2, 5.0), (2, 2, 8.0),
            (1, 3, 10.0), (2, 3, 12.0),
            (1, 4, 1.0), (2, 4, 0.0));

        AssertColumnApproximately(Eval("F.DIST(A1:A2,B1:B2,C1:C2,D1:D2)", sheet), Calc("F.DIST(0.5,5,10,TRUE)"), Calc("F.DIST(2,8,12,FALSE)"));
        AssertColumnApproximately(Eval("F.DIST.RT(A1:A2,B1:B2,C1:C2)", sheet), Calc("F.DIST.RT(0.5,5,10)"), Calc("F.DIST.RT(2,8,12)"));

        var probabilities = MakeSheet(
            (1, 1, 0.25), (2, 1, 0.75),
            (1, 2, 5.0), (2, 2, 8.0),
            (1, 3, 10.0), (2, 3, 12.0));
        AssertColumnApproximately(Eval("F.INV(A1:A2,B1:B2,C1:C2)", probabilities), Calc("F.INV(0.25,5,10)"), Calc("F.INV(0.75,8,12)"));
        AssertColumnApproximately(Eval("F.INV.RT(A1:A2,B1:B2,C1:C2)", probabilities), Calc("F.INV.RT(0.25,5,10)"), Calc("F.INV.RT(0.75,8,12)"));

        Eval("F.DIST(A1:A2,B1:B3,10,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void FDist_RightTailComplementsCdf()
    {
        double cdf = Calc("F.DIST(2,5,10,TRUE)");
        double rt = Calc("F.DIST.RT(2,5,10)");
        (cdf + rt).Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void FInv_RoundTrip()
    {
        double x = Calc("F.INV(0.95,5,10)");
        double p = Calc($"F.DIST({x.ToString("R")},5,10,TRUE)");
        p.Should().BeApproximately(0.95, 1e-5);
    }

    [Fact]
    public void FInvRt_RoundTrip()
    {
        double x = Calc("F.INV.RT(0.05,5,10)");
        double rt = Calc($"F.DIST.RT({x.ToString("R")},5,10)");
        rt.Should().BeApproximately(0.05, 1e-5);
    }

    // ── CHISQ.DIST ───────────────────────────────────────────────────────────

    [Fact]
    public void ChiSqDist_CumulativeAt0_Returns0()
        => Calc("CHISQ.DIST(0,5,TRUE)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void ChiSqDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 2.0), (2, 1, 5.0));
        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 0.95));
        var rightTailProbabilities = MakeSheet((1, 1, 0.25), (2, 1, 0.05));

        AssertColumnApproximately(Eval("CHISQ.DIST(A1:A2,5,TRUE)", xValues), Calc("CHISQ.DIST(2,5,TRUE)"), Calc("CHISQ.DIST(5,5,TRUE)"));
        AssertColumnApproximately(Eval("CHISQ.DIST.RT(A1:A2,5)", xValues), Calc("CHISQ.DIST.RT(2,5)"), Calc("CHISQ.DIST.RT(5,5)"));
        AssertColumnApproximately(Eval("CHISQ.INV(A1:A2,5)", probabilities), Calc("CHISQ.INV(0.5,5)"), Calc("CHISQ.INV(0.95,5)"));
        AssertColumnApproximately(Eval("CHISQ.INV.RT(A1:A2,5)", rightTailProbabilities), Calc("CHISQ.INV.RT(0.25,5)"), Calc("CHISQ.INV.RT(0.05,5)"));
    }

    [Fact]
    public void ChiSqDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 2.0), (2, 1, 5.0),
            (1, 2, 5.0), (2, 2, 8.0),
            (1, 3, 0.5), (2, 3, 0.95));

        AssertColumnApproximately(Eval("CHISQ.DIST(A1:A2,B1:B2,TRUE)", sheet), Calc("CHISQ.DIST(2,5,TRUE)"), Calc("CHISQ.DIST(5,8,TRUE)"));
        AssertColumnApproximately(Eval("CHISQ.DIST.RT(A1:A2,B1:B2)", sheet), Calc("CHISQ.DIST.RT(2,5)"), Calc("CHISQ.DIST.RT(5,8)"));
        AssertColumnApproximately(Eval("CHISQ.INV(C1:C2,B1:B2)", sheet), Calc("CHISQ.INV(0.5,5)"), Calc("CHISQ.INV(0.95,8)"));
        AssertColumnApproximately(Eval("CHISQ.INV.RT(C1:C2,B1:B2)", sheet), Calc("CHISQ.INV.RT(0.5,5)"), Calc("CHISQ.INV.RT(0.95,8)"));

        Eval("CHISQ.DIST(A1:A2,B1:B3,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ChiSqDist_RightTailComplementsCdf()
    {
        double cdf = Calc("CHISQ.DIST(5,5,TRUE)");
        double rt = Calc("CHISQ.DIST.RT(5,5)");
        (cdf + rt).Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void ChiSqInv_RoundTrip()
    {
        double x = Calc("CHISQ.INV(0.95,5)");
        double p = Calc($"CHISQ.DIST({x.ToString("R")},5,TRUE)");
        p.Should().BeApproximately(0.95, 1e-6);
    }

    [Fact]
    public void ChiSqInvRt_RoundTrip()
    {
        double x = Calc("CHISQ.INV.RT(0.05,5)");
        double rt = Calc("CHISQ.DIST.RT(11.0705,5)");
        rt.Should().BeApproximately(0.05, 1e-3);
    }

    // ── SKEW ─────────────────────────────────────────────────────────────────
}
