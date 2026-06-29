using FreeW.App.Localization;

namespace FreeW.Ribbon.Definitions;

public static class FreeWRibbonDefinitionData
{
    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];

    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Times New Roman", "Inter", "Verdana", "Georgia", "Courier New"];

    public static readonly string[] FloatSizes =
        ["36", "54", "72", "90", "108", "144", "180", "216", "288", "360", "432"];

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

    public static readonly (string CommandId, string Label)[] PageColors =
    [
        ("freew.page-color.none", "No Color"),
        ("freew.page-color.white", "White"),
        ("freew.page-color.light-gray", "Light Gray"),
        ("freew.page-color.tan", "Tan"),
        ("freew.page-color.light-blue", "Light Blue"),
        ("freew.page-color.light-green", "Light Green"),
        ("freew.page-color.light-yellow", "Light Yellow"),
        ("freew.page-color.rose", "Rose"),
    ];

    public static readonly string[] MultilevelListPresetNames =
    [
        "Outline: 1. / 1.1. / 1.1.1.",
        "Outline: 1. / a. / i.",
        "Outline (Headings): link to Heading styles",
    ];

    public static readonly IReadOnlyList<(string Id, string Glyph, string Label)> Symbols =
    [
        ("freew.symbol.euro", "€", "Euro Sign"),
        ("freew.symbol.pound", "£", "Pound Sign"),
        ("freew.symbol.yen", "¥", "Yen Sign"),
        ("freew.symbol.cent", "¢", "Cent Sign"),
        ("freew.symbol.copyright", "©", "Copyright"),
        ("freew.symbol.registered", "®", "Registered"),
        ("freew.symbol.trademark", "™", "Trademark"),
        ("freew.symbol.degree", "°", "Degree Sign"),
        ("freew.symbol.plusminus", "±", "Plus-Minus"),
        ("freew.symbol.multiply", "×", "Multiplication"),
        ("freew.symbol.divide", "÷", "Division"),
        ("freew.symbol.notequal", "≠", "Not Equal"),
        ("freew.symbol.lessequal", "≤", "Less-Or-Equal"),
        ("freew.symbol.greaterequal", "≥", "Greater-Or-Equal"),
        ("freew.symbol.bullet", "•", "Bullet"),
        ("freew.symbol.ellipsis", "…", "Ellipsis"),
        ("freew.symbol.emdash", "—", "Em Dash"),
        ("freew.symbol.endash", "–", "En Dash"),
        ("freew.symbol.arrow-right", "→", "Right Arrow"),
        ("freew.symbol.arrow-left", "←", "Left Arrow"),
    ];

    public static string StyleCommandId(string styleId) => $"freew.style.{styleId}";

    public static string ParaSpacingId(string name) =>
        name.ToLowerInvariant().Replace(' ', '-');
}
