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

    [Fact]
    public void ScaleToFitRibbonOptions_ExposeAutomaticPageCountsAndPercentChoices()
    {
        PageLayoutInputParser.ScalePageCountOptions.Should().ContainInOrder("Automatic", "1 page", "2 pages", "3 pages");
        PageLayoutInputParser.ScalePercentOptions.Should().ContainInOrder("Automatic", "10%", "25%", "50%", "75%", "90%", "100%");
    }

    [Theory]
    [InlineData(null, "Automatic")]
    [InlineData(1, "1 page")]
    [InlineData(2, "2 pages")]
    public void FormatScalePages_FormatsAutomaticSingularAndPlural(int? pages, string expected)
    {
        PageLayoutInputParser.FormatScalePages(pages).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "Automatic")]
    [InlineData(100, "100%")]
    [InlineData(75, "75%")]
    public void FormatScalePercent_FormatsAutomaticAndPercentages(int? percent, string expected)
    {
        PageLayoutInputParser.FormatScalePercent(percent).Should().Be(expected);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("Automatic", true, null)]
    [InlineData("1 page", true, 1)]
    [InlineData("2 pages", true, 2)]
    [InlineData("3", true, 3)]
    [InlineData("0 pages", false, null)]
    [InlineData("abc", false, null)]
    public void TryParseScalePages_ParsesRibbonPageOptions(string input, bool expected, int? expectedPages)
    {
        var result = PageLayoutInputParser.TryParseScalePages(input, out var pages);

        result.Should().Be(expected);
        pages.Should().Be(expectedPages);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("Automatic", true, null)]
    [InlineData("75", true, 75)]
    [InlineData("75%", true, 75)]
    [InlineData("9%", false, null)]
    [InlineData("401%", false, null)]
    public void TryParseScalePercent_ParsesRibbonPercentOptions(string input, bool expected, int? expectedPercent)
    {
        var result = PageLayoutInputParser.TryParseScalePercent(input, out var percent);

        result.Should().Be(expected);
        percent.Should().Be(expectedPercent);
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
