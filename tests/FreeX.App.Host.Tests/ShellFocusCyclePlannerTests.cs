using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ShellFocusCyclePlannerTests
{
    [Theory]
    [InlineData(ShellFocusTarget.Worksheet, false, ShellFocusTarget.Ribbon)]
    [InlineData(ShellFocusTarget.Ribbon, false, ShellFocusTarget.FormulaBar)]
    [InlineData(ShellFocusTarget.FormulaBar, false, ShellFocusTarget.SheetTabs)]
    [InlineData(ShellFocusTarget.SheetTabs, false, ShellFocusTarget.TaskPane)]
    [InlineData(ShellFocusTarget.TaskPane, false, ShellFocusTarget.StatusBar)]
    [InlineData(ShellFocusTarget.StatusBar, false, ShellFocusTarget.Worksheet)]
    [InlineData(ShellFocusTarget.Worksheet, true, ShellFocusTarget.StatusBar)]
    [InlineData(ShellFocusTarget.StatusBar, true, ShellFocusTarget.TaskPane)]
    [InlineData(ShellFocusTarget.TaskPane, true, ShellFocusTarget.SheetTabs)]
    [InlineData(ShellFocusTarget.Ribbon, true, ShellFocusTarget.Worksheet)]
    public void GetNext_CyclesThroughExcelShellRegions(
        ShellFocusTarget current,
        bool reverse,
        ShellFocusTarget expected)
    {
        var next = ShellFocusCyclePlanner.GetNext(current, reverse);

        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(ShellFocusTarget.SheetTabs, false, ShellFocusTarget.StatusBar)]
    [InlineData(ShellFocusTarget.StatusBar, true, ShellFocusTarget.SheetTabs)]
    public void GetNextAvailable_SkipsUnavailableTaskPane(
        ShellFocusTarget current,
        bool reverse,
        ShellFocusTarget expected)
    {
        var next = ShellFocusCyclePlanner.GetNextAvailable(
            current,
            reverse,
            target => target != ShellFocusTarget.TaskPane);

        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(ShellFocusTarget.SheetTabs, false, ShellFocusTarget.TaskPane)]
    [InlineData(ShellFocusTarget.StatusBar, true, ShellFocusTarget.TaskPane)]
    public void GetNextAvailable_IncludesVisibleContextualTaskPane(
        ShellFocusTarget current,
        bool reverse,
        ShellFocusTarget expected)
    {
        var next = ShellFocusCyclePlanner.GetNextAvailable(
            current,
            reverse,
            target => target is not ShellFocusTarget.TaskPane || IsContextualTaskPaneVisible(
                pivotFieldListVisible: true,
                slicerTimelineVisible: false));

        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GetNextAvailable_TreatsEitherContextualPaneAsTaskPaneAvailable(
        bool pivotFieldListVisible,
        bool slicerTimelineVisible)
    {
        var next = ShellFocusCyclePlanner.GetNextAvailable(
            ShellFocusTarget.SheetTabs,
            reverse: false,
            target => target is not ShellFocusTarget.TaskPane ||
                      IsContextualTaskPaneVisible(pivotFieldListVisible, slicerTimelineVisible));

        next.Should().Be(ShellFocusTarget.TaskPane);
    }

    [Fact]
    public void GetNextAvailable_SkipsUnavailableIntermediateShellRegions()
    {
        var next = ShellFocusCyclePlanner.GetNextAvailable(
            ShellFocusTarget.FormulaBar,
            reverse: false,
            target => target is ShellFocusTarget.Worksheet or ShellFocusTarget.StatusBar);

        next.Should().Be(ShellFocusTarget.StatusBar);
    }

    private static bool IsContextualTaskPaneVisible(
        bool pivotFieldListVisible,
        bool slicerTimelineVisible) =>
        pivotFieldListVisible || slicerTimelineVisible;
}
