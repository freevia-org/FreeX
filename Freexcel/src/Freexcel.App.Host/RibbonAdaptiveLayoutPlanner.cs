namespace Freexcel.App.Host;

public static class RibbonAdaptiveLayoutPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth = 0)
    {
        availableWidth = Math.Max(0, availableWidth - Math.Max(0, fixedChromeWidth));
        var states = Enumerable
            .Repeat(RibbonAdaptiveGroupState.Full, groups.Count)
            .ToArray();

        if (groups.Count == 0 || Fits(availableWidth, groups, states))
            return states;

        foreach (var index in RightToLeft(groups.Count))
        {
            states[index] = RibbonAdaptiveGroupState.SmallWithLabels;
            if (Fits(availableWidth, groups, states))
                return states;

            states[index] = RibbonAdaptiveGroupState.IconOnly;
            if (Fits(availableWidth, groups, states))
                return states;

            if (index == 0 && groups.Count > 1)
                continue;

            states[index] = RibbonAdaptiveGroupState.Collapsed;
            if (Fits(availableWidth, groups, states))
                return states;
        }

        return states;
    }

    private static bool Fits(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        groups.Select((group, index) => WidthFor(group, states[index])).Sum() <= availableWidth;

    private static double WidthFor(RibbonAdaptiveGroup group, RibbonAdaptiveGroupState state) =>
        state switch
        {
            RibbonAdaptiveGroupState.Full => group.FullWidth,
            RibbonAdaptiveGroupState.SmallWithLabels => group.SmallWithLabelsWidth,
            RibbonAdaptiveGroupState.IconOnly => group.IconOnlyWidth,
            RibbonAdaptiveGroupState.Collapsed => group.CollapsedWidth,
            _ => group.FullWidth
        };

    private static IEnumerable<int> RightToLeft(int count)
    {
        for (var i = count - 1; i >= 0; i--)
            yield return i;
    }
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
