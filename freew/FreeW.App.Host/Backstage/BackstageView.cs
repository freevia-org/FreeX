using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// FreeW's Word-style Backstage (the full-window "File" screen), rebuilt on top of the shared,
/// app-neutral <see cref="BackstageFrame"/>. FreeW supplies the entries (Info / New / Open / Save /
/// Save As / Print / Export / Recent / Options / Close) and the content panes; the frame owns the
/// coloured nav rail, selection/hover, the back-arrow + Esc close, and the FreeX Office-backstage look.
///
/// The view re-tints the rail to FreeW's Word accent (#2B579A) and reimplements no file IO — every
/// action routes back into the host's existing command implementations through <see cref="BackstageActions"/>.
/// </summary>
internal sealed class BackstageView : UserControl
{
    // Word's File-screen rail + the darker selection/hover bands, matching the app's FreeX-navy title bar
    // (#17324D). Selection/hover are darker shades of the same navy.
    private static readonly Color AccentColor = Color.FromRgb(0x17, 0x32, 0x4D);
    private static readonly Color AccentSelectedColor = Color.FromRgb(0x0F, 0x24, 0x38);
    private static readonly Color AccentHoverColor = Color.FromRgb(0x26, 0x4B, 0x6B);
    private static readonly Color SeparatorColor = Color.FromRgb(0x36, 0x55, 0x73);

    private static readonly Brush HeadingBrush = Freeze(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush MutedBrush = Freeze(Color.FromRgb(0x70, 0x70, 0x70));
    // The teal FreeX accent (#0F6D8C) for in-content links, matching the ribbon accent.
    private static readonly Brush LinkBrush = Freeze(Color.FromRgb(0x0F, 0x6D, 0x8C));
    private static readonly Brush TileBorderBrush = Freeze(Color.FromRgb(0xD0, 0xD7, 0xE5));

    private readonly DocumentView _editor;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly BackstageFrame _frame;

    public BackstageView(DocumentView editor, FileCommands file, BackstageActions actions)
    {
        _editor = editor;
        _file = file;
        _actions = actions;

        _frame = new BackstageFrame();
        _frame.SetAccent(AccentColor, AccentHoverColor, AccentSelectedColor, SeparatorColor);
        _frame.SetEntries(BuildEntries());
        // When the frame closes itself (Esc / back arrow / an action entry), collapse this wrapper too so
        // the document shows through, then notify the host. Hide() also funnels through here.
        _frame.Closed += () =>
        {
            Visibility = Visibility.Collapsed;
            _actions.OnClosed();
        };

        // Code-built control: no XAML, just hosts the shared frame edge-to-edge.
        Padding = new Thickness(0);
        Background = Brushes.White;
        Content = _frame;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Show the backstage, landing on the Info pane with live content.</summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        _frame.Show("Info");
    }

    /// <summary>Hide the backstage and return to the document (collapse happens via the frame's Closed event).</summary>
    public void Hide() => _frame.Hide();

    private System.Collections.Generic.IEnumerable<BackstageEntry> BuildEntries()
    {
        // Pane entries show content and stay highlighted; action entries fire a host callback and close.
        // The frame closes itself before invoking an action, so each callback just runs the command.
        yield return BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, BuildInfoPane);
        yield return BackstageEntry.Command("New", RibbonCommandIconKind.Insert, () => _actions.New());
        yield return BackstageEntry.Command("Open", RibbonCommandIconKind.GetData, () => _actions.Open());
        yield return BackstageEntry.Divider();
        yield return BackstageEntry.Command("Save", RibbonCommandIconKind.Save, () => _actions.Save());
        yield return BackstageEntry.Command("Save As", RibbonCommandIconKind.Save, () => _actions.SaveAs());
        yield return BackstageEntry.Command("Print", RibbonCommandIconKind.Print, () => _actions.Print());
        yield return BackstageEntry.Pane("Export", RibbonCommandIconKind.Share, BuildExportPane);
        yield return BackstageEntry.Pane("Recent", RibbonCommandIconKind.GetData, BuildRecentPane);
        yield return BackstageEntry.Pane("New from template", RibbonCommandIconKind.Grid, BuildNewPane);
        yield return BackstageEntry.Pane("Options", RibbonCommandIconKind.View, BuildOptionsPane, dockBottom: true);
        yield return BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, () => { }, dockBottom: true);
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
    // Document path + properties + statistics, an Edit-properties link, plus cheap doc actions.
    private UIElement BuildInfoPane()
    {
        _editor.CommitToModel();
        var model = _editor.Model;
        var stats = WordCount.Of(model);
        var properties = model.Properties;

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Info"));

        var path = _file.CurrentPath;
        panel.Children.Add(Field("Document", _file.DisplayName + (_file.IsDirty ? "  (unsaved changes)" : "")));
        panel.Children.Add(Field("Location", path ?? "Not saved yet"));

        panel.Children.Add(SubHeading("Properties"));
        panel.Children.Add(Field("Title", Or(properties.Title)));
        panel.Children.Add(Field("Author", Or(properties.Author)));
        panel.Children.Add(Field("Subject", Or(properties.Subject)));
        panel.Children.Add(Field("Keywords", Or(properties.Keywords)));

        var edit = LinkButton("Edit document properties…", () => { Hide(); _actions.EditProperties(); });
        edit.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(edit);

        panel.Children.Add(SubHeading("Statistics"));
        panel.Children.Add(Field("Words", stats.Words.ToString()));
        panel.Children.Add(Field("Characters", stats.CharactersWithSpaces.ToString()));
        panel.Children.Add(Field("Paragraphs", stats.Paragraphs.ToString()));

        return Scroll(panel);
    }

