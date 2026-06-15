namespace FreeX.App.Host;

public static class RibbonAdaptiveLayoutPlanner
{
    public static RibbonAdaptiveGroupState[] Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth = 0)
    {
        availableWidth = Math.Max(0, availableWidth - Math.Max(0, fixedChromeWidth));
        var states = new RibbonAdaptiveGroupState[groups.Count];
        Array.Fill(states, RibbonAdaptiveGroupState.Full);

        if (groups.Count == 0)
            return states;

        var width = 0d;
        for (var index = 0; index < groups.Count; index++)
            width += WidthFor(groups[index], RibbonAdaptiveGroupState.Full);

        if (width <= availableWidth)
            return states;

        while (width > availableWidth &&
               TryFallbackNextGroup(states, groups, ref width))
        {
        }

        return states;
    }

    private static bool TryFallbackNextGroup(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        ref double width)
    {
        for (var targetValue = (int)RibbonAdaptiveGroupState.SmallWithLabels;
             targetValue <= (int)RibbonAdaptiveGroupState.Collapsed;
             targetValue++)
        {
            var targetState = (RibbonAdaptiveGroupState)targetValue;
            for (var index = groups.Count - 1; index >= 0; index--)
            {
                var currentState = states[index];
                if ((int)currentState >= targetValue)
                    continue;

                var currentWidth = WidthFor(groups[index], currentState);
                var targetWidth = WidthFor(groups[index], targetState);
                if (targetWidth >= currentWidth - 0.5)
                    continue;

                states[index] = targetState;
                width = width - currentWidth + targetWidth;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates) =>
        RibbonAdaptiveTabProfiles.ApplyBreakpointOverrides(availableWidth, groupNames, plannedStates);

    private static double WidthFor(RibbonAdaptiveGroup group, RibbonAdaptiveGroupState state) =>
        state switch
        {
            RibbonAdaptiveGroupState.Full => group.FullWidth,
            RibbonAdaptiveGroupState.SmallWithLabels => group.SmallWithLabelsWidth,
            RibbonAdaptiveGroupState.IconOnly => group.IconOnlyWidth,
            RibbonAdaptiveGroupState.Collapsed => group.CollapsedWidth,
            _ => group.FullWidth
        };

}
