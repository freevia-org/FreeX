using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Galleries for the SmartArt Design contextual tab: Change Layout, Change Colors, and SmartArt Styles.
/// Each gallery is a horizontal strip of thumbnail swatches; hovering a swatch live-previews it on the
/// selected SmartArt (via <see cref="DocumentView.ApplySmartArtLayout"/> etc.), clicking commits.
/// Matches the ThemeGallery / StylesGallery pattern: catalog-driven swatches, borderless hover highlight,
/// injected via <c>InjectGallery</c> in <c>MainWindow.BuildRibbon</c>.
/// </summary>
internal static class SmartArtGallery
{
    // ── Change Layout gallery ────────────────────────────────────────────────────────────────────────

    public static FrameworkElement BuildLayouts(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var preset in SmartArtLayoutPreset.Catalog)
            strip.Children.Add(BuildLayoutSwatch(editor, preset));
        return WithLabel("Layouts", strip);
    }

    private static FrameworkElement BuildLayoutSwatch(DocumentView editor, SmartArtLayoutPreset preset)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 56 };

        // Preview thumbnail: a small diagram sketch matching the layout geometry.
        var preview = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Width = 52,
            Height = 36,
            Padding = new Thickness(2),
            SnapsToDevicePixels = true,
            Child = BuildLayoutMiniature(preset)
        };
        thumb.Children.Add(preview);
        thumb.Children.Add(new TextBlock
        {
            Text = preset.Name,
            FontSize = 10,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapGalleryButton(thumb, preset.Name,
            onEnter: () => PreviewLayout(editor, preset),
            onLeave: () => EndPreview(editor),
            onClick: () => { EndPreview(editor); editor.ApplySmartArtLayout(preset); });
    }

    // Miniature that sketches the layout with simple bars/boxes.
    private static FrameworkElement BuildLayoutMiniature(SmartArtLayoutPreset preset)
    {
        static Border Bar(string hex, double w, double h) => new()
        {
            Background = BrushFor(hex),
            Width = w, Height = h,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1)
        };
        return preset.Id switch
        {
            "process1" => HStack(
                Bar("#4E81BD", 10, 8), Arrow(), Bar("#C0504D", 10, 8), Arrow(), Bar("#9BBB59", 10, 8)),
            "continuousBlockProcess" => HStack(
                Bar("#4E81BD", 12, 12), Bar("#C0504D", 12, 12), Bar("#9BBB59", 12, 12)),
            "stepup1" => StepSketch(ascending: true),
            "stepdown1" => StepSketch(ascending: false),
            "cycle1" => CycleSketch(),
            "hierarchy1" or "orgchart1" => HierarchySketch(),
            "radial1" => RadialSketch(),
            "matrix1" => MatrixSketch(),
            "horizbullet1" => HStack(Bar("#4E81BD", 13, 7), Bar("#C0504D", 13, 7), Bar("#9BBB59", 13, 7)),
            _ => VStack(Bar("#4E81BD", 34, 6), Bar("#2F5496", 28, 6), Bar("#1F3864", 22, 6))
        };
    }

    private static Panel HStack(params FrameworkElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    private static Panel VStack(params FrameworkElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    private static FrameworkElement Arrow() => new System.Windows.Shapes.Polygon
    {
        Points = new PointCollection([new Point(0, 3), new Point(5, 5), new Point(0, 7)]),
        Fill = BrushFor("#808080"),
        Width = 5, Height = 8,
        Stretch = System.Windows.Media.Stretch.Fill,
        Margin = new Thickness(0, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement StepSketch(bool ascending)
    {
        var c = new Canvas { Width = 48, Height = 28 };
        string[] colors = ["#4E81BD", "#C0504D", "#9BBB59"];
        for (var i = 0; i < 3; i++)
        {
            var x = i * 14;
            var y = ascending ? (2 - i) * 7 : i * 7;
            var b = new Border { Background = BrushFor(colors[i]), Width = 13, Height = 9 };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
            c.Children.Add(b);
        }
        return c;
    }

    private static FrameworkElement CycleSketch()
    {
        var c = new Canvas { Width = 48, Height = 32 };
        double[] angles = [0, 2.094, 4.189]; // 0, 120, 240 degrees
        string[] colors = ["#4E81BD", "#C0504D", "#9BBB59"];
        for (var i = 0; i < 3; i++)
        {
            var x = 22 + 14 * Math.Cos(angles[i] - Math.PI / 2) - 5;
            var y = 14 + 11 * Math.Sin(angles[i] - Math.PI / 2) - 4;
            var b = new Border { Background = BrushFor(colors[i]), Width = 10, Height = 8 };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
            c.Children.Add(b);
        }
        return c;
    }

    private static FrameworkElement HierarchySketch()
    {
        var v = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        v.Children.Add(new Border { Background = BrushFor("#4E81BD"), Width = 28, Height = 8, Margin = new Thickness(0, 0, 0, 2) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Background = BrushFor("#C0504D"), Width = 13, Height = 7, Margin = new Thickness(1) });
        row.Children.Add(new Border { Background = BrushFor("#9BBB59"), Width = 13, Height = 7, Margin = new Thickness(1) });
        v.Children.Add(row);
        return v;
    }

    private static FrameworkElement RadialSketch()
    {
        var c = new Canvas { Width = 48, Height = 32 };
        var center = new Border { Background = BrushFor("#4E81BD"), Width = 14, Height = 10 };
        Canvas.SetLeft(center, 17); Canvas.SetTop(center, 11);
        c.Children.Add(center);
        double[] angles = [-Math.PI / 2, Math.PI / 6, 5 * Math.PI / 6];
        string[] colors = ["#C0504D", "#9BBB59", "#8064A2"];
        for (var i = 0; i < 3; i++)
        {
            var x = 24 + 17 * Math.Cos(angles[i]) - 4;
            var y = 16 + 12 * Math.Sin(angles[i]) - 3;
            var b = new Border { Background = BrushFor(colors[i]), Width = 9, Height = 7 };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
            c.Children.Add(b);
        }
        return c;
    }

    private static FrameworkElement MatrixSketch()
    {
        var g = new UniformGrid { Columns = 2, Margin = new Thickness(2) };
        string[] colors = ["#4E81BD", "#C0504D", "#9BBB59", "#8064A2"];
        foreach (var hex in colors)
            g.Children.Add(new Border { Background = BrushFor(hex), Width = 16, Height = 10, Margin = new Thickness(1) });
        return g;
    }

    // ── Change Colors gallery ────────────────────────────────────────────────────────────────────────

    public static FrameworkElement BuildColors(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var scheme in SmartArtColorScheme.Catalog)
            strip.Children.Add(BuildColorSwatch(editor, scheme));
        return WithLabel("Change Colors", strip);
    }

    private static FrameworkElement BuildColorSwatch(DocumentView editor, SmartArtColorScheme scheme)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 44 };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var hex in new[] { scheme.Color1Hex, scheme.Color2Hex, scheme.Color3Hex, scheme.Color4Hex })
            row.Children.Add(new Border
            {
                Background = BrushFor(hex),
                Width = 9,
                Height = 22,
                BorderBrush = System.Windows.Media.Brushes.White,
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
            FontSize = 9.5,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 10,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapGalleryButton(thumb, scheme.Name + " colors",
            onEnter: () => PreviewColor(editor, scheme),
            onLeave: () => EndPreview(editor),
            onClick: () => { EndPreview(editor); editor.ApplySmartArtColorScheme(scheme); });
    }

    // ── SmartArt Styles gallery ──────────────────────────────────────────────────────────────────────

    public static FrameworkElement BuildStyles(DocumentView editor)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var style in SmartArtStyle.Catalog)
            strip.Children.Add(BuildStyleSwatch(editor, style));
        return WithLabel("SmartArt Styles", strip);
    }

    private static FrameworkElement BuildStyleSwatch(DocumentView editor, SmartArtStyle style)
    {
        var thumb = new StackPanel { Margin = new Thickness(3, 3, 3, 3), Width = 52 };

        // Swatch: a representative node box rendered in the style's visual treatment.
        Effect? effect = null;
        if (style.ShadowOpacity > 0)
        {
            effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4 + style.ShadowOpacity * 6,
                ShadowDepth = 1 + style.ShadowOpacity * 2,
                Opacity = style.ShadowOpacity,
                Color = Colors.Black
            };
        }

        var fillColor = Color.FromRgb(0x4E, 0x81, 0xBD);
        if (style.BrightnessAdjust != 0.0)
        {
            static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, v));
            var d = style.BrightnessAdjust * 255;
            fillColor = Color.FromRgb(Clamp(fillColor.R + d), Clamp(fillColor.G + d), Clamp(fillColor.B + d));
        }

        var shape = new Border
        {
            Background = new SolidColorBrush(fillColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x62, 0x8F)),
            BorderThickness = new Thickness(style.BorderThickness > 0 ? style.BorderThickness : 1),
            CornerRadius = new CornerRadius(style.CornerRadius),
            Height = 36,
            Effect = effect,
            Child = new TextBlock
            {
                Text = "A",
                FontSize = 14,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        thumb.Children.Add(shape);
        thumb.Children.Add(new TextBlock
        {
            Text = style.Name,
            FontSize = 10,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return WrapGalleryButton(thumb, style.Name + " style",
            onEnter: () => PreviewStyle(editor, style),
            onLeave: () => EndPreview(editor),
            onClick: () => { EndPreview(editor); editor.ApplySmartArtStyle(style); });
    }

    private sealed record PreviewSnapshot(
        SmartArt SmartArt,
        SmartArtKind Kind,
        string? LayoutId,
        string? ColorSchemeId,
        string? StyleId);

    private static PreviewSnapshot? _previewSnapshot;

    internal static void PreviewLayout(DocumentView editor, SmartArtLayoutPreset preset)
    {
        if (BeginPreview(editor) is not { } smartArt)
            return;
        smartArt.Kind = preset.Kind;
        smartArt.LayoutId = preset.Id;
        editor.Rerender();
    }

    internal static void PreviewColor(DocumentView editor, SmartArtColorScheme scheme)
    {
        if (BeginPreview(editor) is not { } smartArt)
            return;
        smartArt.ColorSchemeId = scheme.Id;
        editor.Rerender();
    }

    internal static void PreviewStyle(DocumentView editor, SmartArtStyle style)
    {
        if (BeginPreview(editor) is not { } smartArt)
            return;
        smartArt.StyleId = style.Id;
        editor.Rerender();
    }

    private static SmartArt? BeginPreview(DocumentView editor)
    {
        RestorePreviewFields();
        var smartArt = editor.SelectedSmartArt();
        if (smartArt is null)
            return null;
        _previewSnapshot = new PreviewSnapshot(
            smartArt,
            smartArt.Kind,
            smartArt.LayoutId,
            smartArt.ColorSchemeId,
            smartArt.StyleId);
        return smartArt;
    }

    internal static void EndPreview(DocumentView editor)
    {
        if (_previewSnapshot is null)
            return;
        RestorePreviewFields();
        editor.Rerender();
    }

    private static void RestorePreviewFields()
    {
        if (_previewSnapshot is not { } snapshot)
            return;
        snapshot.SmartArt.Kind = snapshot.Kind;
        snapshot.SmartArt.LayoutId = snapshot.LayoutId;
        snapshot.SmartArt.ColorSchemeId = snapshot.ColorSchemeId;
        snapshot.SmartArt.StyleId = snapshot.StyleId;
        _previewSnapshot = null;
    }

    // ── Shared helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wrap a thumbnail in a borderless button. <paramref name="onEnter"/> starts live preview,
    /// <paramref name="onLeave"/> restores it, and <paramref name="onClick"/> commits one edit.
    /// </summary>
    private static FrameworkElement WrapGalleryButton(
        FrameworkElement content,
        string tip,
        Action onEnter,
        Action onLeave,
        Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip
        };
        AutomationProperties.SetName(button, tip);

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
            onEnter();
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.BorderBrush = System.Windows.Media.Brushes.Transparent;
            onLeave();
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static FrameworkElement WithLabel(string label, FrameworkElement content)
    {
        var host = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(host, label);
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 1)
        });
        host.Children.Add(content);
        return host;
    }

    private static SolidColorBrush BrushFor(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Color.FromRgb(0x4E, 0x81, 0xBD)); }
    }
}
