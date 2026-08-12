using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Presentation.ThemeUI;

namespace FreeX.App.Presentation.Ribbon;

public sealed record RibbonRuntimeCatalogSurface(
    string TabHeader,
    string CommandTitle,
    string InventorySection,
    string InventoryRow,
    string Source,
    IReadOnlyList<RibbonRuntimeCatalogGroup> Groups)
{
    public int ItemCount => Groups.Sum(group => group.Items.Count);
}

public sealed record RibbonRuntimeCatalogGroup(
    string Name,
    IReadOnlyList<string> Items);

public sealed record RibbonRuntimeCatalogNumberFormatOption(
    string Label,
    bool OpensFormatCellsDialog = false);

public sealed record RibbonRuntimeCatalogAccountingSymbolOption(
    string CommandId,
    string Label);

public static class RibbonRuntimeCatalogPlanner
{
    public static IReadOnlyList<RibbonRuntimeCatalogSurface> GetSurfaces(
        Func<string, string> textProvider,
        IReadOnlyList<RibbonRuntimeCatalogNumberFormatOption> numberFormatOptions,
        IReadOnlyList<RibbonRuntimeCatalogAccountingSymbolOption> accountingSymbolOptions)
    {
        ArgumentNullException.ThrowIfNull(textProvider);
        ArgumentNullException.ThrowIfNull(numberFormatOptions);
        ArgumentNullException.ThrowIfNull(accountingSymbolOptions);

        return
        [
            CreateFormatAsTableSurface(),
            CreateNumberFormatSurface(numberFormatOptions),
            CreateAccountingSymbolSurface(accountingSymbolOptions),
            CreateFontColorPopupSurface(),
            CreateBordersPopupSurface(),
            CreateConditionalFormattingPopupSurface(),
            CreateConditionalFormattingDataBarSurface(textProvider),
            CreateConditionalFormattingColorScaleSurface(textProvider),
            CreateConditionalFormattingIconSetSurface(textProvider),
            CreatePageLayoutThemeSurface(),
            CreatePivotTableStyleSurface()
        ];
    }

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

    private static RibbonRuntimeCatalogSurface CreateNumberFormatSurface(
        IReadOnlyList<RibbonRuntimeCatalogNumberFormatOption> numberFormatOptions) =>
        new(
            "Home",
            "Number Format Dropdown",
            "Home",
            "Custom Number Format",
            "HomeNumberFormatDropdownPlanner",
            [
                new RibbonRuntimeCatalogGroup(
                    "Formats",
                    numberFormatOptions
                        .Where(option => !option.OpensFormatCellsDialog)
                        .Select(option => option.Label)
                        .ToArray()),
                new RibbonRuntimeCatalogGroup(
                    "Actions",
                    numberFormatOptions
                        .Where(option => option.OpensFormatCellsDialog)
                        .Select(option => option.Label)
                        .ToArray())
            ]);

    private static RibbonRuntimeCatalogSurface CreateAccountingSymbolSurface(
        IReadOnlyList<RibbonRuntimeCatalogAccountingSymbolOption> accountingSymbolOptions) =>
        new(
            "Home",
            "Accounting Symbol Dropdown",
            "Home",
            "Accounting/Date/Time",
            "HomeNumberFormatDropdownPlanner",
            [
                new RibbonRuntimeCatalogGroup(
                    "Symbols",
                    accountingSymbolOptions.Select(option => option.CommandId).ToArray())
            ]);

    private static RibbonRuntimeCatalogSurface CreateFontColorPopupSurface() =>
        new(
            "Home",
            "Font Color Popup",
            "Home",
            "Font Color",
            nameof(HomeFontBorderPopupCatalogPlanner),
            HomeFontBorderPopupCatalogPlanner.FontColorPopupGroups
                .Select(group => new RibbonRuntimeCatalogGroup(group.Name, group.Items))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateBordersPopupSurface() =>
        new(
            "Home",
            "Borders Popup",
            "Home",
            "Full Border Gallery",
            nameof(HomeFontBorderPopupCatalogPlanner),
            HomeFontBorderPopupCatalogPlanner.BorderPopupGroups
                .Select(group => new RibbonRuntimeCatalogGroup(group.Name, group.Items))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateConditionalFormattingPopupSurface() =>
        new(
            "Home",
            "Conditional Formatting Popup",
            "Home",
            "Conditional Formatting",
            nameof(ConditionalFormatPresetGalleryPlanner),
            ConditionalFormatPresetGalleryPlanner.PopupGroups
                .Select(group => new RibbonRuntimeCatalogGroup(
                    group.Name,
                    group.Items.Select(item => item.CommandId).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateConditionalFormattingDataBarSurface(Func<string, string> textProvider) =>
        new(
            "Home",
            "Conditional Formatting Data Bars",
            "Home",
            "Conditional Formatting",
            nameof(ConditionalFormatPresetGalleryPlanner),
            ConditionalFormatPresetGalleryPlanner.DataBarGroups
                .Select(group => new RibbonRuntimeCatalogGroup(
                    textProvider(group.CategoryKey),
                    group.Options.Select(option => textProvider(option.LabelKey)).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateConditionalFormattingColorScaleSurface(Func<string, string> textProvider) =>
        new(
            "Home",
            "Conditional Formatting Color Scales",
            "Home",
            "Conditional Formatting",
            nameof(ConditionalFormatPresetGalleryPlanner),
            ConditionalFormatPresetGalleryPlanner.ColorScaleGroups
                .Select(group => new RibbonRuntimeCatalogGroup(
                    textProvider(group.CategoryKey),
                    group.Options.Select(option => textProvider(option.LabelKey)).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreateConditionalFormattingIconSetSurface(Func<string, string> textProvider) =>
        new(
            "Home",
            "Conditional Formatting Icon Sets",
            "Home",
            "Conditional Formatting",
            nameof(ConditionalFormatIconSetCatalog),
            ConditionalFormatIconSetCatalog.GalleryGroups
                .Select(group => new RibbonRuntimeCatalogGroup(
                    textProvider(group.CategoryKey),
                    group.Options.Select(option => textProvider(option.LabelKey)).ToArray()))
                .ToArray());

    private static RibbonRuntimeCatalogSurface CreatePageLayoutThemeSurface() =>
        new(
            "Page Layout",
            "Themes",
            "Page Layout",
            "Themes",
            nameof(WorkbookThemeCatalog),
            [
                new RibbonRuntimeCatalogGroup(
                    "Themes",
                    WorkbookThemeCatalog.ThemePresets.Select(option => option.Label).ToArray()),
                new RibbonRuntimeCatalogGroup(
                    "Colors",
                    WorkbookThemeCatalog.ColorPresets.Select(option => option.Label).ToArray()),
                new RibbonRuntimeCatalogGroup(
                    "Fonts",
                    WorkbookThemeCatalog.FontPresets.Select(option => option.Label).ToArray()),
                new RibbonRuntimeCatalogGroup(
                    "Effects",
                    WorkbookThemeCatalog.EffectPresets.Select(option => option.Label).ToArray())
            ]);

    private static RibbonRuntimeCatalogSurface CreatePivotTableStyleSurface()
    {
        var groups = PivotStyleGalleryPlanner.BuiltInStyleNames
            .GroupBy(GetPivotStyleFamily)
            .Select(group => new RibbonRuntimeCatalogGroup(group.Key, group.ToArray()))
            .ToArray();

        return new RibbonRuntimeCatalogSurface(
            "Design",
            "PivotTable Styles",
            "Insert",
            "PivotTable",
            nameof(PivotStyleGalleryPlanner),
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
