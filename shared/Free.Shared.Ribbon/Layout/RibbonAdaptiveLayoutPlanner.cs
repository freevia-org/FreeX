namespace Free.Shared.Ribbon;

/// <summary>
/// Pure width-based adaptive ribbon planning shared by renderers. App-specific tab policy can still
/// layer overrides on top, but this first-pass fallback order is renderer-neutral.
/// </summary>
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
               RibbonAdaptiveStateTransitions.TryFindNextFallback(
                   states,
                   groups,
                   preserveFirstGroup: false,
                   protectedGroupIndexes: null,
                   availableWidth,
                   widthResolver: null,
                   out var transition))
        {
            var currentWidth = WidthFor(groups[transition.Index], transition.PreviousState);
            var targetWidth = WidthFor(groups[transition.Index], transition.NextState);
            states[transition.Index] = transition.NextState;
            width = width - currentWidth + targetWidth;
        }

        return states;
    }

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
