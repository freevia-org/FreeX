using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Rail colours for the Avalonia Backstage frame. Mirrors the WPF <c>BackstageAccent</c>:
/// <paramref name="Foreground"/> is the ink for rail labels, icons and the back arrow and defaults to
/// white (the dark-rail look) when omitted.
/// </summary>
public sealed record AvaloniaBackstageAccent(
    Color Sidebar,
    Color Hover,
    Color Selected,
    Color Separator,
    Color? Foreground = null)
{
    /// <summary>
    /// Opt-in WPF rail-button states (<c>BackstageSidebarNavButton*</c> in BackstageChromeResources.xaml):
    /// a square, chrome-less plate whose only fills are <see cref="Hover"/> (pointer-over or keyboard
    /// focus) and <see cref="Selected"/>; no theme pointer-over/pressed plate, no press scale, no focus
    /// rectangle; the selected label is SemiBold; and the back button hugs its arrow instead of spanning
    /// the rail. Off by default so existing hosts keep the stock button chrome.
    /// </summary>
    public bool FlatRailChrome { get; init; }
}

public sealed record AvaloniaBackstageFrameChrome(
    Func<BackstageIconKind, string?, double, IBrush, Control> CreateIcon);

/// <summary>
/// Shared Avalonia realization of the Office-style Backstage overlay. Hosts supply neutral entry plans and
/// lazy pane controls; the frame owns rail layout, selection, command dismissal, back/Escape navigation,
/// scrolling, focus, and accent chrome.
/// </summary>
public sealed class AvaloniaBackstageFrame : UserControl
{
    private readonly BackstageFrameSession<Control> _session = new();
    private readonly AvaloniaBackstageAccent _accent;
    private readonly AvaloniaBackstageFrameChrome _chrome;
    private readonly StackPanel _topNav = new();
    private readonly StackPanel _bottomNav = new();
    private readonly ContentControl _content = new();
    private readonly Button _backButton;
    private readonly List<(SisterBackstageEntryPlan<Control> Entry, Button Button)> _navButtons = [];
    private readonly IBrush _railForeground;
    private Button? _selectedButton;
    private Button? _keyboardFocusedButton;

    // Chrome-less rail button template used when the accent opts into FlatRailChrome. The presenter is
    // deliberately left unnamed so the theme's PART_ContentPresenter pointer-over/pressed/disabled fills
    // do not apply; the frame paints the plate through Button.Background instead.
    private static readonly FuncControlTemplate<Button> FlatRailButtonTemplate = new((button, _) =>
    {
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
        presenter.Bind(Layoutable.HorizontalAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = button });
        presenter.Bind(Layoutable.VerticalAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = button });

