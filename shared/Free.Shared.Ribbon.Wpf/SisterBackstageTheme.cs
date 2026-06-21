using System.Windows.Media;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Shared Backstage rail and tile presets for the WPF sister apps.
/// </summary>
public sealed record SisterBackstageTheme(
    BackstageAccent Accent,
    Color LinkColor,
    double TileWidth,
    double TileHeight)
{
    public static SisterBackstageTheme FreeW { get; } = new(
        new BackstageAccent(
            Sidebar: Color.FromRgb(0x17, 0x32, 0x4D),
            Hover: Color.FromRgb(0x26, 0x4B, 0x6B),
            Selected: Color.FromRgb(0x0F, 0x24, 0x38),
            Separator: Color.FromRgb(0x36, 0x55, 0x73)),
        LinkColor: Color.FromRgb(0x0F, 0x6D, 0x8C),
        TileWidth: 150,
        TileHeight: 190);

    public static SisterBackstageTheme FreeP { get; } = new(
        new BackstageAccent(
            Sidebar: Color.FromRgb(0xB7, 0x47, 0x2A),
            Hover: Color.FromRgb(0xC9, 0x5A, 0x3D),
            Selected: Color.FromRgb(0x8F, 0x37, 0x21),
            Separator: Color.FromRgb(0xCE, 0x6A, 0x4F)),
        LinkColor: Color.FromRgb(0xB7, 0x47, 0x2A),
        TileWidth: 190,
        TileHeight: 150);
}
