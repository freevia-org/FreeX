using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for two ExcelTextNumberParser defects fixed in fix/text-number-coercion-20260612:
/// 1. Bare month/day names ("March", "Monday") no longer coerce — they need at least one digit.
/// 2. Comma thousands grouping is validated — "1,2" and "12,34" are #VALUE!, not silently parsed.
/// </summary>
public sealed class ExcelTextNumberParserCoercionTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        return _eval.Evaluate(formula, sheet);
    }

    // ── Bug 1: bare month / day names must be #VALUE! ────────────────────────

    [Theory]
    [InlineData("=\"March\"+0")]
    [InlineData("=\"march\"+0")]
    [InlineData("=\"MARCH\"+0")]
    [InlineData("=\"Monday\"+0")]
    [InlineData("=\"January\"+0")]
    [InlineData("=\"December\"+0")]
    public void BareMonthOrDayName_IsValueError(string formula) =>
        Eval(formula).Should().Be(ErrorValue.Value);

    [Fact]
    public void BareMonthName_ValueFunction_IsValueError() =>
        Eval("=VALUE(\"March\")").Should().Be(ErrorValue.Value);

    // ── Dates that contain digits still work ──────────────────────────────────

    [Fact]
    public void MonthAndDay_CoercesToSerial()
    {
        // "March 14" contains a digit → coerces to a date serial in the current year
        var result = Eval("=\"March 14\"+0");
        result.Should().BeOfType<NumberValue>()
            .Subject.Value.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("=\"3/14\"+0")]
    [InlineData("=\"2026-03-14\"+0")]
    public void DateWithDigitsAndSeparator_CoercesToSerial(string formula) =>
        Eval(formula).Should().BeOfType<NumberValue>()
            .Subject.Value.Should().BeGreaterThan(0);

    [Fact]
    public void TimeText_CoercesToFraction()
    {
        // "1:30 PM" → 13.5/24 = 0.5625
        Eval("=\"1:30 PM\"*24").Should().Be(new NumberValue(13.5));
    }

    [Fact]
    public void IsoDateText_CoercesToSerial()
    {
        // "2026-03-14" → fixed serial (not year-dependent)
        var result = Eval("=\"2026-03-14\"+0");
        result.Should().BeOfType<NumberValue>()
            .Subject.Value.Should().BeGreaterThan(45000); // well past 2023
    }

    // ── Bug 2: thousands grouping must be validated ───────────────────────────

    [Theory]
    [InlineData("=\"1,2\"+0")]
    [InlineData("=\"12,34\"+0")]
    [InlineData("=\"1,2345\"+0")]
    [InlineData("=\"1,23\"+0")]
    public void BadGrouping_IsValueError(string formula) =>
        Eval(formula).Should().Be(ErrorValue.Value);

    [Theory]
    [InlineData("=\"1,234\"+0", 1234.0)]
    [InlineData("=\"1,234,567.5\"+0", 1234567.5)]
    [InlineData("=0+\"$1,234.50\"", 1234.5)]
    public void CorrectGrouping_CoercesToNumber(string formula, double expected) =>
        Eval(formula).Should().Be(new NumberValue(expected));

    [Theory]
    [InlineData("=VALUE(\"1,2\")")]
    [InlineData("=VALUE(\"12,34\")")]
    [InlineData("=VALUE(\"1,2345\")")]
    public void ValueFunction_BadGrouping_IsValueError(string formula) =>
        Eval(formula).Should().Be(ErrorValue.Value);

    [Theory]
    [InlineData("=VALUE(\"1,234\")", 1234.0)]
    [InlineData("=VALUE(\"1,234,567.5\")", 1234567.5)]
    [InlineData("=VALUE(\"$1,234.50\")", 1234.5)]
    public void ValueFunction_CorrectGrouping_CoercesToNumber(string formula, double expected) =>
        Eval(formula).Should().Be(new NumberValue(expected));

    // ── Plain numbers, negatives, decimals, scientific notation — unchanged ───

    [Theory]
    [InlineData("=\"42\"+0", 42.0)]
    [InlineData("=\"-3.5\"+0", -3.5)]
    [InlineData("=\"1.5e2\"+0", 150.0)]
    [InlineData("=\"50%\"+0", 0.5)]
    [InlineData("=\"50%%\"+0", 0.005)]
    public void PlainNumbers_StillCoerce(string formula, double expected) =>
        Eval(formula).Should().Be(new NumberValue(expected));
}
