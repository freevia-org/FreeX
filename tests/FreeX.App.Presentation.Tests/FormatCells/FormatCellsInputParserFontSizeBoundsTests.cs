using FluentAssertions;
using FreeX.App.Presentation.FormatCells;

namespace FreeX.App.Presentation.Tests.FormatCells;

public sealed class FormatCellsInputParserFontSizeBoundsTests
{
    [Theory]
    [InlineData("5000")]
    [InlineData("410")]
    [InlineData("409.01")]
    public void TryParseFontSize_RejectsSizesAboveExcelUpperBound(string input)
    {
        FormatCellsInputParser.TryParseFontSize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("13.5", 13.5)]
    [InlineData("409", 409)]
    [InlineData("1", 1)]
    public void TryParseFontSize_AcceptsSizesWithinExcelRange(string input, double expected)
    {
        FormatCellsInputParser.TryParseFontSize(input).Should().Be(expected);
    }
}
