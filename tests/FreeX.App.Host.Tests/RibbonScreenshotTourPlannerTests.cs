using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonScreenshotTourPlannerTests
{
    private static readonly RibbonScreenshotTourTab[] Tabs =
    [
        new("Home", "Home"),
        new("Insert", "Insert"),
        new("Page Layout", "Page_Layout"),
        new("Data", "Data"),
        new("Help", "Help")
    ];

    [Fact]
    public void DefaultTabs_CoverMainRibbonTourOrderAndFileNames()
    {
        RibbonScreenshotTourPlanner.DefaultTabs
            .Should()
            .Equal(
            [
                new("Home", "Home"),
                new("Insert", "Insert"),
                new("Draw", "Draw"),
                new("Page Layout", "Page_Layout"),
                new("Formulas", "Formulas"),
                new("Data", "Data"),
                new("Review", "Review"),
                new("View", "View"),
                new("Help", "Help")
            ]);
    }

    [Fact]
    public void DefaultTabs_MatchVisibleRibbonCatalogExceptBackstageAndContextualTabs()
    {
        var expectedTabs = RibbonXamlCatalogSnapshotReader.ReadMainWindow()
            .VisibleTabs
            .Select(tab => tab.Header)
            .Where(header => header != "File")
            .ToArray();

        RibbonScreenshotTourPlanner.DefaultTabs.Select(tab => tab.Header)
            .Should()
            .Equal(expectedTabs);
    }

    [Fact]
    public void DefaultWidths_CoverRepresentativeRibbonWidths()
    {
        RibbonScreenshotTourPlanner.DefaultWidths
            .Should()
            .Equal(
            [
                new("max", null),
                new("1100", 1100),
                new("900", 900),
                new("750", 750)
            ]);
    }

    [Fact]
    public void BurstPhases_CoverImmediateFirstRenderAndSettledLayoutMoments()
    {
        RibbonScreenshotTourPlanner.BurstPhases
            .Select(phase => $"{phase.Label}:{phase.FileNameSuffix}")
            .Should()
            .Equal(
            [
                "immediate:immediate",
                "first-render:first_render",
                "settled:settled"
            ]);
    }

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
            .Equal([new("Page Layout", "Page_Layout"), new("Data", "Data")]);
    }

    [Fact]
    public void FilterTabs_RejectsUnknownTabNames()
    {
        var act = () => RibbonScreenshotTourPlanner.FilterTabs(Tabs, "Home, Missing");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*unknown tab(s): Missing*Valid tabs: Home, Insert, Page Layout, Data, Help*");
    }

    [Fact]
    public void FilterTabs_RejectsMissingTabEntries()
    {
        var act = () => RibbonScreenshotTourPlanner.FilterTabs(Tabs, "Home,,Data");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*tab list contains empty entry*position(s): 2*");
    }

    [Fact]
    public void ParseWidths_ReturnsRepresentativeWidthsWhenNoFilterIsProvided()
    {
        RibbonScreenshotTourPlanner.ParseWidths(null)
            .Should()
            .Equal(RibbonScreenshotTourPlanner.DefaultWidths);

        RibbonScreenshotTourPlanner.ParseWidths("  ")
            .Should()
            .Equal(RibbonScreenshotTourPlanner.DefaultWidths);
    }

    [Fact]
    public void ParseWidths_UsesInvariantCultureAndAcceptsMax()
    {
        RibbonScreenshotTourPlanner.ParseWidths("max, 1100, 900.5, 750")
            .Should()
            .Equal(
            [
                new("max", null),
                new("1100", 1100),
                new("900.5", 900.5),
                new("750", 750)
            ]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("nope")]
    public void ParseWidths_RejectsInvalidNonPositiveOrNonFiniteValues(string requestedWidths)
    {
        var act = () => RibbonScreenshotTourPlanner.ParseWidths(requestedWidths);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*invalid width(s): {requestedWidths}*");
    }

    [Fact]
    public void ParseWidths_RejectsMissingWidthEntries()
    {
        var act = () => RibbonScreenshotTourPlanner.ParseWidths("1100,,750");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*width list contains empty entry*position(s): 2*");
    }

    [Fact]
    public void CreatePlan_CoversEveryDefaultTabAtEveryRepresentativeWidth()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan(null, null);

        plan.Tabs.Should().Equal(RibbonScreenshotTourPlanner.DefaultTabs);
        plan.Widths.Should().Equal(RibbonScreenshotTourPlanner.DefaultWidths);
        plan.Phases.Should().Equal(RibbonScreenshotTourPlanner.DefaultPhases);
        plan.IsBurst.Should().BeFalse();
        plan.Captures.Should().HaveCount(
            RibbonScreenshotTourPlanner.DefaultTabs.Count *
            RibbonScreenshotTourPlanner.DefaultWidths.Count);
        plan.Captures.Should().OnlyHaveUniqueItems(capture => capture.FileName);
        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}:{capture.FileName}")
            .Should()
            .Equal(
                RibbonScreenshotTourPlanner.DefaultWidths.SelectMany(width =>
                    RibbonScreenshotTourPlanner.DefaultTabs.Select(tab =>
                        $"{width.Label}:{tab.Header}:{width.Label}_{tab.FileName}")));
    }

    [Fact]
    public void CreatePlan_WithBurstMode_CapturesEveryTabWidthAcrossTransientLayoutPhases()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Home,Data", "900", burstMode: true);

        plan.Tabs.Should().Equal([new("Home", "Home"), new("Data", "Data")]);
        plan.Widths.Should().Equal([new("900", 900)]);
        plan.Phases.Should().Equal(RibbonScreenshotTourPlanner.BurstPhases);
        plan.IsBurst.Should().BeTrue();
        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}:{capture.Phase.Label}:{capture.FileName}")
            .Should()
            .Equal(
            [
                "900:Home:immediate:900_Home_immediate",
                "900:Home:first-render:900_Home_first_render",
                "900:Home:settled:900_Home_settled",
                "900:Data:immediate:900_Data_immediate",
                "900:Data:first-render:900_Data_first_render",
                "900:Data:settled:900_Data_settled"
            ]);
    }

    [Fact]
    public void CreatePlan_AppliesTabAndWidthFiltersDeterministically()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Data,Home", "900,750");

        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}")
            .Should()
            .Equal(
            [
                "900:Home",
                "900:Data",
                "750:Home",
                "750:Data"
            ]);
    }

    [Fact]
    public void MainWindowScreenshotTour_UsesPlannerForEnvironmentFilters()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ScreenshotTour.cs"));

        source.Should().Contain("RibbonScreenshotTourPlanner.CreatePlan");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_BURST\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_TABS\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_WIDTHS\")");
        source.Should().Contain("RibbonScreenshotTourPlan?");
        source.Should().Contain("PrepareRibbonBurstCapturePhaseAsync");
        source.Should().Contain("WaitForRibbonScreenshotRenderPassAsync");
        source.Should().Contain("throw new InvalidOperationException");
    }
}
