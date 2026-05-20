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

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(" #,##0.0 \"kg\" ", "#,##0.0 \"kg\"")]
    public void ResolveOptionalNumberFormatCode_TrimsBlankToNull(string input, string? expected)
    {
        PivotValueFieldSettingsInputParser.ResolveOptionalNumberFormatCode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(4, null, 4)]
    [InlineData(null, "#,##0.0 \"kg\"", 164)]
    [InlineData(4, "#,##0.0 \"kg\"", 164)]
    [InlineData(165, "#,##0.0 \"kg\"", 165)]
    public void ResolveNumberFormatIdForCode_AssignsCustomIdWhenFormatCodeIsPresent(
        int? inputId,
        string? inputCode,
        int? expected)
    {
        PivotValueFieldSettingsInputParser.ResolveNumberFormatIdForCode(inputId, inputCode).Should().Be(expected);
    }
}
