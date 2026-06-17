using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfContentControl = System.Windows.Controls.ContentControl;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// The Word-style Backstage view: the full-window "File" screen. A green vertical nav rail
/// (Word accent #2B579A) on the left with entries (Info, New, Open, Save, Save As, Print, Export,
/// Recent, Options, Close); a right content pane that swaps per selected entry.
///
/// The view owns no file IO of its own — every action routes back into the host's existing command
/// implementations through the <see cref="BackstageActions"/> callbacks (the same New/Open/Save/…
/// MainWindow already wires). It is a code-built <see cref="UserControl"/>, matching the rest of the
/// FreeW window's code-only UI style, and reuses the app's accent/typography vocabulary.
/// </summary>
internal sealed class BackstageView : UserControl
{
    // Word's File-screen accent and the darker selection band, matching the app's title-bar accent.
    private static readonly Brush AccentBrush = Freeze(Color.FromRgb(0x2B, 0x57, 0x9A));
    private static readonly Brush AccentSelectedBrush = Freeze(Color.FromRgb(0x1F, 0x43, 0x77));
    private static readonly Brush ContentBrush = Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush HeadingBrush = Freeze(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush MutedBrush = Freeze(Color.FromRgb(0x70, 0x70, 0x70));
    private static readonly Brush LinkBrush = Freeze(Color.FromRgb(0x2B, 0x57, 0x9A));

    private readonly DocumentView _editor;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly WpfContentControl _content;
    private readonly StackPanel _nav;
    private Button? _selectedNav;

    public BackstageView(DocumentView editor, FileCommands file, BackstageActions actions)
    {
        _editor = editor;
        _file = file;
        _actions = actions;

        Visibility = Visibility.Collapsed;
        Background = ContentBrush;
        FocusVisualStyle = null;

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _nav = BuildNavRail();
        var rail = new Border { Background = AccentBrush, Child = _nav };
        Grid.SetColumn(rail, 0);
        layout.Children.Add(rail);

        _content = new WpfContentControl { Margin = new Thickness(40, 28, 40, 28) };
        Grid.SetColumn(_content, 1);
        layout.Children.Add(_content);

        Content = layout;

        // Esc returns to the document; the view is focusable so it receives the key while shown.
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
    }

    /// <summary>Show the backstage and select the Info entry, refreshing its live content.</summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        Select("Info");
        Focus();
    }

