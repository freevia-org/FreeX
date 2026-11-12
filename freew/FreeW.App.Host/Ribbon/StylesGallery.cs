using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A Word-style live-preview Styles gallery for the Home tab: a horizontal strip of style swatches,
/// each rendered in that style's own formatting (font, size, weight, colour), plus an expander
/// (<c>▾</c>) that drops the full style list. Hovering a swatch live-previews the style on the current
/// selection via <see cref="DocumentView.PreviewParagraphStyle"/>; leaving reverts via
/// <see cref="DocumentView.EndStylePreview"/>; clicking commits through the editor's normal reversible
/// named-style path. The gallery retains custom WPF swatch rendering, while the preview transaction and
/// command semantics are shared with Avalonia.
/// </summary>
internal sealed class StylesGallery : Control
{
    // The paragraph styles surfaced as swatches, in Word's familiar order. Each entry is (display name,
    // style id). Custom styles defined on the document are appended after the built-ins.
    private static readonly (string Name, string Id)[] BuiltIns =
    [
        ("Normal", "Normal"),
        ("No Spacing", "NoSpacing"),
        ("Heading 1", "Heading1"),
        ("Heading 2", "Heading2"),
        ("Heading 3", "Heading3"),
        ("Title", "Title"),
        ("Subtitle", "Subtitle"),
        ("Quote", "Quote"),
    ];

