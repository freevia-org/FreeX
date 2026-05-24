using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonCommandPresentationPlannerTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", RibbonCommandLayoutKind.Large)]
    [InlineData("Column Chart", "Column Chart", RibbonCommandLayoutKind.Small)]
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
    [InlineData("Column Chart", true)]
    [InlineData("Surface Chart", true)]
    [InlineData("3D Surface Chart", true)]
    [InlineData("Column", true)]
    [InlineData("Trend Order", false)]
    [InlineData("R-squared", false)]
    public void IsInsertRibbonChartCommand_AllowsOnlyInsertChartTypes(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title).Should().Be(expected);
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
    [InlineData("PivotTable", RibbonCommandIconKind.PivotTable)]
    [InlineData("Table", RibbonCommandIconKind.Table)]
    [InlineData("Column Chart", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Line Chart", RibbonCommandIconKind.ChartLine)]
    [InlineData("Get Data", RibbonCommandIconKind.GetData)]
    [InlineData("Refresh All", RibbonCommandIconKind.Refresh)]
    [InlineData("Insert Function", RibbonCommandIconKind.Function)]
    [InlineData("Spelling", RibbonCommandIconKind.Spelling)]
    [InlineData("Check Accessibility", RibbonCommandIconKind.Accessibility)]
    [InlineData("Protect Sheet", RibbonCommandIconKind.Protect)]
    [InlineData("Help Online", RibbonCommandIconKind.Help)]
    [InlineData("What's New", RibbonCommandIconKind.Info)]
    [InlineData("Unknown Command", RibbonCommandIconKind.Generic)]
    public void GetIcon_MapsKnownCommandsToSemanticVectorKinds(string commandName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);

        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Column Chart", RibbonCommandIconAccent.Chart)]
    [InlineData("Get Data", RibbonCommandIconAccent.Data)]
    [InlineData("Theme Colors", RibbonCommandIconAccent.Theme)]
    [InlineData("Fill", RibbonCommandIconAccent.Fill)]
    [InlineData("Error Checking", RibbonCommandIconAccent.Warning)]
    [InlineData("Protect Workbook", RibbonCommandIconAccent.Protect)]
    [InlineData("Send Feedback", RibbonCommandIconAccent.Help)]
    public void GetIcon_AssignsExcelLikeAccentFamilies(string commandName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Accent.Should().Be(expectedAccent);
    }

    [Theory]
    [InlineData("Clipboard", RibbonCommandIconKind.Paste)]
    [InlineData("Font", RibbonCommandIconKind.Font)]
    [InlineData("Editing", RibbonCommandIconKind.Search)]
    [InlineData("Convert", RibbonCommandIconKind.Math)]
    [InlineData("Help", RibbonCommandIconKind.Help)]
    [InlineData("Unknown", RibbonCommandIconKind.Generic)]
    public void GetGroupIcon_MapsExcelRibbonGroupsToSemanticVectorKinds(string groupName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);

        icon.Kind.Should().Be(expectedKind);
    }
}
