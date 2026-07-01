namespace FreeX.Ribbon.Definitions;

public static class RibbonCollapsedGroupCatalogPlanner
{
    public const string OptionsDisplayResourceKey = "MainWindow_Content_Options";
    public const string StylesDisplayResourceKey = "MainWindow_Content_Styles";

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
        availableWidth > 760 &&
        catalogId is "DataToolsGroup";

    public static bool ShouldCollapseIconOnlyGroup(string? catalogId, double availableWidth) =>
        availableWidth <= 1300 &&
        catalogId is "DataSortFilterGroup";

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
