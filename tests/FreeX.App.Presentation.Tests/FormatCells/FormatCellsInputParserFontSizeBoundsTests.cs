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

    /// <summary>
    /// r183. The bound check read "> 0", so a sub-point size like 0.5 was accepted and written
    /// straight onto the cell style -- contradicting this parser's own doc comment, and Excel, which
    /// rejects anything outside 1-409 inclusive. The upper bound was already pinned here; the lower
    /// one had no case between 0 and 1, which is exactly where the gap was.
    /// </summary>
    [Theory]
    [InlineData("0.5")]
    [InlineData("0.01")]
    [InlineData("0.999")]
    public void TryParseFontSize_RejectsSizesBelowOnePoint(string text)
    {
        FormatCellsInputParser.TryParseFontSize(text).Should().BeNull();
    }

    [Fact]
    public void TryParseFontSize_AcceptsExactlyOnePoint()
    {
        FormatCellsInputParser.TryParseFontSize("1").Should().Be(1);
    }
}
