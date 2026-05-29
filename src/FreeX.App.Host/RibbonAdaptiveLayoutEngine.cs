namespace FreeX.App.Host;

internal static class RibbonAdaptiveLayoutEngine
{
    public static RibbonAdaptiveLayoutResult Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        string? selectedTabHeader = null)
    {
        if (groups.Count == 0)
            return new RibbonAdaptiveLayoutResult([], 0, false);

        var groupNames = GetGroupNames(groups);
        return Plan(availableWidth, groups, groupNames, fixedChromeWidth, selectedTabHeader);
    }

    private static RibbonAdaptiveLayoutResult Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupNames,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        var states = RibbonAdaptiveLayoutPlanner.Plan(availableWidth, groups, fixedChromeWidth).ToArray();
        states = RibbonAdaptiveTabProfiles
            .ApplyBreakpointOverrides(availableWidth, groupNames, states, selectedTabHeader)
            .ToArray();
        states = RibbonAdaptivePriorityPlanner
            .ApplyRuntimePriorityStates(availableWidth, groupNames, states, selectedTabHeader)
            .ToArray();
        states = RibbonAdaptivePriorityPlanner
            .ApplyRuntimeVisibilityStates(availableWidth, groupNames, states, selectedTabHeader)
            .ToArray();

        FitStatesToWidth(states, groups, groupNames, fixedChromeWidth, availableWidth, selectedTabHeader);
        ExpandStatesIntoAvailableWidth(states, groups, groupNames, fixedChromeWidth, availableWidth, selectedTabHeader);

        return new RibbonAdaptiveLayoutResult(
            states,
            MeasureStates(groups, states, fixedChromeWidth, availableWidth),
            RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(groupNames, selectedTabHeader));
    }

    public static IReadOnlyList<double> BuildResizeThresholds(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        string? selectedTabHeader = null)
    {
        var groupNames = GetGroupNames(groups);
        var thresholds = new SortedSet<double>(RibbonAdaptiveTabProfiles.GetBreakpointThresholds(groupNames, selectedTabHeader));
        foreach (var width in EnumerateThresholdCandidates(groups, fixedChromeWidth))
        {
            var layout = Plan(width, groups, groupNames, fixedChromeWidth, selectedTabHeader);
            thresholds.Add(layout.PlannedWidth);
        }

        var positiveThresholds = new List<double>(thresholds.Count);
        foreach (var width in thresholds)
        {
            if (width > 0)
                positiveThresholds.Add(width);
        }

        return positiveThresholds;
    }

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(GetGroupNames(groups), availableWidth, selectedTabHeader);

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
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(GetGroupNames(groups), availableWidth, selectedTabHeader)
            .ToHashSet();

    public static HashSet<int> GetRuntimeVisibilityProtectedGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(GetGroupNames(groups), availableWidth, selectedTabHeader)
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
        IReadOnlyList<string> groupNames,
        double fixedChromeWidth,
        double availableWidth,
        string? selectedTabHeader)
    {
        var protectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(groupNames, availableWidth, selectedTabHeader)
            .ToHashSet();
        var runtimeVisibilityProtectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(groupNames, availableWidth, selectedTabHeader)
            .ToHashSet();
        protectedGroupIndexes.UnionWith(runtimeVisibilityProtectedGroupIndexes);
        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: availableWidth > 760, protectedGroupIndexes))
        {
        }

        if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
            return;

        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: false, runtimeVisibilityProtectedGroupIndexes))
        {
        }
    }

    private static void ExpandStatesIntoAvailableWidth(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupNames,
        double fixedChromeWidth,
        double availableWidth,
        string? selectedTabHeader)
    {
        var expandableIndexes = RibbonAdaptivePriorityPlanner
            .GetExpandableGroupIndexes(groupNames, availableWidth, selectedTabHeader)
            .ToHashSet();
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
