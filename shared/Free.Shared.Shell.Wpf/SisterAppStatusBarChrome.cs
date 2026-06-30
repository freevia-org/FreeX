using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterAppStatusBarSpec(
    Brush Background,
    UIElement LeftContent,
    IReadOnlyList<FrameworkElement>? RightItems = null,
    double MinHeight = SisterAppStatusBarChromeDefaults.Height,
    Thickness LeftMargin = default);

public sealed record SisterAppStatusBarBuildResult(
    Border Root,
    Grid Layout,
    StackPanel LeftHost);

/// <summary>
/// Builds the shared WPF status-bar chrome used by the sister Office-style apps.
/// </summary>
public static class SisterAppStatusBarChrome
{
    private static readonly Thickness DefaultLeftMargin = new(
        SisterAppStatusBarChromeDefaults.LeftMarginLeft,
        SisterAppStatusBarChromeDefaults.LeftMarginTop,
        SisterAppStatusBarChromeDefaults.LeftMarginRight,
        SisterAppStatusBarChromeDefaults.LeftMarginBottom);

    public static SisterAppStatusBarBuildResult Build(SisterAppStatusBarSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Background);
        ArgumentNullException.ThrowIfNull(spec.LeftContent);

        var rightItems = spec.RightItems ?? [];
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in rightItems)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = spec.LeftMargin.Equals(default(Thickness)) ? DefaultLeftMargin : spec.LeftMargin,
            ClipToBounds = true
        };
        leftHost.Children.Add(spec.LeftContent);
        Grid.SetColumn(leftHost, 0);
        grid.Children.Add(leftHost);

        for (var i = 0; i < rightItems.Count; i++)
        {
            Grid.SetColumn(rightItems[i], i + 1);
            grid.Children.Add(rightItems[i]);
        }

        var root = new Border
        {
            Background = spec.Background,
            MinHeight = spec.MinHeight,
            Child = grid
        };

        return new SisterAppStatusBarBuildResult(root, grid, leftHost);
    }

    public static TextBlock CreateInfoText(string text = "") =>
        new()
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = SisterAppStatusBarChromeDefaults.TextFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

    public static Rectangle CreateSeparator() =>
        new()
        {
            Width = SisterAppStatusBarChromeDefaults.SeparatorWidth,
            Margin = new Thickness(
                SisterAppStatusBarChromeDefaults.SeparatorHorizontalMargin,
                SisterAppStatusBarChromeDefaults.SeparatorVerticalMargin,
                SisterAppStatusBarChromeDefaults.SeparatorHorizontalMargin,
                SisterAppStatusBarChromeDefaults.SeparatorVerticalMargin),
            Fill = new SolidColorBrush(Color.FromArgb(
                SisterAppStatusBarChromeDefaults.SeparatorAlpha,
                SisterAppStatusBarChromeDefaults.SeparatorRgb,
                SisterAppStatusBarChromeDefaults.SeparatorRgb,
                SisterAppStatusBarChromeDefaults.SeparatorRgb)),
            VerticalAlignment = VerticalAlignment.Stretch
        };
}
