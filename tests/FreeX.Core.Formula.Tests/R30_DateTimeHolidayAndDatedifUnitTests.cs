using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R30-datetime-deep-1: WORKDAY/WORKDAY.INTL/NETWORKDAYS/NETWORKDAYS.INTL silently dropped
// text-literal (and text-cell) holiday dates because TryCollectHolidays only recognized
// NumberValue/DateTimeValue via TryCellNumber. A holiday given as "1/2/2024" was ignored
// entirely, so it never reduced the workday count. Fixed by coercing a text/DirectTextLiteral
// scalar holiday through the same ExcelTextNumberParser text-date path already used for the
// start/end/days scalar arguments (see ToNumber). This deliberately does NOT extend to text
// cells inside a holidays *range* — ranges keep ignoring text, matching how ranges elsewhere
// (e.g. SUM) ignore text cells while a direct scalar argument coerces.
//
// R30-datetime-deep-2: DATEDIF with an invalid/unrecognized unit returned #VALUE! but real
// Excel returns #NUM!.
public sealed class R30_DateTimeHolidayAndDatedifUnitTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Networkdays_TextLiteralHoliday_IsExcludedFromCount()
    {
        // 2024-01-01 (Mon) .. 2024-01-10 (Wed) has 8 weekdays. "1/2/2024" (Tue) as a text
        // literal holiday must be recognized and excluded, leaving 7.
        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,1),DATE(2024,1,10))", MakeSheet())
            .Should().Be(new NumberValue(8));

        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,1),DATE(2024,1,10),\"1/2/2024\")", MakeSheet())
            .Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Networkdays_TextCellHoliday_IsExcludedFromCount()
    {
        // Same as the literal case above, but the holiday text comes from a referenced cell
        // (TextValue) rather than a direct formula literal (DirectTextLiteralValue).
        var sheet = MakeSheet((1, 3, new TextValue("1/2/2024")));

        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,1),DATE(2024,1,10),C1)", sheet)
            .Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Networkdays_NumericHoliday_StillExcludedFromCount()
    {
        // Sibling already-working case: a numeric/date-serial holiday must keep working
        // exactly as before after the text-coercion fix.
        _eval.Evaluate("=NETWORKDAYS(DATE(2024,1,1),DATE(2024,1,10),DATE(2024,1,2))", MakeSheet())
            .Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Workday_TextLiteralHoliday_IsSkipped()
    {
        // 2024-01-08 (Mon) + 5 workdays with no holidays lands on 2024-01-15 (Mon).
        // Making 2024-01-15 itself a text-literal holiday pushes the result to 2024-01-16.
        double expectedNoHoliday = new DateTime(2024, 1, 15).ToOADate();
        double expectedWithHoliday = new DateTime(2024, 1, 16).ToOADate();

        ((NumberValue)_eval.Evaluate("=WORKDAY(DATE(2024,1,8),5)", MakeSheet())).Value
            .Should().BeApproximately(expectedNoHoliday, 1);

        ((NumberValue)_eval.Evaluate("=WORKDAY(DATE(2024,1,8),5,\"1/15/2024\")", MakeSheet())).Value
            .Should().BeApproximately(expectedWithHoliday, 1);
    }

    [Fact]
    public void Datedif_InvalidUnit_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 4, 1).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,B1,\"Q\")", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=DATEDIF(A1,B1,\"XYZ\")", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Datedif_ValidUnit_StillWorksAfterInvalidUnitFix()
    {
        // Sibling already-working case: a recognized unit must be unaffected by changing the
        // default/invalid-unit arm from #VALUE! to #NUM!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(new DateTime(2024, 1, 1).ToOADate())),
            (1, 2, new NumberValue(new DateTime(2024, 4, 1).ToOADate())));

        _eval.Evaluate("=DATEDIF(A1,B1,\"M\")", sheet).Should().Be(new NumberValue(3));
    }

    private static Sheet MakeSheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        }

        return sheet;
    }
}
