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

        var groupProfileKeys = GetGroupProfileKeys(groups);
        return Plan(availableWidth, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
    }

    private static RibbonAdaptiveLayoutResult Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        var states = RibbonAdaptiveLayoutPlanner.Plan(availableWidth, groups, fixedChromeWidth);
        RibbonAdaptiveTabProfiles.ApplyPlanOverridesInPlace(
            availableWidth,
            groupProfileKeys,
            states,
            selectedTabHeader);

        FitStatesToWidth(states, groups, groupProfileKeys, fixedChromeWidth, availableWidth, selectedTabHeader);
        ExpandStatesIntoAvailableWidth(states, groups, groupProfileKeys, fixedChromeWidth, availableWidth, selectedTabHeader);

        return new RibbonAdaptiveLayoutResult(
            states,
            MeasureStates(groups, states, fixedChromeWidth, availableWidth),
            RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(groupProfileKeys, selectedTabHeader));
    }

    public static IReadOnlyList<double> BuildResizeThresholds(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        string? selectedTabHeader = null)
    {
        var groupProfileKeys = GetGroupProfileKeys(groups);
        var thresholds = new SortedSet<double>(RibbonAdaptiveTabProfiles.GetBreakpointThresholds(groupProfileKeys, selectedTabHeader));
        foreach (var width in EnumerateThresholdCandidates(groups, fixedChromeWidth))
        {
            var layout = Plan(width, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
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
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(GetGroupProfileKeys(groups), availableWidth, selectedTabHeader);

    public static IReadOnlyList<int> GetSpaceFillingExpandableGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptivePriorityPlanner.GetSpaceFillingExpandableGroupIndexes(GetGroupProfileKeys(groups), availableWidth, selectedTabHeader);

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
            .GetFallbackProtectedGroupIndexes(GetGroupProfileKeys(groups), availableWidth, selectedTabHeader)
            .ToHashSet();

    public static HashSet<int> GetRuntimeVisibilityProtectedGroupIndexes(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(GetGroupProfileKeys(groups), availableWidth, selectedTabHeader)
            .ToHashSet();

    public static bool TryCollapseOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes = null)
    {
        var rollbackCount = 0;
        return TryCollapseOneMoreGroupCore(
            states,
            preserveFirstGroup,
            protectedGroupIndexes,
            rollbackIndexes: null,
            rollbackStates: null,
            ref rollbackCount);
    }

    private static bool TryCollapseOneMoreGroupCore(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        int[]? rollbackIndexes,
        RibbonAdaptiveGroupState[]? rollbackStates,
        ref int rollbackCount)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var i = states.Length - 1; i >= firstCollapsibleIndex; i--)
        {
            if (states[i] == RibbonAdaptiveGroupState.Collapsed)
                continue;

            if (protectedGroupIndexes?.Contains(i) == true)
                continue;

            RecordStateChange(i, states[i], rollbackIndexes, rollbackStates, ref rollbackCount);
            states[i] = RibbonAdaptiveGroupState.Collapsed;
            return true;
        }

        return false;
    }

    private static void RecordStateChange(
        int index,
        RibbonAdaptiveGroupState previousState,
        int[]? rollbackIndexes,
        RibbonAdaptiveGroupState[]? rollbackStates,
        ref int rollbackCount)
    {
        if (rollbackIndexes is null || rollbackStates is null)
            return;

        rollbackIndexes[rollbackCount] = index;
        rollbackStates[rollbackCount] = previousState;
        rollbackCount++;
    }

    private static void RollbackStateChanges(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<int> rollbackIndexes,
        IReadOnlyList<RibbonAdaptiveGroupState> rollbackStates,
        int rollbackCount)
    {
        for (var rollbackIndex = rollbackCount - 1; rollbackIndex >= 0; rollbackIndex--)
            states[rollbackIndexes[rollbackIndex]] = rollbackStates[rollbackIndex];
    }

    private static void FitStatesToWidth(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        double availableWidth,
        string? selectedTabHeader)
    {
        var protectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        var runtimeVisibilityProtectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        protectedGroupIndexes.UnionWith(runtimeVisibilityProtectedGroupIndexes);
        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: availableWidth > 760, protectedGroupIndexes))
        {
        }

        if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
            return;

        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroup(states, preserveFirstGroup: false, protectedGroupIndexes))
        {
        }
    }

    private static void ExpandStatesIntoAvailableWidth(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        double availableWidth,
        string? selectedTabHeader)
    {
        var expandableIndexes = RibbonAdaptivePriorityPlanner
            .GetExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        var protectedIndexes = RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        protectedIndexes.UnionWith(
            RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader));
        var rollbackIndexes = new int[states.Length];
        var rollbackStates = new RibbonAdaptiveGroupState[states.Length];
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

                var rollbackCount = 0;
                RecordStateChange(i, currentState, rollbackIndexes, rollbackStates, ref rollbackCount);
                states[i] = expandedState;
                if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
                {
                    madeProgress = true;
                    continue;
                }

                protectedIndexes.Add(i);
                if (TryCollapseUnprotectedGroupsToFit(
                    states,
                    groups,
                    fixedChromeWidth,
                    availableWidth,
                    protectedIndexes,
                    rollbackIndexes,
                    rollbackStates,
                    ref rollbackCount))
                {
                    madeProgress = true;
                    continue;
                }

                RollbackStateChanges(states, rollbackIndexes, rollbackStates, rollbackCount);
            }
        }

        var spaceFillingExpandableIndexes = RibbonAdaptivePriorityPlanner
            .GetSpaceFillingExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader);
        madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;
            for (var i = 0; i < states.Length; i++)
            {
                if (!ContainsIndex(spaceFillingExpandableIndexes, i))
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

    private static bool ContainsIndex(IReadOnlyList<int> indexes, int index)
    {
        for (var i = 0; i < indexes.Count; i++)
        {
            if (indexes[i] == index)
                return true;
        }

        return false;
    }

    private static bool TryCollapseUnprotectedGroupsToFit(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth,
        double availableWidth,
        IReadOnlySet<int> protectedGroupIndexes,
        int[] rollbackIndexes,
        RibbonAdaptiveGroupState[] rollbackStates,
        ref int rollbackCount)
    {
        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryCollapseOneMoreGroupCore(
                   states,
                   preserveFirstGroup: false,
                   protectedGroupIndexes,
                   rollbackIndexes,
                   rollbackStates,
                   ref rollbackCount))
        {
        }

        return StatesFit(groups, states, fixedChromeWidth, availableWidth);
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

    private static IReadOnlyList<string> GetGroupProfileKeys(IReadOnlyList<RibbonAdaptiveGroup> groups)
    {
        var names = new string[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            names[i] = string.IsNullOrWhiteSpace(groups[i].CatalogId)
                ? groups[i].Name
                : groups[i].CatalogId!;

        return names;
    }
}

internal readonly record struct RibbonAdaptiveLayoutResult(
    IReadOnlyList<RibbonAdaptiveGroupState> States,
    double PlannedWidth,
    bool RequiresMeasuredCorrection);
