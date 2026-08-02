using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseBDistributionTests
{
    [Fact]
    public void BinomDist_Pmf_KnownCase()
    {
        // BINOM.DIST(6,10,0.5,FALSE) = C(10,6) * 0.5^10
        double result = Calc("BINOM.DIST(6,10,0.5,FALSE)");
        result.Should().BeApproximately(0.2050781250, 1e-8);
    }

    [Fact]
    public void DiscreteDistributionFunctions_RangeArguments_SpillElementwise()
    {
        var counts = MakeSheet((1, 1, 4.0), (2, 1, 6.0));
        var alphaValues = MakeSheet((1, 1, 0.25), (2, 1, 0.75));
        var sampleSuccesses = MakeSheet((1, 1, 0.0), (2, 1, 1.0));

        AssertColumnApproximately(Eval("BINOM.DIST(A1:A2,10,0.5,FALSE)", counts), Calc("BINOM.DIST(4,10,0.5,FALSE)"), Calc("BINOM.DIST(6,10,0.5,FALSE)"));
        AssertColumnApproximately(Eval("BINOM.INV(10,0.5,A1:A2)", alphaValues), Calc("BINOM.INV(10,0.5,0.25)"), Calc("BINOM.INV(10,0.5,0.75)"));
        AssertColumnApproximately(Eval("NEGBINOM.DIST(A1:A2,5,0.25,FALSE)", counts), Calc("NEGBINOM.DIST(4,5,0.25,FALSE)"), Calc("NEGBINOM.DIST(6,5,0.25,FALSE)"));
        AssertColumnApproximately(Eval("HYPGEOM.DIST(A1:A2,4,2,10,FALSE)", sampleSuccesses), Calc("HYPGEOM.DIST(0,4,2,10,FALSE)"), Calc("HYPGEOM.DIST(1,4,2,10,FALSE)"));
    }

    [Fact]
    public void DiscreteDistributionFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 4.0), (2, 1, 6.0),
            (1, 2, 8.0), (2, 2, 10.0),
            (1, 3, 0.25), (2, 3, 0.5),
            // D1/D2 lowered from 5/6 to 4/5 so the HYPGEOM.DIST row below stays within its
            // documented domain (sample_s >= max(0, sample_size - population_size + population_s));
            // the original 5/6 values made both rows mathematically impossible sample_s draws
            // (R20-statistical-functions-2: HYPGEOM.DIST now correctly returns #NUM! for those).
            (1, 4, 4.0), (2, 4, 5.0),
            (1, 5, 0.0), (2, 5, 1.0),
            (1, 6, 0.0), (2, 6, 1.0));

        AssertColumnApproximately(Eval("BINOM.DIST(A1:A2,B1:B2,C1:C2,E1:E2)", sheet), Calc("BINOM.DIST(4,8,0.25,FALSE)"), Calc("BINOM.DIST(6,10,0.5,TRUE)"));
        AssertColumnApproximately(Eval("BINOM.INV(B1:B2,C1:C2,A1:A2/10)", sheet), Calc("BINOM.INV(8,0.25,0.4)"), Calc("BINOM.INV(10,0.5,0.6)"));
        AssertColumnApproximately(Eval("NEGBINOM.DIST(A1:A2,D1:D2,C1:C2,E1:E2)", sheet), Calc("NEGBINOM.DIST(4,4,0.25,FALSE)"), Calc("NEGBINOM.DIST(6,5,0.5,TRUE)"));
        AssertColumnApproximately(Eval("HYPGEOM.DIST(E1:E2,A1:A2,D1:D2,B1:B2,F1:F2)", sheet), Calc("HYPGEOM.DIST(0,4,4,8,FALSE)"), Calc("HYPGEOM.DIST(1,6,5,10,TRUE)"));

        Eval("BINOM.DIST(A1:A2,B1:B3,0.5,FALSE)", sheet).Should().Be(ErrorValue.Value);
        Eval("NEGBINOM.DIST(A1:A2,B1:B3,0.5,FALSE)", sheet).Should().Be(ErrorValue.Value);
        Eval("HYPGEOM.DIST(E1:E2,A1:A3,8,5,FALSE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void BinomDist_Cumulative_KnownCase()
    {
        // BINOM.DIST(6,10,0.5,TRUE)
        double result = Calc("BINOM.DIST(6,10,0.5,TRUE)");
        result.Should().BeApproximately(0.828125, 1e-6);
    }

    [Fact]
    public void BinomDist_InvalidProbability_ReturnsNum()
        => CalcError("BINOM.DIST(6,10,1.5,FALSE)").Should().Be("#NUM!");

    // ── BINOM.INV ────────────────────────────────────────────────────────────

    [Fact]
    public void BinomInv_BasicCase()
    {
        // BINOM.INV(10,0.5,0.75) = 6
        double result = Calc("BINOM.INV(10,0.5,0.75)");
        result.Should().BeApproximately(6.0, 1e-10);
    }

    // ── POISSON.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void PoissonDist_Pmf_KnownCase()
    {
        // POISSON.DIST(2,5,FALSE) = e^-5 * 5^2 / 2! = 0.08422...
        double result = Calc("POISSON.DIST(2,5,FALSE)");
        result.Should().BeApproximately(0.08422433748, 1e-8);
    }

    [Fact]
    public void PoissonDist_Cumulative_KnownCase()
    {
        // POISSON.DIST(2,5,TRUE)
        double result = Calc("POISSON.DIST(2,5,TRUE)");
        result.Should().BeApproximately(0.12465201948, 1e-8);
    }

    // ── HYPGEOM.DIST ────────────────────────────────────────────────────────

    [Fact]
    public void HypergeomDist_Pmf_KnownCase()
    {
        // HYPGEOM.DIST(1,4,2,10,FALSE): P(X=1) when drawing 4 from pop 10 with 2 successes
        double result = Calc("HYPGEOM.DIST(1,4,2,10,FALSE)");
        result.Should().BeApproximately(0.5333333333, 1e-6);
    }

    // ── NEGBINOM.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void NegbinomDist_Pmf_KnownCase()
    {
        // NEGBINOM.DIST(10,5,0.25,FALSE)
        double result = Calc("NEGBINOM.DIST(10,5,0.25,FALSE)");
        result.Should().BeApproximately(0.0550487637, 1e-6);
    }

    // ── EXPON.DIST ────────────────────────────────────────────────────────────

    [Fact]
    public void TTest_TwoSampleEqualVariance_ReturnsValidPValue()
    {
        // Two independent samples with some overlap → p-value in (0,1)
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] a = [2, 3, 4, 5, 6];
        double[] b = [5, 6, 7, 8, 9];
        for (int i = 0; i < a.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(a[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(b[i]));
        }
        var result = _eval.Evaluate("=T.TEST(A1:A5,B1:B5,2,2)", sheet, wb);
        result.Should().BeOfType<NumberValue>("T.TEST should return a number");
        double p = ((NumberValue)result).Value;
        p.Should().BeInRange(0, 1, "p-value must be in [0,1]");
        p.Should().BeLessThan(0.1, "means differ by 3 units — should be significant");
    }

    // ── CHISQ.TEST round-trip check ──────────────────────────────────────────

    [Fact]
    public void FTest_IdenticalSamples_ReturnsOne()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (int i = 1; i <= 4; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(i));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 2), new NumberValue(i));
        }

        var result = _eval.Evaluate("=F.TEST(A1:A4,B1:B4)", sheet, wb);

        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void ChiSqTest_LargeDivergence_ReturnsSmallPValue()
    {
        // Highly divergent observed vs expected → small p-value
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] obs = [50, 5, 5];
        double[] exp = [20, 20, 20];
        for (int i = 0; i < obs.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(obs[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(exp[i]));
        }
        var result = _eval.Evaluate("=CHISQ.TEST(A1:A3,B1:B3)", sheet, wb);
        result.Should().BeOfType<NumberValue>("CHISQ.TEST should return a number");
        double p = ((NumberValue)result).Value;
        p.Should().BeInRange(0, 1);
        p.Should().BeLessThan(0.001, "large divergence should give very small p-value");
    }

    // ── FREQUENCY ────────────────────────────────────────────────────────────

    [Fact]
    public void BinomDistRange_SinglePoint_MatchesBinomDistPmf()
    {
        double point = Calc("BINOM.DIST(3,10,0.5,FALSE)");
        double range = Calc("BINOM.DIST.RANGE(10,0.5,3,3)");
        range.Should().BeApproximately(point, 1e-10);
    }

    [Fact]
    public void BinomDistRange_AllValues_Returns1()
        => Calc("BINOM.DIST.RANGE(10,0.5,0,10)").Should().BeApproximately(1.0, 1e-10);

    [Fact]
    public void BinomDistRange_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, 10.0), (2, 1, 12.0),
            (1, 2, 0.5), (2, 2, 0.25),
            (1, 3, 3.0), (2, 3, 2.0),
            (1, 4, 5.0), (2, 4, 4.0));

        AssertColumnApproximately(
            Eval("BINOM.DIST.RANGE(A1:A2,B1:B2,C1:C2,D1:D2)", sheet),
            Calc("BINOM.DIST.RANGE(10,0.5,3,5)"),
            Calc("BINOM.DIST.RANGE(12,0.25,2,4)"));
        AssertColumnApproximately(
            Eval("BINOM.DIST.RANGE(A1:A2,B1:B2,C1:C2)", sheet),
            Calc("BINOM.DIST.RANGE(10,0.5,3)"),
            Calc("BINOM.DIST.RANGE(12,0.25,2)"));

        Eval("BINOM.DIST.RANGE(A1:A2,B1:B3,3)", sheet).Should().Be(ErrorValue.Value);
    }
}
