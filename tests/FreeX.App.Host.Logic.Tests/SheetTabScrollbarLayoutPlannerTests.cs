using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class SheetTabScrollbarLayoutPlannerTests
{
    [Fact]
    public void Plan_KeepsPreferredScrollbarUntilSheetTabsReachIt()
    {
        var layout = SheetTabScrollbarLayoutPlanner.Plan(
            tabContentWidth: 260,
            rowHeaderWidth: 46,
            rowWidth: 560);

        layout.SheetTabsViewportWidth.Should().Be(260);
        layout.HorizontalScrollbarWidth.Should().Be(185.04);
    }

    [Fact]
    public void Plan_ShrinksSheetTabsBeforePreferredScrollbar()
    {
        var layout = SheetTabScrollbarLayoutPlanner.Plan(
            tabContentWidth: 600,
            rowHeaderWidth: 46,
            rowWidth: 500);

        layout.HorizontalScrollbarWidth.Should().Be(180);
        layout.SheetTabsViewportWidth.Should().Be(274);
    }

    [Fact]
    public void Plan_ShrinksScrollbarAfterSheetTabsReachMinimumViewport()
    {
        var layout = SheetTabScrollbarLayoutPlanner.Plan(
            tabContentWidth: 600,
            rowHeaderWidth: 46,
            rowWidth: 280);

        layout.SheetTabsViewportWidth.Should().Be(80);
        layout.HorizontalScrollbarWidth.Should().Be(154);
    }

    [Fact]
    public void Plan_KeepsNavigationButtonsSeparatedWhenSpaceIsTight()
    {
        var layout = SheetTabScrollbarLayoutPlanner.Plan(
            tabContentWidth: 600,
            rowHeaderWidth: 46,
            rowWidth: 170);

        layout.SheetTabsViewportWidth.Should().Be(80);
        layout.HorizontalScrollbarWidth.Should().Be(44);
    }
}
