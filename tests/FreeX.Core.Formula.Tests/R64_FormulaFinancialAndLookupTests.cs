using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-64 formula-bucket regression tests:
///
/// R64-formula-financial-6-1: IRR/XIRR's guess argument (BuiltInFunctions.Financial.CashFlow.cs)
/// and the IRR direct-range fast path (FormulaEvaluator.FinancialFastPaths.cs,
/// TryEvaluateIrrDirectRange) treated an explicitly-supplied-but-blank guess argument (e.g. a
/// reference to an empty cell) the same as a genuinely omitted one, substituting the
/// omitted-argument default (0.1) instead of letting it flow through normal blank-to-0 numeric
/// coercion -- mirrors the r56 RATE fix.
///
/// R64-formula-financial-6-2: CUMIPMT/CUMPRINC (BuiltInFunctions.Financial.LoanPayments.cs) summed
/// via an unbounded per-period for-loop, so a large-but-bounds-valid nper (e.g. billions of
/// periods) with a full-span start/end would hang; replaced with a closed-form annuity balance
/// calculation.
///
/// R64-formula-lookup-modern-6-1: XLOOKUP with an array lookup_value (BuiltInFunctions.Lookup.
/// Modern.cs, XlookupRangeLookupValues) wrongly returned #VALUE! when the lookup_value's own row/
/// column orientation was crossed vs. lookup_array's orientation, even on a full match -- the
/// reshape logic assumed a per-hit orientation based on lookupValues' own shape instead of the
/// actual per-hit RangeValue orientation decided by lookupIsVertical/XlookupReturnAt.
/// </summary>
public sealed class R64_FormulaFinancialAndLookupTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // --- R64-formula-financial-6-1 ---

    [Fact]
    public void Irr_ExplicitBlankGuessReference_CoercesToZeroNotDefault()
    {
        // A1 is a genuinely blank cell, explicitly referenced (not omitted) as IRR's 2nd (guess)
        // argument. Excel coerces a blank-cell numeric argument to 0, seeding Newton's method
        // differently than the omitted-argument default of 0.1 -- for this cash flow the two
        // seeds converge to different (both legitimate) roots, so the results must differ.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(-1600)), (2, 2, new NumberValue(10000)), (3, 2, new NumberValue(-10000)));

        var withExplicitBlankGuess = _eval.Evaluate("=IRR(B1:B3,A1)", sheet);
        var withOmittedGuess = _eval.Evaluate("=IRR(B1:B3)", sheet);

        withExplicitBlankGuess.Should().BeOfType<NumberValue>();
        withOmittedGuess.Should().BeOfType<NumberValue>();
        ((NumberValue)withExplicitBlankGuess).Value.Should().NotBe(((NumberValue)withOmittedGuess).Value);
    }

    [Fact]
    public void Irr_OmittedGuess_StillDefaultsToPointOne()
    {
        // Sibling no-regression: a genuinely omitted 2nd argument must still default to Excel's
        // 0.1 guess -- the direct-range fast path and the slow path must agree.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(-1600)), (2, 2, new NumberValue(10000)), (3, 2, new NumberValue(-10000)));

        var directRange = _eval.Evaluate("=IRR(B1:B3)", sheet);
        var explicitDefault = _eval.Evaluate("=IRR(B1:B3,0.1)", sheet);

        directRange.Should().BeOfType<NumberValue>();
        explicitDefault.Should().BeOfType<NumberValue>();
        ((NumberValue)directRange).Value.Should().BeApproximately(((NumberValue)explicitDefault).Value, 1e-9);
    }

    [Fact]
    public void Xirr_ExplicitBlankGuessReference_CoercesToZeroNotDefault()
    {
        // Same blank-guess-reference contract as IRR, for XIRR: A1 is explicitly referenced as
        // the blank 3rd (guess) argument and must coerce to 0.0.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(-10000)), (2, 2, new NumberValue(2750)), (3, 2, new NumberValue(4250)), (4, 2, new NumberValue(3250)), (5, 2, new NumberValue(2750)),
            (1, 3, new DateTimeValue(DateOnlyToSerial(2020, 1, 1))),
            (2, 3, new DateTimeValue(DateOnlyToSerial(2020, 3, 1))),
            (3, 3, new DateTimeValue(DateOnlyToSerial(2020, 10, 30))),
            (4, 3, new DateTimeValue(DateOnlyToSerial(2021, 2, 15))),
            (5, 3, new DateTimeValue(DateOnlyToSerial(2021, 4, 1))));

        var withExplicitBlankGuess = _eval.Evaluate("=XIRR(B1:B5,C1:C5,A1)", sheet);
        var withOmittedGuess = _eval.Evaluate("=XIRR(B1:B5,C1:C5)", sheet);

        withExplicitBlankGuess.Should().BeOfType<NumberValue>();
        withOmittedGuess.Should().BeOfType<NumberValue>();
        // Both seeds converge to the same well-behaved root for this cash flow (Newton is stable
        // here), so the coercion contract itself is verified via the direct guess=0 comparison
        // below rather than requiring a different converged root.
        var explicitZeroGuess = _eval.Evaluate("=XIRR(B1:B5,C1:C5,0)", sheet);
        explicitZeroGuess.Should().BeOfType<NumberValue>();
        ((NumberValue)withExplicitBlankGuess).Value.Should().BeApproximately(((NumberValue)explicitZeroGuess).Value, 1e-9);
    }

    [Fact]
    public void Xirr_OmittedGuess_StillDefaultsToPointOne()
    {
        // Sibling no-regression: XIRR with an omitted guess must still use Excel's 0.1 default.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(-10000)), (2, 2, new NumberValue(2750)), (3, 2, new NumberValue(4250)), (4, 2, new NumberValue(3250)), (5, 2, new NumberValue(2750)),
            (1, 3, new DateTimeValue(DateOnlyToSerial(2020, 1, 1))),
            (2, 3, new DateTimeValue(DateOnlyToSerial(2020, 3, 1))),
            (3, 3, new DateTimeValue(DateOnlyToSerial(2020, 10, 30))),
            (4, 3, new DateTimeValue(DateOnlyToSerial(2021, 2, 15))),
            (5, 3, new DateTimeValue(DateOnlyToSerial(2021, 4, 1))));

        var omitted = _eval.Evaluate("=XIRR(B1:B5,C1:C5)", sheet);
        var explicitDefault = _eval.Evaluate("=XIRR(B1:B5,C1:C5,0.1)", sheet);

        omitted.Should().BeOfType<NumberValue>();
        explicitDefault.Should().BeOfType<NumberValue>();
        ((NumberValue)omitted).Value.Should().BeApproximately(((NumberValue)explicitDefault).Value, 1e-9);
    }

    private static double DateOnlyToSerial(int year, int month, int day)
    {
        // Excel 1900 date system serial number (matches ExcelDateSystem used elsewhere in tests).
        var epoch = new DateTime(1899, 12, 30);
        var date = new DateTime(year, month, day);
        return (date - epoch).TotalDays;
    }

    // --- R64-formula-financial-6-2 ---

    [Fact]
    public void Cumipmt_SmallSpan_MatchesNaiveLoopSum()
    {
        // rate=0.01, nper=360, pv=100000, start=1, end=12: verify the closed form matches an
        // independently-computed naive per-period loop sum to a tight tolerance.
        var sheet = new Sheet(SheetId.New(), "S");
        var result = _eval.Evaluate("=CUMIPMT(0.01,360,100000,1,12,0)", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(-11980.471816382955, 1e-6);
    }

    [Fact]
    public void Cumprinc_SmallSpan_MatchesNaiveLoopSum()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var result = _eval.Evaluate("=CUMPRINC(0.01,360,100000,1,12,0)", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(-362.8793467230979, 1e-6);
    }

    [Fact]
    public void Cumipmt_SmallSpan_Type1_MatchesNaiveLoopSum()
    {
        // Sibling no-regression: type=1 (annuity-due) closed form must also match the naive loop.
        var sheet = new Sheet(SheetId.New(), "S");
        var result = _eval.Evaluate("=CUMIPMT(0.01,360,100000,1,12,1)", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(-10871.754273646487, 1e-6);
    }

    [Fact]
    public void Cumprinc_SmallSpan_Type1_MatchesNaiveLoopSum()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var result = _eval.Evaluate("=CUMPRINC(0.01,360,100000,1,12,1)", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(-1349.3854918050488, 1e-6);
    }

    [Fact]
    public void Cumipmt_HugeNper_FullSpanCappedByArgs_CompletesQuickly()
    {
        // A bounds-valid but astronomically large nper with a full [1, nper] span used to hang
        // for minutes iterating a per-period loop; the closed form must resolve near-instantly.
        var sheet = new Sheet(SheetId.New(), "S");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _eval.Evaluate("=CUMIPMT(0.0000001,2000000000,100000,1,2000000000,0)", sheet);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
        result.Should().BeOfType<NumberValue>();
    }

    [Fact]
    public void Cumprinc_HugeNper_FullSpanCappedByArgs_CompletesQuickly()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _eval.Evaluate("=CUMPRINC(0.0000001,2000000000,100000,1,2000000000,0)", sheet);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
        result.Should().BeOfType<NumberValue>();
        // Over the FULL life of the loan, all principal must be repaid.
        ((NumberValue)result).Value.Should().BeApproximately(-100000, 1e-3);
    }

    // --- R64-formula-lookup-modern-6-1 ---

    [Fact]
    public void Xlookup_HorizontalLookupValue_VerticalLookupArray_MultiColumnReturn_SpillsGrid()
    {
        // A1:A3 = ids (vertical lookup_array), B1:C3 = return columns, E1:G1 = 3 queried ids
        // (horizontal lookup_value). Each query must pair with its own [B,C] row -- a 3x2 spill.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("b1")), (2, 2, new TextValue("b2")), (3, 2, new TextValue("b3")),
            (1, 3, new TextValue("c1")), (2, 3, new TextValue("c2")), (3, 3, new TextValue("c3")),
            (1, 5, new NumberValue(20)), (1, 6, new NumberValue(30)), (1, 7, new NumberValue(10)));

        var result = _eval.Evaluate("=XLOOKUP(E1:G1,A1:A3,B1:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("b2"));
        result.At(1, 2).Should().Be(new TextValue("c2"));
        result.At(2, 1).Should().Be(new TextValue("b3"));
        result.At(2, 2).Should().Be(new TextValue("c3"));
        result.At(3, 1).Should().Be(new TextValue("b1"));
        result.At(3, 2).Should().Be(new TextValue("c1"));
    }

    [Fact]
    public void Xlookup_VerticalLookupValue_HorizontalLookupArray_MultiRowReturn_SpillsGrid()
    {
        // A1:C1 = ids (horizontal lookup_array), A2:C3 = return rows (2 rows x 3 cols), E1:E3 =
        // 3 queried ids (vertical lookup_value). Mirror of the case above -- must spill 2x3.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)), (1, 3, new NumberValue(30)),
            (2, 1, new TextValue("r2c1")), (2, 2, new TextValue("r2c2")), (2, 3, new TextValue("r2c3")),
            (3, 1, new TextValue("r3c1")), (3, 2, new TextValue("r3c2")), (3, 3, new TextValue("r3c3")),
            (1, 5, new NumberValue(20)), (2, 5, new NumberValue(30)), (3, 5, new NumberValue(10)));

        var result = _eval.Evaluate("=XLOOKUP(E1:E3,A1:C1,A2:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new TextValue("r2c2"));
        result.At(2, 1).Should().Be(new TextValue("r3c2"));
        result.At(1, 2).Should().Be(new TextValue("r2c3"));
        result.At(2, 2).Should().Be(new TextValue("r3c3"));
        result.At(1, 3).Should().Be(new TextValue("r2c1"));
        result.At(2, 3).Should().Be(new TextValue("r3c1"));
    }

    [Fact]
    public void Xlookup_VerticalLookupValue_VerticalLookupArray_AlignedCase_StillWorks()
    {
        // Sibling no-regression: the aligned vertical/vertical case must be unaffected.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("b1")), (2, 2, new TextValue("b2")), (3, 2, new TextValue("b3")),
            (1, 3, new TextValue("c1")), (2, 3, new TextValue("c2")), (3, 3, new TextValue("c3")),
            (1, 5, new NumberValue(20)), (2, 5, new NumberValue(30)));

        var result = _eval.Evaluate("=XLOOKUP(E1:E2,A1:A3,B1:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("b2"));
        result.At(1, 2).Should().Be(new TextValue("c2"));
        result.At(2, 1).Should().Be(new TextValue("b3"));
        result.At(2, 2).Should().Be(new TextValue("c3"));
    }

    [Fact]
    public void Xlookup_HorizontalLookupValue_HorizontalLookupArray_AlignedCase_StillWorks()
    {
        // Sibling no-regression: the aligned horizontal/horizontal case must be unaffected.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)), (1, 3, new NumberValue(30)),
            (2, 1, new TextValue("r2c1")), (2, 2, new TextValue("r2c2")), (2, 3, new TextValue("r2c3")),
            (3, 1, new TextValue("r3c1")), (3, 2, new TextValue("r3c2")), (3, 3, new TextValue("r3c3")),
            (1, 5, new NumberValue(20)), (1, 6, new NumberValue(30)));

        var result = _eval.Evaluate("=XLOOKUP(E1:F1,A1:C1,A2:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("r2c2"));
        result.At(2, 1).Should().Be(new TextValue("r3c2"));
        result.At(1, 2).Should().Be(new TextValue("r2c3"));
        result.At(2, 2).Should().Be(new TextValue("r3c3"));
    }
}
