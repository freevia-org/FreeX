using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FormatCellsInputParserTests
{
    [Theory]
    [InlineData("13.5", 13.5)]
    [InlineData(" 11 ", 11)]
    public void TryParseFontSize_AcceptsPositiveFiniteSizes(string input, double expected)
    {
        FormatCellsInputParser.TryParseFontSize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("bad")]
    public void TryParseFontSize_RejectsInvalidOrNonFiniteSizes(string input)
    {
        FormatCellsInputParser.TryParseFontSize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("-4", 0)]
    [InlineData("7", 7)]
    [InlineData("99", 15)]
    public void TryParseIndentLevel_ParsesAndClampsExcelIndentRange(string input, int expected)
    {
        FormatCellsInputParser.TryParseIndentLevel(input).Should().Be(expected);
    }

    [Fact]
    public void TryParseIndentLevel_RejectsInvalidText()
    {
        FormatCellsInputParser.TryParseIndentLevel("bad").Should().BeNull();
    }

    [Theory]
    [InlineData("-90", -90)]
    [InlineData("0", 0)]
    [InlineData("90", 90)]
    [InlineData("255", 255)]
    public void TryParseSupportedTextRotation_AcceptsExcelSupportedRotations(string input, int expected)
    {
        FormatCellsInputParser.TryParseSupportedTextRotation(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("-91")]
    [InlineData("91")]
    [InlineData("999")]
    [InlineData("bad")]
    public void TryParseSupportedTextRotation_RejectsUnsupportedRotations(string input)
    {
        FormatCellsInputParser.TryParseSupportedTextRotation(input).Should().BeNull();
    }
}
