using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookWindowSelectionPlannerTests
{
    [Fact]
    public void BuildSwitchWindowTargets_ListsVisibleWindowsAndMarksCurrent()
    {
        var windows = new[]
        {
            new WorkbookWindowSelectionEntry<string>("w1", 0),
            new WorkbookWindowSelectionEntry<string>("w3", 2)
        };

        var targets = WorkbookWindowSelectionPlanner.BuildSwitchWindowTargets(windows, "w3", "Book1", 3);

        targets.Select(target => target.Window).Should().Equal("w1", "w3");
        targets.Select(target => target.DisplayName).Should().Equal("Book1 - 1", "Book1 - 3");
        targets.Select(target => target.IsCurrent).Should().Equal(false, true);
        targets.Select(target => target.KeyTip).Should().Equal("1", "2");
    }

    [Fact]
    public void BuildUnhideWindowTargets_ListsHiddenWindowsInRegistrationOrder()
    {
        var windows = new[]
        {
            new WorkbookWindowSelectionEntry<string>("w1", 0),
            new WorkbookWindowSelectionEntry<string>("w3", 2)
        };

        var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(windows, "Book1", 3);

        targets.Select(target => target.Window).Should().Equal("w1", "w3");
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
