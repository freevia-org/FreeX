namespace Freexcel.App.Host;

internal static class RibbonAdaptiveTabProfiles
{
    private const double VeryNarrowWidth = 700;

    private static readonly IReadOnlyList<RibbonAdaptiveTabProfile> Profiles =
    [
        new(
            Name: "Home",
            RequiredGroups: ["Clipboard", "Font", "Alignment", "Number"],
            Defaults: [],
            Breakpoints:
            [
                Rule(900, collapseFromGroup: "Alignment"),
                Rule(1300, collapseFromGroup: "Styles"),
                Rule(1500, collapseFromGroup: "Editing")
            ]),
        new(
            Name: "Insert",
            RequiredGroups: ["Tables", "Illustrations"],
            Defaults:
            [
                State("Tables", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(760, collapseAll: true),
                Rule(
                    1120,
                    states: [State("Tables", RibbonAdaptiveGroupState.SmallWithLabels)],
                    collapseGroups: ["Add-ins", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"]),
                Rule(
                    1320,
                    collapseGroups: ["Add-ins", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"])
            ],
            RuntimeStates:
            [
                Runtime(900, "Charts", RibbonAdaptiveGroupState.Collapsed)
            ],
            RuntimeVisibility:
            [
                Runtime(900, "Charts", RibbonAdaptiveGroupState.Collapsed)
            ],
            ProtectedGroups:
            [
                Protected(double.PositiveInfinity, ["Tables"])
            ]),
        new(
            Name: "Formulas",
            RequiredGroups: ["Function Library", "Formula Auditing"],
            Defaults:
            [
                State("Function Library", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(760, collapseAll: true),
                Rule(1320, collapseFromIndex: 2)
            ]),
        new(
            Name: "Data",
            RequiredGroups: ["Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools"],
            Defaults:
            [
                State("Get & Transform Data", RibbonAdaptiveGroupState.Full),
                State("Data Tools", RibbonAdaptiveGroupState.Full),
                State("Forecast", RibbonAdaptiveGroupState.Full),
                State("Sort & Filter", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(
                    1120,
                    states: [State("Sort & Filter", RibbonAdaptiveGroupState.IconOnly)],
                    collapseGroups: ["Queries & Connections", "Data Types", "Outline"]),
                Rule(
                    1320,
                    collapseGroups: ["Data Types", "Outline"])
            ],
            RuntimeVisibility:
            [
                Runtime(1120, "Data Tools", RibbonAdaptiveGroupState.IconOnly)
            ],
            ProtectedGroups:
            [
                Protected(900, ["Data Tools", "Forecast"]),
                Protected(1120, ["Sort & Filter", "Data Tools", "Forecast"]),
                Protected(double.PositiveInfinity, ["Sort & Filter"])
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Page Layout",
            RequiredGroups: ["Themes", "Page Setup"],
            Defaults:
            [
                State("Page Setup", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(1120, collapseGroups: ["Themes", "Arrange"]),
                Rule(1320, collapseGroups: ["Arrange"])
            ],
            ProtectedGroups:
            [
                Protected(double.PositiveInfinity, ["Page Setup"])
            ]),
        new(
            Name: "Review",
            RequiredGroups: ["Proofing", "Accessibility", "Comments"],
            Defaults: [],
            Breakpoints:
            [
                Rule(1320, collapseGroups: ["Notes", "Protect"])
            ],
            DisablePriorityExpansion: true),
        new(
            Name: "View",
            RequiredGroups: ["Workbook Views", "Show", "Window"],
            Defaults:
            [
                State("Workbook Views", RibbonAdaptiveGroupState.Full),
                State("Show", RibbonAdaptiveGroupState.Full),
                State("Zoom", RibbonAdaptiveGroupState.Full),
                State("Window", RibbonAdaptiveGroupState.Full),
                State("Macros", RibbonAdaptiveGroupState.Collapsed)
            ],
            Breakpoints:
            [
                Rule(760, collapseGroups: ["Show", "Macros"])
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Draw",
            RequiredGroups: ["Tools", "Pens", "Convert"],
            Defaults: [],
            Breakpoints:
            [
                Rule(760, collapseFromIndex: 0),
                Rule(1120, collapseFromIndex: 3),
                Rule(1320, collapseFromIndex: 3)
            ]),
        new(
            Name: "Tiny",
            RequiredGroups: [],
            Defaults: [],
            Breakpoints: [],
            TinyGroupNames: ["Help", "PivotTable", "Layout"])
    ];

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates)
    {
        var states = plannedStates.ToArray();
        if (availableWidth <= VeryNarrowWidth)
        {
            CollapseAll(states);
            return states;
        }

        if (FindProfile(groupNames) is { } profile)
        {
            profile.Apply(availableWidth, groupNames, states);
            return states;
        }

        ApplyGenericFallback(availableWidth, states);
        return states;
    }

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeStateOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames) =>
        FindProfile(groupNames)?.RuntimeStatesFor(availableWidth, groupNames) ?? [];

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeVisibilityOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames) =>
        FindProfile(groupNames)?.RuntimeVisibilityFor(availableWidth, groupNames) ?? [];

    public static IReadOnlySet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        var protectedIndexes = new HashSet<int>();
        if (availableWidth <= 760)
            return protectedIndexes;

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
        var profile = FindProfile(groupNames);
        if (profile?.DisablePriorityExpansion == true)
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
        FindProfile(groupNames)?.RequiresMeasuredCorrection == true;

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth)
    {
        if (availableWidth <= 760)
            return [];

        return FindProfile(groupNames)?.ProtectedGroupsFor(availableWidth) ?? [];
    }

    public static IReadOnlyList<double> GetBreakpointThresholds(IReadOnlyList<string> groupNames)
    {
        var thresholds = new SortedSet<double> { VeryNarrowWidth };
        var matchedProfile = false;
        foreach (var profile in Profiles)
        {
            if (!profile.Matches(groupNames))
                continue;

            matchedProfile = true;
            foreach (var threshold in profile.BreakpointThresholds)
                thresholds.Add(threshold);
            foreach (var threshold in profile.RuntimeThresholds)
                thresholds.Add(threshold);
            foreach (var threshold in profile.ProtectedThresholds)
                thresholds.Add(threshold);

            break;
        }

        if (!matchedProfile)
        {
            thresholds.Add(760);
            thresholds.Add(1120);
            thresholds.Add(1320);
        }

        foreach (var threshold in RibbonCollapsedGroupPresentationPlanner.BreakpointThresholds)
            thresholds.Add(threshold);

        return thresholds
            .Where(width => width > 0 && !double.IsInfinity(width))
            .OrderBy(width => width)
            .ToList();
    }

    internal static string? ResolveProfileName(IReadOnlyList<string> groupNames) =>
        FindProfile(groupNames)?.Name;

    private static RibbonAdaptiveTabProfile? FindProfile(IReadOnlyList<string> groupNames) =>
        Profiles.FirstOrDefault(profile => profile.Matches(groupNames));

    private static void ApplyGenericFallback(double availableWidth, RibbonAdaptiveGroupState[] states)
    {
        var collapseFrom = availableWidth switch
        {
            <= 760 => 0,
            <= 1120 => 1,
            <= 1320 => 2,
            _ => -1
        };

        if (collapseFrom >= 0)
            CollapseFrom(states, collapseFrom);
    }

    private static RibbonAdaptiveBreakpointRule Rule(
        double maxWidth,
        bool collapseAll = false,
        string? collapseFromGroup = null,
        int? collapseFromIndex = null,
        IReadOnlyList<string>? collapseGroups = null,
        IReadOnlyList<RibbonAdaptiveGroupStateAssignment>? states = null) =>
        new(maxWidth, collapseAll, collapseFromGroup, collapseFromIndex, collapseGroups ?? [], states ?? []);

    private static RibbonAdaptiveGroupStateAssignment State(
        string groupName,
        RibbonAdaptiveGroupState state) =>
        new(groupName, state);

    private static RibbonAdaptiveRuntimeStateOverrideRule Runtime(
        double maxWidth,
        string groupName,
        RibbonAdaptiveGroupState state) =>
        new(maxWidth, groupName, state);

    private static RibbonAdaptiveProtectedGroupsRule Protected(
        double maxWidth,
        IReadOnlyList<string> groupNames) =>
        new(maxWidth, groupNames);

    private static void ApplyRule(
        RibbonAdaptiveBreakpointRule rule,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states)
    {
        if (rule.CollapseAll)
        {
            CollapseAll(states);
            return;
        }

        if (rule.CollapseFromIndex is { } firstCollapsedIndex)
            CollapseFrom(states, firstCollapsedIndex);

        if (!string.IsNullOrWhiteSpace(rule.CollapseFromGroup) &&
            TryFindGroupIndex(groupNames, rule.CollapseFromGroup, out var groupIndex))
        {
            CollapseFrom(states, groupIndex);
        }

        foreach (var groupName in rule.CollapseGroups)
            ApplyState(groupNames, states, groupName, RibbonAdaptiveGroupState.Collapsed);

        foreach (var assignment in rule.States)
            ApplyState(groupNames, states, assignment.GroupName, assignment.State);
    }

    private static void ApplyState(
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        string groupName,
        RibbonAdaptiveGroupState state)
    {
        if (TryFindGroupIndex(groupNames, groupName, out var index))
            states[index] = state;
    }

    private static void CollapseAll(RibbonAdaptiveGroupState[] states)
    {
        for (var i = 0; i < states.Length; i++)
            states[i] = RibbonAdaptiveGroupState.Collapsed;
    }

    private static void CollapseFrom(RibbonAdaptiveGroupState[] states, int firstCollapsedIndex)
    {
        for (var i = Math.Clamp(firstCollapsedIndex, 0, states.Length); i < states.Length; i++)
            states[i] = RibbonAdaptiveGroupState.Collapsed;
    }

    private static bool ContainsGroup(IReadOnlyList<string> groupNames, string groupName) =>
        groupNames.Contains(groupName, StringComparer.Ordinal);

    private static bool TryFindGroupIndex(
        IReadOnlyList<string> groupNames,
        string groupName,
        out int index)
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

    private sealed record RibbonAdaptiveTabProfile(
        string Name,
        IReadOnlyList<string> RequiredGroups,
        IReadOnlyList<RibbonAdaptiveGroupStateAssignment> Defaults,
        IReadOnlyList<RibbonAdaptiveBreakpointRule> Breakpoints,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule>? RuntimeStates = null,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule>? RuntimeVisibility = null,
        IReadOnlyList<RibbonAdaptiveProtectedGroupsRule>? ProtectedGroups = null,
        bool RequiresMeasuredCorrection = false,
        bool DisablePriorityExpansion = false,
        IReadOnlyList<string>? TinyGroupNames = null)
    {
        public bool Matches(IReadOnlyList<string> groupNames)
        {
            if (TinyGroupNames is { Count: > 0 })
            {
                return groupNames.Count <= 2 &&
                    TinyGroupNames.Any(groupName => ContainsGroup(groupNames, groupName));
            }

            return RequiredGroups.All(groupName => ContainsGroup(groupNames, groupName));
        }

        public void Apply(
            double availableWidth,
            IReadOnlyList<string> groupNames,
            RibbonAdaptiveGroupState[] states)
        {
            foreach (var assignment in Defaults)
                ApplyState(groupNames, states, assignment.GroupName, assignment.State);

            var breakpoint = Breakpoints.FirstOrDefault(rule => availableWidth <= rule.MaxWidth);
            if (breakpoint is not null)
                ApplyRule(breakpoint, groupNames, states);
        }

        public IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> RuntimeStatesFor(
            double availableWidth,
            IReadOnlyList<string> groupNames) =>
            RuntimeOverridesFor(RuntimeStates ?? [], availableWidth, groupNames);

        public IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> RuntimeVisibilityFor(
            double availableWidth,
            IReadOnlyList<string> groupNames) =>
            RuntimeOverridesFor(RuntimeVisibility ?? [], availableWidth, groupNames);

        public IReadOnlyList<string> ProtectedGroupsFor(double availableWidth) =>
            (ProtectedGroups ?? [])
                .FirstOrDefault(rule => availableWidth <= rule.MaxWidth)
                ?.GroupNames ?? [];

        public IEnumerable<double> BreakpointThresholds => Breakpoints.Select(rule => rule.MaxWidth);

        public IEnumerable<double> RuntimeThresholds =>
            (RuntimeStates ?? []).Concat(RuntimeVisibility ?? []).Select(rule => rule.MaxWidth);

        public IEnumerable<double> ProtectedThresholds =>
            (ProtectedGroups ?? []).Select(rule => rule.MaxWidth);
    }

    private static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> RuntimeOverridesFor(
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule> rules,
        double availableWidth,
        IReadOnlyList<string> groupNames)
    {
        var decisions = new List<RibbonAdaptiveRuntimeStateOverride>();
        foreach (var rule in rules)
        {
            if (availableWidth > rule.MaxWidth)
                continue;

            if (TryFindGroupIndex(groupNames, rule.GroupName, out var index))
                decisions.Add(new RibbonAdaptiveRuntimeStateOverride(index, rule.State));
        }

        return decisions;
    }

    private sealed record RibbonAdaptiveBreakpointRule(
        double MaxWidth,
        bool CollapseAll,
        string? CollapseFromGroup,
        int? CollapseFromIndex,
        IReadOnlyList<string> CollapseGroups,
        IReadOnlyList<RibbonAdaptiveGroupStateAssignment> States);

    private sealed record RibbonAdaptiveGroupStateAssignment(
        string GroupName,
        RibbonAdaptiveGroupState State);

    private sealed record RibbonAdaptiveRuntimeStateOverrideRule(
        double MaxWidth,
        string GroupName,
        RibbonAdaptiveGroupState State);

    private sealed record RibbonAdaptiveProtectedGroupsRule(
        double MaxWidth,
        IReadOnlyList<string> GroupNames);
}
