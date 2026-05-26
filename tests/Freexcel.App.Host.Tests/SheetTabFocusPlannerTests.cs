using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SheetTabFocusPlannerTests
{
    [Fact]
    public void AdjacentTab_ReturnsNullWhenNoVisibleTabsExist()
    {
        SheetTabFocusPlanner.AdjacentTab([], SheetId.New(), 1).Should().BeNull();
    }

    [Fact]
    public void AdjacentTab_ClampsWithinVisibleTabList()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[0].Id, -1).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[0].Id, 1).Should().Be(tabs[1].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[2].Id, 1).Should().Be(tabs[2].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[2].Id, -1).Should().Be(tabs[1].Id);
    }

    [Fact]
    public void AdjacentTab_TreatsMissingCurrentAsBeforeOrAfterVisibleTabs()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2");

        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), 1).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), -1).Should().Be(tabs[1].Id);
    }

    [Fact]
    public void EdgeTab_ReturnsRequestedEdgeOrNull()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.EdgeTab(tabs, first: true).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.EdgeTab(tabs, first: false).Should().Be(tabs[2].Id);
        SheetTabFocusPlanner.EdgeTab([], first: true).Should().BeNull();
    }

    private static IReadOnlyList<SheetTabViewModel> CreateTabs(params string[] names) =>
        names.Select(name => new SheetTabViewModel(SheetId.New(), name, null)).ToList();
}
