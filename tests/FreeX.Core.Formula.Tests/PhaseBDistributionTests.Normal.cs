using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseBDistributionTests
{
    [Fact]
    public void NormDist_StandardNormal_CumulativeAtZero_Returns0Point5()
        => Calc("NORM.DIST(0,0,1,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormDist_StandardNormal_CumulativeAtPositive1_Returns0Point84()
        => Calc("NORM.DIST(1,0,1,TRUE)").Should().BeApproximately(0.8413447460685429, 1e-8);

    [Fact]
    public void NormDist_StandardNormal_PdfAtZero_Returns1OverSqrt2Pi()
        => Calc("NORM.DIST(0,0,1,FALSE)").Should().BeApproximately(1.0 / Math.Sqrt(2 * Math.PI), 1e-9);

    [Fact]
    public void NormDist_NonStandard_CumulativeAtMean_Returns0Point5()
        => Calc("NORM.DIST(5,5,2,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormDist_NegativeStdev_ReturnsNum()
        => CalcError("NORM.DIST(0,0,-1,TRUE)").Should().Be("#NUM!");

    // ── NORM.INV ─────────────────────────────────────────────────────────────

    [Fact]
    public void NormInv_At0Point5_ReturnsMean()
        => Calc("NORM.INV(0.5,0,1)").Should().BeApproximately(0.0, 1e-8);

    [Fact]
    public void NormInv_At0Point84_ReturnsApprox1()
        => Calc("NORM.INV(0.8413447460685429,0,1)").Should().BeApproximately(1.0, 1e-3);

    [Fact]
    public void NormInv_At0_ReturnsNum()
        => CalcError("NORM.INV(0,0,1)").Should().Be("#NUM!");

    [Fact]
    public void NormInv_At1_ReturnsNum()
        => CalcError("NORM.INV(1,0,1)").Should().Be("#NUM!");

    // ── NORM.S.DIST ──────────────────────────────────────────────────────────

    [Fact]
    public void NormSDist_CumulativeAtZero_Returns0Point5()
        => Calc("NORM.S.DIST(0,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormSDist_PdfAtZero_ReturnsCorrectValue()
        => Calc("NORM.S.DIST(0,FALSE)").Should().BeApproximately(0.3989422804014327, 1e-10);

    [Fact]
    public void Phi_ReturnsStandardNormalDensity()
        => Calc("PHI(0.75)").Should().BeApproximately(0.30113743215480443, 1e-12);

    [Fact]
    public void Gauss_ReturnsStandardNormalCumulativeMinusHalf()
        => Calc("GAUSS(2)").Should().BeApproximately(0.4772498680518208, 1e-12);

    [Fact]
    public void PhiAndGauss_InvalidNumericText_ReturnNum()
    {
        CalcError("""PHI("1E309")""").Should().Be("#NUM!");
        CalcError("""GAUSS("1E309")""").Should().Be("#NUM!");
    }

    // ── NORM.S.INV ───────────────────────────────────────────────────────────

    [Fact]
    public void NormSInv_At0Point5_Returns0()
        => Calc("NORM.S.INV(0.5)").Should().BeApproximately(0.0, 1e-8);

    [Fact]
    public void NormSInv_At0Point975_Returns1Point96()
        => Calc("NORM.S.INV(0.975)").Should().BeApproximately(1.959963984540054, 1e-3);

    // ── STANDARDIZE ──────────────────────────────────────────────────────────

    [Fact]
    public void Standardize_BasicCase()
        => Calc("STANDARDIZE(6,4,2)").Should().BeApproximately(1.0, 1e-10);

    [Fact]
    public void Standardize_ZeroStdev_ReturnsNum()
        => CalcError("STANDARDIZE(5,4,0)").Should().Be("#NUM!");

    // ── T.DIST ───────────────────────────────────────────────────────────────

    [Fact]
    public void NormalDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var sheet = MakeSheet((1, 1, 0.0), (2, 1, 1.0));

        AssertColumnApproximately(Eval("NORM.S.DIST(A1:A2,TRUE)", sheet), 0.5, 0.8413447460685429);
        AssertColumnApproximately(Eval("PHI(A1:A2)", sheet), 0.3989422804014327, 0.24197072451914337);
        AssertColumnApproximately(Eval("GAUSS(A1:A2)", sheet), 0.0, 0.3413447460685429);
        AssertColumnApproximately(Eval("NORM.DIST(A1:A2,0,1,TRUE)", sheet), 0.5, 0.8413447460685429);
        AssertColumnApproximately(Eval("STANDARDIZE(A1:A2,0,1)", sheet), 0.0, 1.0);

        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 0.8413447460685429));
        AssertColumnApproximately(Eval("NORM.S.INV(A1:A2)", probabilities), 0.0, 1.0);
        AssertColumnApproximately(Eval("NORM.INV(A1:A2,0,1)", probabilities), 0.0, 1.0);
    }

    [Fact]
    public void NormalDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 0.0), (2, 1, 1.0),
            (1, 2, 0.0), (2, 2, 1.0),
            (1, 3, 1.0), (2, 3, 1.0),
            (1, 4, 2.0), (2, 4, 4.0),
            (1, 5, 1.0), (2, 5, 2.0),
            (1, 6, 1.0), (2, 6, 2.0));

        AssertColumnApproximately(Eval("NORM.DIST(A1:A2,B1:B2,C1:C2,TRUE)", sheet), 0.5, 0.5);
        AssertColumnApproximately(Eval("STANDARDIZE(D1:D2,E1:E2,F1:F2)", sheet), 1.0, 1.0);

        var probabilities = MakeSheet(
            (1, 1, 0.5), (2, 1, 0.8413447460685429),
            (1, 2, 0.0), (2, 2, 2.0),
            (1, 3, 1.0), (2, 3, 3.0));
        AssertColumnApproximately(Eval("NORM.INV(A1:A2,B1:B2,C1:C2)", probabilities), 0.0, 5.0);

        Eval("NORM.DIST(A1:A2,B1:B3,1,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }
}
