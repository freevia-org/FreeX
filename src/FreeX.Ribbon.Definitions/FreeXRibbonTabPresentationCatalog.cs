namespace FreeX.Ribbon.Definitions;

public sealed record FreeXRibbonTabPresentation(
    string TabId,
    string ResourceKey,
    string EnglishFallback);

/// <summary>Definition-owned presentation metadata for every FreeX ribbon tab shell.</summary>
public static class FreeXRibbonTabPresentationCatalog
{
    public static IReadOnlyList<FreeXRibbonTabPresentation> All { get; } =
    [
        new(FreeXRibbonTabIds.File, "MainWindow_Header_File", "File"),
        new(FreeXRibbonTabIds.Home, "MainWindow_Header_Home", "Home"),
        new("InsertTab", "MainWindow_Header_Insert", "Insert"),
        new("DrawTab", "MainWindow_Header_Draw", "Draw"),
        new("PageLayoutTab", "MainWindow_Header_PageLayout", "Page Layout"),
        new("FormulasTab", "MainWindow_Header_Formulas", "Formulas"),
        new("DataTab", "MainWindow_Header_Data", "Data"),
        new("ReviewTab", "MainWindow_Header_Review", "Review"),
        new("ViewTab", "MainWindow_Header_View", "View"),
        new(FreeXRibbonTabIds.ShapeFormat, "MainWindow_Header_ShapeFormat", "Shape Format"),
        new(FreeXRibbonTabIds.PictureFormat, "MainWindow_Header_PictureFormat", "Picture Format"),
        new(FreeXRibbonTabIds.ChartDesign, "MainWindow_Header_ChartDesign", "Chart Design"),
        new(FreeXRibbonTabIds.ChartFormat, "MainWindow_Text_Format", "Format"),
        new(FreeXRibbonTabIds.TableDesign, "MainWindow_Header_TableDesign", "Table Design"),
        new(FreeXRibbonTabIds.PivotTableAnalyze, "MainWindow_Header_PivotTableAnalyze", "PivotTable Analyze"),
        new(FreeXRibbonTabIds.PivotTableDesign, "MainWindow_Header_Design", "Design"),
        new(FreeXRibbonTabIds.Help, "MainWindow_Header_Help", "Help"),
    ];

    private static readonly IReadOnlyDictionary<string, FreeXRibbonTabPresentation> ById =
        All.ToDictionary(item => item.TabId, StringComparer.Ordinal);

    public static FreeXRibbonTabPresentation GetRequired(string tabId) =>
        ById.TryGetValue(tabId, out var presentation)
            ? presentation
            : throw new ArgumentException($"Unknown FreeX ribbon tab id '{tabId}'.", nameof(tabId));

    public static string Resolve(string tabId, Func<string, string?> resourceResolver)
    {
        ArgumentNullException.ThrowIfNull(resourceResolver);
        var presentation = GetRequired(tabId);
        var localized = resourceResolver(presentation.ResourceKey);
        return string.IsNullOrWhiteSpace(localized) ? presentation.EnglishFallback : localized;
    }
}
