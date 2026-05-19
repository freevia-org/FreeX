using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutPlannerTests
{
    [Fact]
    public void Plan_KeepsGroupsFullWhenTheyFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 80, 56, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(320, groups);

        states.Should().Equal(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void Plan_CompressesGroupsFromRightToLeftBeforeCollapsing()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(390, groups);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void Plan_CollapsesGroupsWhenIconOnlyFootprintStillDoesNotFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56),
            new RibbonAdaptiveGroup("Number", 150, 112, 76, 54)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(270, groups);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void Plan_ReservesFixedChromeWidthForDividersAndPadding()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 100, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 200, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 180, 88, 56)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(500, groups, fixedChromeWidth: 50);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void ApplyBreakpointOverrides_CollapsesAllGroupsAtVeryNarrowWidths()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            700,
            ["Clipboard", "Font", "Alignment"],
            [RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.IconOnly]);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesHomeTabExcelLikeBreakpoints()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 7).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesFormulasFunctionLibraryBreakpoint()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1500,
            ["Function Library", "Defined Names", "Formula Auditing", "Calculation"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 4).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesGenericCollapseFromRules()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Review", "Comments", "Protect"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 3).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }
}
