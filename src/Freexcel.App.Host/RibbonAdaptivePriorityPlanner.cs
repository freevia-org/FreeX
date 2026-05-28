namespace Freexcel.App.Host;

internal static class RibbonAdaptivePriorityPlanner
{
    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyRuntimePriorityStates(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates)
    {
        var states = plannedStates.ToArray();
        foreach (var decision in GetRuntimeStateOverrides(availableWidth, groupNames))
        {
            if (decision.Index >= 0 && decision.Index < states.Length)
                states[decision.Index] = decision.State;
        }

        return states;
    }

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

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth) =>
        RibbonAdaptiveTabProfiles.GetExpandableGroupIndexes(groupNames, availableWidth);

    public static bool RequiresMeasuredCorrection(IReadOnlyList<string> groupNames) =>
        RibbonAdaptiveTabProfiles.RequiresMeasuredCorrection(groupNames);

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth) =>
        RibbonAdaptiveTabProfiles.GetPriorityProtectedGroupNames(groupNames, availableWidth);
}

internal readonly record struct RibbonAdaptiveRuntimeStateOverride(
    int Index,
    RibbonAdaptiveGroupState State);
