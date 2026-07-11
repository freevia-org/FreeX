using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R22-datetime-functions-3: DAYS and DATEDIF(...,"D") used to round-trip their operands through
// DateTime (DateToSerial(end)-DateToSerial(start)). ExcelDateSystem.SerialToDate maps both serial
// 59 ("1900-02-28") and serial 60 (Excel's phantom 1900 leap day, "1900-02-29" — which does not
// exist in the real Gregorian calendar) onto the identical DateTime value, so that round trip
// silently collapsed the two, e.g. DAYS(60,59) returned 0 instead of the correct 1. The fix
// computes directly in serial space (ExcelDateSystem.SerialDayDifference) instead.
public sealed class R22_PhantomLeapDaySerialArithmeticTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Days_PhantomLeapDaySerial60_MinusSerial59_ReturnsOne()
    {
        // DAYS(60,59) == DAYS(DATE(1900,2,29), DATE(1900,2,28)) — Excel treats these as
        // consecutive serials/days, so the result must be 1, not 0.
        var sheet = Sheet((1, 1, new NumberValue(60)), (2, 1, new NumberValue(59)));

        _eval.Evaluate("=DAYS(A1,A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Days_Serial59_MinusPhantomLeapDaySerial60_ReturnsNegativeOne()
    {
        var sheet = Sheet((1, 1, new NumberValue(59)), (2, 1, new NumberValue(60)));

        _eval.Evaluate("=DAYS(A1,A2)", sheet).Should().Be(new NumberValue(-1));
    }

    [Fact]
    public void Days_OrdinaryPair_UnaffectedByFix()
    {
        // Regression guard: the direct serial-arithmetic rewrite must not change ordinary,
        // far-from-the-1900-boundary DAYS results.
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 11).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())));

        _eval.Evaluate("=DAYS(A1,A2)", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Datedif_D_PhantomLeapDaySerial60_MinusSerial59_ReturnsOne()
    {
        // DATEDIF(59,60,"D") — same underlying collapse bug as DAYS, reached via DatedifScalar's
        // "D" unit rather than the DAYS function directly.
        var sheet = Sheet((1, 1, new NumberValue(59)), (2, 1, new NumberValue(60)));

        _eval.Evaluate("=DATEDIF(A1,A2,\"D\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Datedif_D_OrdinaryPair_UnaffectedByFix()
    {
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 1, 11).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,A2,\"D\")", sheet).Should().Be(new NumberValue(10));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        }

        return sheet;
    }
}
