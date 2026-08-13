using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record FormatCellsBorderStyleChoice(BorderStyle Style, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public enum FormatCellsBorderColorEntryKind
{
    Color,
    More
}

public sealed record FormatCellsBorderColorEntry(
    FormatCellsBorderColorEntryKind Kind,
    CellColor? Color,
    string ResourceKey)
{
    public bool IsColor => Kind == FormatCellsBorderColorEntryKind.Color;
    public bool IsMore => Kind == FormatCellsBorderColorEntryKind.More;
}

/// <summary>
/// Canonical Format Cells border style and color palettes. Hosts own native control construction only.
/// </summary>
public static class FormatCellsBorderPalettePlanner
{
    public static IReadOnlyList<FormatCellsBorderStyleChoice> StyleChoices { get; } =
    [
        new(BorderStyle.None, "None"),
        new(BorderStyle.Thin, "Thin"),
        new(BorderStyle.Medium, "Medium"),
        new(BorderStyle.Thick, "Thick"),
        new(BorderStyle.Dashed, "Dashed"),
        new(BorderStyle.Dotted, "Dotted"),
        new(BorderStyle.Double, "Double"),
        new(BorderStyle.Hair, "Hair"),
        new(BorderStyle.SlantDashDot, "Slant Dash Dot"),
        new(BorderStyle.MediumDashed, "Medium Dashed"),
        new(BorderStyle.DashDot, "Dash Dot"),
        new(BorderStyle.MediumDashDot, "Medium Dash Dot"),
        new(BorderStyle.DashDotDot, "Dash Dot Dot"),
        new(BorderStyle.MediumDashDotDot, "Medium Dash Dot Dot"),
    ];

    public static IReadOnlyList<FormatCellsBorderColorEntry> ColorEntries { get; } =
    [
        Color(0, 0, 0, "FormatCells_BlackBorder"),
        Color(128, 128, 128, "FormatCells_GrayBorder"),
        Color(255, 0, 0, "FormatCells_RedBorder"),
        Color(255, 192, 0, "FormatCells_GoldBorder"),
        Color(0, 176, 80, "FormatCells_GreenBorder"),
        Color(0, 112, 192, "FormatCells_BlueBorder"),
        Color(112, 48, 160, "FormatCells_PurpleBorder"),
        new(FormatCellsBorderColorEntryKind.More, null, "FormatCells_MoreBorderColors"),
    ];

    public static FormatCellsBorderStyleChoice ChoiceFor(BorderStyle style) =>
        StyleChoices.First(choice => choice.Style == style);

    private static FormatCellsBorderColorEntry Color(byte r, byte g, byte b, string resourceKey) =>
        new(FormatCellsBorderColorEntryKind.Color, new CellColor(r, g, b), resourceKey);
}
