using FreeW.App.Localization;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

public sealed record FreeWFloatingPositionPreset(
    string Suffix,
    string Label,
    double HorizontalOffsetPt,
    double VerticalOffsetPt,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor) : IFreeWRibbonFloatingPositionPreset;

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
        FreeWRibbonPaletteCatalog.FontColors
            .Select(choice => (choice.CommandId, choice.Label))
            .ToArray();

    public static (string CommandId, string Label)[] PageColors =>
        FreeWRibbonPaletteCatalog.PageColors
            .Select(choice => (choice.CommandId, choice.Label))
            .ToArray();

    public static string[] MultilevelListPresetNames =>
    [
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineDecimal_Label"),
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineMixed_Label"),
        Loc.Get("Ribbon_Palette_MultilevelList_OutlineHeadings_Label"),
    ];

}
