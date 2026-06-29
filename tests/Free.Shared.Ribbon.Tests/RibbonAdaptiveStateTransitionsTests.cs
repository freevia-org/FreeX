namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonAdaptiveStateTransitionsTests
{
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
        var result = RibbonAdaptiveStateTransitions.TryGetNextExpandedState(currentState, out var expandedState);

        result.Should().Be(expectedResult);
        expandedState.Should().Be(expectedState);
    }

    [Fact]
    public void TryApplyNextCollapse_RespectsPreservedAndProtectedGroups()
    {
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full
        };

        var collapsed = RibbonAdaptiveStateTransitions.TryApplyNextCollapse(
            states,
            preserveFirstGroup: true,
            protectedGroupIndexes: new HashSet<int> { 2 },
            out var changedIndex,
            out var previousState);

        collapsed.Should().BeTrue();
        changedIndex.Should().Be(1);
        previousState.Should().Be(RibbonAdaptiveGroupState.Full);
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void TryFindNextFallback_UsesWidthAwareStagedFallbacks()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 100, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56)
        };
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full
        };

        RibbonAdaptiveStateTransitions.TryFindNextFallback(
                states,
                groups,
                preserveFirstGroup: false,
                protectedGroupIndexes: null,
                availableWidth: 900,
                widthResolver: null,
                out var firstTransition)
            .Should()
            .BeTrue();
        RibbonAdaptiveStateTransitions.Apply(states, firstTransition);
        RibbonAdaptiveStateTransitions.TryFindNextFallback(
                states,
                groups,
                preserveFirstGroup: false,
                protectedGroupIndexes: null,
                availableWidth: 900,
                widthResolver: null,
                out var secondTransition)
            .Should()
            .BeTrue();

        firstTransition.Should().Be(new RibbonAdaptiveStateTransition(
            2,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.SmallWithLabels));
        secondTransition.Should().Be(new RibbonAdaptiveStateTransition(
            1,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.SmallWithLabels));
    }
}
