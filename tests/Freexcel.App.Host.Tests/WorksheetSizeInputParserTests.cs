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

    [Theory]
    [InlineData("0", 0)]
    [InlineData("409", 409)]
    [InlineData("409.5", 409.5)]
    [InlineData(" 255.5 ", 255.5)]
    public void TryParseSizeInRange_AcceptsInclusiveExcelDialogBounds(string input, double expected)
    {
        WorksheetSizeInputParser.TryParseSizeInRange(input, 0, 409.5, out var size).Should().BeTrue();
        size.Should().Be(expected);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("409.6")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("auto")]
    public void TryParseSizeInRange_RejectsValuesOutsideInclusiveBounds(string input)
    {
        WorksheetSizeInputParser.TryParseSizeInRange(input, 0, 409.5, out var size).Should().BeFalse();
        size.Should().Be(0);
    }
}
