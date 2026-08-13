using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Presentation.Ribbon;

public sealed record HomeFontColorSwatchCatalogItem(
    string Label,
    string HexColor,
    string? BoundCommandId = null);

public sealed record HomeBorderLineColorCatalogItem(
    string Label,
    string HexColor);

public sealed record HomeBorderLineStyleCatalogItem(
    string Label,
    BorderStyle Style);

public sealed record HomeBorderPopupCatalogGroup(
    string Name,
    IReadOnlyList<string> Items);

/// <summary>
/// Shared Home-tab popup catalog evidence for split-button rows that are real choices rather than
/// standalone command ids in the binding matrix.
/// </summary>
public static class HomeFontBorderPopupCatalogPlanner
{
    public static readonly IReadOnlyList<HomeFontColorSwatchCatalogItem> FontColorSwatches =
    [
        Swatch("Black", CellColor.Black, "home.fontColorAuto"),
        Swatch("Red", new CellColor(255, 0, 0), "home.fontColorRed"),
        Swatch("Green", new CellColor(0, 128, 0), "home.fontColorGreen"),
        Swatch("Blue", new CellColor(0, 0, 255), "home.fontColorBlue"),
        Swatch("Accent 1", new CellColor(68, 114, 196)),
        Swatch("Accent 2", new CellColor(237, 125, 49)),
    ];

    public static readonly IReadOnlyList<HomeBorderLineColorCatalogItem> BorderLineColorSwatches =
        HomeBorderMenuCatalog.LineColors
            .Select(item => new HomeBorderLineColorCatalogItem(item.CommandId, item.HexColor))
            .ToArray();

    public static readonly IReadOnlyList<HomeBorderLineStyleCatalogItem> BorderLineStyles =
        HomeBorderMenuCatalog.LineStyles
            .Select(item => new HomeBorderLineStyleCatalogItem(item.CommandId, MapBorderStyle(item.Style)))
            .ToArray();

    public static readonly IReadOnlyList<HomeBorderPopupCatalogGroup> FontColorPopupGroups =
    [
        new("Swatches", FontColorSwatches.Select(swatch => swatch.Label).ToArray()),
        new("Actions", ["More Colors"]),
    ];

    public static readonly IReadOnlyList<HomeBorderPopupCatalogGroup> BorderPopupGroups =
    [
        Group("Presets", HomeBorderMenuCatalog.Presets),
        Group("Draw", HomeBorderMenuCatalog.Draw),
        Group("Line Color", HomeBorderMenuCatalog.LineColors),
        Group("Line Style", HomeBorderMenuCatalog.LineStyles),
        Group("Actions", HomeBorderMenuCatalog.Actions),
    ];

    public static IReadOnlyList<string> FontColorItems =>
        FontColorPopupGroups.SelectMany(group => group.Items).ToArray();

    public static IReadOnlyList<string> BorderItems =>
        BorderPopupGroups.SelectMany(group => group.Items).ToArray();

    public static IReadOnlySet<string> ClassifiedFontBorderRowsCovered { get; } =
        BorderLineColorSwatches.Select(swatch => swatch.Label)
            .Concat(BorderLineStyles.Select(style => style.Label))
            .ToHashSet(StringComparer.Ordinal);

    private static HomeFontColorSwatchCatalogItem Swatch(
        string label,
        CellColor color,
        string? boundCommandId = null) =>
        new(label, FormatHexColor(color), boundCommandId);

    private static HomeBorderPopupCatalogGroup Group(
        string name,
        IEnumerable<HomeBorderMenuCatalogItem> items) =>
        new(name, items.Select(item => item.CommandId).ToArray());

    private static BorderStyle MapBorderStyle(HomeBorderLineStyleKind style) => style switch
    {
        HomeBorderLineStyleKind.Thin => BorderStyle.Thin,
        HomeBorderLineStyleKind.Medium => BorderStyle.Medium,
        HomeBorderLineStyleKind.Thick => BorderStyle.Thick,
        HomeBorderLineStyleKind.Dashed => BorderStyle.Dashed,
        HomeBorderLineStyleKind.Dotted => BorderStyle.Dotted,
        HomeBorderLineStyleKind.Double => BorderStyle.Double,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    private static string FormatHexColor(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
