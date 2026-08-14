using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Projects the shared Backstage scroll and text-rasterization contract into Avalonia.
/// Route renderers remain responsible only for their content and optional scrollbar template.
/// </summary>
public static class AvaloniaBackstageScrollChrome
{
    public static void Apply(
        ScrollViewer scroll,
        BackstagePaneComposerProfile profile,
        bool useClassicScrollChrome = false)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        ArgumentNullException.ThrowIfNull(profile);

        scroll.Padding = new Thickness(0);
        scroll.Margin = useClassicScrollChrome ? new Thickness(0, 0, 1, 0) : new Thickness(0);
        scroll.FontFamily = new FontFamily(profile.PaneFontFamilyName);
        scroll.FontSize = profile.PaneFontSize;
        scroll.HorizontalContentAlignment = HorizontalAlignment.Left;
        scroll.VerticalContentAlignment = VerticalAlignment.Top;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scroll.SetValue(ScrollViewer.AllowAutoHideProperty, !profile.DisableScrollBarAutoHide);

        ApplyTextRasterization(scroll, profile.TextRasterizationMode);
    }

    public static void ApplyTextRasterization(
        Visual target,
        BackstageTextRasterizationMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);

        switch (mode)
        {
            case BackstageTextRasterizationMode.PlatformDefault:
                TextOptions.SetTextRenderingMode(target, TextRenderingMode.Unspecified);
                break;
            case BackstageTextRasterizationMode.Grayscale:
                TextOptions.SetTextRenderingMode(target, TextRenderingMode.Antialias);
                break;
            case BackstageTextRasterizationMode.Subpixel:
                TextOptions.SetTextRenderingMode(target, TextRenderingMode.SubpixelAntialias);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
