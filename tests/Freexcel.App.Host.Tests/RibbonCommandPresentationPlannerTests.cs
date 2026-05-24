using FluentAssertions;
using System.IO;
using System.Text.RegularExpressions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonCommandPresentationPlannerTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", RibbonCommandLayoutKind.Large)]
    [InlineData("Column Chart", "Column Chart", RibbonCommandLayoutKind.Small)]
    [InlineData("Bold", "Bold", RibbonCommandLayoutKind.Small)]
    [InlineData("Excluded Share", "Share", RibbonCommandLayoutKind.Large)]
    [InlineData("Get Add-ins", "Get Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("My Add-ins", "My Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("Contact Support", "Contact Support", RibbonCommandLayoutKind.Large)]
    [InlineData("Show Training", "Show Training", RibbonCommandLayoutKind.Large)]
    [InlineData("What's New", "What's New", RibbonCommandLayoutKind.Large)]
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
    [InlineData("Contact Support", RibbonCommandIconAccent.Help)]
    [InlineData("Show Training", RibbonCommandIconAccent.Help)]
    [InlineData("What's New", RibbonCommandIconAccent.Help)]
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

    [Theory]
    [InlineData("Charts", RibbonCommandIconAccent.Chart)]
    [InlineData("Get & Transform Data", RibbonCommandIconAccent.Data)]
    [InlineData("Themes", RibbonCommandIconAccent.Theme)]
    [InlineData("Protect", RibbonCommandIconAccent.Protect)]
    [InlineData("Help", RibbonCommandIconAccent.Help)]
    public void GetGroupIcon_AssignsExcelLikeAccentFamilies(string groupName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetGroupIcon(groupName).Accent.Should().Be(expectedAccent);
    }

    [Fact]
    public void MainRibbonGroupLabels_MapToSemanticIcons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var ribbonXaml = xaml[
            xaml.IndexOf("<TabControl x:Name=\"RibbonTabs\"", StringComparison.Ordinal)..xaml.IndexOf("<Grid Grid.Row=\"3\"", StringComparison.Ordinal)];
        var genericGroupLabels = Regex
            .Matches(ribbonXaml, "<TextBlock Text=\"(?<label>[^\"]+)\" Style=\"\\{StaticResource GroupLbl\\}\"")
            .Select(match => match.Groups["label"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(label => RibbonCommandPresentationPlanner.GetGroupIcon(label).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericGroupLabels.Should().BeEmpty("collapsed ribbon groups should use a semantic icon rather than the generic fallback");
    }

    [Fact]
    public void MainRibbonCommandTitles_MapToSemanticIcons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var ribbonXaml = xaml[
            xaml.IndexOf("<TabControl x:Name=\"RibbonTabs\"", StringComparison.Ordinal)..xaml.IndexOf("<Grid Grid.Row=\"3\"", StringComparison.Ordinal)];
        var genericTitles = Regex
            .Matches(ribbonXaml, "local:RibbonTooltip.Title=\"(?<title>[^\"]+)\"")
            .Select(match => match.Groups["title"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(title => !title.StartsWith("Excluded ", StringComparison.Ordinal))
            .Where(title => RibbonCommandPresentationPlanner.GetIcon(title).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericTitles.Should().BeEmpty("visible ribbon commands should use a specific semantic icon rather than the generic fallback");
    }
}
