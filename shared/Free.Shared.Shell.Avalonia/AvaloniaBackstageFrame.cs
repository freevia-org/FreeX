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
    private readonly AvaloniaBackstageAccent _accent;
    private readonly AvaloniaBackstageFrameChrome _chrome;
    private readonly StackPanel _topNav = new();
    private readonly StackPanel _bottomNav = new();
    private readonly ContentControl _content = new();
    private readonly Button _backButton;
    private readonly List<(SisterBackstageEntryPlan<Control> Entry, Button Button)> _navButtons = [];
    private IReadOnlyList<SisterBackstageEntryPlan<Control>> _entries = [];
    private Button? _selectedButton;
    private string? _defaultPaneLabel;

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

    public bool IsOpen => IsVisible;

    public string? CurrentPaneLabel { get; private set; }

    public IReadOnlyList<SisterBackstageEntryPlan<Control>> Entries => _entries;

    public void Show(string? paneLabel = null)
    {
        IsVisible = true;
        var target = paneLabel ?? _defaultPaneLabel;
        if (target is not null)
            TryActivateEntry(target);
        _backButton.Focus();
    }

    public void Hide()
    {
        if (!IsVisible)
            return;

        IsVisible = false;
        Closed?.Invoke();
    }

    public Action ShowPane(string paneLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        return () => TryActivateEntry(paneLabel);
    }

    public bool TryActivateEntry(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var pair = _navButtons.FirstOrDefault(candidate =>
            string.Equals(candidate.Entry.Label, label, StringComparison.OrdinalIgnoreCase));
        if (pair.Entry is null)
            return false;

        Activate(pair.Entry, pair.Button);
        return true;
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
            Content = _chrome.CreateIcon(BackstageIconKind.Previous, "back", 20, Brushes.White),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 13),
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
        _entries = entries.ToArray();
        _topNav.Children.Clear();
        _bottomNav.Children.Clear();
        _navButtons.Clear();
        _selectedButton = null;
        _defaultPaneLabel = null;

        foreach (var entry in _entries)
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

            if (entry.Kind == SisterBackstageEntryKind.Pane && _defaultPaneLabel is null)
                _defaultPaneLabel = entry.Label;
        }
    }

    private Button CreateNavButton(SisterBackstageEntryPlan<Control> entry)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (entry.Icon is { } icon)
        {
            content.Children.Add(_chrome.CreateIcon(
                icon,
                entry.IconCommandName,
                22,
                Brushes.White));
        }
        content.Children.Add(new TextBlock
        {
            Text = entry.Label,
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = entry,
        };
        AutomationProperties.SetAutomationId(button, "BackstageNav_" + AutomationToken(entry.Label));
        AutomationProperties.SetName(button, entry.Label);
        ApplyHoverChrome(button, () => ReferenceEquals(_selectedButton, button));
        button.Click += (_, _) => Activate(entry, button);
        return button;
    }

    private void Activate(SisterBackstageEntryPlan<Control> entry, Button button)
    {
        switch (entry.Kind)
        {
            case SisterBackstageEntryKind.Pane:
                SetSelected(button);
                CurrentPaneLabel = entry.Label;
                _content.Content = entry.ContentFactory?.Invoke()
                    ?? throw new InvalidOperationException($"Pane '{entry.Label}' has no content factory.");
                break;
            case SisterBackstageEntryKind.Command:
                Hide();
                (entry.Action ?? throw new InvalidOperationException($"Command '{entry.Label}' has no action."))();
                break;
            case SisterBackstageEntryKind.Divider:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, null);
        }
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsVisible)
            return;

        if (HandleKey(e.Key))
            e.Handled = true;
    }

    public bool HandleKey(Key key)
    {
        if (!IsVisible)
            return false;

        if (key == Key.Escape)
        {
            Hide();
            return true;
        }

        if (key is not (Key.Up or Key.Down or Key.Home or Key.End))
            return false;

        var buttons = new[] { _backButton }
            .Concat(_navButtons.Select(pair => pair.Button))
            .ToArray();
        var current = Array.FindIndex(buttons, button => button.IsFocused);
        if (current < 0)
            return false;

        var target = key switch
        {
            Key.Home => 0,
            Key.End => buttons.Length - 1,
            Key.Up => Math.Max(0, current - 1),
            Key.Down => Math.Min(buttons.Length - 1, current + 1),
            _ => current,
        };
        buttons[target].Focus();
        return true;
    }

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

    private static string AutomationToken(string label) =>
        string.Concat(label.Where(char.IsLetterOrDigit));

    private static IBrush Brush(Color color) => new SolidColorBrush(color);

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
