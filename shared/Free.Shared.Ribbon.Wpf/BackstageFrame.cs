using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// One entry on the Backstage sidebar. Two flavours:
///   • a <b>pane</b> entry (<see cref="ContentFactory"/> set) — selecting it highlights the entry and
///     swaps the content host to the factory's element; it stays selected.
///   • an <b>action</b> entry (<see cref="Action"/> set) — selecting it invokes the callback (e.g.
///     New / Open / Save) and the frame closes; it never stays banded.
/// An optional <see cref="Icon"/> draws a leading glyph (the FreeX sidebar look). A <see cref="Separator"/>
/// entry instead renders a thin divider line (no button) — used to group the rail like Word/Office.
/// </summary>
public sealed class BackstageEntry
{
    /// <summary>The visible label. Ignored for separators.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional leading glyph drawn left of the label.</summary>
    public RibbonCommandIconKind? Icon { get; init; }

    /// <summary>For a pane entry: builds the content shown when this entry is selected.</summary>
    public Func<UIElement>? ContentFactory { get; init; }

    /// <summary>For an action entry: invoked on select; the frame then closes.</summary>
    public Action? Action { get; init; }

    /// <summary>When true this entry is a thin divider, not a clickable button.</summary>
    public bool Separator { get; init; }

    /// <summary>When true the entry docks to the bottom of the rail (e.g. Options / Close).</summary>
    public bool DockBottom { get; init; }

    public static BackstageEntry Pane(string label, RibbonCommandIconKind? icon, Func<UIElement> content, bool dockBottom = false) =>
        new() { Label = label, Icon = icon, ContentFactory = content, DockBottom = dockBottom };

    public static BackstageEntry Command(string label, RibbonCommandIconKind? icon, Action action, bool dockBottom = false) =>
        new() { Label = label, Icon = icon, Action = action, DockBottom = dockBottom };

    public static BackstageEntry Divider() => new() { Separator = true };
}

/// <summary>
/// App-neutral full-window Backstage (File-screen) overlay, ported from FreeX's start-screen shell so
/// FreeW and FreeX can share it. It renders a coloured nav rail on the left — a back arrow at the top,
/// then the supplied <see cref="BackstageEntry"/> list (icon + label, hover band, accent selection
/// band, the FreeX Office-backstage look) — and a content host on the right that swaps to the selected
/// pane entry's element. Action entries fire their callback and close the frame.
///
/// The frame owns: sidebar styling (reuses <c>BackstageSidebarNavButton*</c> from
/// <c>SharedChromeResources.xaml</c>), <see cref="Show"/>/<see cref="Hide"/>, back-arrow + Esc to close,
/// and the accent colours (re-tintable per app). It reimplements no file IO — every action routes back
/// into a host callback. Dependency-light: WPF + Free.Shared.Ribbon only.
/// </summary>
public sealed class BackstageFrame : UserControl
{
    private readonly StackPanel _topNav;       // back arrow + top entries
    private readonly StackPanel _bottomNav;     // bottom-docked entries (Options / Close)
    private readonly Border _rail;
    private readonly ContentControl _content;
    private readonly List<(BackstageEntry Entry, Button Button)> _navButtons = new();

    private Button? _selectedButton;
    private string? _defaultPaneLabel;

    /// <summary>Raised after the frame hides (the host restores document focus / state).</summary>
    public event Action? Closed;

