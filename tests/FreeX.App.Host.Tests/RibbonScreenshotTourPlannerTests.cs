using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonScreenshotTourPlannerTests
{
    private static readonly (string Header, string FileName)[] Tabs =
    [
        ("Home", "Home"),
        ("Insert", "Insert"),
        ("Page Layout", "Page_Layout"),
        ("Data", "Data"),
        ("Help", "Help")
    ];

    [Fact]
    public void FilterTabs_ReturnsDefaultTourWhenNoFilterIsProvided()
    {
        RibbonScreenshotTourPlanner.FilterTabs(Tabs, null)
            .Should()
            .Equal(Tabs);

        RibbonScreenshotTourPlanner.FilterTabs(Tabs, "  ")
            .Should()
            .Equal(Tabs);
    }

    [Fact]
    public void FilterTabs_MatchesHeaderOrFileNameCaseInsensitivelyInTourOrder()
    {
        RibbonScreenshotTourPlanner.FilterTabs(Tabs, " data, page_layout ")
            .Should()
            .Equal([("Page Layout", "Page_Layout"), ("Data", "Data")]);
    }

    [Fact]
    public void ParseWidths_UsesInvariantCultureAndIgnoresInvalidOrNonPositiveValues()
    {
        RibbonScreenshotTourPlanner.ParseWidths("1100, 900.5, 0, -1, nope, 750")
            .Should()
            .Equal([1100, 900.5, 750]);
    }

    [Fact]
    public void MainWindowScreenshotTour_UsesPlannerForEnvironmentFilters()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ScreenshotTour.cs"));

        source.Should().Contain("RibbonScreenshotTourPlanner.FilterTabs");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_TABS\")");
        source.Should().Contain("RibbonScreenshotTourPlanner.ParseWidths");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_WIDTHS\")");
    }
}
