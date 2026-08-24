using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Compact native Chart Design style and palette thumbnails for Avalonia.</summary>
internal static class ChartStylesGallery
{
    public static Control BuildQuickLayouts(IRibbonCommandRegistry registry)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) };
        foreach (var layout in ChartQuickLayout.Catalog) host.Children.Add(LayoutButton(layout, registry));
        return host;
    }

    public static Control Build(IRibbonCommandRegistry registry)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) };
        foreach (var style in ChartStyle.Catalog) host.Children.Add(StyleButton(style, registry));
        host.Children.Add(new Border { Width = 1, Height = 48, Background = Brush("#D0D0D0"), Margin = new Thickness(2, 0) });
        foreach (var scheme in ChartColorScheme.Catalog) host.Children.Add(ColorButton(scheme, registry));
        return host;
    }

    private static Button StyleButton(ChartStyle style, IRibbonCommandRegistry registry)
    {
        var sample = new StackPanel { Width = 42, Margin = new Thickness(2) };
        var page = new Border { Height = 32, Padding = new Thickness(4), Background = style.PlotAreaFill ? Brush("#D9E2F3") : Brushes.White, BorderBrush = Brush("#C0C0C0"), BorderThickness = new Thickness(1) };
        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        bars.Children.Add(Bar("#4472C4", 5, 16)); bars.Children.Add(Bar("#ED7D31", 5, 10)); bars.Children.Add(Bar("#A5A5A5", 5, 14)); page.Child = bars;
        sample.Children.Add(page); sample.Children.Add(new TextBlock { Text = style.Name, FontSize = 8, TextAlignment = global::Avalonia.Media.TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return Button(sample, style.Name, new RibbonCommandId($"freew.chart-style-{style.Id}"), registry);
    }

    private static Button LayoutButton(ChartQuickLayout layout, IRibbonCommandRegistry registry)
    {
        var sample = new StackPanel { Width = 42, Margin = new Thickness(2) };
        var page = new Border { Height = 32, Padding = new Thickness(4, 2), Background = Brushes.White, BorderBrush = Brush("#C0C0C0"), BorderThickness = new Thickness(1) };
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (layout.ShowTitle) lines.Children.Add(Bar("#2F5496", 30, 2));
        if (layout.ShowGridlines) lines.Children.Add(Bar("#D0D0D0", 30, 1));
        lines.Children.Add(Bar("#5B9BD5", 22, 4));
        if (layout.ShowDataLabels) lines.Children.Add(Bar("#888888", 14, 1));
        if (layout.ShowLegend) lines.Children.Add(Bar("#888888", 24, 1));
        page.Child = lines; sample.Children.Add(page); sample.Children.Add(new TextBlock { Text = layout.Name, FontSize = 8, TextAlignment = global::Avalonia.Media.TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return Button(sample, layout.Name, new RibbonCommandId($"freew.chart-quick-layout-{layout.Id}"), registry);
    }

    private static Button ColorButton(ChartColorScheme scheme, IRibbonCommandRegistry registry)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Width = 38, Height = 18, Margin = new Thickness(2, 5, 2, 0) };
        foreach (var color in scheme.Colors.Take(4)) row.Children.Add(new Border { Width = 9, Height = 18, Background = Brush(color), BorderBrush = Brushes.White, BorderThickness = new Thickness(.5) });
        var sample = new StackPanel { Width = 42, Children = { row, new TextBlock { Text = scheme.Name, FontSize = 8, TextAlignment = global::Avalonia.Media.TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
        return Button(sample, scheme.Name, ChartColorRibbonCommandCatalog.CommandId(scheme), registry);
    }

    private static Button Button(Control content, string name, RibbonCommandId id, IRibbonCommandRegistry registry)
    {
        var button = new Button { Content = content, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(1) };
        ToolTip.SetTip(button, name); AutomationProperties.SetName(button, name);
        button.PointerEntered += (_, _) => { button.Background = Brush("#EAF1FB"); button.BorderBrush = Brush("#2B579A"); Preview(id, registry, command => command.BeginPreview(RibbonCommandContext.Empty)); };
        button.PointerExited += (_, _) => { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; Preview(id, registry, command => command.CancelPreview()); };
        button.Click += (_, _) => { if (registry.TryGet(id, out var command) && command is not null) command.Execute(RibbonCommandContext.Empty); };
        return button;
    }

    private static void Preview(RibbonCommandId id, IRibbonCommandRegistry registry, Action<IRibbonPreviewCommand> action)
    { if (registry.TryGet(id, out var command) && command is IRibbonPreviewCommand preview) action(preview); }
    private static Border Bar(string color, double width, double height) => new() { Width = width, Height = height, Margin = new Thickness(1, 0), Background = Brush(color) };
    private static IBrush Brush(string hex) => Color.TryParse(hex.StartsWith('#') ? hex : "#" + hex, out var color) ? new SolidColorBrush(color) : Brushes.Black;
}
