using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum FormatCellsFillPaletteEntryKind
{
    Color,
    Clear,
    More
}

public sealed record FormatCellsFillPaletteEntry(
    FormatCellsFillPaletteEntryKind Kind,
    CellColor? Color,
    string ResourceKey)
{
    public bool IsColor => Kind == FormatCellsFillPaletteEntryKind.Color;
    public bool IsClear => Kind == FormatCellsFillPaletteEntryKind.Clear;
    public bool IsMore => Kind == FormatCellsFillPaletteEntryKind.More;
}

/// <summary>
/// The persistent Format Cells Fill palettes. The order is intentionally the WPF/Excel order;
/// both hosts consume this catalog so platform UI code cannot drift into a different palette.
/// </summary>
public static class FormatCellsFillPalettePlanner
{
    public static IReadOnlyList<FormatCellsFillPaletteEntry> BackgroundEntries { get; } =
    [
        Clear("FormatCells_NoFill"),
        Color(255, 255, 255, "FormatCells_White"),
        Color(0, 0, 0, "FormatCells_Black"),
        Color(89, 89, 89, "FormatCells_DarkGray"),
        Color(128, 128, 128, "FormatCells_Gray"),
        Color(217, 217, 217, "FormatCells_LightGray"),
        Color(192, 0, 0, "FormatCells_DarkRed"),
        Color(255, 0, 0, "FormatCells_Red"),
        Color(237, 125, 49, "FormatCells_Orange"),
        Color(255, 192, 0, "FormatCells_Gold"),
        Color(255, 255, 0, "FormatCells_Yellow"),
        Color(255, 242, 204, "FormatCells_LightYellow"),
        Color(0, 97, 0, "FormatCells_DarkGreen"),
        Color(146, 208, 80, "FormatCells_Green"),
        Color(226, 239, 218, "FormatCells_LightGreen"),
        Color(31, 78, 121, "FormatCells_DarkBlue"),
        Color(91, 155, 213, "FormatCells_Blue"),
        Color(221, 235, 247, "FormatCells_LightBlue"),
        Color(112, 48, 160, "FormatCells_Purple"),
        More("FormatCells_MoreColors"),
        Color(0, 176, 240, "FormatCells_AccentTeal"),
        Color(0, 176, 180, "FormatCells_AccentAqua"),
        Color(112, 173, 71, "FormatCells_AccentLime"),
        Color(84, 130, 53, "FormatCells_AccentOlive"),
        Color(255, 199, 206, "FormatCells_AccentRose"),
        Color(244, 176, 132, "FormatCells_AccentPeach"),
        Color(204, 192, 218, "FormatCells_AccentLavender"),
        Color(68, 84, 106, "FormatCells_AccentSlate"),
        Color(131, 60, 12, "FormatCells_AccentBrown"),
        Color(197, 90, 17, "FormatCells_AccentTan")
    ];

    public static IReadOnlyList<FormatCellsFillPaletteEntry> PatternEntries { get; } =
    [
        Color(0, 0, 0, "FormatCells_PatternBlack"),
        Color(128, 128, 128, "FormatCells_PatternGray"),
        Color(255, 0, 0, "FormatCells_PatternRed"),
        Color(255, 192, 0, "FormatCells_PatternGold"),
        Color(0, 176, 80, "FormatCells_PatternGreen"),
        Color(0, 112, 192, "FormatCells_PatternAccentBlue"),
        Color(112, 48, 160, "FormatCells_PatternPurple"),
        More("FormatCells_MorePatternColors")
    ];

    private static FormatCellsFillPaletteEntry Color(byte r, byte g, byte b, string key) =>
        new(FormatCellsFillPaletteEntryKind.Color, new CellColor(r, g, b), key);

    private static FormatCellsFillPaletteEntry Clear(string key) =>
        new(FormatCellsFillPaletteEntryKind.Clear, null, key);

    private static FormatCellsFillPaletteEntry More(string key) =>
        new(FormatCellsFillPaletteEntryKind.More, null, key);
}
