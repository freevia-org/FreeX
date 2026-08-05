using System.Linq;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/>.
/// </summary>
public static class FreeWRibbon
{
    public static RibbonDefinition Build(FreeWRibbonCapabilities? capabilities = null)
    {
        capabilities ??= FreeWRibbonCapabilities.Wpf;
        if (capabilities.UseAvaloniaBackedSurface)
            return FreeWAvaloniaRibbonDefinition.Build(capabilities);

        var definition = new RibbonDefinitionBuilder()
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

        return definition with { Tabs = OrderVisibleTabs(definition.Tabs) };
    }

    private static IReadOnlyList<RibbonTab> OrderVisibleTabs(IReadOnlyList<RibbonTab> tabs)
    {
        string[] wordOrder =
        [
            "home",
            "insert",
            "design",
            "layout",
            "references",
            "mailings",
            "review",
            "view",
            "help",
            "developer"
        ];

        var visibleOrder = wordOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);

        var visible = tabs
            .Where(tab => !tab.IsContextual)
            .OrderBy(tab => visibleOrder.TryGetValue(tab.Id, out var index) ? index : int.MaxValue)
            .ThenBy(tab => visibleOrder.ContainsKey(tab.Id) ? 0 : 1)
            .ToArray();
        var contextual = tabs.Where(tab => tab.IsContextual).ToArray();

        return visible.Concat(contextual).ToArray();
    }
}
