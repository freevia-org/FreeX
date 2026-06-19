using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Backstage;

/// <summary>
/// FreeP's Office-style Backstage (the full-window "File" screen), built on top of the shared, app-neutral
/// <see cref="BackstageFrame"/>. FreeP supplies the entries (Info / New / Open / Save / Save As / Recent /
/// Options / Close) and the content panes; the frame owns the coloured nav rail, selection/hover, the
/// back-arrow + Esc close, and the Office-backstage look. No file IO is reimplemented here — every action
/// routes back into the host's command implementations through <see cref="BackstageActions"/>. Mirrors
/// FreeW.Backstage.BackstageView.
/// </summary>
internal sealed class BackstageView : UserControl
{
    // FreeP's rail accent (PowerPoint-style brick/orange) and the darker selection/hover bands.
    private static readonly Color AccentColor = Color.FromRgb(0xB7, 0x47, 0x2A);
    private static readonly Color AccentSelectedColor = Color.FromRgb(0x8F, 0x37, 0x21);
    private static readonly Color AccentHoverColor = Color.FromRgb(0xC9, 0x5A, 0x3D);
    private static readonly Color SeparatorColor = Color.FromRgb(0xCE, 0x6A, 0x4F);

    private static readonly Brush HeadingBrush = Freeze(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush MutedBrush = Freeze(Color.FromRgb(0x70, 0x70, 0x70));
    private static readonly Brush LinkBrush = Freeze(Color.FromRgb(0xB7, 0x47, 0x2A));
    private static readonly Brush TileBorderBrush = Freeze(Color.FromRgb(0xD0, 0xD7, 0xE5));

    private readonly Func<Presentation> _getModel;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly BackstageFrame _frame;

    public BackstageView(Func<Presentation> getModel, FileCommands file, BackstageActions actions)
    {
        _getModel = getModel;
        _file = file;
        _actions = actions;

        _frame = new BackstageFrame();
        _frame.SetAccent(AccentColor, AccentHoverColor, AccentSelectedColor, SeparatorColor);
        _frame.SetEntries(BuildEntries());
        _frame.Closed += () =>
        {
            Visibility = Visibility.Collapsed;
            _actions.OnClosed();
        };

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

    private IEnumerable<BackstageEntry> BuildEntries()
    {
        yield return BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, BuildInfoPane, iconName: "info");
        yield return BackstageEntry.Command("New", RibbonCommandIconKind.Insert, () => _actions.New(), iconName: "new");
        yield return BackstageEntry.Command("Open", RibbonCommandIconKind.GetData, () => _actions.Open(), iconName: "open");
        yield return BackstageEntry.Divider();
        yield return BackstageEntry.Command("Save", RibbonCommandIconKind.Save, () => _actions.Save(), iconName: "save");
        yield return BackstageEntry.Command("Save As", RibbonCommandIconKind.Save, () => _actions.SaveAs(), iconName: "save-as");
        yield return BackstageEntry.Pane("Recent", RibbonCommandIconKind.GetData, BuildRecentPane, iconName: "recent");
        yield return BackstageEntry.Pane("New from template", RibbonCommandIconKind.Grid, BuildNewPane, iconName: "new");
        yield return BackstageEntry.Pane("Options", RibbonCommandIconKind.View, BuildOptionsPane, dockBottom: true, iconName: "options");
        yield return BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, () => { }, dockBottom: true, iconName: "close");
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
    private UIElement BuildInfoPane()
    {
        var model = _getModel();
        var properties = model.Properties;

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Info"));

        var path = _file.CurrentPath;
        panel.Children.Add(Field("Presentation", _file.DisplayName + (_file.IsDirty ? "  (unsaved changes)" : "")));
        panel.Children.Add(Field("Location", path ?? "Not saved yet"));

        panel.Children.Add(SubHeading("Properties"));
        panel.Children.Add(Field("Title", Or(properties.Title)));
        panel.Children.Add(Field("Author", Or(properties.Author)));
        panel.Children.Add(Field("Subject", Or(properties.Subject)));
        panel.Children.Add(Field("Keywords", Or(properties.Keywords)));

        panel.Children.Add(SubHeading("Statistics"));
        panel.Children.Add(Field("Slides", model.Slides.Count.ToString()));

        return Scroll(panel);
    }

    // ── Recent pane ────────────────────────────────────────────────────────────
    private UIElement BuildRecentPane()
    {
        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Recent"));

        var entries = _file.RecentEntries;
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No recent presentations.",
                Foreground = MutedBrush,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return panel;
        }

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Cursor = System.Windows.Input.Cursors.Hand };
            item.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = LinkBrush, FontSize = 14 });
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
    private UIElement BuildNewPane()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("New"));

        var gallery = new WrapPanel { Orientation = Orientation.Horizontal };
        gallery.Children.Add(TemplateTile("Blank presentation", () => { Hide(); _actions.New(); }));
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
        var options = _actions.CurrentOptions();

        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Options"));
        panel.Children.Add(new TextBlock
        {
            Text = "FreeP application settings. These persist between sessions.",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Field("Recent files kept", options.RecentFilesCap.ToString()));
        panel.Children.Add(Field("Default save format", options.DefaultSaveFormat));
        panel.Children.Add(Field(
            "UI language",
            string.IsNullOrEmpty(options.UiLanguage) ? "System default" : options.UiLanguage));
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

    private static UIElement TemplateTile(string caption, Action onClick)
    {
        var preview = new Border
        {
            Width = 190,
            Height = 150,
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
/// The host callbacks the <see cref="BackstageView"/> drives. Every entry routes to an existing MainWindow
/// command implementation (no file IO reimplemented here). Mirrors FreeW's BackstageActions.
/// </summary>
internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Func<FreePOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
