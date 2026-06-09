using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowSelectionPlannerTests
{
    [Fact]
    public void BuildSwitchWindowTargets_ListsVisibleWindowsAndMarksCurrent()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);
        registry.Hide(w2);

        var targets = WorkbookWindowSelectionPlanner.BuildSwitchWindowTargets(registry, w3, "Book1");

        targets.Select(target => target.Window).Should().Equal(w1, w3);
        targets.Select(target => target.DisplayName).Should().Equal("Book1 - 1", "Book1 - 3");
        targets.Select(target => target.IsCurrent).Should().Equal(false, true);
        targets.Select(target => target.KeyTip).Should().Equal("1", "2");
    }

    [Fact]
    public void BuildUnhideWindowTargets_ListsHiddenWindowsInRegistrationOrder()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);
        registry.Hide(w3);
        registry.Hide(w1);

        var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(registry, "Book1");

        targets.Select(target => target.Window).Should().Equal(w1, w3);
        targets.Select(target => target.DisplayName).Should().Equal("Book1 - 1", "Book1 - 3");
        targets.Select(target => target.IsCurrent).Should().Equal(false, false);
        targets.Select(target => target.KeyTip).Should().Equal("1", "2");
    }

    [Fact]
    public void FormatDisplayName_FallsBackForBlankWorkbookName()
    {
        WorkbookWindowSelectionPlanner.FormatDisplayName("  ", 1, 3)
            .Should().Be("Workbook - 2");
    }
}
