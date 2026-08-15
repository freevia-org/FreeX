using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Word-style galleries for the Chart Design contextual tab:
/// <list type="bullet">
///   <item><description><b>Quick Layout</b> — nine layout thumbnails each toggling which chart elements are visible (title/legend/gridlines/labels/axis-titles).</description></item>
///   <item><description><b>Chart Styles</b> — eight style swatches controlling gridline visibility, plot-area fill, markers and data-value labels.</description></item>
///   <item><description><b>Change Colors</b> — seven colour-scheme swatches (colorful + monochromatic palettes) applied to series/slices.</description></item>
/// </list>
/// Native WPF thumbnails and pointer events adapt to the shared chart-gallery commands. Presentation
/// owns target freezing, baseline restoration, cancel, and one-entry commit semantics.
/// </summary>
internal static class ChartDesignGallery
{
    // ── Quick Layout ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Quick Layout gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildQuickLayouts(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var layout in ChartQuickLayout.Catalog)
            strip.Children.Add(BuildLayoutSwatch(layout, registry));
        return WithLabel("Quick Layout", strip);
    }

    private static FrameworkElement BuildLayoutSwatch(
        ChartQuickLayout layout,
        IRibbonCommandRegistry registry)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 46 };

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 36,
            Padding = new Thickness(4, 3, 4, 3),
            SnapsToDevicePixels = true
        };
        var sample = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Miniature representation: grey bars for elements that are visible
        if (layout.ShowTitle)
            sample.Children.Add(Bar("#2F5496", 30, 2.5));
        sample.Children.Add(new Border { Height = 1 });
        // Plot area sketch — a short grid line to imply gridlines
        if (layout.ShowGridlines)
            sample.Children.Add(Bar("#D0D0D0", 30, 1));
        sample.Children.Add(Bar("#5B9BD5", 22, 4));
        if (layout.ShowDataLabels)
        {
            sample.Children.Add(new Border { Height = 1 });
            sample.Children.Add(Bar("#888888", 14, 1.5));
        }
        if (layout.ShowLegend)
        {
            sample.Children.Add(new Border { Height = 1 });
            sample.Children.Add(Bar("#888888", 24, 1.5));
        }
        page.Child = sample;

        thumb.Children.Add(page);
        thumb.Children.Add(new TextBlock
        {
            Text = layout.Name,
            FontSize = 9,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 9,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var command = Resolve(registry, $"freew.chart-quick-layout-{layout.Id}");
        return WrapAsChartButton(thumb, layout.Name,
            onEnter: () => BeginPreview(command),
            onLeave: () => CancelPreview(command),
            onClick: () => command.Execute(RibbonCommandContext.Empty));
    }

    // ── Chart Styles ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Chart Styles gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildChartStyles(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var style in ChartStyle.Catalog)
            strip.Children.Add(BuildStyleSwatch(style, registry));
        return WithLabel("Chart Styles", strip);
    }

    private static FrameworkElement BuildStyleSwatch(
        ChartStyle style,
        IRibbonCommandRegistry registry)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 46 };

        var page = new Border
        {
            Background = style.PlotAreaFill ? BrushFor("#D9E2F3") : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Height = 36,
            Padding = new Thickness(3, 3, 3, 3),
            SnapsToDevicePixels = true
        };
        var sample = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (style.ShowGridlines)
        {
            sample.Children.Add(Bar("#D0D0D0", 34, 1));
            sample.Children.Add(new Border { Height = 3 });
        }
        // A little bar-chart sketch showing data
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        bars.Children.Add(MiniBar("#4472C4", 5, 16));
        bars.Children.Add(new Border { Width = 1 });
        bars.Children.Add(MiniBar("#ED7D31", 5, 10));
        bars.Children.Add(new Border { Width = 1 });
        bars.Children.Add(MiniBar("#A5A5A5", 5, 14));
        if (style.ShowDataLabels)
        {
            bars.Children.Add(new TextBlock { Text = "1", FontSize = 6, Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top });
        }
        sample.Children.Add(bars);
        page.Child = sample;

        thumb.Children.Add(page);
        thumb.Children.Add(new TextBlock
        {
            Text = style.Name,
            FontSize = 9,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 9,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var command = Resolve(registry, $"freew.chart-style-{style.Id}");
        return WrapAsChartButton(thumb, style.Name,
            onEnter: () => BeginPreview(command),
            onLeave: () => CancelPreview(command),
            onClick: () => command.Execute(RibbonCommandContext.Empty));
    }

    // ── Change Colors ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Change Colors gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildChangeColors(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var scheme in ChartColorScheme.Catalog)
            strip.Children.Add(BuildColorSwatch(scheme, registry));
        return WithLabel("Change Colors", strip);
    }

    private static FrameworkElement BuildColorSwatch(
        ChartColorScheme scheme,
        IRibbonCommandRegistry registry)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 46 };

        // Show the first four colours of the scheme as adjacent swatches.
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < Math.Min(4, scheme.Colors.Count); i++)
            row.Children.Add(new Border
            {
                Background = BrushFor(scheme.Colors[i]),
                Width = 10,
                Height = 24,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.5)
            });

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Child = row,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        thumb.Children.Add(border);
        thumb.Children.Add(new TextBlock
        {
            Text = scheme.Name,
            FontSize = 9,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 9,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var command = Resolve(registry, ChartColorRibbonCommandCatalog.CommandId(scheme));
        return WrapAsChartButton(thumb, scheme.Name,
            onEnter: () => BeginPreview(command),
            onLeave: () => CancelPreview(command),
            onClick: () => command.Execute(RibbonCommandContext.Empty));
    }

    // ── Preview helpers ───────────────────────────────────────────────────────────────────────────
    private static IRibbonCommand Resolve(IRibbonCommandRegistry registry, RibbonCommandId id) =>
        registry.TryGet(id, out var command)
            ? command!
            : throw new InvalidOperationException($"Missing shared chart gallery command '{id}'.");

    private static void BeginPreview(IRibbonCommand command)
    {
        if (command is IRibbonPreviewCommand preview)
            preview.BeginPreview(RibbonCommandContext.Empty);
    }

    private static void CancelPreview(IRibbonCommand command)
    {
        if (command is IRibbonPreviewCommand preview)
            preview.CancelPreview();
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────────

    private static FrameworkElement WrapAsChartButton(
        FrameworkElement content,
        string tip,
        Action onEnter,
        Action onLeave,
        Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip
        };

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
            onEnter();
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            onLeave();
        };
        button.Click += (_, _) => onClick();
        AutomationProperties.SetName(button, tip);
        return button;
    }

    private static FrameworkElement WithLabel(string label, FrameworkElement content)
    {
        var host = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(host, label);
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 1)
        });
        host.Children.Add(content);
        return host;
    }

    private static FrameworkElement Bar(string hex, double width, double height) => new Border
    {
        Background = BrushFor(hex),
        Width = width,
        Height = height,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static FrameworkElement MiniBar(string hex, double width, double height) => new Border
    {
        Background = BrushFor(hex),
        Width = width,
        Height = height,
        VerticalAlignment = VerticalAlignment.Bottom
    };

    private static Brush BrushFor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.Gray;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}
