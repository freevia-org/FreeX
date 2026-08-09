namespace FreeX.Ribbon.Definitions;

public static class RibbonCollapsedGroupCatalogPlanner
{
    public const string OptionsDisplayResourceKey = "MainWindow_Content_Options";
    public const string StylesDisplayResourceKey = "MainWindow_Content_Styles";
    public const double DataPrimaryGroupProtectionWidth = 760;
    public const double DataSortFilterCollapseWidth = 1300;
    public const double FullMeasuredOverflowProtectionWidth = 1000;

    public static RibbonCollapsedGroupCatalogPresentation PlanPresentation(
        string? catalogId,
        string groupName)
    {
        ArgumentNullException.ThrowIfNull(groupName);

        return new RibbonCollapsedGroupCatalogPresentation(
            groupName,
            GetDisplayResourceKey(catalogId),
            GetIconKey(catalogId, groupName));
    }

    public static RibbonAdaptiveGroupState NormalizeIconOnlyState(
        string? catalogId,
        RibbonAdaptiveGroupState plannedState,
        double availableWidth)
    {
        if (plannedState != RibbonAdaptiveGroupState.IconOnly)
            return plannedState;

        if (ShouldCollapseIconOnlyGroup(catalogId, availableWidth))
            return RibbonAdaptiveGroupState.Collapsed;

        if (ShouldUseFullLayoutForIconOnlyGroup(catalogId, availableWidth))
            return RibbonAdaptiveGroupState.Full;

        return ShouldUseSmallWithLabelsForIconOnlyGroup(catalogId)
            ? RibbonAdaptiveGroupState.SmallWithLabels
            : plannedState;
    }

    public static IReadOnlyList<RibbonAdaptiveGroupState> NormalizeDataSurfaceStates(
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        double availableWidth,
        string? selectedTabHeader)
    {
        ArgumentNullException.ThrowIfNull(adaptiveGroups);
        ArgumentNullException.ThrowIfNull(plannedStates);

        if (!IsDataAdaptiveSurface(adaptiveGroups, selectedTabHeader))
            return plannedStates;

        RibbonAdaptiveGroupState[]? normalizedStates = null;
        if (availableWidth <= DataSortFilterCollapseWidth &&
            FindGroupIndex(adaptiveGroups, "DataSortFilterGroup", "Sort & Filter") is var sortFilterIndex &&
            sortFilterIndex >= 0 &&
            sortFilterIndex < plannedStates.Count &&
            plannedStates[sortFilterIndex] != RibbonAdaptiveGroupState.Collapsed)
        {
            normalizedStates = plannedStates.ToArray();
            normalizedStates[sortFilterIndex] = RibbonAdaptiveGroupState.Collapsed;
        }

        var currentStates = normalizedStates ?? plannedStates;
        if (availableWidth > DataPrimaryGroupProtectionWidth &&
            FindGroupIndex(adaptiveGroups, "DataToolsGroup", "Data Tools") is var dataToolsIndex &&
            dataToolsIndex >= 0 &&
            dataToolsIndex < currentStates.Count &&
            currentStates[dataToolsIndex] == RibbonAdaptiveGroupState.IconOnly)
        {
            normalizedStates ??= plannedStates.ToArray();
            normalizedStates[dataToolsIndex] = RibbonAdaptiveGroupState.Full;
        }

        return normalizedStates ?? plannedStates;
    }

    public static RibbonAdaptiveRuntimeStateOverride? PlanDataPrimaryCorrection(
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        double availableWidth,
        string? selectedTabHeader)
    {
        ArgumentNullException.ThrowIfNull(adaptiveGroups);
        ArgumentNullException.ThrowIfNull(plannedStates);

        if (availableWidth <= DataPrimaryGroupProtectionWidth ||
            !IsDataAdaptiveSurface(adaptiveGroups, selectedTabHeader))
        {
            return null;
        }

        var primaryIndex = FindGroupIndex(
            adaptiveGroups,
            "DataGetTransformGroup",
            "Get & Transform Data");
        return primaryIndex >= 0 &&
               primaryIndex < plannedStates.Count &&
               plannedStates[primaryIndex] == RibbonAdaptiveGroupState.Collapsed
            ? new RibbonAdaptiveRuntimeStateOverride(
                primaryIndex,
                RibbonAdaptiveGroupState.Full)
            : null;
    }

    public static RibbonMeasuredOverflowProtectionPlan PlanMeasuredOverflowProtection(
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        IReadOnlyList<string> groupProfileKeys,
        double availableWidth,
        string? selectedTabHeader)
    {
        ArgumentNullException.ThrowIfNull(adaptiveGroups);
        ArgumentNullException.ThrowIfNull(groupProfileKeys);

        var runtimeVisibilityProtectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetRuntimeVisibilityProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        RelaxDataOverflowProtection(
            runtimeVisibilityProtectedGroupIndexes,
            adaptiveGroups,
            availableWidth,
            selectedTabHeader);

        var initialFallbackProtectedGroupIndexes = RibbonAdaptivePriorityPlanner
            .GetFallbackProtectedGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader)
            .ToHashSet();
        initialFallbackProtectedGroupIndexes.UnionWith(runtimeVisibilityProtectedGroupIndexes);
        RelaxDataOverflowProtection(
            initialFallbackProtectedGroupIndexes,
            adaptiveGroups,
            availableWidth,
            selectedTabHeader);

