using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OptionsInputParserTests
{
    [Theory]
    [InlineData("11", 9, 11)]
    [InlineData(" 14 ", 9, 14)]
    [InlineData("0", 9, 9)]
    [InlineData("-1", 9, 9)]
    [InlineData("bad", 9, 9)]
    public void ParseDefaultFontSizeOrFallback_AcceptsPositiveIntegerOnly(
        string input,
        int fallback,
        int expected)
    {
        OptionsInputParser.ParseDefaultFontSizeOrFallback(input, fallback).Should().Be(expected);
    }

    [Theory]
    [InlineData("1", 3, 1)]
    [InlineData("255", 3, 255)]
    [InlineData(" 12 ", 3, 12)]
    [InlineData("0", 3, 3)]
    [InlineData("256", 3, 3)]
    [InlineData("bad", 3, 3)]
    public void ParseDefaultSheetCountOrFallback_AcceptsExcelDialogRange(
        string input,
        int fallback,
        int expected)
    {
        OptionsInputParser.ParseDefaultSheetCountOrFallback(input, fallback).Should().Be(expected);
    }
}
