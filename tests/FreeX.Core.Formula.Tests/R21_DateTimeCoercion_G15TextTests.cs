using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R21-text-functions-deep-1: DateTimeValue-to-text coercion must follow the same Excel
// General/15-significant-digit rule as NumberValue, not .NET's raw shortest-round-trippable
// ToString(). A datetime whose OADate serial has a non-terminating binary fraction (i.e. any
// non-midnight time-of-day) previously printed 17 significant digits via "&"/CONCATENATE/
// TEXTJOIN instead of Excel's 15-sig-digit text.
public sealed class R21_DateTimeCoercion_G15TextTests
{
    private readonly FormulaEvaluator _eval = new();

    // 45292.0 + 1.0/3.0 == "45292.333333333336" via plain ToString(InvariantCulture) on .NET 10
    // (17 significant digits), but "45292.3333333333" via the Excel-General G15 rule that
    // NumberValue's coercion already applies.
    private const double FractionalSerial = 45292.0 + 1.0 / 3.0;
    private const string ExpectedG15Text = "45292.3333333333";

    [Fact]
    public void ConcatOperator_CoercesDateTimeCellUsing15SignificantDigits()
    {
        var sheet = Sheet((1, 1, new DateTimeValue(FractionalSerial)));

        _eval.Evaluate("=A1&\"\"", sheet).Should().Be(new TextValue(ExpectedG15Text));
    }

    [Fact]
    public void Concatenate_CoercesDateTimeCellUsing15SignificantDigits()
    {
        var sheet = Sheet((1, 1, new DateTimeValue(FractionalSerial)));

        _eval.Evaluate("=CONCATENATE(A1,\"\")", sheet).Should().Be(new TextValue(ExpectedG15Text));
    }

    [Fact]
    public void Textjoin_CoercesDateTimeCellUsing15SignificantDigits()
    {
        var sheet = Sheet((1, 1, new DateTimeValue(FractionalSerial)));

        _eval.Evaluate("=TEXTJOIN(\"\",TRUE,A1)", sheet).Should().Be(new TextValue(ExpectedG15Text));
    }

    [Fact]
    public void ReferencedDateTimeValue_ConcatOperator_MatchesNumberValueTextForSameDouble()
    {
        var sheet = Sheet(
            (1, 1, new DateTimeValue(FractionalSerial)),
            (2, 1, new NumberValue(FractionalSerial)));

        // A DateTimeValue and a NumberValue holding the exact same bit-identical double must
        // produce the same text via "&" coercion, since Excel's General text rule does not
        // distinguish between the two for this purpose.
        _eval.Evaluate("=A1&\"\"", sheet).Should().Be(new TextValue(ExpectedG15Text));
        _eval.Evaluate("=A2&\"\"", sheet).Should().Be(new TextValue(ExpectedG15Text));
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
