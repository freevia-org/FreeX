namespace FreeX.App.Host;

internal static class RibbonAdaptiveTabProfiles
{
    private const double VeryNarrowWidth = 700;

    private static readonly IReadOnlyList<RibbonAdaptiveTabProfile> Profiles =
    [
        new(
            Name: "Home",
            CatalogId: "HomeTab",
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
            CatalogId: "InsertTab",
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
                    collapseGroups: ["Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"]),
                Rule(
                    1320,
                    collapseGroups: ["Sparklines", "Filters", "Links", "Text", "Symbols", "Comments"])
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
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Formulas",
            CatalogId: "FormulasTab",
            RequiredGroups: ["Function Library", "Formula Auditing"],
            Defaults:
            [
                State("Function Library", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(760, collapseAll: true),
                Rule(1120, collapseFromIndex: 1),
                Rule(1320, collapseFromIndex: 2)
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Data",
            CatalogId: "DataTab",
            RequiredGroups: ["Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools"],
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
                    collapseGroups: ["Queries & Connections", "Outline"]),
                Rule(
                    1465,
                    collapseGroups: ["Outline"])
            ],
            RuntimeVisibility:
            [
                Runtime(1120, "Sort & Filter", RibbonAdaptiveGroupState.IconOnly),
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
            CatalogId: "PageLayoutTab",
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
            CatalogId: "ReviewTab",
            RequiredGroups: ["Proofing", "Accessibility", "Comments"],
            Defaults: [],
            Breakpoints:
            [
                Rule(1320, collapseGroups: ["Notes", "Protect"]),
                Rule(1366, collapseGroups: ["Protect"])
            ],
            DisablePriorityExpansion: true),
        new(
            Name: "View",
            CatalogId: "ViewTab",
            RequiredGroups: ["Workbook Views", "Show", "Window"],
            Defaults:
            [
                State("Workbook Views", RibbonAdaptiveGroupState.Full),
                State("Show", RibbonAdaptiveGroupState.Full),
                State("Zoom", RibbonAdaptiveGroupState.Full),
                State("Window", RibbonAdaptiveGroupState.Full)
            ],
            Breakpoints:
            [
                Rule(760, collapseGroups: ["Show"])
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Draw",
            CatalogId: "DrawTab",
            RequiredGroups: ["Tools", "Pens", "Convert"],
            Defaults: [],
            Breakpoints:
            [
                Rule(760, collapseFromIndex: 0),
                Rule(1120, collapseFromIndex: 2),
                Rule(1320, collapseFromIndex: 3)
            ],
            RequiresMeasuredCorrection: true),
        new(
            Name: "Tiny",
            CatalogId: "HelpTab",
            RequiredGroups: [],
            Defaults: [],
            Breakpoints: [],
            TinyGroupNames: ["Help", "PivotTable", "Layout"])
    ];

    public static IReadOnlyList<RibbonAdaptiveGroupState> ApplyBreakpointOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        string? selectedTabHeader = null)
    {
        var states = plannedStates.ToArray();
        ApplyBreakpointOverridesInPlace(availableWidth, groupNames, states, selectedTabHeader);
        return states;
    }

    public static void ApplyBreakpointOverridesInPlace(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        string? selectedTabHeader = null) =>
        ApplyBreakpointOverridesInPlace(
            availableWidth,
            groupNames,
            states,
            FindProfile(groupNames, selectedTabHeader));

    public static void ApplyPlanOverridesInPlace(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        string? selectedTabHeader = null)
    {
        var profile = FindProfile(groupNames, selectedTabHeader);
        ApplyBreakpointOverridesInPlace(availableWidth, groupNames, states, profile);
        profile?.ApplyRuntimeStateOverrides(availableWidth, groupNames, states);
        profile?.ApplyRuntimeVisibilityOverrides(availableWidth, groupNames, states);
    }

    private static void ApplyBreakpointOverridesInPlace(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        RibbonAdaptiveTabProfile? profile)
    {
        if (availableWidth <= VeryNarrowWidth)
        {
            CollapseAll(states);
            return;
        }

        if (profile is not null)
        {
            profile.Apply(availableWidth, groupNames, states);
            return;
        }

        ApplyGenericFallback(availableWidth, states);
    }

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeStateOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        FindProfile(groupNames, selectedTabHeader)?.RuntimeStatesFor(availableWidth, groupNames) ?? [];

