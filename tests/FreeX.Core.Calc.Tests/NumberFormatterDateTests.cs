using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class NumberFormatterDateTests
{
    [Fact]
    public void Format_InvalidDateSerialFallsBackToSerialInsteadOfThrowing()
    {
        var action = () => NumberFormatter.Format(new DateTimeValue(9_999_999), "General");

        action.Should().NotThrow();
        action().Should().Be("9999999");
    }

    [Fact]
    public void Format_DateTimeValueWithNumericFormat_RendersSerialNumber()
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292.125), "0.00");

        result.Should().Be("45292.13");
    }

    [Fact]
    public void Format_DateTimeValueWithTintedThemeColorDirective_RendersDateText()
    {
        var result = NumberFormatter.Format(
            new DateTimeValue(new DateTime(2026, 6, 3).ToOADate()),
            "[ThemeAccent1Tint40]yyyy-mm-dd");

        result.Should().Be("2026-06-03");
    }
}
