using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OptionsInputParserTests
{
    [Theory]
    [InlineData("11", true, 11)]
    [InlineData(" 14 ", true, 14)]
    [InlineData("0", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("bad", false, 0)]
    public void TryParseDefaultFontSize_AcceptsPositiveIntegerOnly(
        string input,
        bool expectedResult,
        int expectedValue)
    {
        OptionsInputParser.TryParseDefaultFontSize(input, out var value).Should().Be(expectedResult);
        value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("255", true, 255)]
    [InlineData(" 12 ", true, 12)]
    [InlineData("0", false, 0)]
    [InlineData("256", false, 0)]
    [InlineData("bad", false, 0)]
    public void TryParseDefaultSheetCount_AcceptsExcelDialogRange(
        string input,
        bool expectedResult,
        int expectedValue)
    {
        OptionsInputParser.TryParseDefaultSheetCount(input, out var value).Should().Be(expectedResult);
        value.Should().Be(expectedValue);
    }
}