    public static IReadOnlyList<RibbonAdaptiveRuntimeStateOverride> GetRuntimeVisibilityOverrides(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        FindProfile(groupNames, selectedTabHeader)?.RuntimeVisibilityFor(availableWidth, groupNames) ?? [];

    public static void ApplyRuntimeStateOverridesInPlace(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        string? selectedTabHeader = null)
    {
        var profile = FindProfile(groupNames, selectedTabHeader);
        profile?.ApplyRuntimeStateOverrides(availableWidth, groupNames, states);
    }

    public static void ApplyRuntimeVisibilityOverridesInPlace(
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states,
        string? selectedTabHeader = null)
    {
        var profile = FindProfile(groupNames, selectedTabHeader);
        profile?.ApplyRuntimeVisibilityOverrides(availableWidth, groupNames, states);
    }

    public static IReadOnlySet<int> GetFallbackProtectedGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null)
    {
        var protectedIndexes = new HashSet<int>();
        if (availableWidth <= 760)
            return protectedIndexes;

        foreach (var protectedGroupName in GetPriorityProtectedGroupNames(groupNames, availableWidth, selectedTabHeader))
        {
            if (TryFindGroupIndex(groupNames, protectedGroupName, out var index))
                protectedIndexes.Add(index);
        }

        return protectedIndexes;
    }

