using Free.Shared.Ribbon;

namespace FreeX.Ribbon.Definitions;

/// <summary>
/// The complete FreeX ribbon: the hand-authored, high-fidelity <see cref="HomeRibbonDefinition"/> Home tab
/// followed by the remaining main + contextual tabs generated from the catalog
/// (<see cref="FreeXRibbonDefinition"/>). This is the single source of truth consumed by the renderer.
/// </summary>
public static class FreeXRibbon
{
    public static RibbonDefinition Build()
    {
        var generated = FreeXRibbonDefinition.Build();
        var tabs = new List<RibbonTab>
        {
            new(FreeXRibbonTabIds.File, "File", "F", Context: null, Groups: []),
            HomeRibbonDefinition.HomeTab(),
        };
        tabs.AddRange(generated.VisibleTabs.Where(tab => tab.Id != FreeXRibbonTabIds.Help));
        tabs.AddRange(generated.ContextualTabs.OrderBy(tab => tab.Context!.DisplayOrder));
        tabs.Add(generated.FindTab(FreeXRibbonTabIds.Help)!);
        return new RibbonDefinition(tabs);
    }
}

public static class FreeXRibbonTabIds
{
    public const string File = "FileTab";
    public const string Home = "HomeTab";
    public const string Help = "HelpTab";
    public const string ShapeFormat = "ShapeFormatTab";
    public const string PictureFormat = "PictureFormatTab";
    public const string ChartDesign = "ChartDesignTab";
    public const string ChartFormat = "ChartFormatTab";
    public const string TableDesign = "TableDesignTab";
    public const string PivotTableAnalyze = "PivotTableAnalyzeTab";
    public const string PivotTableDesign = "PivotTableDesignTab";
}
