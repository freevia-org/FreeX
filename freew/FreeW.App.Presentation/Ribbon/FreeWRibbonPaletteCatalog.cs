using FreeW.App.Localization;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FreeWRibbonPaletteChoice(
    string CommandId,
    string Label,
    string? Hex,
    bool StartsNewGroup = false,
    string? PickerLabel = null);

/// <summary>
/// Canonical FreeW ribbon palette semantics. Renderers choose their native menu, swatch, and brush
/// controls, while command ids, labels, grouping, and color payloads stay identical.
/// </summary>
public static class FreeWRibbonPaletteCatalog
{
    public static IReadOnlyList<FreeWRibbonPaletteChoice> FontColors =>
    [
        new("freew.font-color.automatic", Loc.Get("Ribbon_Palette_FontColor_Automatic_Label"), null),
        new("freew.font-color.black", Loc.Get("Ribbon_Palette_FontColor_Black_Label"), "#000000"),
        new("freew.font-color.dark-red", Loc.Get("Ribbon_Palette_FontColor_DarkRed_Label"), "#C00000"),
        new("freew.font-color.red", Loc.Get("Ribbon_Palette_FontColor_Red_Label"), "#FF0000"),
        new("freew.font-color.orange", Loc.Get("Ribbon_Palette_FontColor_Orange_Label"), "#FF6600"),
        new("freew.font-color.yellow", Loc.Get("Ribbon_Palette_FontColor_Yellow_Label"), "#FFFF00"),
        new("freew.font-color.green", Loc.Get("Ribbon_Palette_FontColor_Green_Label"), "#00B050"),
        new("freew.font-color.blue", Loc.Get("Ribbon_Palette_FontColor_Blue_Label"), "#0070C0"),
        new("freew.font-color.dark-blue", Loc.Get("Ribbon_Palette_FontColor_DarkBlue_Label"), "#00008B"),
        new("freew.font-color.purple", Loc.Get("Ribbon_Palette_FontColor_Purple_Label"), "#7030A0"),
        new("freew.font-color.white", Loc.Get("Ribbon_Palette_FontColor_White_Label"), "#FFFFFF"),
    ];

    public static readonly IReadOnlyList<FreeWRibbonPaletteChoice> ParagraphShading =
    [
        new("freew.para-shading.yellow", "Yellow", "#FFFF00"),
        new("freew.para-shading.green", "Green", "#92D050"),
        new("freew.para-shading.cyan", "Cyan", "#00B0F0"),
        new("freew.para-shading.gold", "Gold", "#FFC000"),
        new("freew.para-shading.red", "Red", "#FF0000"),
        new("freew.para-shading.gray", "Gray", "#D9D9D9"),
        new("freew.para-shading.light-gray", "Light Gray", "#A6A6A6"),
        new("freew.para-shading.light-yellow", "Light Yellow", "#FFF2CC"),
        new("freew.para-shading.light-blue", "Light Blue", "#DEEBF7"),
        new("freew.para-shading.light-green", "Light Green", "#E2EFDA"),
        new("freew.para-shading.light-peach", "Light Peach", "#FCE4D6"),
        new("freew.para-shading.very-light-gray", "Very Light Gray", "#EDEDED"),
        new("freew.para-shading.none", "No Color", null, StartsNewGroup: true),
    ];

    public static readonly IReadOnlyList<FreeWRibbonPaletteChoice> CharacterShading =
    [
        new("freew.char-shading.yellow", "Yellow", "#FFFF00"),
        new("freew.char-shading.green", "Green", "#92D050"),
        new("freew.char-shading.cyan", "Cyan", "#00B0F0"),
        new("freew.char-shading.gold", "Gold", "#FFC000"),
        new("freew.char-shading.red", "Red", "#FF0000"),
        new("freew.char-shading.gray", "Gray", "#D9D9D9"),
        new("freew.char-shading.light-gray", "Light Gray", "#A6A6A6", PickerLabel: "Dark Gray"),
        new("freew.char-shading.light-yellow", "Light Yellow", "#FFF2CC"),
        new("freew.char-shading.light-blue", "Light Blue", "#DEEBF7"),
        new("freew.char-shading.light-green", "Light Green", "#E2EFDA"),
        new("freew.char-shading.light-peach", "Light Peach", "#FCE4D6", PickerLabel: "Light Orange"),
        new("freew.char-shading.very-light-gray", "Very Light Gray", "#EDEDED", PickerLabel: "Light Gray"),
        new("freew.char-shading.none", "No Color", null, StartsNewGroup: true),
    ];

