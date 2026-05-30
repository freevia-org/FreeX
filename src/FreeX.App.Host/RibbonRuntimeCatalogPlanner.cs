namespace FreeX.App.Host;

internal sealed record RibbonRuntimeCatalogSurface(
    string TabHeader,
    string CommandTitle,
    string InventorySection,
    string InventoryRow,
    string Source,
    IReadOnlyList<RibbonRuntimeCatalogGroup> Groups)
{
    public int ItemCount => Groups.Sum(group => group.Items.Count);
}

internal sealed record RibbonRuntimeCatalogGroup(
    string Name,
    IReadOnlyList<string> Items);

internal static class RibbonRuntimeCatalogPlanner
{
    public static IReadOnlyList<RibbonRuntimeCatalogSurface> GetSurfaces() =>
    [
        CreateFormatAsTableSurface(),
        CreateNumberFormatSurface(),
        CreateConditionalFormattingIconSetSurface(),
        CreatePageLayoutThemeSurface(),
        CreatePivotTableStyleSurface()
    ];

    private static RibbonRuntimeCatalogSurface CreateFormatAsTableSurface() =>
        new(
            "Home",
            "Format as Table",
            "Home",
            "Format as Table",
            nameof(TableStyleGalleryPlanner),
            TableStyleGalleryPlanner.GetOptions()
                .GroupBy(option => option.Label.Split(' ', 2)[0])
                .Select(group => new RibbonRuntimeCatalogGroup(
                    group.Key,
                    group.Select(option => option.StyleName).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateNumberFormatSurface() =>
        new(
            "Home",
            "Format Cells Number Catalog",
            "Home",
            "Custom Number Format",
            nameof(FormatCellsNumberFormatPlanner),
            FormatCellsNumberFormatPlanner.Categories
                .Select(category => new RibbonRuntimeCatalogGroup(
                    category,
                    FormatCellsNumberFormatPlanner.LabelsForCategory(category).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateConditionalFormattingIconSetSurface() =>
        new(
            "Home",
            "Conditional Formatting Icon Sets",
            "Home",
            "Conditional Formatting",
            nameof(ConditionalFormatIconSetPlanner),
            ConditionalFormatIconSetPlanner.GalleryGroups
                .Select(group => new RibbonRuntimeCatalogGroup(
                    group.Name,
                    group.Options.Select(option => option.Label).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreatePageLayoutThemeSurface() =>
        new(
            "Page Layout",
            "Themes",
            "Page Layout",
            "Themes",
            nameof(WorkbookThemeWorkflow),
            [
                new RibbonRuntimeCatalogGroup("Themes", ["Office", "FreeX Colorful", "Grayscale", "Custom Theme..."]),
                new RibbonRuntimeCatalogGroup("Colors", ["Office", "Colorful", "Grayscale", "Customize Colors..."]),
                new RibbonRuntimeCatalogGroup("Fonts", ["Office", "Customize Fonts..."]),
                new RibbonRuntimeCatalogGroup("Effects", ["Office", "Subtle", "Refined", "Customize Effects..."])
            ]);

    private static RibbonRuntimeCatalogSurface CreatePivotTableStyleSurface()
    {
        var groups = PivotStyleCatalog.BuiltInStyleNames
            .GroupBy(GetPivotStyleFamily)
            .Select(group => new RibbonRuntimeCatalogGroup(group.Key, group.ToArray()))
            .ToArray();

        return new RibbonRuntimeCatalogSurface(
            "Design",
            "PivotTable Styles",
            "Insert",
            "PivotTable",
            nameof(PivotStyleCatalog),
            groups);
    }

    private static string GetPivotStyleFamily(string styleName)
    {
        if (styleName.Contains("Medium", StringComparison.Ordinal))
            return "Medium";
        if (styleName.Contains("Dark", StringComparison.Ordinal))
            return "Dark";

        return "Light";
    }
}
