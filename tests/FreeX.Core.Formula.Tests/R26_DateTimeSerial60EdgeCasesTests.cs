using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round 26 findings R26-datetime-functions-deep-1/2/3: three more places where Excel's 1900
// phantom leap day (serial 60, "1900-02-29" -- which does not exist in the real Gregorian
// calendar) was mishandled.
public sealed class R26_DateTimeSerial60EdgeCasesTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet(params (int Row, int Column, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, column, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)column), value);
        return sheet;
    }

    // ── R26-datetime-functions-deep-1: DATE(1900,2,d>=30) / DATE(1900,1,d>=61) ──────────────

    [Theory]
    [InlineData("=DATE(1900,2,30)", 61)]   // bug case: was 62
    [InlineData("=DATE(1900,2,31)", 62)]   // bug case: was 63
    [InlineData("=DATE(1900,1,61)", 61)]   // bug case: was 62
    [InlineData("=DATE(1900,1,60)", 60)]   // sibling: exact phantom-day boundary, must stay correct
    [InlineData("=DATE(1900,2,29)", 60)]   // sibling: the phantom day itself, unaffected
    [InlineData("=DATE(1900,2,28)", 59)]   // sibling: ordinary pre-boundary date, unaffected
    [InlineData("=DATE(1900,2,0)", 31)]    // sibling: ordinary pre-boundary date, unaffected
    [InlineData("=DATE(1900,3,1)", 61)]    // sibling: month>=3 branch, must stay unaffected
    [InlineData("=DATE(1900,3,0)", 60)]    // sibling: month>=3 backward-rollover branch, unaffected
    [InlineData("=DATE(2024,1,15)", 45306)] // sibling: ordinary modern date, unaffected
    public void Date_1900PhantomLeapDayRegion_ReturnsExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    // ── R26-datetime-functions-deep-2: YEARFRAC basis 1/2/3 with a serial-60 endpoint ───────

    [Fact]
    public void Yearfrac_Basis3_Serial59ToPhantomLeapDaySerial60_ReturnsOneDayFraction()
    {
        // YEARFRAC(59,60,3) == YEARFRAC(DATE(1900,2,28),DATE(1900,2,29),3): a 1-day span must
        // not collapse to 0 just because both endpoints' DateTime representation collides.
        var sheet = Sheet((1, 1, new NumberValue(59)), (1, 2, new NumberValue(60)));
        var result = _eval.Evaluate("=YEARFRAC(A1,B1,3)", sheet).Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(1.0 / 365.0, 1e-12);
    }

    [Fact]
    public void Yearfrac_Basis3_PhantomLeapDaySerial60ToSerial61_ReturnsOneDayFraction()
    {
        // YEARFRAC(60,61,3): must not over-count to 2 days just because serial 60 collides
        // with serial 59 in DateTime space.
        var sheet = Sheet((1, 1, new NumberValue(60)), (1, 2, new NumberValue(61)));
        var result = _eval.Evaluate("=YEARFRAC(A1,B1,3)", sheet).Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(1.0 / 365.0, 1e-12);
    }

    [Fact]
    public void Yearfrac_Basis3_ReversedPhantomLeapDaySerial60_StaysPositiveOneDayFraction()
    {
        // YEARFRAC(60,59,3) (reversed order): YEARFRAC always returns a non-negative
        // fraction regardless of argument order, even across the 59/60 collision.
        var sheet = Sheet((1, 1, new NumberValue(60)), (1, 2, new NumberValue(59)));
        var result = _eval.Evaluate("=YEARFRAC(A1,B1,3)", sheet).Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(1.0 / 365.0, 1e-12);
    }

    [Fact]
    public void Yearfrac_Basis3_OrdinaryPair_UnaffectedByFix()
    {
        // Sibling: DATE(1900,1,1)=1, DATE(1900,3,1)=61 -- neither endpoint is serial 60, so
        // this must keep returning the pre-existing pinned value (60/365).
        var result = _eval.Evaluate("=YEARFRAC(DATE(1900,1,1),DATE(1900,3,1),3)", Sheet())
            .Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(60.0 / 365.0, 1e-12);
    }

    // ── R26-datetime-functions-deep-3: DAYS360 / YEARFRAC basis 0/4 with a serial-60 endpoint ─

    [Fact]
    public void Days360_PhantomLeapDaySerial60Start_UsMethod_UsesDay29NotDay28()
    {
        // DAYS360(60, DATE(1900,3,31), FALSE): the start's day-of-month component must be 29
        // (matching DAY(60)=29), not the DateTime-collapsed 28.
        var sheet = Sheet((1, 1, new NumberValue(60)));
        _eval.Evaluate("=DAYS360(A1,DATE(1900,3,31),FALSE)", sheet).Should().Be(new NumberValue(32));
    }

    [Fact]
    public void Days360_OrdinaryPair_UnaffectedByFix()
    {
        // Sibling: neither endpoint is serial 60 -- pre-existing pinned value (3) must hold.
        _eval.Evaluate("=DAYS360(DATE(1900,2,28),DATE(1900,3,1))", Sheet()).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Yearfrac_Basis0_PhantomLeapDaySerial60Start_UsesDay29NotDay28()
    {
        var sheet = Sheet((1, 1, new NumberValue(60)));
        var result = _eval.Evaluate("=YEARFRAC(A1,DATE(1900,3,31),0)", sheet)
            .Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(32.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Yearfrac_Basis4_PhantomLeapDaySerial60Start_UsesDay29NotDay28()
    {
        var sheet = Sheet((1, 1, new NumberValue(60)));
        var result = _eval.Evaluate("=YEARFRAC(A1,DATE(1900,3,31),4)", sheet)
            .Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(31.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Yearfrac_Basis0_OrdinaryPair_UnaffectedByFix()
    {
        // Sibling: ordinary (non-1900) last-day-of-February case must keep applying the
        // existing NASD Feb-end adjustment unchanged.
        var result = _eval.Evaluate("=YEARFRAC(DATE(2024,2,29),DATE(2024,3,31),0)", Sheet())
            .Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(30.0 / 360.0, 1e-12);
    }
}