    // ── Export pane ────────────────────────────────────────────────────────────
    // No PDF/export back-end exists in FreeW yet, so this is an honest placeholder offering Save As.
    private UIElement BuildExportPane()
    {
        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Export"));
        panel.Children.Add(new TextBlock
        {
            Text = "Exporting to PDF or other formats is not available in this build yet. "
                 + "Use Save As to write a Word document (.docx).",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(LinkButton("Save As…", () => { Hide(); _actions.SaveAs(); }));
        return panel;
    }

    // ── Recent pane ────────────────────────────────────────────────────────────
    // Lists RecentFilesStore entries (name + path); a click opens via the host and closes the backstage.
    private UIElement BuildRecentPane()
    {
        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Recent"));

        var entries = _file.RecentEntries;
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No recent documents.",
                Foreground = MutedBrush,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return panel;
        }

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Cursor = System.Windows.Input.Cursors.Hand };

            item.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = LinkBrush,
                FontSize = 14
            });
            item.Children.Add(new TextBlock
            {
                Text = path,
                Foreground = MutedBrush,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            item.MouseLeftButtonUp += (_, _) => { Hide(); _actions.OpenPath(path); };
            panel.Children.Add(item);
        }

        return Scroll(panel);
    }

    // ── New pane ───────────────────────────────────────────────────────────────
    // A "Blank document" tile (the only template FreeW ships), styled like Office's New gallery.
    private UIElement BuildNewPane()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("New"));

        var gallery = new WrapPanel { Orientation = Orientation.Horizontal };
        gallery.Children.Add(TemplateTile("Blank document", () => { Hide(); _actions.New(); }));
        panel.Children.Add(gallery);

        panel.Children.Add(new TextBlock
        {
            Text = "More templates are not available in this build.",
            Foreground = MutedBrush,
            Margin = new Thickness(0, 18, 0, 0)
        });
        return panel;
    }

    // ── Options pane ───────────────────────────────────────────────────────────
    private UIElement BuildOptionsPane()
    {
        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Options"));
        panel.Children.Add(new TextBlock
        {
            Text = "FreeW does not have a dedicated Options dialog yet. Formatting, view, and document "
                 + "settings are available on the ribbon tabs.",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(Field("Data folder", _actions.DataFolder()));
        return panel;
    }

    // ── Small visual helpers ─────────────────────────────────────────────────────
    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeights.Light,
        Foreground = HeadingBrush,
        Margin = new Thickness(0, 0, 0, 18)
    };

    private static TextBlock SubHeading(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = HeadingBrush,
        Margin = new Thickness(0, 16, 0, 6)
    };

    private static UIElement Field(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 12 };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var content = new TextBlock
        {
            Text = value,
            Foreground = HeadingBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    // An Office-style New tile: a bordered card with a blank "page" preview and a caption under it.
    private static UIElement TemplateTile(string caption, Action onClick)
    {
        var preview = new Border
        {
            Width = 150,
            Height = 190,
            Background = Brushes.White,
            BorderBrush = TileBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(18),
                Background = Brushes.White,
                BorderBrush = Freeze(Color.FromRgb(0xE2, 0xE6, 0xEF)),
                BorderThickness = new Thickness(1)
            }
        };

        var stack = new StackPanel { Margin = new Thickness(0, 0, 18, 0), Cursor = System.Windows.Input.Cursors.Hand };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = HeadingBrush,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.MouseLeftButtonUp += (_, _) => onClick();
        return stack;
    }

    private static Button LinkButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Foreground = LinkBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static ScrollViewer Scroll(UIElement child) => new()
    {
        Content = child,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value!;

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The host callbacks the <see cref="BackstageView"/> drives. Every entry routes back to an existing
/// MainWindow command implementation (no file IO is reimplemented here).
/// </summary>
internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action Print,
    Action EditProperties,
    Action OnClosed,
    Func<string> DataFolder);
