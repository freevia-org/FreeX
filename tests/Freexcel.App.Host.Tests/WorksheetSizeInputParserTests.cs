using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WorksheetSizeInputParserTests
{
    [Theory]
    [InlineData("20", 20)]
    [InlineData(" 8.5 ", 8.5)]
    public void TryParsePositiveSize_AcceptsPositiveNumericInput(string input, double expected)
    {
        WorksheetSizeInputParser.TryParsePositiveSize(input, out var size).Should().BeTrue();
        size.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("auto")]
    public void TryParsePositiveSize_RejectsBlankNonNumericAndNonPositiveInput(string input)
    {
        WorksheetSizeInputParser.TryParsePositiveSize(input, out var size).Should().BeFalse();
        size.Should().Be(0);
    }
}