    private readonly DocumentView _editor;
    private readonly IRibbonCommandRegistry? _registry;
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal };
    private string? _signature;

    private StylesGallery(DocumentView editor, IRibbonCommandRegistry? registry)
    {
        _editor = editor;
        _registry = registry;
    }

    // Rebuild the visible strip when the style catalog no longer matches what the swatches show (a
    // style was created, renamed, deleted, re-flagged for the gallery, or the edit was undone).
    private void RefreshSwatches()
    {
        var signature = StyleGalleryPlanner.Signature(_editor.Model);
        if (string.Equals(signature, _signature, System.StringComparison.Ordinal))
            return;

        _swatches.Children.Clear();
        foreach (var (name, id) in Entries())
            _swatches.Children.Add(BuildSwatch(name, id, large: true));
        // Entries() may seed Normal / No Spacing, so take the signature after it ran.
        _signature = StyleGalleryPlanner.Signature(_editor.Model);
    }

    /// <summary>Build the gallery strip (visible swatches + a "more" expander) for the Home > Styles group.</summary>
    public static FrameworkElement Build(DocumentView editor, IRibbonCommandRegistry? registry = null)
    {
        var gallery = new StylesGallery(editor, registry);
        var root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var strip = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            SnapsToDevicePixels = true
        };
        var swatches = gallery._swatches;
        gallery.RefreshSwatches();
        // Follow the catalog while the gallery is on screen; a gallery instance is rebuilt per ribbon
        // presentation, so it must not stay subscribed (and alive) after it is unloaded.
        root.Loaded += (_, _) =>
        {
            editor.EditingSessionChanged -= gallery.RefreshSwatches;
            editor.EditingSessionChanged += gallery.RefreshSwatches;
            gallery.RefreshSwatches();
        };
        root.Unloaded += (_, _) => editor.EditingSessionChanged -= gallery.RefreshSwatches;
        // A newly opened document replaces the model without an edit notification.
        root.MouseEnter += (_, _) => gallery.RefreshSwatches();
        // Word shows a fixed-width scrollable styles gallery, not the whole list inline. Bound the visible
        // strip so the group stays compact (and doesn't force the adaptive panel to collapse it).
        strip.Child = new ScrollViewer
        {
            // A 1280-DIP Word window keeps Font, Paragraph, and a three-swatch Styles strip visible.
            // Reserve only that compact lane here; remaining styles stay in the More popup instead of
            // triggering the adaptive renderer to collapse the whole group.
            Width = 180,
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
        // The full list is rebuilt each time it opens, so it always shows the current catalog.
        more.Checked += (_, _) =>
        {
            var popup = gallery.BuildMorePopup(more);
            popup.Closed += (_, _) => more.IsChecked = false;
            popup.IsOpen = true;
        };
        root.Children.Add(more);

        return root;
    }

    // The styles to show: the built-ins that exist in the document, plus the custom paragraph and
    // character styles flagged for the gallery (StyleGalleryPlanner owns that rule).
    // "Normal" and "No Spacing" are Word's two always-offered Quick Styles -- shown (and, since a fresh
    // document only auto-seeds the built-ins that carry a formatting role, NoSpacing does not -- seeded)
    // regardless of whether the document's catalog already defines them, exactly like Normal already was.
    // Seeding here (idempotent; a no-op once the style exists) also satisfies the Styles.ContainsKey
    // guards in DocumentView.PreviewParagraphStyle/CommitStylePreview, which would otherwise silently
    // no-op the very first hover/click on a brand-new document.
    private IEnumerable<(string Name, string Id)> Entries()
    {
        var model = _editor.Model;
        foreach (var entry in BuiltIns)
        {
            if (entry.Id is "Normal" or "NoSpacing")
            {
                BuiltInStyles.EnsureSeeded(model, entry.Id);
                yield return entry;
            }
            else if (model.Styles.ContainsKey(entry.Id))
            {
                yield return entry;
            }
        }

        var builtInIds = new HashSet<string>(BuiltIns.Select(e => e.Id));
        foreach (var entry in StyleGalleryPlanner.CustomEntries(model))
        {
            if (!builtInIds.Contains(entry.Id))
                yield return (entry.Name, entry.Id);
        }
    }

    // Word's swatch right-click menu: Update <style> to Match Selection, Modify…, Remove from Style
    // Gallery and Delete, planned by StyleGalleryPlanner and built when the menu opens so the labels and
    // enabled states reflect the catalog at that moment.
    private ContextMenu BuildSwatchContextMenu(string styleId)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var planned in StyleGalleryPlanner.BuildContextMenu(_editor.Model, styleId))
            {
                var item = new MenuItem { Header = planned.Label, IsEnabled = planned.IsEnabled };
                System.Windows.Automation.AutomationProperties.SetAutomationId(item, planned.AutomationId);
                var command = planned.Command;
                item.Click += (_, _) => RunSwatchCommand(command, styleId);
                menu.Items.Add(item);
            }
        };
        // WPF only opens a context menu that has items; the real ones replace this when it opens.
        menu.Items.Add(new MenuItem());
        return menu;
    }

    private void RunSwatchCommand(StyleGalleryContextCommand command, string styleId)
    {
        _editor.EndStylePreview();
        switch (command)
        {
            case StyleGalleryContextCommand.UpdateToMatchSelection:
                _editor.UpdateStyleToMatchSelection(styleId);
                break;

            case StyleGalleryContextCommand.Modify:
                if (_editor.Model.Styles.TryGetValue(styleId, out var existing)
                    && StyleDefinitionDialog.AskModify(Window.GetWindow(_editor), _editor.Model, existing) is { } definition)
                {
                    _editor.ModifyStyle(styleId, definition);
                }
                break;

            case StyleGalleryContextCommand.RemoveFromGallery:
                _editor.SetStyleGalleryVisibility(styleId, showInGallery: false);
                break;

            case StyleGalleryContextCommand.Delete:
                _editor.DeleteParagraphStyle(styleId);
                break;
        }

        _editor.Focus();
        RefreshSwatches();
    }

    // The full-list popup shown by the "▾" expander: every style entry as a tall row swatch.
    private Popup BuildMorePopup(UIElement anchor)
    {
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var (name, id) in Entries())
            list.Children.Add(BuildSwatch(name, id, large: false));

        // The compact ribbon lane intentionally shows only the visual style swatches. Keep the
        // non-style commands that previously occupied the same group reachable from this overflow,
        // matching Office's gallery-more affordance without spending three full-width buttons beside it.
        if (_registry is not null)
        {
            list.Children.Add(new Separator { Margin = new Thickness(4, 5, 4, 4) });
            AddCommandButton(list, "Clear Style", "freew.style-clear");
            AddCommandButton(list, "New Style…", "freew.new-style");
            AddCommandButton(list, "Manage Styles…", "freew.manage-styles");
        }

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

    private void AddCommandButton(Panel host, string label, string commandId)
    {
        if (_registry is null || !_registry.TryGet(new RibbonCommandId(commandId), out var command) || command is null)
            return;

        var button = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 220
        };
        button.Click += (_, _) => command.Execute(RibbonCommandContext.Empty);
        host.Children.Add(button);
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
        if (run.Underline || run.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (run.Underline)
                decorations.Add(TextDecorations.Underline);
            if (run.Strikethrough)
                decorations.Add(TextDecorations.Strikethrough);
            label.TextDecorations = decorations;
        }

        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = large ? new Thickness(8, 2, 8, 2) : new Thickness(8, 4, 8, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = name,
            ContextMenu = BuildSwatchContextMenu(styleId)
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
        Strikethrough = over.Strikethrough || baseRun.Strikethrough,
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
