using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-84 bucket "formula-annuity-numfmt-precision": three independent small Core.Formula gaps.
///
/// R84-formula-financial-annuity-5-1 (BuiltInFunctions.Financial.LoanValues.cs): PMT/PV/FV/NPER
/// keyed their zero-rate shortcut on Math.Abs(rate) &lt; 1e-10 alone, ignoring nper -- so a tiny but
/// nonzero rate compounded over a huge nper (rate*nper far from negligible) wrongly took the exact
/// -zero-rate closed form instead of the true annuity formula. Excel only special-cases a rate that
/// is exactly (bit-for-bit) 0.0.
///
/// R84-render-numfmt-display-5-1 (NumberFormatter.cs): a numeric value under a Text ("@") number
/// format never showed the '#####' width-overflow indicator, no matter how narrow the column --
/// Excel still renders such a value via General numeric formatting (Text format does not convert an
/// already-numeric cell to a string), including the standard overflow fallback.
///
/// R84-calc-precision-display-5-2 (BuiltInFunctions.StatisticalCore.Aggregates.cs /
/// BuiltInFunctions.MathCore.Aggregates.cs): SUM/SUMPRODUCT accumulated their running total with no
/// 15-significant-digit rounding, unlike the +,-,*,/,^ binary arithmetic operators
/// (FormulaEvaluator.Operators.cs), so SUM(range) could diverge from the textually-expanded chain of
/// + over the same cells even though Excel keeps the two forms interchangeable.
/// </summary>
public sealed class R84_AnnuityNumFmtPrecisionTests
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

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // ── 5-1: PMT/PV/FV/NPER zero-rate branch must key on an exact rate==0 ─────────────────────

    [Fact]
    public void Pmt_TinyNonzeroRate_HugeNper_UsesClosedFormNotZeroRateShortcut()
    {
        // rate*nper == 0.05 here (far from negligible), so PMT must use the true closed-form
        // annuity formula (same one it already uses for rate >= 1e-10), NOT the exact-zero
        // shortcut -(pv+fv)/nper == -10.0. True closed form: rn = (1+5e-11)^1e9 = e^0.05
        // (to overwhelming precision), pmt = -(pv*rn)*rate/(rn-1) ~= -10.252082.
        var pmt = Calc("PMT(0.00000000005,1000000000,10000000000)");
        pmt.Should().NotBeApproximately(-10.0, 0.01, "a rate this small compounded over 1e9 periods is not negligible");
        pmt.Should().BeApproximately(-10.252082419551602, 1e-6);
    }

    [Fact]
    public void Pmt_ExactZeroRate_StillUsesZeroRateShortcut()
    {
        // No-regression: a genuinely-zero rate must still take the fast/exact -(pv+fv)/nper path.
        // PMT(0, 10, 1000) == -100.
        Calc("PMT(0,10,1000)").Should().BeApproximately(-100.0, 1e-9);
    }

    [Fact]
    public void Nper_TinyNonzeroRate_HugeNper_UsesClosedFormNotZeroRateShortcut()
    {
        // Solve direction of the same bug: NPER must recover ~1e9 (using the pmt from the PMT
        // test above), not the zero-rate-shortcut answer of ~9.75e8 (a ~2.5% divergence).
        var nper = Calc("NPER(0.00000000005,-10.252082419551602,10000000000)");
        nper.Should().BeApproximately(1000000000, 2000000, "the zero-rate shortcut would be ~2.5% off (~9.75e8)");
    }

    [Fact]
    public void Nper_ExactZeroRate_StillUsesZeroRateShortcut()
    {
        // No-regression: NPER(0, pmt, pv) == -(pv+fv)/pmt exactly.
        Calc("NPER(0,-100,1000)").Should().BeApproximately(10.0, 1e-9);
    }

    // ── 5-1 (render): numeric value under "@" (Text) format must overflow to '#####' ───────────

    [Fact]
    public void TextFormat_NumericValueTooWideForColumn_ShowsHashFillSizedToColumn()
    {
        // A 15-digit number under a Text ("@") format still renders via General numeric
        // formatting -- once even the narrowest (scientific-notation) General rendering no
        // longer fits the column's character budget, Excel shows '#' sized to the column,
        // exactly like the General-format dispatch two lines above. At width 1 (a 4-character
        // budget once the General-format digit-bonus is applied) even "1E+14" (5 chars) can't
        // fit, so this must fall back to a single '#' rather than the raw 15-digit string.
        NumberFormatter.Format(new NumberValue(123456789012345), "@", 1)
            .Should().Be("#");
    }

    [Fact]
    public void TextFormat_NumericValueFitsColumn_ShowsFullValue()
    {
        // No-regression: a numeric value under "@" that DOES fit the column still renders in
        // full (via General formatting), and text values are untouched by width at all.
        NumberFormatter.Format(new NumberValue(42), "@", 20).Should().Be("42");
        NumberFormatter.Format(new TextValue("hello"), "@", 2).Should().Be("hello");
    }

    // ── 5-2: SUM/SUMPRODUCT must apply the same 15-sig rounding as +,-,*,/,^ ───────────────────

    [Fact]
    public void Sum_OfRepeatedTenths_RoundsToFifteenSignificantDigitsLikeArithmeticOperators()
    {
        // Summing 0.1 ten times via raw sequential IEEE-754 double addition (exactly how
        // BuiltInFunctions.Sum's `total += value` loop accumulates) drifts to
        // 0.99999999999999989 -- 17 apparent significant digits of linear-summation noise, not
        // the mathematically-exact 1.0. SUM must round its final total to 15 significant digits
        // exactly like every +,-,*,/,^ result does (FormulaEvaluator.Operators.cs), instead of
        // returning the raw unrounded accumulator.
        const int n = 10;
        double naiveTotal = 0;
        for (var i = 0; i < n; i++) naiveTotal += 0.1;
        var expected = FormulaEvaluator.RoundTo15SignificantDigits(naiveTotal);
        naiveTotal.Should().NotBe(expected, "the raw linear-accumulation total must actually carry rounding noise for this test to be meaningful");
        expected.Should().Be(1.0);

        var cells = Enumerable.Range(1, n).Select(r => (r, 1, (ScalarValue)new NumberValue(0.1))).ToArray();
        var sheet = MakeSheet(cells);

        var sumResult = _eval.Evaluate($"=SUM(A1:A{n})", sheet);

        sumResult.Should().BeOfType<NumberValue>();
        ((NumberValue)sumResult).Value.Should().Be(expected,
            "SUM must round its total to 15 significant digits, matching the arithmetic-operator path");
    }

    [Fact]
    public void Sum_OfSimpleIntegers_StillReturnsExactValue()
    {
        // No-regression: ordinary SUM inputs that carry no floating-point noise must be
        // completely unaffected by the added 15-sig rounding.
        Calc("SUM(1,2,3,4,5)").Should().Be(15.0);
    }

    [Fact]
    public void Sumproduct_OfRepeatedTenths_RoundsToFifteenSignificantDigitsLikeArithmeticOperators()
    {
        // Same underlying gap as SUM, but for SUMPRODUCT's running total (BuiltInFunctions.
        // MathCore.Aggregates.cs): 10 products of (0.1 * 1.0) accumulate the same raw
        // linear-summation noise (0.99999999999999989, not 1.0) and must be rounded like every
        // arithmetic operator result.
        const int n = 10;
        double naiveTotal = 0;
        for (var i = 0; i < n; i++) naiveTotal += 0.1 * 1.0;
        var expected = FormulaEvaluator.RoundTo15SignificantDigits(naiveTotal);
        naiveTotal.Should().NotBe(expected, "the raw linear-accumulation total must actually carry rounding noise for this test to be meaningful");
        expected.Should().Be(1.0);

        var cells = Enumerable.Range(1, n)
            .SelectMany(r => new (int, int, ScalarValue)[]
            {
                (r, 1, new NumberValue(0.1)),
                (r, 2, new NumberValue(1.0)),
            })
            .ToArray();
        var sheet = MakeSheet(cells);

        var sumproductResult = _eval.Evaluate($"=SUMPRODUCT(A1:A{n},B1:B{n})", sheet);

        sumproductResult.Should().BeOfType<NumberValue>();
        ((NumberValue)sumproductResult).Value.Should().Be(expected,
            "SUMPRODUCT must round its total to 15 significant digits, matching the arithmetic-operator path");
    }

    [Fact]
    public void Sumproduct_OfSimpleIntegers_StillReturnsExactValue()
    {
        // No-regression: ordinary SUMPRODUCT inputs unaffected by the added 15-sig rounding.
        Calc("SUMPRODUCT({1,2,3},{4,5,6})").Should().Be(32.0);
    }
}
