using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
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
            .Equal([new("Page Layout", "Page_Layout", "PageLayoutTab"), new("Data", "Data", "DataTab")]);
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

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" table ", "table")]
    [InlineData("table-design", "table")]
    [InlineData("structured_table", "table")]
    [InlineData("pivot", "pivot")]
    [InlineData("pivot-table", "pivot")]
    [InlineData("pivottable", "pivot")]
    [InlineData("chart", "chart")]
    [InlineData("chart-design", "chart")]
    [InlineData("chart_format", "chart")]
    [InlineData("embedded-chart", "chart")]
    [InlineData("drawing", "drawing")]
    [InlineData("shape-format", "drawing")]
    [InlineData("picture", "drawing")]
    public void NormalizeContext_AcceptsExcelContextAliases(string? context, string? expected)
    {
        RibbonScreenshotTourPlanner.NormalizeContext(context)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void TabsForContext_RejectsUnsupportedContexts()
    {
        var act = () => RibbonScreenshotTourPlanner.TabsForContext("slides");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*context 'slides' is not supported*Valid contexts: drawing, table, pivot, chart*");
    }
}
