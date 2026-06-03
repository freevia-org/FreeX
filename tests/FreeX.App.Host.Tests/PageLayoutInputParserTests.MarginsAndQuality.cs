using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class PageLayoutInputParserTests
{
    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("0.75", true, 0.75)]
    [InlineData("-0.1", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("Infinity", false, 0)]
    [InlineData("abc", false, 0)]
    public void TryParseMarginDistance_ParsesNonNegativeFiniteDistances(
        string input,
        bool expected,
        double expectedValue)
    {
        var result = PageLayoutInputParser.TryParseMarginDistance(input, out var value);

        result.Should().Be(expected);
        if (expected)
            value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("300", true, 300)]
    [InlineData("300 dpi", true, 300)]
    [InlineData(" 600 DPI ", true, 600)]
    [InlineData("0", false, null)]
    [InlineData("-1", false, null)]
    [InlineData("600.5", false, null)]
    [InlineData("600.5 dpi", false, null)]
    [InlineData("0 dpi", false, null)]
    [InlineData("dpi", false, null)]
    public void TryParseOptionalPrintQuality_ParsesAutoOrPositiveIntegerDpi(
        string input,
        bool expected,
        int? expectedValue)
    {
        var result = PageLayoutInputParser.TryParseOptionalPrintQuality(input, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
    }
}
