using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonTabParityTests
{
    [Fact]
    public void InsertTab_UsesExcelLikeGroupOrderAndCommandPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var insertTab = ExtractTabXaml(xaml, "Insert", "Draw");

        ExtractGroupLabels(insertTab).Should().Equal(
            "Tables",
            "Illustrations",
            "Add-ins",
            "Charts",
            "Tours",
            "Sparklines",
            "Filters",
            "Links",
            "Text",
            "Symbols");

        ExtractGroupXaml(insertTab, "Tables").Should().Contain("Recommended PivotTables");
        ExtractGroupXaml(insertTab, "Illustrations").Should().Contain("local:RibbonTooltip.Title=\"Insert Picture\"");
        ExtractGroupXaml(insertTab, "Illustrations").Should().Contain("local:RibbonTooltip.Title=\"Shapes\"");
        ExtractGroupXaml(insertTab, "Charts").Should().Contain("local:RibbonTooltip.Title=\"Recommended Charts\"");
        ExtractGroupXaml(insertTab, "Filters").Should().Contain("local:RibbonTooltip.Title=\"Insert Slicer\"");
        ExtractGroupXaml(insertTab, "Filters").Should().Contain("local:RibbonTooltip.Title=\"Insert Timeline\"");
        ExtractGroupXaml(insertTab, "Links").Should().Contain("local:RibbonTooltip.Title=\"Insert Link\"");
        ExtractGroupXaml(insertTab, "Text").Should().Contain("local:RibbonTooltip.Title=\"Text Box\"");
        ExtractGroupXaml(insertTab, "Text").Should().Contain("local:RibbonTooltip.Title=\"Header &amp; Footer\"");
        ExtractGroupXaml(insertTab, "Symbols").Should().Contain("local:RibbonTooltip.Title=\"Insert Symbol\"");
    }

    [Fact]
    public void PageLayoutTab_UsesExcelLikeGroupOrderAndArrangeCommands()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var pageLayoutTab = ExtractTabXaml(xaml, "Page Layout", "Formulas");

        ExtractGroupLabels(pageLayoutTab).Should().Equal(
            "Themes",
            "Page Setup",
            "Scale to Fit",
            "Sheet Options",
            "Arrange");

        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Bring Forward\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Send Backward\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Selection Pane\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Rotate Object\"");
    }

    [Fact]
    public void DataTab_UsesExcelLikeGroupOrderAndForecastPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var dataTab = ExtractTabXaml(xaml, "Data", "Review");

        ExtractGroupLabels(dataTab).Should().Equal(
            "Get & Transform Data",
            "Queries & Connections",
            "Sort & Filter",
            "Data Tools",
            "Forecast",
            "Outline");

        ExtractGroupXaml(dataTab, "Get & Transform Data").Should().Contain("local:RibbonTooltip.Title=\"Get Data\"");
        ExtractGroupXaml(dataTab, "Queries & Connections").Should().Contain("local:RibbonTooltip.Title=\"Refresh All\"");
        ExtractGroupXaml(dataTab, "Forecast").Should().Contain("local:RibbonTooltip.Title=\"Forecast Sheet\"");
        ExtractGroupXaml(dataTab, "Forecast").Should().Contain("local:RibbonTooltip.Title=\"What-If Analysis\"");
    }

    [Fact]
    public void ReviewTab_SeparatesCommentsAndNotesLikeExcel()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var reviewTab = ExtractTabXaml(xaml, "Review", "View");

        ExtractGroupLabels(reviewTab).Should().Equal(
            "Proofing",
            "Accessibility",
            "Comments",
            "Notes",
            "Protect");

        ExtractGroupXaml(reviewTab, "Comments").Should().Contain("local:RibbonTooltip.Title=\"New Comment\"");
        ExtractGroupXaml(reviewTab, "Notes").Should().Contain("local:RibbonTooltip.Title=\"New Note\"");
        ExtractGroupXaml(reviewTab, "Notes").Should().Contain("local:RibbonTooltip.Title=\"Show Notes\"");
    }

    [Fact]
    public void ViewTab_UsesExcelLikeGroupOrderAndWindowPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var viewTab = ExtractTabXaml(xaml, "View", "PivotTable Analyze");

        ExtractGroupLabels(viewTab).Should().Equal(
            "Workbook Views",
            "Show",
            "Zoom",
            "Window",
            "Macros");

        ExtractGroupXaml(viewTab, "Window").Should().Contain("local:RibbonTooltip.Title=\"Freeze Panes\"");
        ExtractGroupXaml(viewTab, "Window").Should().Contain("local:RibbonTooltip.Title=\"Split\"");
        ExtractGroupXaml(viewTab, "Macros").Should().Contain("local:RibbonTooltip.Title=\"Macros\"");
    }

    private static string ExtractTabXaml(string xaml, string header, string nextHeader)
    {
        var start = FindTabStart(xaml, header, 0);
        var end = FindTabStart(xaml, nextHeader, start + 1);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);

        return xaml[start..end];
    }

    private static int FindTabStart(string xaml, string header, int startIndex)
    {
        var headerIndex = xaml.IndexOf($"Header=\"{header}\"", startIndex, StringComparison.Ordinal);
        return headerIndex < 0
            ? -1
            : xaml.LastIndexOf("<TabItem", headerIndex, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractGroupLabels(string tabXaml) =>
        Regex.Matches(tabXaml, "<TextBlock Text=\"(?<label>[^\"]+)\" Style=\"\\{StaticResource GroupLbl\\}\"")
            .Select(match => match.Groups["label"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .ToList();

    private static string ExtractGroupXaml(string tabXaml, string groupName)
    {
        var label = $"<TextBlock Text=\"{groupName.Replace("&", "&amp;", StringComparison.Ordinal)}\" Style=\"{{StaticResource GroupLbl}}\"";
        var labelIndex = tabXaml.IndexOf(label, StringComparison.Ordinal);
        labelIndex.Should().BeGreaterThanOrEqualTo(0);

        var start = tabXaml.LastIndexOf("<Grid Style=\"{StaticResource RibbonGroupPanel}\">", labelIndex, StringComparison.Ordinal);
        var nextStart = tabXaml.IndexOf("<Grid Style=\"{StaticResource RibbonGroupPanel}\">", labelIndex, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        return nextStart < 0 ? tabXaml[start..] : tabXaml[start..nextStart];
    }
}