        var relaxedFallbackProtectedGroupIndexes =
            availableWidth >= FullMeasuredOverflowProtectionWidth
                ? initialFallbackProtectedGroupIndexes
                : runtimeVisibilityProtectedGroupIndexes;

        return new RibbonMeasuredOverflowProtectionPlan(
            runtimeVisibilityProtectedGroupIndexes,
            initialFallbackProtectedGroupIndexes,
            relaxedFallbackProtectedGroupIndexes,
            PreserveFirstGroupDuringInitialFallback:
                availableWidth > DataPrimaryGroupProtectionWidth);
    }

    public static bool IsDataAdaptiveSurface(
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string? selectedTabHeader)
    {
        ArgumentNullException.ThrowIfNull(adaptiveGroups);

        return string.Equals(selectedTabHeader, "Data", StringComparison.Ordinal) ||
               string.Equals(selectedTabHeader, "DataTab", StringComparison.Ordinal) ||
               FindGroupIndex(adaptiveGroups, "DataToolsGroup", "Data Tools") >= 0;
    }

    public static bool ShouldKeepLabelsAtIconWidth(
        string groupName,
        RibbonAdaptiveGroupState plannedState,
        double availableWidth) =>
        plannedState == RibbonAdaptiveGroupState.IconOnly &&
        availableWidth > 820 &&
        string.Equals(groupName, "Tables", StringComparison.Ordinal);

    public static bool ShouldUseSmallWithLabelsForIconOnlyGroup(string? catalogId) =>
        catalogId is
            "DataToolsGroup" or
            "InsertChartsGroup" or
            "FormulasFormulaAuditingGroup" or
            "ReviewCommentsGroup" or
            "ViewWindowGroup" or
            "TableDesignStyleOptionsGroup" or
            "PivotTableAnalyzeCalculationsGroup" or
            "PivotTableDesignStyleOptionsGroup";

    public static bool ShouldUseFullLayoutForIconOnlyGroup(string? catalogId, double availableWidth) =>
        availableWidth > DataPrimaryGroupProtectionWidth &&
        catalogId is "DataToolsGroup";

    public static bool ShouldCollapseIconOnlyGroup(string? catalogId, double availableWidth) =>
        availableWidth <= DataSortFilterCollapseWidth &&
        catalogId is "DataSortFilterGroup";

    private static void RelaxDataOverflowProtection(
        HashSet<int> protectedGroupIndexes,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double availableWidth,
        string? selectedTabHeader)
    {
        if (!IsDataAdaptiveSurface(adaptiveGroups, selectedTabHeader))
            return;

        RemoveProtectedGroup(
            protectedGroupIndexes,
            adaptiveGroups,
            "DataSortFilterGroup",
            "Sort & Filter");
        if (availableWidth <= DataPrimaryGroupProtectionWidth)
        {
            RemoveProtectedGroup(
                protectedGroupIndexes,
                adaptiveGroups,
                "DataToolsGroup",
                "Data Tools");
        }
    }

    private static void RemoveProtectedGroup(
        HashSet<int> protectedGroupIndexes,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string catalogId,
        string name)
    {
        var index = FindGroupIndex(adaptiveGroups, catalogId, name);
        if (index >= 0)
            protectedGroupIndexes.Remove(index);
    }

    private static int FindGroupIndex(
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string catalogId,
        string name)
    {
        for (var index = 0; index < adaptiveGroups.Count; index++)
        {
            if (string.Equals(adaptiveGroups[index].CatalogId, catalogId, StringComparison.Ordinal) ||
                string.Equals(adaptiveGroups[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? GetDisplayResourceKey(string? catalogId) =>
        catalogId switch
        {
            "TableDesignStyleOptionsGroup" => OptionsDisplayResourceKey,
            "TableDesignStylesGroup" => StylesDisplayResourceKey,
            "PivotTableDesignStyleOptionsGroup" => OptionsDisplayResourceKey,
            "PivotTableDesignStylesGroup" => StylesDisplayResourceKey,
            _ => null
        };

    private static string GetIconKey(string? catalogId, string groupName) =>
        catalogId switch
        {
            "DataSortFilterGroup" => "Sort & Filter",
            "DataToolsGroup" => "Data Tools",
            "FormulasFormulaAuditingGroup" => "Formula Auditing",
            "ReviewCommentsGroup" => "Comments",
            "ViewWindowGroup" => "Window",
            "TableDesignStyleOptionsGroup" => "Table Style Options",
            "TableDesignStylesGroup" => "Table Styles",
            "PivotTableAnalyzeCalculationsGroup" => "Calculations",
            "PivotTableDesignStyleOptionsGroup" => "PivotTable Style Options",
            "PivotTableDesignStylesGroup" => "PivotTable Styles",
            _ => groupName
        };
}

public readonly record struct RibbonCollapsedGroupCatalogPresentation(
    string FallbackDisplayName,
    string? DisplayResourceKey,
    string IconKey)
{
    public string ResolveDisplayName(Func<string, string> resolveResource)
    {
        ArgumentNullException.ThrowIfNull(resolveResource);

        return DisplayResourceKey is null
            ? FallbackDisplayName
            : resolveResource(DisplayResourceKey);
    }
}

public readonly record struct RibbonMeasuredOverflowProtectionPlan(
    IReadOnlySet<int> RuntimeVisibilityProtectedGroupIndexes,
    IReadOnlySet<int> InitialFallbackProtectedGroupIndexes,
    IReadOnlySet<int> RelaxedFallbackProtectedGroupIndexes,
    bool PreserveFirstGroupDuringInitialFallback);
