using System.Windows.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal abstract class FreePBrushes : FreePVisualBrushCatalog<Brush, WpfFreePVisualBrushAdapter>
{
    internal static Color AccentColor => ResolveThemeColor("Accent", BrandThemes.FreeP.Colors.Accent);
    internal static Color AccentDarkColor => ResolveThemeColor("AccentDark", BrandThemes.FreeP.Colors.AccentDark);
    internal static Color TitleBarColor => ResolveThemeColor("TitleBar", BrandThemes.FreeP.Colors.TitleBar);
    internal static Color TitleBarForegroundColor => ResolveThemeColor("TitleBarForeground", BrandThemes.FreeP.Colors.TitleBarForeground);

    private static Color ResolveThemeColor(string role, ThemeColor fallback) =>
        WpfThemeResourceResolver.ResolveProjectedOr<SolidColorBrush, Color>(
            ProductThemeResourceProfiles.FreeP.Brush(role),
            brush => brush.Color,
            WpfThemeApplier.ToColor(fallback));
}

internal readonly struct WpfFreePVisualBrushAdapter : IFreePVisualBrushAdapter<Brush>
{
    public static Brush ResolveTheme(ThemeResourceDescriptor resource, ThemeColor fallback) =>
        WpfThemeResourceResolver.Find<Brush>(resource) ?? Create(fallback);

    public static Brush Create(ThemeColor color) =>
        new SolidColorBrush(WpfThemeApplier.ToColor(color));
}
