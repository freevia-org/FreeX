using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SheetUI;

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

        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[0].Id, -1, TabId).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[0].Id, 1, TabId).Should().Be(tabs[1].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[2].Id, 1, TabId).Should().Be(tabs[2].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[2].Id, -1, TabId).Should().Be(tabs[1].Id);
    }

    [Fact]
    public void AdjacentTab_TreatsMissingCurrentAsBeforeOrAfterVisibleTabs()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2");

        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), 1, TabId).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), -1, TabId).Should().Be(tabs[1].Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void AdjacentTab_TreatsMissingCurrentPositiveDirectionAsSingleStepFromBeforeFirstTab(int direction)
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), direction, TabId).Should().Be(tabs[0].Id);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-10)]
    public void AdjacentTab_TreatsMissingCurrentNegativeDirectionAsSingleStepFromAfterLastTab(int direction)
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), direction, TabId).Should().Be(tabs[2].Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void AdjacentTab_TreatsPositiveDirectionAsSingleStep(int direction)
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3", "Sheet4");

        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[0].Id, direction, TabId).Should().Be(tabs[1].Id);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-10)]
    public void AdjacentTab_TreatsNegativeDirectionAsSingleStep(int direction)
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3", "Sheet4");

        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[3].Id, direction, TabId).Should().Be(tabs[2].Id);
    }

    [Fact]
    public void AdjacentTab_ZeroDirectionKeepsCurrentVisibleTab()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.AdjacentTab(tabs, tabs[1].Id, 0, TabId).Should().Be(tabs[1].Id);
    }

    [Fact]
    public void AdjacentTab_ZeroDirectionRecoversMissingCurrentToFirstVisibleTab()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.AdjacentTab(tabs, SheetId.New(), 0, TabId).Should().Be(tabs[0].Id);
    }

    [Fact]
    public void AdjacentTab_SupportsSheetIdListsWithoutASelector()
    {
        var ids = new[] { SheetId.New(), SheetId.New(), SheetId.New() };

        SheetTabFocusPlanner.AdjacentTab(ids, ids[0], 1).Should().Be(ids[1]);
    }

    [Fact]
    public void EdgeTab_ReturnsRequestedEdgeOrNull()
    {
        var tabs = CreateTabs("Sheet1", "Sheet2", "Sheet3");

        SheetTabFocusPlanner.EdgeTab(tabs, first: true, getSheetId: TabId).Should().Be(tabs[0].Id);
        SheetTabFocusPlanner.EdgeTab(tabs, first: false, getSheetId: TabId).Should().Be(tabs[2].Id);
        SheetTabFocusPlanner.EdgeTab([], first: true).Should().BeNull();
    }

    private static IReadOnlyList<TestSheetTab> CreateTabs(params string[] names) =>
        names.Select(name => new TestSheetTab(SheetId.New(), name)).ToList();

    private static SheetId TabId(TestSheetTab tab) => tab.Id;

    private sealed record TestSheetTab(SheetId Id, string Name);
}
