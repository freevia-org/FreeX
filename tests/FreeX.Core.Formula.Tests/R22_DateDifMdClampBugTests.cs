using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R22-datetime-functions-1: DateDifMD's clamp of start.Day against days-in-END's-month was
// copy-pasted from DateDifYD (which genuinely needs to clamp a Feb-29 start when constructing
// a real DateTime anchor). DateDifMD is pure integer arithmetic and never constructs a DateTime,
// so the clamp served no crash-prevention purpose — it just silently corrupted ordinary MD pairs
// whenever start.Day is 29/30/31 and end's month is shorter (e.g. Feb, or a 30-day month when
// start.Day == 31).
public sealed class R22_DateDifMdClampBugTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Datedif_Md_OrdinaryPairWithShorterEndMonth_ReturnsExcelValue()
    {
        // DATEDIF(DATE(2019,1,30), DATE(2019,2,28), "MD") — real Excel returns 29 (end.Day(28)
        // < start.Day(30), so it wraps: 28 + daysInJan2019(31) - 30 = 29). The buggy clamp
        // instead did Math.Min(30, daysInFeb2019=28) = 28, then 28 - 28 = 0.
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2019, 1, 30).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2019, 2, 28).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,A2,\"MD\")", sheet).Should().Be(new NumberValue(29));
    }

    [Fact]
    public void Datedif_Md_ClassicJanuary31ToMarch1Quirk_StillReturnsNegativeTwo()
    {
        // The well-known documented Excel MD quirk: DATEDIF(1/31/2018,3/1/2018,"MD") = -2.
        // This case must continue to hold after removing the clamp (start.Day=31 <=
        // daysInMarch2018=31, so the clamp was already a no-op here).
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2018, 1, 31).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2018, 3, 1).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,A2,\"MD\")", sheet).Should().Be(new NumberValue(-2));
    }

    [Fact]
    public void Datedif_Md_LeapDayStart_NonLeapAnchorYear_StillDoesNotThrowAndReturnsZero()
    {
        // Existing regression: DATEDIF(DATE(2020,2,29), DATE(2021,3,1), "MD") — Excel returns 0.
        // This is pure integer arithmetic (day.Day(1) < start.Day(29), wraps to
        // 1 + daysInFeb2021(28) - 29 = 0) and must still work without throwing after the clamp
        // is removed, since removing the clamp does not reintroduce any DateTime construction.
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2020, 2, 29).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2021, 3, 1).ToOADate())));

        var result = _eval.Evaluate("=DATEDIF(A1,A2,\"MD\")", sheet);
        result.Should().NotBe(ErrorValue.Num, "leap-day start in non-leap anchor year must not throw #NUM!");
        result.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Datedif_Md_31DayStartToThirtyDayEndMonth_ReturnsExcelValue()
    {
        // DATEDIF(DATE(2024,5,31), DATE(2024,6,30), "MD") — end.Day(30) < start.Day(31), so
        // wraps: 30 + daysInMay2024(31) - 31 = 30. The buggy clamp instead did
        // Math.Min(31, daysInJune2024=30) = 30, then 30 - 30 = 0.
        var sheet = Sheet(
            (1, 1, new NumberValue(new DateTime(2024, 5, 31).ToOADate())),
            (2, 1, new NumberValue(new DateTime(2024, 6, 30).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,A2,\"MD\")", sheet).Should().Be(new NumberValue(30));
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
