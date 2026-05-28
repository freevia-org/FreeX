using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutEngineTests
{
    [Fact]
    public void Plan_ReturnsEmptyLayoutForEmptyGroupSet()
    {
        var layout = RibbonAdaptiveLayoutEngine.Plan(900, [], fixedChromeWidth: 36);

        layout.States.Should().BeEmpty();
        layout.PlannedWidth.Should().Be(0);
        layout.RequiresMeasuredCorrection.Should().BeFalse();
    }

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

    [Theory]
    [InlineData(RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.IconOnly, true)]
    [InlineData(RibbonAdaptiveGroupState.IconOnly, RibbonAdaptiveGroupState.SmallWithLabels, true)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, RibbonAdaptiveGroupState.Full, true)]
    [InlineData(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full, false)]
    public void TryGetNextExpandedState_ExpandsOneStepUntilFull(
        RibbonAdaptiveGroupState currentState,
        RibbonAdaptiveGroupState expectedState,
        bool expectedResult)
    {
        var result = RibbonAdaptiveLayoutEngine.TryGetNextExpandedState(currentState, out var expandedState);

        result.Should().Be(expectedResult);
        expandedState.Should().Be(expectedState);
    }

    [Fact]
    public void Plan_RelaxesProtectedFallbacksWhenPriorityGroupsStillOverflow()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 170, 130, 78, 58),
            new RibbonAdaptiveGroup("Queries & Connections", 155, 118, 70, 58),
            new RibbonAdaptiveGroup("Data Types", 140, 110, 66, 58),
            new RibbonAdaptiveGroup("Sort & Filter", 150, 112, 72, 58),
            new RibbonAdaptiveGroup("Data Tools", 500, 168, 92, 58),
            new RibbonAdaptiveGroup("Forecast", 420, 96, 58, 58),
            new RibbonAdaptiveGroup("Outline", 120, 92, 54, 58)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(820, groups, fixedChromeWidth: 42);
        var groupNames = groups.Select(group => group.Name).ToArray();

        layout.PlannedWidth.Should().BeLessThanOrEqualTo(820);
        layout.States[Array.IndexOf(groupNames, "Data Tools")]
            .Should()
            .NotBe(RibbonAdaptiveGroupState.Full);
        layout.States[Array.IndexOf(groupNames, "Forecast")]
            .Should()
            .NotBe(RibbonAdaptiveGroupState.Full);
    }
}
