using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A Word-style live-preview Styles gallery for the Home tab: a horizontal strip of style swatches,
/// each rendered in that style's own formatting (font, size, weight, colour), plus an expander
/// (<c>▾</c>) that drops the full style list. Hovering a swatch live-previews the style on the current
/// selection via <see cref="DocumentView.PreviewParagraphStyle"/>; leaving reverts via
/// <see cref="DocumentView.EndStylePreview"/>; clicking commits through the editor's normal reversible
/// <see cref="DocumentView.SetParagraphStyle"/> path. The gallery hosts custom WPF content (it is not a
/// shared <c>RibbonGallery</c> render) so it stays entirely app-side.
/// </summary>
internal sealed class StylesGallery : Control
{
    // The paragraph styles surfaced as swatches, in Word's familiar order. Each entry is (display name,
    // style id). Custom styles defined on the document are appended after the built-ins.
    private static readonly (string Name, string Id)[] BuiltIns =
    [
        ("Normal", "Normal"),
        ("No Spacing", "Normal"),
        ("Heading 1", "Heading1"),
        ("Heading 2", "Heading2"),
        ("Heading 3", "Heading3"),
        ("Title", "Title"),
        ("Subtitle", "Subtitle"),
        ("Quote", "Quote"),
    ];

    private readonly DocumentView _editor;

    private StylesGallery(DocumentView editor) => _editor = editor;

    /// <summary>Build the gallery strip (visible swatches + a "more" expander) for the Home > Styles group.</summary>
    public static FrameworkElement Build(DocumentView editor)
    {
        var gallery = new StylesGallery(editor);
        var root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var strip = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            SnapsToDevicePixels = true
        };
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, id) in gallery.Entries())
            swatches.Children.Add(gallery.BuildSwatch(name, id, large: true));
        // Word shows a fixed-width scrollable styles gallery, not the whole list inline. Bound the visible
        // strip so the group stays compact (and doesn't force the adaptive panel to collapse it).
        strip.Child = new ScrollViewer
        {
            MaxWidth = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = swatches
        };
        root.Children.Add(strip);

        // The "▾" expander drops the full list (every entry, one per row) as a popup.
        var more = new ToggleButton
        {
            Content = "▾",
            Width = 20,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            ToolTip = FreeWUiTextCatalog.MoreStylesToolTip
        };
        var popup = gallery.BuildMorePopup(more);
        more.Checked += (_, _) => popup.IsOpen = true;
        popup.Closed += (_, _) => more.IsChecked = false;
        root.Children.Add(more);

        return root;
    }

    // The styles to show: the built-ins that exist in the document, plus any custom paragraph styles.
    private IEnumerable<(string Name, string Id)> Entries()
    {
        var model = _editor.Model;
        foreach (var entry in BuiltIns)
        {
            if (entry.Id == "Normal" || model.Styles.ContainsKey(entry.Id))
                yield return entry;
        }

        var builtInIds = new HashSet<string>(BuiltIns.Select(e => e.Id));
        foreach (var style in model.Styles.Values)
        {
            if (style.Type == StyleType.Paragraph && !builtInIds.Contains(style.Id) && !StyleManager.IsBuiltIn(style.Id))
                yield return (style.Name, style.Id);
        }
    }

    // The full-list popup shown by the "▾" expander: every style entry as a tall row swatch.
    private Popup BuildMorePopup(UIElement anchor)
    {
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var (name, id) in Entries())
            list.Children.Add(BuildSwatch(name, id, large: false));

        return new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xB5, 0xB5)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x60, 0x60, 0x60),
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.4
                },
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 320, Content = list }
            }
        };
    }

    // One swatch: a button whose content is the style name rendered in the style's own formatting.
    // Hover previews the style; leaving reverts; clicking commits. `large` is the compact in-strip
    // form (used in the visible strip); the popup uses a roomier full-width row.
    private FrameworkElement BuildSwatch(string name, string styleId, bool large)
    {
        var run = ResolveRun(styleId);

        var label = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily(run.FontFamily ?? "Calibri"),
            FontSize = SwatchFontSize(run, large),
            FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = run.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = BrushFor(run.ColorHex),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (run.Underline)
            label.TextDecorations = TextDecorations.Underline;

        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = large ? new Thickness(8, 2, 8, 2) : new Thickness(8, 4, 8, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = name
        };
        if (large)
        {
            button.Height = 50;
            button.MinWidth = 64;
        }
        else
        {
            button.Width = 220;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
            _editor.PreviewParagraphStyle(styleId);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            _editor.EndStylePreview();
        };
        button.Click += (_, _) =>
            // Commit: revert the preview and apply for real (reversibly) to the paragraphs the hover
            // session targeted — the gallery's intervening re-renders cleared the editor selection.
            _editor.CommitStylePreview(styleId);
        return button;
    }

    // Cap the rendered swatch font so a Title (28pt) still fits the strip; the popup gets a little more room.
    private static double SwatchFontSize(RunFormatting run, bool large)
    {
        var pt = run.FontSizePt ?? 11;
        var px = pt * 96.0 / 72.0;
        var cap = large ? 16.0 : 20.0;
        return px > cap ? cap : px;
    }

    // Resolve a style's effective run formatting by walking its based-on chain and overlaying onto the
    // document default, so a swatch renders the way the paragraph actually would. Mirrors the editor's
    // style resolution at the level the swatch needs (font/size/weight/italic/underline/colour).
    private RunFormatting ResolveRun(string styleId)
    {
        var model = _editor.Model;
        var result = model.DefaultRun;
        foreach (var style in Chain(styleId).Reverse())
            result = Overlay(result, style.Run);
        return result;
    }

    private IEnumerable<DocumentStyle> Chain(string styleId)
    {
        var model = _editor.Model;
        var seen = new HashSet<string>();
        var id = styleId;
        while (id is not null && seen.Add(id) && model.Styles.TryGetValue(id, out var style))
        {
            yield return style;
            id = style.BasedOnStyleId;
        }
    }

    private static RunFormatting Overlay(RunFormatting baseRun, RunFormatting over) => baseRun with
    {
        Bold = over.Bold || baseRun.Bold,
        Italic = over.Italic || baseRun.Italic,
        Underline = over.Underline || baseRun.Underline,
        FontFamily = over.FontFamily ?? baseRun.FontFamily,
        FontSizePt = over.FontSizePt ?? baseRun.FontSizePt,
        ColorHex = over.ColorHex ?? baseRun.ColorHex
    };

    private static Brush BrushFor(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Brushes.Black;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Black; }
    }
}
