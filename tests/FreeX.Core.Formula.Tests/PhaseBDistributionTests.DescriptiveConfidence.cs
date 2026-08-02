using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseBDistributionTests
{
    [Fact]
    public void Skew_SymmetricData_ReturnsZero()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] vals = [-2, -1, 0, 1, 2];
        for (int i = 0; i < vals.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(vals[i]));
        var result = _eval.Evaluate("=SKEW(A1:A5)", sheet, wb);
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void Skew_KnownValues_ReturnsCorrect()
    {
        // Excel: SKEW(3,4,5,2,3,4,5,6,4,7) = 0.3595...
        double result = Calc("SKEW(3,4,5,2,3,4,5,6,4,7)");
        result.Should().BeApproximately(0.3595430714, 1e-5);
    }

    [Fact]
    public void SkewP_SymmetricPopulation_ReturnsZero()
        => Calc("SKEW.P(-2,-1,0,1,2)").Should().BeApproximately(0.0, 1e-12);

    [Fact]
    public void Skew_TooFewValues_ReturnsDivByZero()
        => CalcError("SKEW(1,2)").Should().Be("#DIV/0!");

    // ── KURT ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Kurt_KnownValues_ReturnsCorrect()
    {
        // Excel: KURT(3,4,5,2,3,4,5,6,4,7) = -0.151799...
        double result = Calc("KURT(3,4,5,2,3,4,5,6,4,7)");
        result.Should().BeApproximately(-0.1517985612, 1e-5);
    }

    [Fact]
    public void Kurt_TooFewValues_ReturnsDivByZero()
        => CalcError("KURT(1,2,3)").Should().Be("#DIV/0!");

    // ── CONFIDENCE.NORM ───────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceNorm_BasicCase()
    {
        // CONFIDENCE.NORM(0.05,2.5,50): z≈1.96, result=z*2.5/sqrt(50)
        double result = Calc("CONFIDENCE.NORM(0.05,2.5,50)");
        result.Should().BeApproximately(0.6929671390, 5e-3);
    }

    [Fact]
    public void ConfidenceNorm_MatchesExcelCachedResult()
        => Calc("CONFIDENCE.NORM(0.05,2.5,50)")
            .Should().BeApproximately(0.69295191217483865, 1e-12);

    [Fact]
    public void Confidence_LegacyAliasMatchesConfidenceNorm()
        => Calc("CONFIDENCE(0.05,2.5,50)")
            .Should().BeApproximately(Calc("CONFIDENCE.NORM(0.05,2.5,50)"), 1e-12);

    [Fact]
    public void ConfidenceNorm_InvalidAlpha_ReturnsNum()
        => CalcError("CONFIDENCE.NORM(0,2.5,50)").Should().Be("#NUM!");

    // ── CONFIDENCE.T ─────────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceFunctions_RangeAlphaArgument_SpillElementwise()
    {
        var sheet = MakeSheet((1, 1, 0.05), (2, 1, 0.10));

        AssertColumnApproximately(Eval("CONFIDENCE.NORM(A1:A2,2.5,50)", sheet), Calc("CONFIDENCE.NORM(0.05,2.5,50)"), Calc("CONFIDENCE.NORM(0.10,2.5,50)"));
        AssertColumnApproximately(Eval("CONFIDENCE(A1:A2,2.5,50)", sheet), Calc("CONFIDENCE(0.05,2.5,50)"), Calc("CONFIDENCE(0.10,2.5,50)"));
        AssertColumnApproximately(Eval("CONFIDENCE.T(A1:A2,2.5,10)", sheet), Calc("CONFIDENCE.T(0.05,2.5,10)"), Calc("CONFIDENCE.T(0.10,2.5,10)"));

        var parameters = MakeSheet((1, 1, 2.5), (2, 1, 3.0), (1, 2, 50.0), (2, 2, 75.0));
        AssertColumnApproximately(Eval("CONFIDENCE.NORM(0.05,A1:A2,B1:B2)", parameters), Calc("CONFIDENCE.NORM(0.05,2.5,50)"), Calc("CONFIDENCE.NORM(0.05,3,75)"));
        AssertColumnApproximately(Eval("CONFIDENCE(0.05,A1:A2,B1:B2)", parameters), Calc("CONFIDENCE(0.05,2.5,50)"), Calc("CONFIDENCE(0.05,3,75)"));
        AssertColumnApproximately(Eval("CONFIDENCE.T(0.05,A1:A2,B1:B2)", parameters), Calc("CONFIDENCE.T(0.05,2.5,50)"), Calc("CONFIDENCE.T(0.05,3,75)"));

        Eval("CONFIDENCE.NORM(0.05,A1:A2,B1:B3)", parameters).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ConfidenceT_BasicCase()
    {
        // CONFIDENCE.T(0.05,2.5,10): t(9,0.975)*2.5/sqrt(10)
        double result = Calc("CONFIDENCE.T(0.05,2.5,10)");
        result.Should().BeApproximately(1.7872985, 5e-3);
    }

    // ── BINOM.DIST ────────────────────────────────────────────────────────────

    [Fact]
    public void VarS_AndStdevS_UseSampleStatistics()
    {
        Calc("VAR.S(1,2,3)").Should().BeApproximately(1.0, 1e-12);
        Calc("STDEV.S(1,2,3)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void ForecastLinear_UsesKnownYThenKnownXArgumentOrder()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (int i = 1; i <= 3; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(2 * i + 1));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 2), new NumberValue(i));
        }

        var result = _eval.Evaluate("=FORECAST.LINEAR(4,A1:A3,B1:B3)", sheet, wb);

        result.Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Frequency_BasicCounts()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        // Data: 1,2,3,4,5,6 in A1:A6; Bins: 2,4 in B1:B2
        for (int i = 1; i <= 6; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(i));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var result = _eval.Evaluate("=FREQUENCY(A1:A6,B1:B2)", sheet, wb);
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        // Bucket 1: <=2 → 2 items; Bucket 2: >2 and <=4 → 2 items; Bucket 3: >4 → 2 items
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        ((NumberValue)rv.At(1, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(2, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(3, 1)).Value.Should().Be(2);
    }

    // ── BINOM.DIST.RANGE ─────────────────────────────────────────────────────
}
