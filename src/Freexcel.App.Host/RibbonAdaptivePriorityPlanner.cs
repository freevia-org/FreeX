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
        IReadOnlyList<string> groupNames)
    {
        if (availableWidth <= 900 &&
            IsRibbonGroupSet(groupNames, "Tables", "Illustrations") &&
            TryFindGroupIndex(groupNames, "Charts", out var chartsIndex))
        {
            return [new RibbonAdaptiveRuntimeStateOverride(chartsIndex, RibbonAdaptiveGroupState.Collapsed)];
        }

        return [];
    }

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeVisibilityOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames)
    {
        var decisions = new List<RibbonAdaptiveRuntimeStateOverride>();
        if (availableWidth <= 900 &&
            TryFindGroupIndex(groupNames, "Charts", out var chartsIndex))
        {
            decisions.Add(new RibbonAdaptiveRuntimeStateOverride(chartsIndex, RibbonAdaptiveGroupState.Collapsed));
        }

        if (availableWidth <= 1120 &&
            TryFindGroupIndex(groupNames, "Data Tools", out var dataToolsIndex))
        {
            decisions.Add(new RibbonAdaptiveRuntimeStateOverride(dataToolsIndex, RibbonAdaptiveGroupState.IconOnly));
        }

        return decisions;
    }

    public static IReadOnlySet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        var protectedIndexes = new HashSet<int>();
        if (availableWidth <= 760)
            return protectedIndexes;

        var pageSetupIndex = TryFindGroupIndex(groupNames, "Page Setup", out var pageSetupGroupIndex)
            ? pageSetupGroupIndex
            : -1;
        if (groupNames.Contains("Themes", StringComparer.Ordinal) &&
            pageSetupIndex >= 0)
        {
            protectedIndexes.Add(pageSetupIndex);
        }

        foreach (var protectedGroupName in GetPriorityProtectedGroupNames(groupNames, availableWidth))
        {
            if (TryFindGroupIndex(groupNames, protectedGroupName, out var index))
                protectedIndexes.Add(index);
        }

        return protectedIndexes;
    }

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        if (IsRibbonGroupSet(groupNames, "Proofing", "Accessibility", "Comments"))
            return [];

        var protectedNames = GetPriorityProtectedGroupNames(groupNames, availableWidth);
        if (protectedNames.Count == 0)
            return [];

        var indexes = new List<int>();
        foreach (var protectedName in protectedNames)
        {
            if (TryFindGroupIndex(groupNames, protectedName, out var index))
                indexes.Add(index);
        }

        return indexes;
    }

    public static bool RequiresMeasuredCorrection(IReadOnlyList<string> groupNames) =>
        IsRibbonGroupSet(groupNames, "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools");

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        if (availableWidth <= 760)
            return [];

        if (RequiresMeasuredCorrection(groupNames))
        {
            if (availableWidth <= 900)
                return ["Data Tools", "Forecast"];

            return availableWidth <= 1120
                ? ["Sort & Filter", "Data Tools", "Forecast"]
                : ["Sort & Filter"];
        }

        if (IsRibbonGroupSet(groupNames, "Themes", "Page Setup", "Sheet Options"))
            return ["Page Setup"];

        if (IsRibbonGroupSet(groupNames, "Tables", "Illustrations"))
            return ["Tables"];

        return [];
    }

    private static bool IsRibbonGroupSet(IReadOnlyList<string> groupNames, params string[] requiredNames) =>
        requiredNames.All(requiredName => groupNames.Contains(requiredName, StringComparer.Ordinal));

    private static bool TryFindGroupIndex(IReadOnlyList<string> groupNames, string groupName, out int index)
    {
        for (var i = 0; i < groupNames.Count; i++)
        {
            if (string.Equals(groupNames[i], groupName, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}

internal readonly record struct RibbonAdaptiveRuntimeStateOverride(
    int Index,
    RibbonAdaptiveGroupState State);
