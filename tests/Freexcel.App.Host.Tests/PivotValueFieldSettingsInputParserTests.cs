using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PivotValueFieldSettingsInputParserTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("14", 14)]
    [InlineData(" 165 ", 165)]
    public void TryParseOptionalNumberFormatId_AcceptsBlankOrWholeNumber(string input, int? expected)
    {
        PivotValueFieldSettingsInputParser.TryParseOptionalNumberFormatId(input, out var numberFormatId)
            .Should().BeTrue();

        numberFormatId.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void TryParseOptionalNumberFormatId_RejectsNonIntegers(string input)
    {
        PivotValueFieldSettingsInputParser.TryParseOptionalNumberFormatId(input, out _).Should().BeFalse();
    }
}
