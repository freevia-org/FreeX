namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonAdaptiveLayoutPlannerTests
{
    [Fact]
    public void Plan_ReturnsEmptyLayoutForEmptyGroupSet()
    {
        RibbonAdaptiveLayoutPlanner.Plan(900, [], fixedChromeWidth: 36)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Plan_UsesRendererNeutralFallbackOrderFromRightToLeft()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 180, 130, 80, 52),
            new RibbonAdaptiveGroup("Font", 220, 150, 96, 52),
            new RibbonAdaptiveGroup("Editing", 160, 120, 72, 52)
        };

        RibbonAdaptiveLayoutPlanner.Plan(420, groups)
            .Should()
            .Equal(
                RibbonAdaptiveGroupState.SmallWithLabels,
                RibbonAdaptiveGroupState.SmallWithLabels,
                RibbonAdaptiveGroupState.SmallWithLabels);
    }

    [Fact]
    public void Plan_SubtractsFixedChromeBeforePlanning()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 200, 140, 80, 48),
            new RibbonAdaptiveGroup("Charts", 200, 140, 80, 48)
        };

        RibbonAdaptiveLayoutPlanner.Plan(330, groups, fixedChromeWidth: 30)
            .Should()
            .Equal(
                RibbonAdaptiveGroupState.SmallWithLabels,
                RibbonAdaptiveGroupState.SmallWithLabels);
    }

    [Fact]
    public void Plan_DoesNotFallbackWhenNextStateDoesNotSaveSpace()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Narrow", 80, 80, 80, 80),
            new RibbonAdaptiveGroup("Wide", 200, 150, 90, 48)
        };

        RibbonAdaptiveLayoutPlanner.Plan(120, groups)
            .Should()
            .Equal(
                RibbonAdaptiveGroupState.Full,
                RibbonAdaptiveGroupState.Collapsed);
    }
}
