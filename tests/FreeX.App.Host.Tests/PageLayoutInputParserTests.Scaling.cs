using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PageLayoutInputParserTests
{
    [Theory]
    [InlineData(100, null, null, "100")]
    [InlineData(null, 1, 1, "1x1")]
    [InlineData(null, null, null, "1x1")]
    [InlineData(null, 2, 3, "2x3")]
    public void FormatScaleToFit_FormatsPercentOrFitPages(int? percent, int? wide, int? tall, string expected)
    {
        var scaleToFit = new WorksheetScaleToFit(percent, wide, tall);

        PageLayoutInputParser.FormatScaleToFit(scaleToFit).Should().Be(expected);
    }

    [Theory]
    [InlineData("75", true, 75, null, null)]
    [InlineData("75%", true, 75, null, null)]
    [InlineData(" 400 % ", true, 400, null, null)]
    [InlineData("400", true, 400, null, null)]
    [InlineData("1x1", true, null, 1, 1)]
    [InlineData("2 x 3", true, null, 2, 3)]
    [InlineData("2 X 3", true, null, 2, 3)]
    [InlineData("9", false, null, null, null)]
    [InlineData("9%", false, null, null, null)]
    [InlineData("401", false, null, null, null)]
    [InlineData("401%", false, null, null, null)]
    [InlineData("75.5%", false, null, null, null)]
    [InlineData("x3", false, null, null, null)]
    [InlineData("2x", false, null, null, null)]
    [InlineData("0x1", false, null, null, null)]
    [InlineData("abc", false, null, null, null)]
    public void TryParseScaleToFit_ParsesExcelScaleText(
        string input,
        bool expected,
        int? expectedPercent,
        int? expectedWide,
        int? expectedTall)
    {
        var result = PageLayoutInputParser.TryParseScaleToFit(input, out var scaleToFit);

        result.Should().Be(expected);
        if (!expected)
        {
            scaleToFit.Should().Be(WorksheetScaleToFit.Default);
            return;
        }

        scaleToFit.ScalePercent.Should().Be(expectedPercent);
        scaleToFit.FitToPagesWide.Should().Be(expectedWide);
        scaleToFit.FitToPagesTall.Should().Be(expectedTall);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("1", true, 1)]
    [InlineData("-3", true, -3)]
    [InlineData("0", false, null)]
    [InlineData("abc", false, null)]
    public void TryParseOptionalFirstPageNumber_ParsesAutoOrNonZeroIntegers(
        string input,
        bool expected,
        int? expectedValue)
    {
        var result = PageLayoutInputParser.TryParseOptionalFirstPageNumber(input, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
    }
}
