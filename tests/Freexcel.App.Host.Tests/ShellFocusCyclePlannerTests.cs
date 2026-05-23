using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ShellFocusCyclePlannerTests
{
    [Theory]
    [InlineData(ShellFocusTarget.Worksheet, false, ShellFocusTarget.Ribbon)]
    [InlineData(ShellFocusTarget.Ribbon, false, ShellFocusTarget.FormulaBar)]
    [InlineData(ShellFocusTarget.FormulaBar, false, ShellFocusTarget.SheetTabs)]
    [InlineData(ShellFocusTarget.SheetTabs, false, ShellFocusTarget.StatusBar)]
    [InlineData(ShellFocusTarget.StatusBar, false, ShellFocusTarget.Worksheet)]
    [InlineData(ShellFocusTarget.Worksheet, true, ShellFocusTarget.StatusBar)]
    [InlineData(ShellFocusTarget.Ribbon, true, ShellFocusTarget.Worksheet)]
    public void GetNext_CyclesThroughExcelShellRegions(
        ShellFocusTarget current,
        bool reverse,
        ShellFocusTarget expected)
    {
        var next = ShellFocusCyclePlanner.GetNext(current, reverse);

        next.Should().Be(expected);
    }
}
