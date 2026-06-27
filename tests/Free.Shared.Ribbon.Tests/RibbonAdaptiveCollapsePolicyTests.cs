namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonAdaptiveCollapsePolicyTests
{
    [Fact]
    public void Plan_KeepsGroupsExpandedWhenMeasuredWidthsFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveCollapseGroup("Clipboard", 160, 64, 100),
            new RibbonAdaptiveCollapseGroup("Font", 180, 64, 10),
            new RibbonAdaptiveCollapseGroup("Editing", 140, 64, 50)
        };

        RibbonAdaptiveCollapsePolicy.Plan(520, groups, fixedChromeWidth: 20)
            .Should()
            .Equal(
                new RibbonAdaptiveCollapseDecision("Clipboard", RibbonAdaptiveGroupState.Full),
                new RibbonAdaptiveCollapseDecision("Font", RibbonAdaptiveGroupState.Full),
                new RibbonAdaptiveCollapseDecision("Editing", RibbonAdaptiveGroupState.Full));
    }

    [Fact]
    public void Plan_CollapsesLowestPriorityGroupsUntilMeasuredWidthsFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveCollapseGroup("Clipboard", 160, 64, 100),
            new RibbonAdaptiveCollapseGroup("Font", 180, 64, 10),
            new RibbonAdaptiveCollapseGroup("Editing", 140, 64, 50)
        };

        RibbonAdaptiveCollapsePolicy.Plan(360, groups, fixedChromeWidth: 20)
            .Should()
            .Equal(
                new RibbonAdaptiveCollapseDecision("Clipboard", RibbonAdaptiveGroupState.Full),
                new RibbonAdaptiveCollapseDecision("Font", RibbonAdaptiveGroupState.Collapsed),
                new RibbonAdaptiveCollapseDecision("Editing", RibbonAdaptiveGroupState.Collapsed));
    }

    [Fact]
    public void Plan_UsesSourceOrderAsTieBreakForEqualPriorityGroups()
    {
        var groups = new[]
        {
            new RibbonAdaptiveCollapseGroup("First", 150, 64, 10),
            new RibbonAdaptiveCollapseGroup("Second", 150, 64, 10),
            new RibbonAdaptiveCollapseGroup("Protected", 150, 64, 100)
        };

        RibbonAdaptiveCollapsePolicy.Plan(370, groups)
            .Should()
            .Equal(
                new RibbonAdaptiveCollapseDecision("First", RibbonAdaptiveGroupState.Collapsed),
                new RibbonAdaptiveCollapseDecision("Second", RibbonAdaptiveGroupState.Full),
                new RibbonAdaptiveCollapseDecision("Protected", RibbonAdaptiveGroupState.Full));
    }

    [Fact]
    public void Plan_SkipsCollapseWhenCollapsedWidthDoesNotSaveSpace()
    {
        var groups = new[]
        {
            new RibbonAdaptiveCollapseGroup("Narrow", 80, 80, 1),
            new RibbonAdaptiveCollapseGroup("Wide", 200, 64, 2)
        };

        RibbonAdaptiveCollapsePolicy.Plan(160, groups)
            .Should()
            .Equal(
                new RibbonAdaptiveCollapseDecision("Narrow", RibbonAdaptiveGroupState.Full),
                new RibbonAdaptiveCollapseDecision("Wide", RibbonAdaptiveGroupState.Collapsed));
    }
}
