namespace Freexcel.App.Host;

internal static class RibbonAdaptiveLayoutEngine
{
    public static RibbonAdaptiveLayoutResult Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth)
    {
        if (groups.Count == 0)
            return new RibbonAdaptiveLayoutResult([], 0, false);

        var groupNames = GetGroupNames(groups);
        var states = RibbonAdaptiveLayoutPlanner.Plan(availableWidth, groups, fixedChromeWidth).ToArray();
        states = RibbonAdaptiveLayoutPlanner
            .ApplyBreakpointOverrides(availableWidth, groupNames, states)
            .ToArray();
        states = RibbonAdaptivePriorityPlanner
            .ApplyRuntimePriorityStates(availableWidth, groupNames, states)
            .ToArray();

        FitStatesToWidth(states, groups, fixedChromeWidth, availableWidth);
        ExpandStatesIntoAvailableWidth(states, groups, fixedChromeWidth, availableWidth);

        return new RibbonAdaptiveLayoutResult(
            states,
            MeasureStates(groups, states, fixedChromeWidth, availableWidth),
            RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(groupNames));
    }

    public static IReadOnlyList<double> BuildResizeThresholds(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth)
    {
        var thresholds = new SortedSet<double>(RibbonAdaptiveTabProfiles.GetBreakpointThresholds(GetGroupNames(groups)));
        foreach (var width in EnumerateThresholdCandidates(groups, fixedChromeWidth))
        {
            var layout = Plan(width, groups, fixedChromeWidth);
            thresholds.Add(layout.PlannedWidth);
        }

        return thresholds
            .Where(width => width > 0)
            .Distinct()
            .OrderBy(width => width)
            .ToList();
    }

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth) =>
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(GetGroupNames(groups), availableWidth);

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

    public static HashSet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth) =>
        RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(GetGroupNames(groups), availableWidth)
            .ToHashSet();

    public static bool TryCollapseOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes = null)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var i = states.Length - 1; i >= firstCollapsibleIndex; i--)
        {
            if (states[i] == RibbonAdaptiveGroupState.Collapsed)
                continue;

            if (protectedGroupIndexes?.Contains(i) == true)
                continue;

            states[i] = RibbonAdaptiveGroupState.Collapsed;
            return true;
        }

        return false;
    }

    private static void FitStatesToWidth(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        double availableWidth)
    {
        var protectedGroupIndexes = GetFallbackProtectedGroupIndexes(groups, availableWidth);
        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: availableWidth > 760, protectedGroupIndexes))
        {
        }

        if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
            return;

        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: false))
        {
        }
    }

    private static void ExpandStatesIntoAvailableWidth(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        double availableWidth)
    {
        var expandableIndexes = GetExpandableGroupIndexes(groups, availableWidth).ToHashSet();
        var madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;
            for (var i = 0; i < states.Length; i++)
            {
                if (!expandableIndexes.Contains(i))
                    continue;

                var currentState = states[i];
                if (!TryGetNextExpandedState(currentState, out var expandedState))
                    continue;

                states[i] = expandedState;
                if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
                {
                    madeProgress = true;
                    continue;
                }

                states[i] = currentState;
            }
        }
    }

    private static bool StatesFit(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        double fixedChromeWidth,
        double availableWidth) =>
        MeasureStates(groups, states, fixedChromeWidth, availableWidth) <= Math.Max(0, availableWidth - 4);

    private static double MeasureStates(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        double fixedChromeWidth,
        double availableWidth)
    {
        var width = Math.Max(0, fixedChromeWidth);
        for (var i = 0; i < groups.Count; i++)
            width += GetGroupWidth(groups[i], states[i], availableWidth);

        return width;
    }

    private static double GetGroupWidth(
        RibbonAdaptiveGroup group,
        RibbonAdaptiveGroupState state,
        double availableWidth) =>
        state switch
        {
            RibbonAdaptiveGroupState.Full => group.FullWidth,
            RibbonAdaptiveGroupState.SmallWithLabels => group.SmallWithLabelsWidth,
            RibbonAdaptiveGroupState.IconOnly => group.IconOnlyWidth,
            RibbonAdaptiveGroupState.Collapsed => RibbonCollapsedGroupPresentationPlanner.GetPlannedWidth(group.CollapsedWidth, availableWidth),
            _ => group.FullWidth
        };

    private static IEnumerable<double> EnumerateThresholdCandidates(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth)
    {
        if (groups.Count == 0)
            yield break;

        var states = Enumerable
            .Repeat(RibbonAdaptiveGroupState.Full, groups.Count)
            .ToArray();
        yield return MeasureStates(groups, states, fixedChromeWidth, double.PositiveInfinity);

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            states[i] = RibbonAdaptiveGroupState.SmallWithLabels;
            yield return MeasureStates(groups, states, fixedChromeWidth, double.PositiveInfinity);

            states[i] = RibbonAdaptiveGroupState.IconOnly;
            yield return MeasureStates(groups, states, fixedChromeWidth, double.PositiveInfinity);

            states[i] = RibbonAdaptiveGroupState.Collapsed;
            yield return MeasureStates(groups, states, fixedChromeWidth, 1200);
            yield return MeasureStates(groups, states, fixedChromeWidth, 800);
        }
    }

    private static IReadOnlyList<string> GetGroupNames(IReadOnlyList<RibbonAdaptiveGroup> groups)
    {
        var names = new string[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            names[i] = groups[i].Name;

        return names;
    }
}

internal readonly record struct RibbonAdaptiveLayoutResult(
    IReadOnlyList<RibbonAdaptiveGroupState> States,
    double PlannedWidth,
    bool RequiresMeasuredCorrection);
