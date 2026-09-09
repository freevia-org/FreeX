using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round 580: <c>TIMEVALUE</c> could return a cell value that is not a number.
/// <para>
/// <c>TryParseElapsedHmsText</c> reads Excel's unbounded elapsed-hours form ("36:00:00" -> 0.5)
/// behind the regex <c>^\s*(\d+)\s*:\s*([0-5]?\d)...</c>. That pattern constrains which CHARACTERS
/// may appear — digits only, so neither "NaN" nor "1e400" can reach the parse — but it does not
/// constrain HOW MANY. A long enough run of digits overflows double, so <c>hours</c> is Infinity;
/// <c>totalDays</c> is then Infinity, and the very next line is
/// <c>fraction = totalDays - Math.Floor(totalDays)</c>, which is <c>∞ - ∞</c> = <b>NaN</b>. The
/// caller returns it directly as <c>new NumberValue(fraction)</c>.
/// </para>
/// <para>
/// So the arithmetic MANUFACTURES the NaN — nothing non-finite was ever parsed (r549 found the same
/// shape in <c>Infinity % 360</c>). And it is the third time a regex has been mistaken for a bound:
/// a pattern restricts spelling, never magnitude, exactly as NumberStyles does (r550, r553, r559).
/// </para>
/// <para>
/// Excel cannot represent such a time and returns #VALUE!, so the aligned answer is for the parser
/// to report "not an elapsed-hms literal I can read" — its existing false — and let the caller fall
/// through to the error it already produces for unreadable text.
/// </para>
/// </summary>
public sealed class R580_TimevalueOverflowNaNTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, Sheet());

    [Theory]
    [InlineData(400)]
    [InlineData(320)]
    public void Timevalue_OverflowingHoursField_IsAnErrorNotANaNCellValue(int digitCount)
    {
        var hours = new string('9', digitCount);

        var result = Eval($"=TIMEVALUE(\"{hours}:00:00\")");

        if (result is NumberValue number)
            double.IsFinite(number.Value).Should().BeTrue(
                $"a {digitCount}-digit hours field produced the cell value {number.Value}");
        result.Should().BeOfType<ErrorValue>("Excel cannot represent that time and returns #VALUE!");
    }

    [Theory]
    [InlineData("36:00:00", 0.5)]
    [InlineData("25:30:00", 0.0625)]
    [InlineData("12:00:00", 0.5)]
    [InlineData("00:00:00", 0)]
    public void Timevalue_OrdinaryElapsedTimes_StillParse(string text, double expected)
    {
        // Non-vacuity: the guard must not have closed the unbounded elapsed-hours form that this
        // parser exists to support — "36:00:00" is exactly the case .NET's TimeSpan parser gets
        // wrong and this code was written to get right.
        Eval($"=TIMEVALUE(\"{text}\")").Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(expected, 1e-9);
    }
}
