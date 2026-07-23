using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R75-formula-financial-annuity-4-1: PV(rate, 0, pmt, fv, type) must return -fv (the pmt term
/// vanishes because (1+rate)^0 - 1 == 0) instead of the copy-pasted-from-PMT #DIV/0! guard.
/// PMT's own nper==0 guard is a genuine divide-by-zero and must remain intact.
///
/// R75-formula-array-matrix-4-1: FREQUENCY must propagate an ErrorValue found anywhere in
/// data_array or bins_array (matching Excel) instead of silently dropping it like a blank/text
/// cell and returning a wrong numeric histogram.
/// </summary>
public class R75_FinancialAnnuityFrequencyErrorTests
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

    private string CalcError(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<ErrorValue>($"formula {formula} should return an error");
        return ((ErrorValue)result).Code;
    }

    // ── PV(rate, 0, pmt, fv, type) == -fv (R75-formula-financial-annuity-4-1) ──────────

    [Fact]
    public void Pv_ZeroNper_NonZeroRate_ReturnsNegativeFv()
    {
        // Excel: =PV(0.05,0,100,1000) = -1000 (the pmt term vanishes since (1.05)^0-1 == 0;
        // this is NOT a real divide-by-zero the way PMT's nper==0 is).
        Calc("PV(0.05,0,100,1000)").Should().BeApproximately(-1000, 1e-9);
    }

    [Fact]
    public void Pv_ZeroNper_ZeroRate_ReturnsNegativeFv()
    {
        // Excel: =PV(0,0,100,1000) = -1000 (rate==0 branch already handled this correctly).
        Calc("PV(0,0,100,1000)").Should().BeApproximately(-1000, 1e-9);
    }

    [Fact]
    public void Pv_NonZeroNper_StillComputesNormalValue()
    {
        // No-regression: PV with a normal nonzero nper must be unaffected by removing the guard.
        // Excel: =PV(0.05,10,100) ≈ -772.173492918482
        Calc("PV(0.05,10,100)").Should().BeApproximately(-772.173492918482, 1e-6);
    }

    [Fact]
    public void Pmt_ZeroNper_StillReturnsDivByZero()
    {
        // No-regression: PMT's own nper==0 guard is a genuine divide-by-zero and must remain.
        CalcError("PMT(0.05,0,1000)").Should().Be("#DIV/0!");
    }

    [Fact]
    public void Fv_ZeroNper_StillReturnsNegativeFv()
    {
        // No-regression: FV never had the copy-pasted guard and already falls through correctly.
        Calc("FV(0.05,0,100,1000)").Should().BeApproximately(-1000, 1e-9);
    }

    // ── FREQUENCY error propagation (R75-formula-array-matrix-4-1) ─────────────────────

    [Fact]
    public void Frequency_ErrorInDataArray_PropagatesErrorToEveryOutputCell()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var result = _eval.Evaluate("=FREQUENCY(A1:A5,B1:B2)", sheet, wb);

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Frequency_ErrorInBinsArray_PropagatesError()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), ErrorValue.NA);

        var result = _eval.Evaluate("=FREQUENCY(A1:A2,B1:B2)", sheet, wb);

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Frequency_BlankAndTextCells_StillIgnoredNotError()
    {
        // No-regression: FREQUENCY({1,2,"x",,4},{2}) still ignores blank/text (only errors
        // propagate); text "x" and the blank cell A4 are silently dropped from the histogram.
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("x"));
        // A4 left blank intentionally.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));

        var result = _eval.Evaluate("=FREQUENCY(A1:A5,B1:B1)", sheet, wb);

        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        ((NumberValue)rv.At(1, 1)).Value.Should().Be(2); // 1,2 <= 2
        ((NumberValue)rv.At(2, 1)).Value.Should().Be(1); // 4 > 2
    }

    [Fact]
    public void Frequency_NormalCase_Unchanged()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (int i = 1; i <= 6; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(i));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var result = _eval.Evaluate("=FREQUENCY(A1:A6,B1:B2)", sheet, wb);
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        ((NumberValue)rv.At(1, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(2, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(3, 1)).Value.Should().Be(2);
    }
}
