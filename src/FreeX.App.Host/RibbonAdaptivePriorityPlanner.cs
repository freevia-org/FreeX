namespace FreeX.App.Host;

internal static class RibbonAdaptivePriorityPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimePriorityStates(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        string? selectedTabHeader = null) =>
        ApplyRuntimeOverrides(
            plannedStates,
            GetRuntimeStateOverrides(availableWidth, groupNames, selectedTabHeader));

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimeVisibilityStates(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        string? selectedTabHeader = null) =>
        ApplyRuntimeOverrides(
            plannedStates,
            GetRuntimeVisibilityOverrides(availableWidth, groupNames, selectedTabHeader));

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeStateOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        RibbonAdaptiveTabProfiles.GetRuntimeStateOverrides(availableWidth, groupNames, selectedTabHeader);

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeVisibilityOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        RibbonAdaptiveTabProfiles.GetRuntimeVisibilityOverrides(availableWidth, groupNames, selectedTabHeader);

    public static IReadOnlySet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptiveTabProfiles.GetFallbackProtectedGroupIndexes(groupNames, availableWidth, selectedTabHeader);

    public static IReadOnlySet<int> GetRuntimeVisibilityProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null) =>
        GetRuntimeVisibilityProtectedGroupIndexesCore(
            groupNames,
            GetRuntimeVisibilityOverrides(availableWidth, groupNames, selectedTabHeader));

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null)
    {
        var profileIndexes = RibbonAdaptiveTabProfiles.GetExpandableGroupIndexes(groupNames, availableWidth, selectedTabHeader);
        var runtimeStateOverrides = GetRuntimeStateOverrides(availableWidth, groupNames, selectedTabHeader);
        var runtimeVisibilityOverrides = GetRuntimeVisibilityOverrides(availableWidth, groupNames, selectedTabHeader);
        var indexes = new List<int>(profileIndexes.Count);
        for (var profileIndex = 0; profileIndex < profileIndexes.Count; profileIndex++)
        {
            var index = profileIndexes[profileIndex];
            if (!ContainsDecisionIndex(runtimeStateOverrides, index) &&
                !ContainsDecisionIndex(runtimeVisibilityOverrides, index))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    public static IReadOnlyList<int> GetSpaceFillingExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null)
    {
        if (RibbonAdaptiveTabProfiles.DisablesPriorityExpansion(groupNames, selectedTabHeader))
            return [];

        var runtimeStateOverrides = GetRuntimeStateOverrides(availableWidth, groupNames, selectedTabHeader);
        var runtimeVisibilityOverrides = GetRuntimeVisibilityOverrides(availableWidth, groupNames, selectedTabHeader);
        var indexes = new List<int>(groupNames.Count);
        for (var index = 0; index < groupNames.Count; index++)
        {
            if (!ContainsDecisionIndex(runtimeStateOverrides, index) &&
                !ContainsDecisionIndex(runtimeVisibilityOverrides, index))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    public static bool RequiresMeasuredCorrection(
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        RibbonAdaptiveTabProfiles.RequiresMeasuredCorrection(groupNames, selectedTabHeader);

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null) =>
        RibbonAdaptiveTabProfiles.GetPriorityProtectedGroupNames(groupNames, availableWidth, selectedTabHeader);

    private static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimeOverrides(
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> decisions)
    {
        var states = plannedStates.ToArray();
        foreach (var decision in decisions)
        {
            if (decision.Index >= 0 && decision.Index < states.Length)
                states[decision.Index] = decision.State;
        }

        return states;
    }

    private static bool ContainsDecisionIndex(
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> decisions,
        int index)
    {
        for (var decisionIndex = 0; decisionIndex < decisions.Count; decisionIndex++)
        {
            if (decisions[decisionIndex].Index == index)
                return true;
        }

        return false;
    }

    private static IReadOnlySet<int> GetRuntimeVisibilityProtectedGroupIndexesCore(
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> decisions)
    {
        var indexes = new HashSet<int>();
        for (var decisionIndex = 0; decisionIndex < decisions.Count; decisionIndex++)
        {
            var decision = decisions[decisionIndex];
            if (decision.State != RibbonAdaptiveGroupState.Collapsed &&
                decision.Index >= 0 &&
                decision.Index < groupNames.Count)
            {
                indexes.Add(decision.Index);
            }
        }

        return indexes;
    }
}

internal readonly record struct RibbonAdaptiveRuntimeStateOverride(
    int Index,
    RibbonAdaptiveGroupState State);
