using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Icons;
using SharedRibbonIconFactory = Free.Shared.Ribbon.Wpf.RibbonIconFactory;

namespace FreeX.App.Host;

/// <summary>
/// FreeX command-artwork adapter. App-bundled SVGs stay local while all fallback vector geometry is
/// rendered by <see cref="SharedRibbonIconFactory"/>.
/// </summary>
public static partial class RibbonIconFactory
{
    public static int ResolveCommandIconPixelSizeForDpi(double logicalSize, double dpiScale)
    {
        if (double.IsNaN(logicalSize) || double.IsInfinity(logicalSize) || logicalSize <= 0 ||
            double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(logicalSize * dpiScale, MidpointRounding.AwayFromZero));
    }

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush) =>
        TryCreateCommandIconElement(commandName, fallbackIcon, size, glyphBrush) ??
        SharedRibbonIconFactory.CreateIcon(fallbackIcon, size, glyphBrush);

    /// <summary>
    /// Resolver installed into the shared icon factory. Returns FreeX's SVG artwork when available;
    /// returning <c>null</c> lets the shared renderer draw its neutral geometry fallback.
    /// </summary>
    public static FrameworkElement? TryCreateCommandIconElement(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (TryLoadCommandIcon(commandName, glyphBrush, size) is not { } source)
            return null;

        return new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
    }

    public static FrameworkElement CreateIcon(
        RibbonCommandIcon icon,
        double size,
        Brush glyphBrush) =>
        SharedRibbonIconFactory.CreateIcon(icon, size, glyphBrush);
}
