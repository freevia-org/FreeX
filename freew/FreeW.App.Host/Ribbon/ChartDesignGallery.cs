using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word-style galleries for the Chart Design contextual tab:
/// <list type="bullet">
///   <item><description><b>Quick Layout</b> — nine layout thumbnails each toggling which chart elements are visible (title/legend/gridlines/labels/axis-titles).</description></item>
///   <item><description><b>Chart Styles</b> — eight style swatches controlling gridline visibility, plot-area fill, markers and data-value labels.</description></item>
///   <item><description><b>Change Colors</b> — seven colour-scheme swatches (colorful + monochromatic palettes) applied to series/slices.</description></item>
/// </list>
/// All three galleries follow the ThemeGallery pattern: hover previews via model mutation + Render,
/// leave reverts, click commits. Hosted as app-side custom content (no shared RibbonGallery render).
/// </summary>
internal static class ChartDesignGallery
{
    // ── Quick Layout ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Quick Layout gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildQuickLayouts(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var layout in ChartQuickLayout.Catalog)
            strip.Children.Add(BuildLayoutSwatch(editor, layout));
        return WithLabel("Quick Layout", strip);
    }

    private static FrameworkElement BuildLayoutSwatch(DocumentView editor, ChartQuickLayout layout)
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

        return WrapAsChartButton(editor, thumb, layout.Name,
            onEnter: () => PreviewQuickLayout(editor, layout),
            onLeave: () => RevertChart(editor),
            onClick: () => { RevertChart(editor); editor.ApplySelectedChartQuickLayout(layout); });
    }

    // ── Chart Styles ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Chart Styles gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildChartStyles(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var style in ChartStyle.Catalog)
            strip.Children.Add(BuildStyleSwatch(editor, style));
        return WithLabel("Chart Styles", strip);
    }

    private static FrameworkElement BuildStyleSwatch(DocumentView editor, ChartStyle style)
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

        return WrapAsChartButton(editor, thumb, style.Name,
            onEnter: () => PreviewStyle(editor, style),
            onLeave: () => RevertChart(editor),
            onClick: () => { RevertChart(editor); editor.ApplySelectedChartStyle(style); });
    }

    // ── Change Colors ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Build the Change Colors gallery strip for the Chart Design contextual tab.</summary>
    public static FrameworkElement BuildChangeColors(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var scheme in ChartColorScheme.Catalog)
            strip.Children.Add(BuildColorSwatch(editor, scheme));
        return WithLabel("Change Colors", strip);
    }

    private static FrameworkElement BuildColorSwatch(DocumentView editor, ChartColorScheme scheme)
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

        return WrapAsChartButton(editor, thumb, scheme.Name,
            onEnter: () => PreviewColorScheme(editor, scheme),
            onLeave: () => RevertChart(editor),
            onClick: () => { RevertChart(editor); editor.ApplySelectedChartColorScheme(scheme); });
    }

    // ── Preview helpers ───────────────────────────────────────────────────────────────────────────
    // Live-preview by temporarily mutating the model chart + calling Render; leave-revert restores.
    // The commit path calls RevertChart (to undo preview) then applies the real change.
    // This mirrors the ThemeGallery pattern (PreviewTheme / EndThemePreview / ApplyTheme).

    private static void PreviewQuickLayout(DocumentView editor, ChartQuickLayout layout)
    {
        var chart = editor.SelectedChart();
        if (chart is null) return;
        _savedQuickLayoutId = chart.QuickLayoutId;
        chart.QuickLayoutId = layout.Id;
        editor.RerenderSelectedChart();
    }

    private static void PreviewStyle(DocumentView editor, ChartStyle style)
    {
        var chart = editor.SelectedChart();
        if (chart is null) return;
        _savedStyleId = chart.StyleId;
        chart.StyleId = style.Id;
        editor.RerenderSelectedChart();
    }

    private static void PreviewColorScheme(DocumentView editor, ChartColorScheme scheme)
    {
        var chart = editor.SelectedChart();
        if (chart is null) return;
        _savedColorSchemeId = chart.ColorSchemeId;
        chart.ColorSchemeId = scheme.Id;
        editor.RerenderSelectedChart();
    }

    private static void RevertChart(DocumentView editor)
    {
        var chart = editor.SelectedChart();
        if (chart is null) return;
        if (_savedQuickLayoutId.HasValue) { chart.QuickLayoutId = _savedQuickLayoutId.Value; _savedQuickLayoutId = null; }
        if (_savedStyleId.HasValue) { chart.StyleId = _savedStyleId.Value; _savedStyleId = null; }
        if (_savedColorSchemeId is not null) { chart.ColorSchemeId = _savedColorSchemeId; _savedColorSchemeId = null; }
        editor.RerenderSelectedChart();
    }

    // Saved-state for revert on leave (only one is active at a time because galleries don't overlap).
    private static int? _savedQuickLayoutId;
    private static int? _savedStyleId;
    private static string? _savedColorSchemeId;

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────────

    private static FrameworkElement WrapAsChartButton(
        DocumentView editor,
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
