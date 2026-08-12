namespace FreeW.Ribbon.Definitions;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/>.
/// </summary>
public static class FreeWRibbon
{
    public static RibbonDefinition Build(FreeWRibbonCapabilities? capabilities = null)
    {
        capabilities ??= FreeWRibbonCapabilities.Wpf;

        var definition = new RibbonDefinitionBuilder()
            .AddFileTab(capabilities)
            .AddHomeTab(capabilities)
            .AddInsertTab(capabilities)
            .AddReferencesTab(capabilities)
            .AddLayoutTab(capabilities)
            .AddDesignTab(capabilities)
            .AddViewTab(capabilities)
            .AddHelpTab(capabilities)
            .AddMailingsTab(capabilities)
            .AddReviewTab(capabilities)
            .AddDeveloperTab(capabilities)
            .AddDrawingContextualTab(capabilities)
            .AddPictureContextualTab(capabilities)
            .AddChartContextualTabs(capabilities)
            .AddSmartArtContextualTab(capabilities)
            .AddTableContextualTabs(capabilities)
            .AddHeaderFooterDesignTab(capabilities)
            .Build();

        return definition with { Tabs = OrderTabs(definition.Tabs, capabilities.TabOrder) };
    }

    private static IReadOnlyList<RibbonTab> OrderTabs(
        IReadOnlyList<RibbonTab> tabs,
        IReadOnlyList<string> tabOrder)
    {
        var order = tabOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);

        return tabs
            .OrderBy(tab => order.TryGetValue(tab.Id, out var index) ? index : int.MaxValue)
            .ThenBy(tab => tab.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
