using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutEngineTests
{
    [Fact]
    public void Plan_CombinesMeasuredWidthsBreakpointsAndPriorityFallbacks()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 170, 130, 78, 58),
            new RibbonAdaptiveGroup("Queries & Connections", 155, 118, 70, 58),
            new RibbonAdaptiveGroup("Data Types", 140, 110, 66, 58),
            new RibbonAdaptiveGroup("Sort & Filter", 150, 112, 72, 58),
            new RibbonAdaptiveGroup("Data Tools", 210, 168, 92, 58),
            new RibbonAdaptiveGroup("Forecast", 125, 96, 58, 58),
            new RibbonAdaptiveGroup("Outline", 120, 92, 54, 58)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(1120, groups, fixedChromeWidth: 42);

        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Queries & Connections")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Collapsed);
        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Data Tools")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Full);
        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Forecast")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(1120);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void BuildResizeThresholds_ReturnsStableSortedBreakpointsForResizeGate()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 120, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 220, 156, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 190, 132, 88, 56),
            new RibbonAdaptiveGroup("Number", 150, 112, 76, 54)
        };

        var thresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(groups, fixedChromeWidth: 36);

        thresholds.Should().BeInAscendingOrder();
        thresholds.Should().Contain([700, 760, 900, 920, 1120, 1300, 1320, 1500]);
        thresholds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TryCollapseOneMoreGroup_RespectsProtectedIndexes()
    {
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full
        };

        var collapsed = RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(
            states,
            preserveFirstGroup: true,
            protectedGroupIndexes: new HashSet<int> { 2 });

        collapsed.Should().BeTrue();
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full);
    }
}
