namespace FreeX.App.Host;

public static class RibbonAdaptiveLayoutPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth = 0)
    {
        availableWidth = Math.Max(0, availableWidth - Math.Max(0, fixedChromeWidth));
        var states = new RibbonAdaptiveGroupState[groups.Count];
        Array.Fill(states, RibbonAdaptiveGroupState.Full);

        if (groups.Count == 0 || Fits(availableWidth, groups, states))
            return states;

        for (var index = groups.Count - 1; index >= 0; index--)
        {
            states[index] = RibbonAdaptiveGroupState.SmallWithLabels;
            if (Fits(availableWidth, groups, states))
                return states;

            states[index] = RibbonAdaptiveGroupState.IconOnly;
            if (Fits(availableWidth, groups, states))
                return states;

            states[index] = RibbonAdaptiveGroupState.Collapsed;
            if (Fits(availableWidth, groups, states))
                return states;
        }

        return states;
    }

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates) =>
        RibbonAdaptiveTabProfiles.ApplyBreakpointOverrides(availableWidth, groupNames, plannedStates);

    private static bool Fits(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        var width = 0d;
        for (var index = 0; index < groups.Count; index++)
        {
            width += WidthFor(groups[index], states[index]);
            if (width > availableWidth)
                return false;
        }

        return true;
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

public sealed record RibbonAdaptiveGroup(
    string Name,
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth);

public enum RibbonAdaptiveGroupState
{
    Full,
    SmallWithLabels,
    IconOnly,
    Collapsed
}