    public static readonly IReadOnlyList<FreeWRibbonPaletteChoice> CharacterBorders =
    [
        new("freew.char-border.black", "Black", "#000000"),
        new("freew.char-border.red", "Red", "#FF0000"),
        new("freew.char-border.blue", "Blue", "#0070C0"),
        new("freew.char-border.green", "Green", "#00B050"),
        new("freew.char-border.gold", "Gold", "#FFC000"),
        new("freew.char-border.purple", "Purple", "#7030A0"),
        new("freew.char-border.gray", "Gray", "#808080"),
        new("freew.char-border.dark-red", "Dark Red", "#C00000"),
        new("freew.char-border.dark-blue", "Dark Blue", "#002060"),
        new("freew.char-border.dark-green", "Dark Green", "#375623"),
        new("freew.char-border.brown", "Brown", "#974706"),
        new("freew.char-border.dark-gray", "Dark Gray", "#3F3F3F"),
        new("freew.char-border.none", "No Border", null, StartsNewGroup: true),
    ];

    public static readonly IReadOnlyList<FreeWRibbonPaletteChoice> Highlights =
    [
        new("freew.highlight.black", "Black", "#000000"),
        new("freew.highlight.dark-gray", "Dark Gray", "#404040"),
        new("freew.highlight.gray", "Gray", "#7F7F7F"),
        new("freew.highlight.dark-red", "Dark Red", "#C00000"),
        new("freew.highlight.red", "Red", "#FF0000"),
        new("freew.highlight.gold", "Gold", "#FFC000"),
        new("freew.highlight.yellow", "Yellow", "#FFFF00"),
        new("freew.highlight.light-green", "Light Green", "#92D050"),
        new("freew.highlight.green", "Green", "#00B050"),
        new("freew.highlight.cyan", "Cyan", "#00B0F0"),
        new("freew.highlight.blue", "Blue", "#0070C0"),
        new("freew.highlight.dark-blue", "Dark Blue", "#2F5496"),
        new("freew.highlight.purple", "Purple", "#7030A0"),
        new("freew.highlight.white", "White", "#FFFFFF"),
        new("freew.highlight.none", "No Color", null, StartsNewGroup: true),
    ];

    public static IReadOnlyList<FreeWRibbonPaletteChoice> PageColors =>
    [
        new("freew.page-color.none", Loc.Get("Ribbon_Palette_PageColor_NoColor_Label"), null),
        new("freew.page-color.white", Loc.Get("Ribbon_Palette_PageColor_White_Label"), "#FFFFFF"),
        new("freew.page-color.light-gray", Loc.Get("Ribbon_Palette_PageColor_LightGray_Label"), "#D9D9D9"),
        new("freew.page-color.tan", Loc.Get("Ribbon_Palette_PageColor_Tan_Label"), "#EAD9C0"),
        new("freew.page-color.light-blue", Loc.Get("Ribbon_Palette_PageColor_LightBlue_Label"), "#DDEBF7"),
        new("freew.page-color.light-green", Loc.Get("Ribbon_Palette_PageColor_LightGreen_Label"), "#E2EFDA"),
        new("freew.page-color.light-yellow", Loc.Get("Ribbon_Palette_PageColor_LightYellow_Label"), "#FFF2CC"),
        new("freew.page-color.rose", Loc.Get("Ribbon_Palette_PageColor_Rose_Label"), "#FCE4EC"),
    ];

    public static IReadOnlyList<string> TextAndHighlightPickerSwatches =>
        Highlights.Where(choice => choice.Hex is not null).Select(choice => choice.Hex!).ToArray();

    public static IReadOnlyList<string> ParagraphShadingPickerSwatches =>
        ParagraphShading.Where(choice => choice.Hex is not null).Select(choice => choice.Hex!).ToArray();

    public static readonly IReadOnlyList<string> PageColorPickerSwatches =
    [
        "#FFFFFF", "#F2F2F2", "#DDD9C3", "#C6D9F1", "#DBE5F1", "#F2DCDB",
        "#EBF1DE", "#E5E0EC", "#FDE9D9", "#FFF2CC", "#DEEBF7", "#E2EFDA",
        "#FCE4D6", "#D9E1F2", "#FFFFCC", "#E2F0D9", "#000000", "#1F1F1F",
    ];
}
