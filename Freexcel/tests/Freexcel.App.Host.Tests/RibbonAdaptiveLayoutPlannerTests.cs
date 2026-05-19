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
}
