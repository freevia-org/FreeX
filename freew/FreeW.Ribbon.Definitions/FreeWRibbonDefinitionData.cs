using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

public sealed record FreeWFloatingPositionPreset(
    string Suffix,
    string Label,
    double HorizontalOffsetPt,
    double VerticalOffsetPt,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor);

public sealed record FreeWFloatingSizePreset(string Suffix, string Label, double WidthPt, double HeightPt);

public sealed record FreeWAltTextPreset(string Suffix, string Label, string? AltText);

public static class FreeWRibbonDefinitionData
{
    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];

    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Times New Roman", "Inter", "Verdana", "Georgia", "Courier New"];

    public static readonly string[] FloatSizes =
        ["36", "54", "72", "90", "108", "144", "180", "216", "288", "360", "432"];

    public static readonly IReadOnlyList<FreeWFloatingPositionPreset> FloatingPositionPresets =
    [
        new("column-paragraph", "Column, Paragraph", 0, 0, HorizontalAnchor.Column, VerticalAnchor.Paragraph),
        new("margin-paragraph", "Margin, Paragraph", 0, 0, HorizontalAnchor.Margin, VerticalAnchor.Paragraph),
        new("page-paragraph", "Page, Paragraph", 0, 0, HorizontalAnchor.Page, VerticalAnchor.Paragraph),
        new("page-top", "Page Top", 0, 0, HorizontalAnchor.Page, VerticalAnchor.Page),
    ];

    public static readonly IReadOnlyList<FreeWFloatingSizePreset> FloatingSizePresets =
    [
        new("small", "Small (1.5 x 1 in)", 108, 72),
        new("medium", "Medium (2 x 1 in)", 144, 72),
        new("wide", "Wide (3 x 1.5 in)", 216, 108),
        new("large", "Large (4 x 3 in)", 288, 216),
    ];

    public static readonly IReadOnlyList<FreeWAltTextPreset> ShapeAltTextPresets =
    [
        new("drawing-object", "Drawing object", "Drawing object"),
        new("process-diagram", "Process diagram", "Process diagram"),
        new("supporting-illustration", "Supporting illustration", "Supporting illustration"),
        new("clear", "Clear Alt Text", null),
    ];

    public static readonly string[] CitationStyleNames =
        Enum.GetValues<CitationStyle>().Select(Citations.StyleName).ToArray();

    public static (string CommandId, string Label)[] FontColors =>
    [
        ("freew.font-color.automatic", Loc.Get("Ribbon_Palette_FontColor_Automatic_Label")),
        ("freew.font-color.black", Loc.Get("Ribbon_Palette_FontColor_Black_Label")),
        ("freew.font-color.dark-red", Loc.Get("Ribbon_Palette_FontColor_DarkRed_Label")),
        ("freew.font-color.red", Loc.Get("Ribbon_Palette_FontColor_Red_Label")),
        ("freew.font-color.orange", Loc.Get("Ribbon_Palette_FontColor_Orange_Label")),
        ("freew.font-color.yellow", Loc.Get("Ribbon_Palette_FontColor_Yellow_Label")),
        ("freew.font-color.green", Loc.Get("Ribbon_Palette_FontColor_Green_Label")),
        ("freew.font-color.blue", Loc.Get("Ribbon_Palette_FontColor_Blue_Label")),
        ("freew.font-color.dark-blue", Loc.Get("Ribbon_Palette_FontColor_DarkBlue_Label")),
        ("freew.font-color.purple", Loc.Get("Ribbon_Palette_FontColor_Purple_Label")),
        ("freew.font-color.white", Loc.Get("Ribbon_Palette_FontColor_White_Label")),
    ];

    public static (string CommandId, string Label)[] PageColors =>
    [
        ("freew.page-color.none", Loc.Get("Ribbon_Palette_PageColor_NoColor_Label")),
        ("freew.page-color.white", Loc.Get("Ribbon_Palette_PageColor_White_Label")),
        ("freew.page-color.light-gray", Loc.Get("Ribbon_Palette_PageColor_LightGray_Label")),
        ("freew.page-color.tan", Loc.Get("Ribbon_Palette_PageColor_Tan_Label")),
        ("freew.page-color.light-blue", Loc.Get("Ribbon_Palette_PageColor_LightBlue_Label")),
        ("freew.page-color.light-green", Loc.Get("Ribbon_Palette_PageColor_LightGreen_Label")),
        ("freew.page-color.light-yellow", Loc.Get("Ribbon_Palette_PageColor_LightYellow_Label")),
        ("freew.page-color.rose", Loc.Get("Ribbon_Palette_PageColor_Rose_Label")),
    ];

    public static string[] MultilevelListPresetNames =>
    [
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineDecimal_Label"),
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineMixed_Label"),
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineHeadings_Label"),
    ];

    public static IReadOnlyList<(string Id, string Glyph, string Label)> Symbols =>
    [
        ("freew.symbol.euro", "€", Loc.Get("Ribbon_Palette_Symbol_Euro_Label")),
        ("freew.symbol.pound", "£", Loc.Get("Ribbon_Palette_Symbol_Pound_Label")),
        ("freew.symbol.yen", "¥", Loc.Get("Ribbon_Palette_Symbol_Yen_Label")),
        ("freew.symbol.cent", "¢", Loc.Get("Ribbon_Palette_Symbol_Cent_Label")),
        ("freew.symbol.copyright", "©", Loc.Get("Ribbon_Palette_Symbol_Copyright_Label")),
        ("freew.symbol.registered", "®", Loc.Get("Ribbon_Palette_Symbol_Registered_Label")),
        ("freew.symbol.trademark", "™", Loc.Get("Ribbon_Palette_Symbol_Trademark_Label")),
        ("freew.symbol.degree", "°", Loc.Get("Ribbon_Palette_Symbol_Degree_Label")),
        ("freew.symbol.plusminus", "±", Loc.Get("Ribbon_Palette_Symbol_PlusMinus_Label")),
        ("freew.symbol.multiply", "×", Loc.Get("Ribbon_Palette_Symbol_Multiplication_Label")),
        ("freew.symbol.divide", "÷", Loc.Get("Ribbon_Palette_Symbol_Division_Label")),
        ("freew.symbol.notequal", "≠", Loc.Get("Ribbon_Palette_Symbol_NotEqual_Label")),
        ("freew.symbol.lessequal", "≤", Loc.Get("Ribbon_Palette_Symbol_LessOrEqual_Label")),
        ("freew.symbol.greaterequal", "≥", Loc.Get("Ribbon_Palette_Symbol_GreaterOrEqual_Label")),
        ("freew.symbol.bullet", "•", Loc.Get("Ribbon_Palette_Symbol_Bullet_Label")),
        ("freew.symbol.ellipsis", "…", Loc.Get("Ribbon_Palette_Symbol_Ellipsis_Label")),
        ("freew.symbol.emdash", "—", Loc.Get("Ribbon_Palette_Symbol_EmDash_Label")),
        ("freew.symbol.endash", "–", Loc.Get("Ribbon_Palette_Symbol_EnDash_Label")),
        ("freew.symbol.arrow-right", "→", Loc.Get("Ribbon_Palette_Symbol_RightArrow_Label")),
        ("freew.symbol.arrow-left", "←", Loc.Get("Ribbon_Palette_Symbol_LeftArrow_Label")),
    ];

    public static string StyleCommandId(string styleId) => $"freew.style.{styleId}";

    public static string ParaSpacingId(string name) =>
        name.ToLowerInvariant().Replace(' ', '-');
}
