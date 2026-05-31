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

        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];
            states[index] = RibbonAdaptiveGroupState.SmallWithLabels;
            width = ReplaceWidth(width, group, RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.SmallWithLabels);
            if (width <= availableWidth)
                return states;

            states[index] = RibbonAdaptiveGroupState.IconOnly;
            width = ReplaceWidth(width, group, RibbonAdaptiveGroupState.SmallWithLabels, RibbonAdaptiveGroupState.IconOnly);
            if (width <= availableWidth)
                return states;

            states[index] = RibbonAdaptiveGroupState.Collapsed;
            width = ReplaceWidth(width, group, RibbonAdaptiveGroupState.IconOnly, RibbonAdaptiveGroupState.Collapsed);
            if (width <= availableWidth)
                return states;
        }

        return states;
    }

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates) =>
        RibbonAdaptiveTabProfiles.ApplyBreakpointOverrides(availableWidth, groupNames, plannedStates);

    private static double ReplaceWidth(
        double width,
        RibbonAdaptiveGroup group,
        RibbonAdaptiveGroupState previousState,
        RibbonAdaptiveGroupState nextState) =>
        width - WidthFor(group, previousState) + WidthFor(group, nextState);

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

public sealed record RibbonAdaptiveGroup(
    string Name,
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth,
    string? CatalogId = null);

public enum RibbonAdaptiveGroupState
{
    Full,
    SmallWithLabels,
    IconOnly,
    Collapsed
}