        var border = new Border { Child = presenter };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = button });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = button });
        return border;
    });

    public AvaloniaBackstageFrame(
        AvaloniaBackstageAccent accent,
        IEnumerable<SisterBackstageEntryPlan<Control>> entries,
        AvaloniaBackstageFrameChrome? chrome = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _accent = accent;
        _railForeground = Brush(accent.Foreground ?? Colors.White);
        _chrome = chrome ?? new AvaloniaBackstageFrameChrome(CreateDefaultIcon);
        IsVisible = false;
        Background = Brushes.White;
        Focusable = true;
        AutomationProperties.SetAutomationId(this, "BackstageOverlay");

        _backButton = CreateBackButton();
        Content = BuildLayout();
        SetEntries(entries);

        AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public event Action? Closed;

    public bool IsOpen => _session.IsOpen;

    public string? CurrentPaneLabel => _session.CurrentPaneLabel;

    public string? CurrentEntryId => _session.CurrentEntryId;

    /// <summary>The currently-displayed pane's root control (null before any pane has been activated).</summary>
    public Control? CurrentPaneContent => _content.Content as Control;

    public IReadOnlyList<SisterBackstageEntryPlan<Control>> Entries => _session.Entries;

    public void Show(string? paneIdOrLabel = null)
    {
        IsVisible = true;
        if (_session.Show(paneIdOrLabel) is { } activation)
            ApplyActivation(activation);
        _backButton.Focus();
    }

    public void Hide()
    {
        if (!_session.Hide())
            return;

        IsVisible = false;
        Closed?.Invoke();
    }

    public Action ShowPane(string paneIdOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneIdOrLabel);
        return () => TryActivateEntry(paneIdOrLabel);
    }

    public bool TryActivateEntry(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        var entry = _session.FindEntry(idOrLabel);
        var button = entry is null ? null : FindButton(entry);
        if (entry is null || button is null)
            return false;
        if (!button.IsVisible || !button.IsEffectivelyEnabled)
            return false;

        ApplyActivation(_session.Activate(entry), button);
        return true;
    }

    public Button? GetEntryButton(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);
        var entry = _session.FindEntry(idOrLabel);
        return entry is null ? null : FindButton(entry);
    }

    private Grid BuildLayout()
    {
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{BackstageVisualContract.Frame.RailWidth},*"),
            Background = Brushes.White,
        };

        var railDock = new DockPanel
        {
            Background = Brush(_accent.Sidebar),
            LastChildFill = true,
        };

        DockPanel.SetDock(_backButton, Dock.Top);
        railDock.Children.Add(_backButton);

        _bottomNav.Margin = ToThickness(BackstageVisualContract.Frame.BottomNavigationMargin);
        DockPanel.SetDock(_bottomNav, Dock.Bottom);
        railDock.Children.Add(_bottomNav);

        var topScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _topNav,
        };
        _topNav.Margin = ToThickness(BackstageVisualContract.Frame.TopNavigationMargin);
        railDock.Children.Add(topScroll);

        Grid.SetColumn(railDock, 0);
        layout.Children.Add(railDock);

        var contentArea = AvaloniaBackstageChrome.CreateContentArea(
            new AvaloniaBackstageContentAreaSpec(_content, Brushes.White)
            {
                Padding = ToThickness(BackstageVisualContract.Frame.ContentPadding),
            });
        Grid.SetColumn(contentArea, 1);
        layout.Children.Add(contentArea);

        return layout;
    }

    private Button CreateBackButton()
    {
        var button = new Button
        {
            Content = _chrome.CreateIcon(
                BackstageIconKind.Previous,
                "back",
                BackstageVisualContract.Frame.BackButtonIconSize,
                _railForeground),
            Background = Brushes.Transparent,
            Foreground = _railForeground,
            BorderThickness = new Thickness(0),
            Padding = ToThickness(BackstageVisualContract.Frame.BackButtonPadding),
            FontSize = BackstageVisualContract.Frame.BackButtonFontSize,
            // WPF's BackstageSidebarBackButton is left-aligned, so its hover plate hugs the arrow.
            HorizontalAlignment = _accent.FlatRailChrome
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, "BackstageBackButton");
        AutomationProperties.SetName(button, "Back");
        ToolTip.SetTip(button, "Back (Esc)");
        button.Click += (_, _) => Hide();
        ApplyHoverChrome(button, isSelected: static () => false);
        return button;
    }

    private void SetEntries(IEnumerable<SisterBackstageEntryPlan<Control>> entries)
    {
        _session.SetEntries(entries);
        _topNav.Children.Clear();
        _bottomNav.Children.Clear();
        _navButtons.Clear();
        _selectedButton = null;
        _content.Content = null;

        foreach (var entry in _session.Entries)
        {
            var host = entry.DockBottom ? _bottomNav : _topNav;
            if (entry.Kind == SisterBackstageEntryKind.Divider)
            {
                host.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush(_accent.Separator),
                    Margin = ToThickness(BackstageVisualContract.Frame.SeparatorMargin),
                });
                continue;
            }

            var button = CreateNavButton(entry);
            _navButtons.Add((entry, button));
            host.Children.Add(button);
        }
    }

    private Button CreateNavButton(SisterBackstageEntryPlan<Control> entry)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = BackstageVisualContract.Frame.NavigationIconLabelGap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (entry.Icon is { } icon)
        {
            content.Children.Add(_chrome.CreateIcon(
                icon,
                entry.IconCommandName,
                BackstageVisualContract.Frame.NavigationIconSize,
                _railForeground));
        }
        content.Children.Add(new TextBlock
        {
            Text = entry.Label,
            Foreground = _railForeground,
            FontSize = BackstageVisualContract.Frame.NavigationFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = _railForeground,
            BorderThickness = new Thickness(0),
            Padding = ToThickness(BackstageVisualContract.Frame.NavigationButtonPadding),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = entry,
        };
        AutomationProperties.SetAutomationId(
            button,
            BackstageFrameEntryIdentity.From(entry).ResolveAutomationId());
        AutomationProperties.SetName(button, entry.AutomationName ?? entry.Label);
        if (entry.AutomationHelpText is { } automationHelpText)
            AutomationProperties.SetHelpText(button, automationHelpText);
        if (BuildTooltip(entry) is { } tooltip)
            ToolTip.SetTip(button, tooltip);
        ApplyHoverChrome(button, () => ReferenceEquals(_selectedButton, button));
        button.Click += (_, _) => ApplyActivation(_session.Activate(entry), button);
        return button;
    }

    private void ApplyActivation(
        BackstageFrameActivation<Control> activation,
        Button? button = null)
    {
        activation.Dispatch(
            paneContent =>
            {
                var targetButton = button ?? FindButton(activation.Entry)
                    ?? throw new InvalidOperationException(
                        $"Backstage pane '{activation.Entry.Label}' is not rendered.");
                SetSelected(targetButton);
                _content.Content = paneContent;
            },
            () =>
            {
                IsVisible = false;
                Closed?.Invoke();
            });
    }

    private void SetSelected(Button button)
    {
        var previous = _selectedButton;
        _selectedButton = button;
        if (previous is not null && !ReferenceEquals(previous, button))
        {
            previous.Background = ResolveRestingBackground(previous, isSelected: false);
            if (_accent.FlatRailChrome)
                previous.FontWeight = FontWeight.Normal;
        }

        button.Background = Brush(_accent.Selected);
        if (_accent.FlatRailChrome)
            button.FontWeight = FontWeight.SemiBold;
    }

    private void ApplyHoverChrome(Button button, Func<bool> isSelected)
    {
        if (_accent.FlatRailChrome)
            ApplyFlatRailChrome(button, isSelected);

        button.PointerEntered += (_, _) =>
        {
            if (!isSelected())
                button.Background = Brush(_accent.Hover);
        };
        button.PointerExited += (_, _) =>
            button.Background = ResolveRestingBackground(button, isSelected());
    }

    // WPF parity for BackstageSidebarNavButton: no theme plate, no press scale, no focus rectangle; a
    // keyboard-focused entry shows the hover fill instead (the IsKeyboardFocused trigger). Only Tab /
    // arrow-key focus counts, so the programmatic focus Show() gives the back button stays unpainted,
    // matching WPF where Show() focuses the frame rather than a rail button.
    private void ApplyFlatRailChrome(Button button, Func<bool> isSelected)
    {
        button.Template = FlatRailButtonTemplate;
        button.FocusAdorner = null;
        button.CornerRadius = new CornerRadius(0);
        // A local value outranks the theme's ":pressed" scale(0.98) transform setter.
        button.RenderTransform = null;

        button.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional))
                return;

            _keyboardFocusedButton = button;
            if (!isSelected())
                button.Background = Brush(_accent.Hover);
        };
        button.LostFocus += (_, _) =>
        {
            if (ReferenceEquals(_keyboardFocusedButton, button))
                _keyboardFocusedButton = null;
            if (!button.IsPointerOver)
                button.Background = ResolveRestingBackground(button, isSelected());
        };
    }

    private IBrush ResolveRestingBackground(Button button, bool isSelected)
    {
        if (isSelected)
            return Brush(_accent.Selected);
        return ReferenceEquals(_keyboardFocusedButton, button)
            ? Brush(_accent.Hover)
            : Brushes.Transparent;
    }

    private Button? FindButton(SisterBackstageEntryPlan<Control> entry) =>
        _navButtons.FirstOrDefault(pair => ReferenceEquals(pair.Entry, entry)).Button;

    private static string? BuildTooltip(SisterBackstageEntryPlan<Control> entry) =>
        (entry.TooltipTitle, entry.TooltipDescription) switch
        {
            (null, null) => null,
            ({ } title, null) => title,
            (null, { } description) => description,
            ({ } title, { } description) => $"{title}\n{description}",
        };

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsVisible)
            return;

        if (HandleKey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    public bool HandleKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (!IsVisible)
            return false;

        var buttons = RailButtons();
        var current = Array.FindIndex(buttons, button => button.IsFocused);
        var plan = BackstageRailNavigationPlanner.Plan(
            ToNavigationKey(key),
            modifiers != KeyModifiers.None,
            current,
            buttons.Length);
        if (!plan.IsHandled)
            return false;

        if (plan.DismissFrame)
            Hide();
        else if (plan.TargetIndex is { } targetIndex)
        {
            if (_accent.FlatRailChrome)
                buttons[targetIndex].Focus(NavigationMethod.Directional);
            else
                buttons[targetIndex].Focus();
        }
        return true;
    }

    private Button[] RailButtons() =>
        new[] { _backButton }
            .Concat(_navButtons.Where(pair => !pair.Entry.DockBottom).Select(pair => pair.Button))
            .Concat(_navButtons.Where(pair => pair.Entry.DockBottom).Select(pair => pair.Button))
            .ToArray();

    private static BackstageRailNavigationKey ToNavigationKey(Key key) => key switch
    {
        Key.Escape => BackstageRailNavigationKey.Escape,
        Key.Home => BackstageRailNavigationKey.Home,
        Key.End => BackstageRailNavigationKey.End,
        Key.Up => BackstageRailNavigationKey.Up,
        Key.Down => BackstageRailNavigationKey.Down,
        _ => BackstageRailNavigationKey.Other,
    };

    private static Control CreateDefaultIcon(
        BackstageIconKind kind,
        string? commandName,
        double size,
        IBrush foreground) =>
        new TextBlock
        {
            Text = kind == BackstageIconKind.Previous ? "<" : "\u25A1",
            Width = size,
            Height = size,
            Foreground = foreground,
            FontSize = Math.Max(12, size - 4),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static IBrush Brush(Color color) => new SolidColorBrush(color);

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