    /// <summary>Hide the backstage and return to the document (via the host's restore callback).</summary>
    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _actions.OnClosed();
    }

    private StackPanel BuildNavRail()
    {
        var nav = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        // A back arrow (returns to the document), then the Word File-screen entries.
        var back = new Button
        {
            Content = "←",
            FontSize = 18,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 40,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            ToolTip = "Back (Esc)"
        };
        back.Click += (_, _) => Hide();
        nav.Children.Add(back);

        AddNav(nav, "Info", () => _content.Content = BuildInfoPane());
        AddNav(nav, "New", () => { Hide(); _actions.New(); });
        AddNav(nav, "Open", () => { Hide(); _actions.Open(); });
        AddNav(nav, "Save", () => { Hide(); _actions.Save(); });
        AddNav(nav, "Save As", () => { Hide(); _actions.SaveAs(); });
        AddNav(nav, "Print", () => { Hide(); _actions.Print(); });
        AddNav(nav, "Export", () => _content.Content = BuildExportPane());
        AddNav(nav, "Recent", () => _content.Content = BuildRecentPane());
        AddNav(nav, "Options", () => _content.Content = BuildOptionsPane());
        AddNav(nav, "Close", () => Hide());

        return nav;
    }

    private void AddNav(Panel host, string label, Action onSelected)
    {
        var button = new Button
        {
            Content = label,
            Tag = label,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Padding = new Thickness(20, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null
        };
        button.Click += (_, _) =>
        {
            SetSelected(button);
            onSelected();
        };
        host.Children.Add(button);
    }

    // Select a nav entry by its label (used by Show to land on Info).
    private void Select(string label)
    {
        foreach (var child in _nav.Children)
        {
            if (child is Button button && (button.Tag as string) == label)
            {
                SetSelected(button);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return;
            }
        }
    }

    // Paint the selected nav entry with the darker accent band; clear the previous one.
    private void SetSelected(Button button)
    {
        if (_selectedNav is not null)
            _selectedNav.Background = Brushes.Transparent;
        // Action-style entries (New/Open/Save/…) close the backstage, so they shouldn't stay banded.
        if (button.Tag as string is "Info" or "Export" or "Recent" or "Options")
        {
            button.Background = AccentSelectedBrush;
            _selectedNav = button;
        }
        else
        {
            _selectedNav = null;
        }
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
    // Document path + properties, plus cheap word/page/paragraph counts from the pure WordCount helper.
    private UIElement BuildInfoPane()
    {
        _editor.CommitToModel();
        var model = _editor.Model;
        var stats = WordCount.Of(model);
        var properties = model.Properties;

        var panel = new StackPanel();
        panel.Children.Add(Heading("Info"));

        var path = _file.CurrentPath;
        panel.Children.Add(Field("Document", _file.DisplayName + (_file.IsDirty ? " (unsaved changes)" : "")));
        panel.Children.Add(Field("Location", path ?? "Not saved yet"));

        panel.Children.Add(SubHeading("Properties"));
        panel.Children.Add(Field("Title", Or(properties.Title)));
        panel.Children.Add(Field("Author", Or(properties.Author)));
        panel.Children.Add(Field("Subject", Or(properties.Subject)));
        panel.Children.Add(Field("Keywords", Or(properties.Keywords)));

        panel.Children.Add(SubHeading("Statistics"));
        panel.Children.Add(Field("Words", stats.Words.ToString()));
        panel.Children.Add(Field("Characters", stats.CharactersWithSpaces.ToString()));
        panel.Children.Add(Field("Paragraphs", stats.Paragraphs.ToString()));

        var edit = LinkButton("Edit document properties…", () => { Hide(); _actions.EditProperties(); });
        edit.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(edit);

        return Scroll(panel);
    }

    // ── Export pane ────────────────────────────────────────────────────────────
    // No PDF/export back-end exists in FreeW yet, so this is an honest placeholder (heading + note)
    // rather than invented IO. Save As is offered as the available way to write the document out.
    private UIElement BuildExportPane()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading("Export"));
        panel.Children.Add(new TextBlock
        {
            Text = "Exporting to PDF or other formats is not available in this build yet. "
                 + "Use Save As to write a Word document (.docx).",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(LinkButton("Save As…", () => { Hide(); _actions.SaveAs(); }));
        return panel;
    }

    // ── Recent pane ────────────────────────────────────────────────────────────
    // Lists entries from the shared RecentFilesStore (the same source MainWindow uses); a click opens
    // the file through the host's existing OpenPath path and closes the backstage.
    private UIElement BuildRecentPane()
    {
        var panel = new StackPanel();
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
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Cursor = Cursors.Hand };

            var name = new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = LinkBrush,
                FontSize = 14
            };
            var location = new TextBlock
            {
                Text = path,
                Foreground = MutedBrush,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            item.Children.Add(name);
            item.Children.Add(location);
            item.MouseLeftButtonUp += (_, _) => { Hide(); _actions.OpenPath(path); };
            panel.Children.Add(item);
        }

        return Scroll(panel);
    }

    // ── Options pane ───────────────────────────────────────────────────────────
    // FreeW has no dedicated Options dialog; show a short placeholder describing where settings live,
    // plus the data-folder location the shared storage helpers resolve for FreeW.
    private UIElement BuildOptionsPane()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading("Options"));
        panel.Children.Add(new TextBlock
        {
            Text = "FreeW does not have a dedicated Options dialog yet. Formatting, view, and document "
                 + "settings are available on the ribbon tabs.",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(Field("Data folder", _actions.DataFolder()));
        return panel;
    }

    // ── Small visual helpers (reusing the app's accent / muted vocabulary) ───────
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
        Margin = new Thickness(0, 14, 0, 6)
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
            Cursor = Cursors.Hand,
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
