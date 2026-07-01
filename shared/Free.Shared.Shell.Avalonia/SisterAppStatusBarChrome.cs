using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAppStatusBarSpec(
    IBrush Background,
    Control LeftContent,
    IReadOnlyList<Control>? RightItems = null,
    double Height = SisterAppStatusBarChromeDefaults.Height,
    IBrush? BorderBrush = null,
    Thickness BorderThickness = default,
    Control? CenterContent = null,
    Thickness Padding = default);

public sealed record SisterAppStatusBarBuildResult(
    Border Root,
    DockPanel Layout);

/// <summary>
/// Builds shared Avalonia status-bar chrome for the simpler sister-app shells.
/// </summary>
public static class SisterAppStatusBarChrome
{
    private static readonly Thickness DefaultSeparatorMargin = new(
        SisterAppStatusBarChromeDefaults.SeparatorHorizontalMargin,
        SisterAppStatusBarChromeDefaults.SeparatorVerticalMargin,
        SisterAppStatusBarChromeDefaults.SeparatorHorizontalMargin,
        SisterAppStatusBarChromeDefaults.SeparatorVerticalMargin);

    public static SisterAppStatusBarBuildResult Build(SisterAppStatusBarSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Background);
        ArgumentNullException.ThrowIfNull(spec.LeftContent);

        var layout = new DockPanel
        {
            LastChildFill = true,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };

        foreach (var item in spec.RightItems ?? [])
        {
            ArgumentNullException.ThrowIfNull(item);
            DockPanel.SetDock(item, Dock.Right);
            layout.Children.Add(item);
        }

        if (spec.CenterContent is { } centerContent)
        {
            DockPanel.SetDock(spec.LeftContent, Dock.Left);
            layout.Children.Add(spec.LeftContent);
            layout.Children.Add(centerContent);
        }
        else
        {
            layout.Children.Add(spec.LeftContent);
        }

        var root = new Border
        {
            Background = spec.Background,
            BorderBrush = spec.BorderBrush,
            BorderThickness = spec.BorderThickness,
            Height = spec.Height,
            Padding = spec.Padding,
            Child = layout,
        };

        return new SisterAppStatusBarBuildResult(root, layout);
    }

    public static TextBlock CreateInfoText(
        string text = "",
        IBrush? foreground = null,
        Thickness margin = default,
        double fontSize = SisterAppStatusBarChromeDefaults.TextFontSize) =>
        new()
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    public static Border CreateSeparator(IBrush? brush = null, Thickness margin = default) =>
        new()
        {
            Width = SisterAppStatusBarChromeDefaults.SeparatorWidth,
            Margin = margin.Equals(default(Thickness)) ? DefaultSeparatorMargin : margin,
            Background = brush ?? new SolidColorBrush(Color.FromArgb(
                SisterAppStatusBarChromeDefaults.SeparatorAlpha,
                SisterAppStatusBarChromeDefaults.SeparatorRgb,
                SisterAppStatusBarChromeDefaults.SeparatorRgb,
                SisterAppStatusBarChromeDefaults.SeparatorRgb)),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
}
