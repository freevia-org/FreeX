using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonCommandPresentationPlannerTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", RibbonCommandLayoutKind.Large)]
    [InlineData("Column Chart", "Column Chart", RibbonCommandLayoutKind.Medium)]
    [InlineData("Bold", "Bold", RibbonCommandLayoutKind.Small)]
    [InlineData("Excluded Share", "Share", RibbonCommandLayoutKind.Large)]
    public void GetLayoutKind_ClassifiesRibbonCommands(string commandName, string label, RibbonCommandLayoutKind expected)
    {
        RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label).Should().Be(expected);
    }

    [Theory]
    [InlineData("Axis Options", true)]
    [InlineData("Legend", true)]
    [InlineData("Column Chart", false)]
    [InlineData("Recommended Chart", false)]
    [InlineData("Sparkline", false)]
    [InlineData("Table", false)]
    public void ShouldHideFromInsertRibbon_HidesChartFormattingCommandsOnly(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("RibbonCompact:74:38", true, 74, 38)]
    [InlineData("RibbonCompact:58.5:30", true, 58.5, 30)]
    [InlineData("Other:74:38", false, 0, 0)]
    [InlineData("RibbonCompact:74", false, 0, 0)]
    public void TryParseCompactWidths_ParsesInvariantCompactTags(
        string tag,
        bool expectedResult,
        double expectedFull,
        double expectedCompact)
    {
        var result = RibbonCommandPresentationPlanner.TryParseCompactWidths(tag, out var full, out var compact);

        result.Should().Be(expectedResult);
        full.Should().Be(expectedFull);
        compact.Should().Be(expectedCompact);
    }

    [Theory]
    [InlineData("Refresh All", "\uE72C")]
    [InlineData("Insert Function", "fx")]
    [InlineData("Unknown Command", "\uE8A5")]
    public void GetIcon_MapsKnownCommandsAndProvidesFallback(string commandName, string expectedGlyph)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);

        icon.Glyph.Should().Be(expectedGlyph);
        icon.FontFamily.Source.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Clipboard", "\uE8C8")]
    [InlineData("Font", "A")]
    [InlineData("Editing", "\uE721")]
    [InlineData("Unknown", "\uE8A5")]
    public void GetGroupIcon_MapsExcelRibbonGroupsAndProvidesFallback(string groupName, string expectedGlyph)
    {
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);

        icon.Glyph.Should().Be(expectedGlyph);
        icon.FontFamily.Source.Should().NotBeNullOrWhiteSpace();
    }
}