    public static IReadOnlyList<int> GetExpandableGroupIndexes(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null)
    {
        var profile = FindProfile(groupNames, selectedTabHeader);
        if (profile?.DisablePriorityExpansion == true)
            return [];

        var protectedNames = GetPriorityProtectedGroupNames(groupNames, availableWidth, selectedTabHeader);
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

    public static bool RequiresMeasuredCorrection(
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        FindProfile(groupNames, selectedTabHeader)?.RequiresMeasuredCorrection == true;

    public static IReadOnlyList<string> GetPriorityProtectedGroupNames(
        IReadOnlyList<string> groupNames,
        double availableWidth,
        string? selectedTabHeader = null)
    {
        if (availableWidth <= 760)
            return [];

        return FindProfile(groupNames, selectedTabHeader)?.ProtectedGroupsFor(availableWidth) ?? [];
    }

    public static IReadOnlyList<double> GetBreakpointThresholds(
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null)
    {
        var thresholds = new SortedSet<double> { VeryNarrowWidth };
        if (FindProfile(groupNames, selectedTabHeader) is { } profile)
        {
            foreach (var threshold in profile.BreakpointThresholds)
                thresholds.Add(threshold);
            foreach (var threshold in profile.RuntimeThresholds)
                thresholds.Add(threshold);
            foreach (var threshold in profile.ProtectedThresholds)
                thresholds.Add(threshold);
        }
        else
        {
            thresholds.Add(760);
            thresholds.Add(1120);
            thresholds.Add(1320);
        }

        foreach (var threshold in RibbonCollapsedGroupPresentationPlanner.BreakpointThresholds)
            thresholds.Add(threshold);

        var positiveThresholds = new List<double>(thresholds.Count);
        foreach (var threshold in thresholds)
        {
            if (threshold > 0 && !double.IsInfinity(threshold))
                positiveThresholds.Add(threshold);
        }

        return positiveThresholds;
    }

    internal static string? ResolveProfileName(
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null) =>
        FindProfile(groupNames, selectedTabHeader)?.Name;

    private static RibbonAdaptiveTabProfile? FindProfile(
        IReadOnlyList<string> groupNames,
        string? selectedTabHeader = null)
    {
        var normalizedTabHeader = NormalizeTabHeader(selectedTabHeader);
        if (normalizedTabHeader is not null)
        {
            var tabProfile = Profiles.FirstOrDefault(profile => profile.MatchesTabHeader(normalizedTabHeader));
            if (tabProfile is not null)
                return tabProfile;
        }

        return Profiles.FirstOrDefault(profile => profile.MatchesGroups(groupNames));
    }

    private static string? NormalizeTabHeader(string? selectedTabHeader)
    {
        if (string.IsNullOrWhiteSpace(selectedTabHeader))
            return null;

        var normalized = selectedTabHeader.Replace("_", "", StringComparison.Ordinal).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

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

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> GroupCatalogIds =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Clipboard"] = ["HomeClipboardGroup"],
            ["Font"] = ["HomeFontGroup"],
            ["Alignment"] = ["HomeAlignmentGroup"],
            ["Number"] = ["HomeNumberGroup"],
            ["Styles"] = ["HomeStylesGroup"],
            ["Cells"] = ["HomeCellsGroup"],
            ["Editing"] = ["HomeEditingGroup"],
            ["Tables"] = ["InsertTablesGroup"],
            ["Illustrations"] = ["InsertIllustrationsGroup"],
            ["Charts"] = ["InsertChartsGroup"],
            ["Sparklines"] = ["InsertSparklinesGroup"],
            ["Filters"] = ["InsertFiltersGroup"],
            ["Links"] = ["InsertLinksGroup"],
            ["Comments"] = ["InsertCommentsGroup", "ReviewCommentsGroup"],
            ["Text"] = ["InsertTextGroup"],
            ["Symbols"] = ["InsertSymbolsGroup"],
            ["Tools"] = ["DrawToolsGroup", "PivotTableAnalyzeToolsGroup"],
            ["Pens"] = ["DrawPensGroup"],
            ["Convert"] = ["DrawConvertGroup"],
            ["Arrange"] = ["DrawArrangeGroup", "PageLayoutArrangeGroup"],
            ["Format"] = ["DrawFormatGroup"],
            ["Themes"] = ["PageLayoutThemesGroup"],
            ["Page Setup"] = ["PageLayoutPageSetupGroup"],
            ["Scale to Fit"] = ["PageLayoutScaleToFitGroup"],
            ["Sheet Options"] = ["PageLayoutSheetOptionsGroup"],
            ["Function Library"] = ["FormulasFunctionLibraryGroup"],
            ["Defined Names"] = ["FormulasDefinedNamesGroup"],
            ["Formula Auditing"] = ["FormulasFormulaAuditingGroup"],
            ["Calculation"] = ["FormulasCalculationGroup"],
            ["Get & Transform Data"] = ["DataGetTransformGroup"],
            ["Queries & Connections"] = ["DataQueriesConnectionsGroup"],
            ["Sort & Filter"] = ["DataSortFilterGroup"],
            ["Data Tools"] = ["DataToolsGroup"],
            ["Forecast"] = ["DataForecastGroup"],
            ["Outline"] = ["DataOutlineGroup"],
            ["Proofing"] = ["ReviewProofingGroup"],
            ["Accessibility"] = ["ReviewAccessibilityGroup"],
            ["Notes"] = ["ReviewNotesGroup"],
            ["Protect"] = ["ReviewProtectGroup"],
            ["Workbook Views"] = ["ViewWorkbookViewsGroup"],
            ["Show"] = ["ViewShowGroup", "PivotTableAnalyzeShowGroup"],
            ["Zoom"] = ["ViewZoomGroup"],
            ["Window"] = ["ViewWindowGroup"],
            ["Help"] = ["HelpHelpGroup"],
            ["PivotTable"] = ["PivotTableAnalyzePivotTableGroup"],
            ["Layout"] = ["PivotTableDesignLayoutGroup"]
        };

    private static bool ContainsGroup(IReadOnlyList<string> groupNames, string groupName) =>
        TryFindGroupIndex(groupNames, groupName, out _);

    private static bool TryFindGroupIndex(
        IReadOnlyList<string> groupNames,
        string groupName,
        out int index)
    {
        for (var i = 0; i < groupNames.Count; i++)
        {
            if (IsGroupKeyMatch(groupNames[i], groupName))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool IsGroupKeyMatch(string candidate, string profileGroupName) =>
        string.Equals(candidate, profileGroupName, StringComparison.Ordinal) ||
        (GroupCatalogIds.TryGetValue(profileGroupName, out var catalogIds) &&
            catalogIds.Contains(candidate, StringComparer.Ordinal));

    private sealed record RibbonAdaptiveTabProfile(
        string Name,
        IReadOnlyList<string> RequiredGroups,
        IReadOnlyList<RibbonAdaptiveGroupStateAssignment> Defaults,
        IReadOnlyList<RibbonAdaptiveBreakpointRule> Breakpoints,
        string? CatalogId = null,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule>? RuntimeStates = null,
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule>? RuntimeVisibility = null,
        IReadOnlyList<RibbonAdaptiveProtectedGroupsRule>? ProtectedGroups = null,
        bool RequiresMeasuredCorrection = false,
        bool DisablePriorityExpansion = false,
        IReadOnlyList<string>? TinyGroupNames = null)
    {
        public bool MatchesTabHeader(string selectedTabHeader) =>
            string.Equals(Name, selectedTabHeader, StringComparison.Ordinal) ||
            string.Equals(CatalogId, selectedTabHeader, StringComparison.Ordinal);

        public bool MatchesGroups(IReadOnlyList<string> groupNames)
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

        public void ApplyRuntimeStateOverrides(
            double availableWidth,
            IReadOnlyList<string> groupNames,
            RibbonAdaptiveGroupState[] states) =>
            ApplyRuntimeOverrides(RuntimeStates ?? [], availableWidth, groupNames, states);

        public void ApplyRuntimeVisibilityOverrides(
            double availableWidth,
            IReadOnlyList<string> groupNames,
            RibbonAdaptiveGroupState[] states) =>
            ApplyRuntimeOverrides(RuntimeVisibility ?? [], availableWidth, groupNames, states);

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

    private static void ApplyRuntimeOverrides(
        IReadOnlyList<RibbonAdaptiveRuntimeStateOverrideRule> rules,
        double availableWidth,
        IReadOnlyList<string> groupNames,
        RibbonAdaptiveGroupState[] states)
    {
        foreach (var rule in rules)
        {
            if (availableWidth > rule.MaxWidth)
                continue;

            if (TryFindGroupIndex(groupNames, rule.GroupName, out var index) &&
                index >= 0 &&
                index < states.Length)
            {
                states[index] = rule.State;
            }
        }
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
