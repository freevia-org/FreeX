using FluentAssertions;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartDialogValueParserTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("auto", null)]
    [InlineData(" Auto ", null)]
    [InlineData("12.5", 12.5)]
    public void TryParseNullableDouble_AcceptsBlankAutoAndFiniteNumbers(string text, double? expected)
    {
        ChartDialogValueParser.TryParseNullableDouble(text, out var value).Should().BeTrue();

        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("abc")]
    public void TryParseNullableDouble_RejectsNonFiniteOrInvalidNumbers(string text) =>
        ChartDialogValueParser.TryParseNullableDouble(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("1", true, 1.0)]
    [InlineData("0", false, null)]
    [InlineData("-1", false, null)]
    public void TryParseNullablePositiveDouble_RequiresPositiveValuesWhenPresent(
        string text,
        bool expectedResult,
        double? expectedValue)
    {
        ChartDialogValueParser.TryParseNullablePositiveDouble(text, out var value).Should().Be(expectedResult);

        if (expectedResult)
            value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    public void TryParsePositiveDouble_RequiresPositiveFiniteValue(string text, bool expected)
    {
        ChartDialogValueParser.TryParsePositiveDouble(text, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData("0.5", 0.5, 10, true)]
    [InlineData("10", 0.5, 10, true)]
    [InlineData("0.49", 0.5, 10, false)]
    [InlineData("10.01", 0.5, 10, false)]
    public void TryParseClampedDouble_RequiresFiniteValueInsideRange(string text, double min, double max, bool expected) =>
        ChartDialogValueParser.TryParseClampedDouble(text, min, max, out _).Should().Be(expected);

    [Fact]
    public void TryParseNullableDouble_AcceptsCommaDecimalInCommaDecimalLocale()
    {
        using var _ = TestCultureScope.CurrentCulture("de-DE");

        ChartDialogValueParser.TryParseNullableDouble("2,5", out var value).Should().BeTrue();
        value.Should().Be(2.5);
    }

    [Fact]
    public void TryParseNullableDouble_AcceptsDotDecimalAsInvariantFallbackInCommaDecimalLocale()
    {
        using var _ = TestCultureScope.CurrentCulture("de-DE");

        ChartDialogValueParser.TryParseNullableDouble("2.5", out var value).Should().BeTrue();
        value.Should().Be(2.5);
    }

    [Fact]
    public void TryParsePositiveDouble_AcceptsCommaDecimalInCommaDecimalLocale()
    {
        using var _ = TestCultureScope.CurrentCulture("fr-FR");

        ChartDialogValueParser.TryParsePositiveDouble("0,5", out var value).Should().BeTrue();
        value.Should().Be(0.5);
    }
}
