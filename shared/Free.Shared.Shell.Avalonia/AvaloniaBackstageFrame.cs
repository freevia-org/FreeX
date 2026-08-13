using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaBackstageAccent(
    Color Sidebar,
    Color Hover,
    Color Selected,
    Color Separator);

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
    private Button? _selectedButton;

    public AvaloniaBackstageFrame(
        AvaloniaBackstageAccent accent,
        IEnumerable<SisterBackstageEntryPlan<Control>> entries,
        AvaloniaBackstageFrameChrome? chrome = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _accent = accent;
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
                Brushes.White),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = ToThickness(BackstageVisualContract.Frame.BackButtonPadding),
            FontSize = BackstageVisualContract.Frame.BackButtonFontSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
                Brushes.White));
        }
        content.Children.Add(new TextBlock
        {
            Text = entry.Label,
            Foreground = Brushes.White,
            FontSize = BackstageVisualContract.Frame.NavigationFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
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
        if (_selectedButton is not null && !ReferenceEquals(_selectedButton, button))
            _selectedButton.Background = Brushes.Transparent;
        button.Background = Brush(_accent.Selected);
        _selectedButton = button;
    }

    private void ApplyHoverChrome(Button button, Func<bool> isSelected)
    {
        button.PointerEntered += (_, _) =>
        {
            if (!isSelected())
                button.Background = Brush(_accent.Hover);
        };
        button.PointerExited += (_, _) =>
            button.Background = isSelected() ? Brush(_accent.Selected) : Brushes.Transparent;
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
            buttons[targetIndex].Focus();
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