    public BackstageFrame()
    {
        // Be self-sufficient: merge the shared chrome dictionary into the frame's own scope so the
        // BackstageSidebar* styles/brushes resolve even if the host window hasn't merged it. Merging here
        // (rather than relying on a tree walk to the window) also lets SetAccent override the brush keys
        // locally without disturbing the app's copy.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Free.Shared.Ribbon.Wpf;component/SharedChromeResources.xaml", UriKind.Relative)
        });

        Visibility = Visibility.Collapsed;
        Background = Brushes.White;
        FocusVisualStyle = null;
        Focusable = true;

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The rail is a DockPanel so top entries flow from the top and bottom entries pin to the bottom.
        var railDock = new DockPanel { LastChildFill = true };

        var back = new Button { ToolTip = "Back (Esc)" };
        ApplyStyle(back, "BackstageSidebarBackButton");
        back.Content = BuildIcon(RibbonCommandIconKind.Previous, size: 20);
        back.Click += (_, _) => Hide();
        DockPanel.SetDock(back, Dock.Top);
        railDock.Children.Add(back);

        _bottomNav = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_bottomNav, Dock.Bottom);
        railDock.Children.Add(_bottomNav);

        _topNav = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        railDock.Children.Add(_topNav); // fills remaining space

        _rail = new Border { Background = ResolveSidebarBrush(), Child = railDock };
        Grid.SetColumn(_rail, 0);
        layout.Children.Add(_rail);

        _content = new ContentControl { Margin = new Thickness(40, 28, 40, 28) };
        Grid.SetColumn(_content, 1);
        layout.Children.Add(_content);

        Content = layout;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Re-tint the nav rail (e.g. FreeW's Word #2B579A). Pass the base, hover and selection colours; the
    /// frame keeps the FreeX navy/teal defaults when this is never called.
    /// </summary>
    public void SetAccent(Color sidebar, Color hover, Color selected, Color separator)
    {
        _rail.Background = Freeze(sidebar);
        Resources["ChromeBackstageSidebarHoverBrush"] = Freeze(hover);
        Resources["ChromeBackstageSidebarSelectedBrush"] = Freeze(selected);
        Resources["ChromeBackstageSidebarSeparatorBrush"] = Freeze(separator);
    }

    /// <summary>Replace the rail's entries. The first pane entry becomes the default landing pane.</summary>
    public void SetEntries(IEnumerable<BackstageEntry> entries)
    {
        _topNav.Children.Clear();
        _bottomNav.Children.Clear();
        _navButtons.Clear();
        _selectedButton = null;
        _defaultPaneLabel = null;

        foreach (var entry in entries)
        {
            var host = entry.DockBottom ? _bottomNav : _topNav;

            if (entry.Separator)
            {
                host.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Margin = new Thickness(0, 6, 0, 6),
                    Fill = ResolveSeparatorBrush()
                });
                continue;
            }

            var button = BuildNavButton(entry);
            _navButtons.Add((entry, button));
            host.Children.Add(button);

            if (entry.ContentFactory is not null && _defaultPaneLabel is null)
                _defaultPaneLabel = entry.Label;
        }
    }

    /// <summary>Show the overlay and land on the default pane (or <paramref name="paneLabel"/> if given).</summary>
    public void Show(string? paneLabel = null)
    {
        Visibility = Visibility.Visible;
        var target = paneLabel ?? _defaultPaneLabel;
        if (target is not null)
            SelectPane(target);
        Focus();
    }

    /// <summary>Hide the overlay and notify the host.</summary>
    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    // Select a pane entry by label: highlight it and swap the content host. No-op for unknown labels.
    private void SelectPane(string label)
    {
        foreach (var (entry, button) in _navButtons)
        {
            if (entry.Label == label && entry.ContentFactory is not null)
            {
                Activate(entry, button);
                return;
            }
        }
    }

    private Button BuildNavButton(BackstageEntry entry)
    {
        var button = new Button { Tag = entry };
        ApplyStyle(button, "BackstageSidebarNavButton");

        if (entry.Icon is { } kind)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(BuildIcon(kind, size: 22));
            row.Children.Add(new TextBlock { Text = entry.Label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
        }
        else
        {
            button.Content = entry.Label;
        }

        button.Click += (_, _) => Activate(entry, button);
        return button;
    }

    // Activate an entry: pane entries highlight + show content; action entries fire + close.
    private void Activate(BackstageEntry entry, Button button)
    {
        if (entry.ContentFactory is not null)
        {
            SetSelected(button);
            _content.Content = entry.ContentFactory();
            return;
        }

        if (entry.Action is not null)
        {
            Hide();
            entry.Action();
        }
    }

    // Paint the selected entry with the accent band; clear the previous selection.
    private void SetSelected(Button button)
    {
        if (ReferenceEquals(_selectedButton, button))
            return;
        if (_selectedButton is not null)
            ApplyStyle(_selectedButton, "BackstageSidebarNavButton");
        ApplyStyle(button, "BackstageSidebarNavButtonActive");
        _selectedButton = button;
    }

    private RibbonIcon BuildIcon(RibbonCommandIconKind kind, double size) => new()
    {
        Kind = kind,
        IconSize = size,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = kind == RibbonCommandIconKind.Previous ? new Thickness(0) : new Thickness(0, 0, 12, 0)
    };

    private void ApplyStyle(Button button, string key)
    {
        if (TryFindResource(key) is Style style)
            button.Style = style;
    }

    private Brush ResolveSidebarBrush() =>
        TryFindResource("ChromeBackstageSidebarBrush") as Brush ?? Freeze(Color.FromRgb(0x10, 0x25, 0x3A));

    private Brush ResolveSeparatorBrush() =>
        (Resources["ChromeBackstageSidebarSeparatorBrush"] as Brush)
        ?? TryFindResource("ChromeBackstageSidebarSeparatorBrush") as Brush
        ?? Freeze(Color.FromRgb(0x24, 0x44, 0x5E));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
