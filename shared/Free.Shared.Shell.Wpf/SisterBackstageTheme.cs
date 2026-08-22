using System.Windows.Media;
using Free.Shared.Shell;
using BrandTheme = Free.Shared.Theme.Theme;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Shared Backstage rail and tile presets for the WPF sister apps.
/// </summary>
public sealed record SisterBackstageTheme(
    BackstageAccent Accent,
    Color LinkColor,
    double TileWidth,
    double TileHeight)
{
    public static SisterBackstageTheme FreeW { get; } = FromPalette(SisterBackstagePalette.FreeW);

    public static SisterBackstageTheme FreeP { get; } = FromPalette(SisterBackstagePalette.FreeP);

    public static SisterBackstageTheme FromTheme(BrandTheme theme, double tileWidth, double tileHeight) =>
        FromPalette(SisterBackstagePalette.FromTheme(theme, tileWidth, tileHeight));

    private static SisterBackstageTheme FromPalette(SisterBackstagePalette palette) => new(
        new BackstageAccent(
            ToColor(palette.Sidebar),
            ToColor(palette.Hover),
            ToColor(palette.Selected),
            ToColor(palette.Separator)),
        ToColor(palette.Link),
        palette.TileWidth,
        palette.TileHeight);

    private static Color ToColor(BackstageRgb color) => Color.FromRgb(color.R, color.G, color.B);
}
