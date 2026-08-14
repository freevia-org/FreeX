using Avalonia.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal abstract class FreePBrushes
    : FreePVisualBrushCatalog<IBrush, AvaloniaFreePVisualBrushAdapter>
{
}

internal readonly struct AvaloniaFreePVisualBrushAdapter : IFreePVisualBrushAdapter<IBrush>
{
    public static IBrush ResolveTheme(ThemeResourceDescriptor resource, ThemeColor fallback) =>
        AvaloniaThemeResourceResolver.Find<IBrush>(resource) ?? Create(fallback);

    public static IBrush Create(ThemeColor color) =>
        new SolidColorBrush(AvaloniaThemeApplier.ToColor(color));
}
