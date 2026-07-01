using FreeX.Core.Model;

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
    [
        BorderLineColor("Black", CellColor.Black),
        BorderLineColor("Gray", new CellColor(128, 128, 128)),
        BorderLineColor("Accent 1", new CellColor(68, 114, 196)),
        BorderLineColor("Accent 2", new CellColor(237, 125, 49)),
    ];

    public static readonly IReadOnlyList<HomeBorderLineStyleCatalogItem> BorderLineStyles =
    [
        new("Thin", BorderStyle.Thin),
        new("Medium", BorderStyle.Medium),
        new("Thick", BorderStyle.Thick),
        new("Dashed", BorderStyle.Dashed),
        new("Dotted", BorderStyle.Dotted),
        new("Double", BorderStyle.Double),
    ];

    public static readonly IReadOnlyList<HomeBorderPopupCatalogGroup> FontColorPopupGroups =
    [
        new("Swatches", FontColorSwatches.Select(swatch => swatch.Label).ToArray()),
        new("Actions", ["More Colors"]),
    ];

    public static readonly IReadOnlyList<HomeBorderPopupCatalogGroup> BorderPopupGroups =
    [
        new(
            "Presets",
            [
                "All Borders",
                "Outside Borders",
                "Inside Borders",
                "No Border",
                "Bottom Border",
                "Top Border",
                "Left Border",
                "Right Border",
                "Thick Bottom Border",
                "Bottom Double Border",
                "Thick Outside Borders",
                "Top and Bottom Border",
                "Top and Thick Bottom Border",
                "Top and Double Bottom Border",
            ]),
        new(
            "Draw",
            [
                "Draw Border",
                "Draw Border Grid",
                "Erase Border",
            ]),
        new("Line Color", BorderLineColorSwatches.Select(swatch => swatch.Label).ToArray()),
        new("Line Style", BorderLineStyles.Select(style => style.Label).ToArray()),
        new("Actions", ["More Borders"]),
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

    private static HomeBorderLineColorCatalogItem BorderLineColor(
        string label,
        CellColor color) =>
        new(label, FormatHexColor(color));

    private static string FormatHexColor(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
