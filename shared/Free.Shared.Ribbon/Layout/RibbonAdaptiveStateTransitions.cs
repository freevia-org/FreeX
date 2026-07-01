namespace Free.Shared.Ribbon;

public readonly record struct RibbonAdaptiveStateTransition(
    int Index,
    RibbonAdaptiveGroupState PreviousState,
    RibbonAdaptiveGroupState NextState);

public static class RibbonAdaptiveStateTransitions
{
    private const double MinimumUsefulWidthDelta = 0.5;

    public static bool TryGetNextExpandedState(
        RibbonAdaptiveGroupState state,
        out RibbonAdaptiveGroupState expandedState)
    {
        expandedState = state switch
        {
            RibbonAdaptiveGroupState.Collapsed => RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly => RibbonAdaptiveGroupState.SmallWithLabels,
            RibbonAdaptiveGroupState.SmallWithLabels => RibbonAdaptiveGroupState.Full,
            _ => state
        };

        return expandedState != state;
    }

    public static bool TryFindNextCollapse(
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        out RibbonAdaptiveStateTransition transition)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var index = states.Count - 1; index >= firstCollapsibleIndex; index--)
        {
            if (states[index] == RibbonAdaptiveGroupState.Collapsed)
                continue;

            if (protectedGroupIndexes?.Contains(index) == true)
                continue;

            transition = new RibbonAdaptiveStateTransition(
                index,
                states[index],
                RibbonAdaptiveGroupState.Collapsed);
            return true;
        }

        transition = default;
        return false;
    }

    public static bool TryApplyNextCollapse(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        out int changedIndex,
        out RibbonAdaptiveGroupState previousState)
    {
        if (TryFindNextCollapse(states, preserveFirstGroup, protectedGroupIndexes, out var transition))
        {
            Apply(states, transition);
            changedIndex = transition.Index;
            previousState = transition.PreviousState;
            return true;
        }

        changedIndex = -1;
        previousState = default;
        return false;
    }

    public static bool TryFindNextFallback(
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        out RibbonAdaptiveStateTransition transition)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var stateValue = (int)RibbonAdaptiveGroupState.Full;
             stateValue <= (int)RibbonAdaptiveGroupState.IconOnly;
             stateValue++)
        {
            var state = (RibbonAdaptiveGroupState)stateValue;
            for (var index = states.Count - 1; index >= firstCollapsibleIndex; index--)
            {
                if (states[index] != state)
                    continue;

                if (protectedGroupIndexes?.Contains(index) == true)
                    continue;

                transition = new RibbonAdaptiveStateTransition(
                    index,
                    states[index],
                    (RibbonAdaptiveGroupState)(stateValue + 1));
                return true;
            }
        }

        transition = default;
        return false;
    }

    public static bool TryFindNextFallback(
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        double availableWidth,
        Func<RibbonAdaptiveGroup, RibbonAdaptiveGroupState, double, double>? widthResolver,
        out RibbonAdaptiveStateTransition transition)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var targetValue = (int)RibbonAdaptiveGroupState.SmallWithLabels;
             targetValue <= (int)RibbonAdaptiveGroupState.Collapsed;
             targetValue++)
        {
            var targetState = (RibbonAdaptiveGroupState)targetValue;
            for (var index = states.Count - 1; index >= firstCollapsibleIndex; index--)
            {
                if (index >= groups.Count ||
                    (int)states[index] >= targetValue)
                {
                    continue;
                }

                if (protectedGroupIndexes?.Contains(index) == true)
                    continue;

                var currentWidth = GetGroupWidth(groups[index], states[index], availableWidth, widthResolver);
                var targetWidth = GetGroupWidth(groups[index], targetState, availableWidth, widthResolver);
                if (targetWidth >= currentWidth - MinimumUsefulWidthDelta)
                    continue;

                transition = new RibbonAdaptiveStateTransition(index, states[index], targetState);
                return true;
            }
        }

        transition = default;
        return false;
    }

    public static void Apply(
        RibbonAdaptiveGroupState[] states,
        RibbonAdaptiveStateTransition transition) =>
        states[transition.Index] = transition.NextState;

    private static double GetGroupWidth(
        RibbonAdaptiveGroup group,
        RibbonAdaptiveGroupState state,
        double availableWidth,
        Func<RibbonAdaptiveGroup, RibbonAdaptiveGroupState, double, double>? widthResolver) =>
        widthResolver?.Invoke(group, state, availableWidth) ??
        state switch
        {
            RibbonAdaptiveGroupState.Full => group.FullWidth,
            RibbonAdaptiveGroupState.SmallWithLabels => group.SmallWithLabelsWidth,
            RibbonAdaptiveGroupState.IconOnly => group.IconOnlyWidth,
            RibbonAdaptiveGroupState.Collapsed => group.CollapsedWidth,
            _ => group.FullWidth
        };
}
