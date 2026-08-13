using Free.Shared.Ribbon;

namespace FreeX.Ribbon.Definitions;

public enum HomeBorderMenuSection
{
    Presets,
    Draw,
    LineColor,
    LineStyle,
    Actions,
}

public enum HomeBorderLineStyleKind
{
    Thin,
    Medium,
    Thick,
    Dashed,
    Dotted,
    Double,
}

public record HomeBorderMenuCatalogItem(
    string CommandId,
    string Header,
    string KeyTip,
    HomeBorderMenuSection Section);

public sealed record HomeBorderLineColorCatalogItem(
    string CommandId,
    string Header,
    string KeyTip,
    string HexColor)
    : HomeBorderMenuCatalogItem(CommandId, Header, KeyTip, HomeBorderMenuSection.LineColor);

public sealed record HomeBorderLineStyleCatalogItem(
    string CommandId,
    string Header,
    string KeyTip,
    HomeBorderLineStyleKind Style)
    : HomeBorderMenuCatalogItem(CommandId, Header, KeyTip, HomeBorderMenuSection.LineStyle);

/// <summary>
/// Authoritative typed catalog for the Home Borders split-button menu and popup evidence.
/// </summary>
public static class HomeBorderMenuCatalog
{
    public static readonly IReadOnlyList<HomeBorderMenuCatalogItem> Presets =
    [
        Item("All Borders", "A", HomeBorderMenuSection.Presets),
        Item("Outside Borders", "O", HomeBorderMenuSection.Presets),
        Item("Inside Borders", "I", HomeBorderMenuSection.Presets),
        Item("No Border", "N", HomeBorderMenuSection.Presets),
        Item("Bottom Border", "B", HomeBorderMenuSection.Presets),
        Item("Top Border", "T", HomeBorderMenuSection.Presets),
        Item("Left Border", "L", HomeBorderMenuSection.Presets),
        Item("Right Border", "R", HomeBorderMenuSection.Presets),
        Item("Thick Bottom Border", "K", HomeBorderMenuSection.Presets),
        Item("Bottom Double Border", "D", HomeBorderMenuSection.Presets),
        Item("Thick Outside Borders", "X", HomeBorderMenuSection.Presets),
        Item("Top and Bottom Border", "U", HomeBorderMenuSection.Presets),
        Item("Top and Thick Bottom Border", "H", HomeBorderMenuSection.Presets),
        Item("Top and Double Bottom Border", "J", HomeBorderMenuSection.Presets),
    ];

    public static readonly IReadOnlyList<HomeBorderMenuCatalogItem> Draw =
    [
        Item("Draw Border", "W", HomeBorderMenuSection.Draw),
        Item("Draw Border Grid", "G", HomeBorderMenuSection.Draw),
        Item("Erase Border", "E", HomeBorderMenuSection.Draw),
    ];

    public static readonly IReadOnlyList<HomeBorderLineColorCatalogItem> LineColors =
    [
        new("Black", "Black", "K", "#000000"),
        new("Gray", "Gray", "G", "#808080"),
        new("Accent 1", "Accent 1", "1", "#4472C4"),
        new("Accent 2", "Accent 2", "2", "#ED7D31"),
    ];

    public static readonly IReadOnlyList<HomeBorderLineStyleCatalogItem> LineStyles =
    [
        new("Thin", "Thin", "T", HomeBorderLineStyleKind.Thin),
        new("Medium", "Medium", "M", HomeBorderLineStyleKind.Medium),
        new("Thick", "Thick", "K", HomeBorderLineStyleKind.Thick),
        new("Dashed", "Dashed", "D", HomeBorderLineStyleKind.Dashed),
        new("Dotted", "Dotted", "O", HomeBorderLineStyleKind.Dotted),
        new("Double", "Double", "U", HomeBorderLineStyleKind.Double),
    ];

    public static readonly IReadOnlyList<HomeBorderMenuCatalogItem> Actions =
    [
        new("More Borders", "More Borders...", "M", HomeBorderMenuSection.Actions),
    ];

    public static IReadOnlyList<HomeBorderMenuCatalogItem> All { get; } =
        Presets.Concat(Draw).Concat(LineColors).Concat(LineStyles).Concat(Actions).ToArray();

    public static void Build(RibbonMenuBuilder menu)
    {
        Add(menu, Presets.Take(4));
        menu.Separator();
        Add(menu, Presets.Skip(4).Take(4));
        menu.Separator();
        Add(menu, Presets.Skip(8).Take(2));
        menu.Separator();
        Add(menu, Presets.Skip(10));
        menu.Separator();
        Add(menu, Draw);
        menu.Separator();
        menu.Submenu("Line Color", "C", submenu => Add(submenu, LineColors));
        menu.Submenu("Line Style", "S", submenu => Add(submenu, LineStyles));
        menu.Separator();
        Add(menu, Actions);
    }

    private static HomeBorderMenuCatalogItem Item(
        string label,
        string keyTip,
        HomeBorderMenuSection section) =>
        new(label, label, keyTip, section);

    private static void Add(RibbonMenuBuilder menu, IEnumerable<HomeBorderMenuCatalogItem> items)
    {
        foreach (var item in items)
            menu.Item(item.CommandId, item.Header, item.KeyTip);
    }
}
