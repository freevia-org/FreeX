using Free.Shared.Theme;

namespace Free.Shared.Shell;

public readonly record struct BackstageRgb(byte R, byte G, byte B);

/// <summary>
/// Platform-neutral Backstage colors and tile dimensions shared by the sister-app renderers.
/// </summary>
public sealed record SisterBackstagePalette(
    BackstageRgb Sidebar,
    BackstageRgb Hover,
    BackstageRgb Selected,
    BackstageRgb Separator,
    BackstageRgb Link,
    double TileWidth,
    double TileHeight)
{
    public static SisterBackstagePalette FreeW { get; } = FromTheme(
        BrandThemes.FreeW,
        tileWidth: 150,
        tileHeight: 190);

    public static SisterBackstagePalette FreeP { get; } = FromTheme(
        BrandThemes.FreeP,
        tileWidth: 190,
        tileHeight: 150);

    private static SisterBackstagePalette FromTheme(global::Free.Shared.Theme.Theme theme, double tileWidth, double tileHeight) => new(
        ToRgb(theme.Colors.TitleBar),
        ToRgb(theme.Colors.TitleBarHover),
        ToRgb(theme.Colors.TitleBarPressed),
        ToRgb(theme.Colors.AccentDark),
        ToRgb(theme.Colors.Accent),
        tileWidth,
        tileHeight);

    private static BackstageRgb ToRgb(ThemeColor color) => new(color.R, color.G, color.B);
}
