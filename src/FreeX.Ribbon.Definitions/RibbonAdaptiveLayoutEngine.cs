namespace FreeX.Ribbon.Definitions;

public static class RibbonAdaptiveLayoutEngine
{
    private const double ResizeThresholdProbeDelta = 0.5;

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

    public static RibbonAdaptiveLayoutResult Plan(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        var states = Free.Shared.Ribbon.RibbonAdaptiveLayoutPlanner.Plan(availableWidth, groups, fixedChromeWidth);
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
        return BuildResizeThresholds(groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
    }

    public static IReadOnlyList<double> BuildResizeThresholds(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        string? selectedTabHeader = null)
    {
        var thresholds = new SortedSet<double>(RibbonAdaptiveTabProfiles.GetBreakpointThresholds(groupProfileKeys, selectedTabHeader));
        foreach (var threshold in Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.Thresholds)
            thresholds.Add(threshold);

        foreach (var width in EnumerateThresholdCandidates(groups, fixedChromeWidth))
        {
            var layout = Plan(width, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
            if (layout.RequiresMeasuredCorrection ||
                ShouldKeepResizeThreshold(layout.PlannedWidth, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader))
            {
                thresholds.Add(layout.PlannedWidth);
            }
        }

        var positiveThresholds = new List<double>(thresholds.Count);
        foreach (var width in thresholds)
        {
            if (width > 0)
                positiveThresholds.Add(width);
        }

        return positiveThresholds;
    }

    private static bool ShouldKeepResizeThreshold(
        double threshold,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        IReadOnlyList<string> groupProfileKeys,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        if (threshold <= 0)
            return false;

        var belowThreshold = Math.Max(0, threshold - ResizeThresholdProbeDelta);
        var aboveThreshold = threshold + ResizeThresholdProbeDelta;
        if (!string.Equals(
                Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetCacheKey(belowThreshold),
                Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetCacheKey(aboveThreshold),
                StringComparison.Ordinal))
        {
            return true;
        }

        var belowLayout = Plan(belowThreshold, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
        var aboveLayout = Plan(aboveThreshold, groups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
        return !StatesEqual(belowLayout.States, aboveLayout.States);
    }

    private static bool StatesEqual(
        IReadOnlyList<RibbonAdaptiveGroupState> first,
        IReadOnlyList<RibbonAdaptiveGroupState> second)
    {
        if (first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (first[i] != second[i])
                return false;
        }

        return true;
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
        => Free.Shared.Ribbon.RibbonAdaptiveStateTransitions.TryGetNextExpandedState(state, out expandedState);

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
        => TryCollapseOneMoreGroup(
            states,
            preserveFirstGroup,
            protectedGroupIndexes,
            out _,
            out _);

    public static bool TryCollapseOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        out int changedIndex,
        out RibbonAdaptiveGroupState previousState) =>
        Free.Shared.Ribbon.RibbonAdaptiveStateTransitions.TryApplyNextCollapse(
            states,
            preserveFirstGroup,
            protectedGroupIndexes,
            out changedIndex,
            out previousState);

    public static bool TryFallbackOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes = null)
        => TryFallbackOneMoreGroup(
            states,
            preserveFirstGroup,
            protectedGroupIndexes,
            out _,
            out _);

    public static bool TryFallbackOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        bool preserveFirstGroup,
        double availableWidth,
        IReadOnlySet<int>? protectedGroupIndexes = null)
        => TryFallbackOneMoreGroup(
            states,
            groups,
            preserveFirstGroup,
            protectedGroupIndexes,
            availableWidth,
            out _,
            out _);

    public static bool TryFallbackOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        double availableWidth,
        out int changedIndex,
        out RibbonAdaptiveGroupState previousState)
    {
        var rollbackCount = 0;
        return TryFallbackOneMoreGroupCore(
            states,
            groups,
            availableWidth,
            preserveFirstGroup,
            protectedGroupIndexes,
            null,
            null,
            ref rollbackCount,
            out changedIndex,
            out previousState);
    }

    public static bool TryFallbackOneMoreGroup(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        out int changedIndex,
        out RibbonAdaptiveGroupState previousState)
    {
        var rollbackCount = 0;
        return TryFallbackOneMoreGroupCore(
            states,
            null,
            double.PositiveInfinity,
            preserveFirstGroup,
            protectedGroupIndexes,
            null,
            null,
            ref rollbackCount,
            out changedIndex,
            out previousState);
    }

    private static bool TryFallbackOneMoreGroupCore(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<RibbonAdaptiveGroup>? groups,
        double availableWidth,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        int[]? rollbackIndexes,
        RibbonAdaptiveGroupState[]? rollbackStates,
        ref int rollbackCount,
        out int changedIndex,
        out RibbonAdaptiveGroupState previousState)
    {
        RibbonAdaptiveStateTransition transition;
        var foundTransition = groups is not null
            ? Free.Shared.Ribbon.RibbonAdaptiveStateTransitions.TryFindNextFallback(
                states,
                groups,
                preserveFirstGroup,
                protectedGroupIndexes,
                availableWidth,
                GetGroupWidth,
                out transition)
            : Free.Shared.Ribbon.RibbonAdaptiveStateTransitions.TryFindNextFallback(
                states,
                preserveFirstGroup,
                protectedGroupIndexes,
                out transition);

        if (!foundTransition)
        {
            changedIndex = -1;
            previousState = default;
            return false;
        }

        changedIndex = transition.Index;
        previousState = transition.PreviousState;
        ApplyStateTransition(states, transition, rollbackIndexes, rollbackStates, ref rollbackCount);
        return true;
    }

    private static void ApplyStateTransition(
        RibbonAdaptiveGroupState[] states,
        RibbonAdaptiveStateTransition transition,
        int[]? rollbackIndexes,
        RibbonAdaptiveGroupState[]? rollbackStates,
        ref int rollbackCount)
    {
        RecordStateChange(
            transition.Index,
            transition.PreviousState,
            rollbackIndexes,
            rollbackStates,
            ref rollbackCount);
        Free.Shared.Ribbon.RibbonAdaptiveStateTransitions.Apply(states, transition);
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
        if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
            return;

        var protectedGroupIndexes = CreateProtectedGroupIndexSet(
            groupProfileKeys,
            availableWidth,
            selectedTabHeader);
        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryFallbackOneMoreGroup(
                   states,
                   groups,
                   availableWidth > 760,
                   availableWidth,
                   protectedGroupIndexes))
        {
        }

        if (StatesFit(groups, states, fixedChromeWidth, availableWidth))
            return;

        while (!StatesFit(groups, states, fixedChromeWidth, availableWidth) &&
               TryFallbackOneMoreGroup(
                   states,
                   groups,
                   false,
                   availableWidth,
                   protectedGroupIndexes))
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
            .GetExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader);
        var spaceFillingExpandableIndexes = RibbonAdaptivePriorityPlanner
            .GetSpaceFillingExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader);
        if (expandableIndexes.Count == 0 && spaceFillingExpandableIndexes.Count == 0)
            return;

        var protectedIndexes = CreateProtectedGroupIndexSet(
            groupProfileKeys,
            availableWidth,
            selectedTabHeader);
        var rollbackIndexes = new int[states.Length];
        var rollbackStates = new RibbonAdaptiveGroupState[states.Length];
        var madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;
            for (var i = 0; i < states.Length; i++)
            {
                if (!ContainsIndex(expandableIndexes, i))
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

                protectedIndexes ??= new HashSet<int>();
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

    private static HashSet<int>? CreateProtectedGroupIndexSet(
        IReadOnlyList<string> groupProfileKeys,
        double availableWidth,
        string? selectedTabHeader)
    {
        var fallbackProtectedIndexes = RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader);
        var runtimeVisibilityProtectedIndexes = RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader);
        if (fallbackProtectedIndexes.Count == 0 && runtimeVisibilityProtectedIndexes.Count == 0)
            return null;

        var protectedIndexes = fallbackProtectedIndexes.Count > 0
            ? new HashSet<int>(fallbackProtectedIndexes)
            : new HashSet<int>();
        if (runtimeVisibilityProtectedIndexes.Count > 0)
            protectedIndexes.UnionWith(runtimeVisibilityProtectedIndexes);

        return protectedIndexes;
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
               TryFallbackOneMoreGroupCore(
                   states,
                   groups,
                   availableWidth,
                   false,
                   protectedGroupIndexes,
                   rollbackIndexes,
                   rollbackStates,
                   ref rollbackCount,
                   out _,
                   out _))
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
            RibbonAdaptiveGroupState.Collapsed => Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetPlannedWidth(group.CollapsedWidth, availableWidth),
            _ => group.FullWidth
        };

    private static IEnumerable<double> EnumerateThresholdCandidates(
        IReadOnlyList<RibbonAdaptiveGroup> groups,
        double fixedChromeWidth)
    {
        if (groups.Count == 0)
            yield break;

        var states = new RibbonAdaptiveGroupState[groups.Count];
        Array.Fill(states, RibbonAdaptiveGroupState.Full);
        yield return MeasureStates(groups, states, fixedChromeWidth, double.PositiveInfinity);

        while (TryFallbackOneMoreGroup(
                   states,
                   preserveFirstGroup: false,
                   protectedGroupIndexes: null,
                   out _,
                   out var previousState))
        {
            if (previousState == RibbonAdaptiveGroupState.IconOnly)
            {
                yield return MeasureStates(groups, states, fixedChromeWidth, 1200);
                yield return MeasureStates(groups, states, fixedChromeWidth, 800);
            }
            else
            {
                yield return MeasureStates(groups, states, fixedChromeWidth, double.PositiveInfinity);
            }
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

public readonly record struct RibbonAdaptiveLayoutResult(
    IReadOnlyList<RibbonAdaptiveGroupState> States,
    double PlannedWidth,
    bool RequiresMeasuredCorrection);
