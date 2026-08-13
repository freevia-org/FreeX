using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Native Avalonia materialization of the shared sister-app Backstage palette.</summary>
public sealed record AvaloniaSisterBackstageTheme(
    AvaloniaBackstageAccent Accent,
    Color LinkColor,
    double TileWidth,
    double TileHeight)
{
    public static AvaloniaSisterBackstageTheme FreeW { get; } = FromPalette(SisterBackstagePalette.FreeW);

    public static AvaloniaSisterBackstageTheme FreeP { get; } = FromPalette(SisterBackstagePalette.FreeP);

    private static AvaloniaSisterBackstageTheme FromPalette(SisterBackstagePalette palette) => new(
        new AvaloniaBackstageAccent(
            ToColor(palette.Sidebar),
            ToColor(palette.Hover),
            ToColor(palette.Selected),
            ToColor(palette.Separator)),
        ToColor(palette.Link),
        palette.TileWidth,
        palette.TileHeight);

    private static Color ToColor(BackstageRgb color) => Color.FromRgb(color.R, color.G, color.B);
}
