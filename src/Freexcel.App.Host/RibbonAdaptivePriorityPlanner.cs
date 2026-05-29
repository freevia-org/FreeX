namespace Freexcel.App.Host;

internal static class RibbonAdaptivePriorityPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimePriorityStates(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates) =>
        ApplyRuntimeOverrides(
            plannedStates,
            GetRuntimeStateOverrides(availableWidth, groupNames));

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimeVisibilityStates(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates) =>
        ApplyRuntimeOverrides(
            plannedStates,
            GetRuntimeVisibilityOverrides(availableWidth, groupNames));

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeStateOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames) =>
        RibbonAdaptiveTabProfiles.GetRuntimeStateOverrides(availableWidth, groupNames);

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeVisibilityOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames) =>
        RibbonAdaptiveTabProfiles.GetRuntimeVisibilityOverrides(availableWidth, groupNames);

    public static IReadOnlySet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth) =>
        RibbonAdaptiveTabProfiles.GetFallbackProtectedGroupIndexes(groupNames, availableWidth);

    public static IReadOnlySet<int> GetRuntimeVisibilityProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth) =>
        GetRuntimeVisibilityOverrides(availableWidth, groupNames)
            .Where(decision => decision.State != RibbonAdaptiveGroupState.Collapsed)
            .Select(decision => decision.Index)
            .ToHashSet();

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        var runtimeOverrideIndexes = GetRuntimeStateOverrides(availableWidth, groupNames)
            .Concat(GetRuntimeVisibilityOverrides(availableWidth, groupNames))
            .Select(decision => decision.Index)
            .ToHashSet();

        return RibbonAdaptiveTabProfiles
            .GetExpandableGroupIndexes(groupNames, availableWidth)
            .Where(index => !runtimeOverrideIndexes.Contains(index))
            .ToList();
    }

    public static bool RequiresMeasuredCorrection(IReadOnlyList<string> groupNames) =>
        RibbonAdaptiveTabProfiles.RequiresMeasuredCorrection(groupNames);

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth) =>
        RibbonAdaptiveTabProfiles.GetPriorityProtectedGroupNames(groupNames, availableWidth);

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
}

internal readonly record struct RibbonAdaptiveRuntimeStateOverride(
    int Index,
    RibbonAdaptiveGroupState State);
