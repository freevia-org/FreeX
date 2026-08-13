using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

public readonly record struct BackstageIconSpec(BackstageIconKind Kind, string? CommandName);

public sealed record BackstageFrameChrome(
    Uri ResourceDictionarySource,
    Func<BackstageIconSpec, double, Brush, FrameworkElement> CreateIcon,
    Action<DependencyObject, string>? SetKeyTip = null,
    Action<DependencyObject, string>? SetTooltipTitle = null,
    Action<DependencyObject, string>? SetTooltipDescription = null)
{
    public static BackstageFrameChrome Default { get; } = new(
        new Uri("/Free.Shared.Shell.Wpf;component/BackstageChromeResources.xaml", UriKind.Relative),
        CreateDefaultIcon);

    private static FrameworkElement CreateDefaultIcon(BackstageIconSpec icon, double size, Brush brush) =>
        new TextBlock
        {
            Text = DefaultGlyph(icon.Kind),
            Width = size,
            Height = size,
            Foreground = brush,
            FontSize = Math.Max(12, size - 4),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static string DefaultGlyph(BackstageIconKind kind) =>
        kind switch
        {
            BackstageIconKind.Previous => "\uE72B",
            BackstageIconKind.Save => "\uE74E",
            BackstageIconKind.Print => "\uE749",
            BackstageIconKind.Info => "i",
            BackstageIconKind.WindowClose => "\uE711",
            _ => "\uE10F"
        };
}

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

    /// <summary>Language-invariant identity shared with the renderer-neutral entry plan.</summary>
    public string? StableId { get; init; }

    /// <summary>Optional leading glyph drawn left of the label.</summary>
    public BackstageIconKind? Icon { get; init; }

    /// <summary>
    /// Optional command-icon slug (e.g. <c>"save-as"</c>) used to resolve a host-supplied rich icon (the
    /// FreeX/Word SVG for that name) for the rail glyph, falling back to <see cref="Icon"/>'s geometry when
    /// the host has no artwork. Lets the backstage reuse the same Office icons the ribbon does.
    /// </summary>
    public string? IconCommandName { get; init; }

    /// <summary>For a pane entry: builds the content shown when this entry is selected.</summary>
    public Func<UIElement>? ContentFactory { get; init; }

    /// <summary>For an action entry: invoked on select; the frame then closes.</summary>
    public Action? Action { get; init; }

    /// <summary>Whether a command dismisses the frame before its action runs.</summary>
    public bool DismissOnActivate { get; init; }

    /// <summary>When true this entry is a thin divider, not a clickable button.</summary>
    public bool Separator { get; init; }

    /// <summary>When true the entry docks to the bottom of the rail (e.g. Options / Close).</summary>
    public bool DockBottom { get; init; }

    /// <summary>Optional ribbon key-tip (Alt-access) badge applied to the nav button (FreeX parity).</summary>
    public string? KeyTip { get; init; }

    /// <summary>Optional automation id for the nav button (UI tests / accessibility tree).</summary>
    public string? AutomationId { get; init; }

    /// <summary>Optional automation name for the nav button; falls back to the label when unset.</summary>
    public string? AutomationName { get; init; }

    /// <summary>Optional automation help-text for the nav button (screen-reader description).</summary>
    public string? AutomationHelpText { get; init; }

    /// <summary>Optional rich-tooltip title (the bold first line of the FreeX hover card).</summary>
    public string? TooltipTitle { get; init; }

    /// <summary>Optional rich-tooltip description (the body line under the title).</summary>
    public string? TooltipDescription { get; init; }

    public static BackstageEntry Pane(string label, BackstageIconKind? icon, Func<UIElement> content, bool dockBottom = false,
        string? keyTip = null, string? automationId = null, string? automationName = null, string? automationHelpText = null,
        string? tooltipTitle = null, string? tooltipDescription = null, string? iconName = null,
        string? stableId = null) =>
        new()
        {
            Label = label, StableId = stableId, Icon = icon, IconCommandName = iconName, ContentFactory = content,
            DockBottom = dockBottom,
            KeyTip = keyTip, AutomationId = automationId, AutomationName = automationName, AutomationHelpText = automationHelpText,
            TooltipTitle = tooltipTitle, TooltipDescription = tooltipDescription
        };

    public static BackstageEntry Command(string label, BackstageIconKind? icon, Action action, bool dockBottom = false,
        string? keyTip = null, string? automationId = null, string? automationName = null, string? automationHelpText = null,
        string? tooltipTitle = null, string? tooltipDescription = null, string? iconName = null,
        string? stableId = null, bool dismissOnActivate = true) =>
        new()
        {
            Label = label, StableId = stableId, Icon = icon, IconCommandName = iconName, Action = action,
            DockBottom = dockBottom, DismissOnActivate = dismissOnActivate,
            KeyTip = keyTip, AutomationId = automationId, AutomationName = automationName, AutomationHelpText = automationHelpText,
            TooltipTitle = tooltipTitle, TooltipDescription = tooltipDescription
        };

    public static BackstageEntry Divider(bool dockBottom = false) => new() { Separator = true, DockBottom = dockBottom };
}

/// <summary>
/// App-neutral full-window Backstage (File-screen) overlay, ported from FreeX's start-screen shell so
/// FreeW and FreeX can share it. It renders a coloured nav rail on the left — a back arrow at the top,
/// then the supplied <see cref="BackstageEntry"/> list (icon + label, hover band, accent selection
/// band, the FreeX Office-backstage look) — and a content host on the right that swaps to the selected
/// pane entry's element. Action entries fire their callback and close the frame.
///
/// The frame owns: sidebar styling (reuses <c>BackstageSidebarNavButton*</c> from
/// <c>BackstageChromeResources.xaml</c>), <see cref="Show"/>/<see cref="Hide"/>, back-arrow + Esc to close,
/// and the accent colours (re-tintable per app). It reimplements no file IO — every action routes back
/// into a host callback. Dependency-light: WPF + Free.Shared.Ribbon only.
/// </summary>
public sealed class BackstageFrame : UserControl
{
    private readonly BackstageFrameSession<UIElement> _session = new();
    private readonly BackstageFrameChrome _chrome;
    private readonly StackPanel _topNav;       // back arrow + top entries
    private readonly StackPanel _bottomNav;     // bottom-docked entries (Options / Close)
    private readonly Border _rail;
    private readonly ContentControl _content;

    public bool IsOpen => _session.IsOpen;
    public string? CurrentEntryId => _session.CurrentEntryId;
    public string? CurrentPaneLabel => _session.CurrentPaneLabel;
    public UIElement? CurrentPaneContent => _content.Content as UIElement;
    public IReadOnlyList<SisterBackstageEntryPlan<UIElement>> Entries => _session.Entries;
    private readonly Button _back;              // top-of-rail back arrow (closes the overlay)
    private readonly List<(
        BackstageEntry Entry,
        Button Button,
        SisterBackstageEntryPlan<UIElement> Plan)> _navButtons = new();

    private Button? _selectedButton;

    /// <summary>Raised after the frame hides (the host restores document focus / state).</summary>
    public event Action? Closed;

    public BackstageFrame(BackstageFrameChrome? chrome = null)
    {
        _chrome = chrome ?? BackstageFrameChrome.Default;
        // Be self-sufficient: merge the shared chrome dictionary into the frame's own scope so the
        // BackstageSidebar* styles/brushes resolve even if the host window hasn't merged it. Merging here
        // (rather than relying on a tree walk to the window) also lets SetAccent override the brush keys
        // locally without disturbing the app's copy.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = _chrome.ResourceDictionarySource
        });

        Visibility = Visibility.Collapsed;
        Background = Brushes.White;
        FocusVisualStyle = null;
        Focusable = true;

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BackstageVisualContract.Frame.RailWidth) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The rail is a DockPanel so top entries flow from the top and bottom entries pin to the bottom.
        var railDock = new DockPanel { LastChildFill = true };

        _back = new Button { ToolTip = "Back (Esc)" };
        ApplyStyle(_back, "BackstageSidebarBackButton");
        _back.Padding = ToThickness(BackstageVisualContract.Frame.BackButtonPadding);
        _back.FontSize = BackstageVisualContract.Frame.BackButtonFontSize;
        _back.Content = BuildIcon(
            BackstageIconKind.Previous,
            BackstageVisualContract.Frame.BackButtonIconSize);
        _back.Click += (_, _) => Hide();
        DockPanel.SetDock(_back, Dock.Top);
        railDock.Children.Add(_back);

        _bottomNav = new StackPanel { Margin = ToThickness(BackstageVisualContract.Frame.BottomNavigationMargin) };
        DockPanel.SetDock(_bottomNav, Dock.Bottom);
        railDock.Children.Add(_bottomNav);

        _topNav = new StackPanel { Margin = ToThickness(BackstageVisualContract.Frame.TopNavigationMargin) };
        railDock.Children.Add(_topNav); // fills remaining space

        _rail = new Border { Background = ResolveSidebarBrush(), Child = railDock };
        Grid.SetColumn(_rail, 0);
        layout.Children.Add(_rail);

        _content = new ContentControl { Margin = ToThickness(BackstageVisualContract.Frame.ContentPadding) };
        Grid.SetColumn(_content, 1);
        layout.Children.Add(_content);

        Content = layout;

        KeyDown += OnKeyDown;
    }

    // Native key translation, visual-tree membership, and focus realization stay here. The portable
    // planner owns modifier, boundary, dismissal, and target-index semantics for both renderers.
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var buttons = RailButtons();
        var focusedIndex = -1;
        if (Keyboard.FocusedElement is DependencyObject focused && IsInsideRail(focused))
        {
            focusedIndex = Array.FindIndex(
                buttons,
                button => ReferenceEquals(button, focused) || button.IsKeyboardFocusWithin);
        }

        var plan = BackstageRailNavigationPlanner.Plan(
            ToNavigationKey(e.Key),
            Keyboard.Modifiers != ModifierKeys.None,
            focusedIndex,
            buttons.Length);
        if (!plan.IsHandled)
            return;

        if (plan.DismissFrame)
            Hide();
        else if (plan.TargetIndex is { } targetIndex)
            FocusButton(buttons[targetIndex]);
        e.Handled = true;
    }

    private Button[] RailButtons() =>
        new[] { _back }
            .Concat(_navButtons.Where(item => !item.Entry.DockBottom).Select(item => item.Button))
            .Concat(_navButtons.Where(item => item.Entry.DockBottom).Select(item => item.Button))
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

    private static void FocusButton(Button button)
    {
        // Set logical focus in the enclosing focus scope as well as keyboard focus, so focus lands
        // deterministically even when the host window is not the OS foreground window (e.g. in tests).
        var scope = FindFocusScope(button);
        if (scope is not null)
            FocusManager.SetFocusedElement(scope, button);
        button.Focus();
        Keyboard.Focus(button);
    }

    private static DependencyObject? FindFocusScope(DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (FocusManager.GetIsFocusScope(current))
                return current;
        }
        return null;
    }

    // True when the element lives under the nav rail (the coloured sidebar), so arrow navigation only
    // hijacks the keys there and leaves the content host alone.
    private bool IsInsideRail(DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, _rail))
                return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
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

    /// <summary>
    /// Override the padding around the content host. The frame defaults to <c>(40,28,40,28)</c> — right for
    /// FreeW's code-built panes that carry no padding of their own. A host whose pane elements already supply
    /// their own insets (e.g. FreeX hosting its existing XAML panes) can set this to <c>0</c> so the content
    /// lands exactly where it did before the migration, instead of being double-inset.
    /// </summary>
    public void SetContentPadding(Thickness padding) => _content.Margin = padding;

    /// <summary>Replace the rail's entries. The first pane entry becomes the default landing pane.</summary>
    public void SetEntries(IEnumerable<BackstageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var projected = entries
            .Select(entry => (Entry: entry, Plan: WpfBackstageEntryProjection.ToPlan(entry)))
            .ToArray();

        _session.SetEntries(projected.Select(item => item.Plan));
        _topNav.Children.Clear();
        _bottomNav.Children.Clear();
        _navButtons.Clear();
        _selectedButton = null;
        _content.Content = null;

        foreach (var (entry, plan) in projected)
        {
            var host = entry.DockBottom ? _bottomNav : _topNav;

            if (entry.Separator)
            {
                host.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Margin = ToThickness(BackstageVisualContract.Frame.SeparatorMargin),
                    Fill = ResolveSeparatorBrush()
                });
                continue;
            }

            var button = BuildNavButton(entry, plan);
            _navButtons.Add((entry, button, plan));
            host.Children.Add(button);
        }
    }

    /// <summary>
    /// Run a host-supplied decorator over every rail nav button (the back arrow plus each entry's button),
    /// pairing each with its <see cref="BackstageEntry"/> (the back arrow is passed with a <c>null</c>
    /// entry). Lets an app stamp its own attached properties on the buttons — e.g. FreeX mirrors the
    /// key-tip/title/description onto its own <c>RibbonTooltip</c> attached properties so its existing
    /// Alt-keytip overlay (which reads the app's attached property, not the shared one) lights up the rail.
    /// Neutral: the frame stays ignorant of the app's property types.
    /// </summary>
    public void DecorateNavButtons(Action<BackstageEntry?, Button> decorator)
    {
        decorator(null, _back);
        foreach (var (entry, button, _) in _navButtons)
            decorator(entry, button);
    }

    /// <summary>
    /// Apply optional FreeX-parity metadata (key-tip, automation id/name/help, rich tooltip) to the
    /// built-in top-of-rail Back arrow. All arguments are optional and null-guarded so FreeW — which never
    /// calls this — keeps its plain back arrow. Mirrors the metadata the hand-rolled FreeX <c>SsBackBtn</c>
    /// carried so Alt-keytips and the accessibility tree are unchanged after the migration.
    /// </summary>
    public void ConfigureBackButton(
        string? automationId = null,
        string? automationName = null,
        string? automationHelpText = null,
        string? toolTip = null,
        string? tooltipTitle = null,
        string? keyTip = null)
    {
        if (toolTip is { } tip)
            _back.ToolTip = tip;
        if (keyTip is { } badge)
            _chrome.SetKeyTip?.Invoke(_back, badge);
        if (tooltipTitle is { } title)
            _chrome.SetTooltipTitle?.Invoke(_back, title);
        if (automationId is { } id)
            System.Windows.Automation.AutomationProperties.SetAutomationId(_back, id);
        if (automationName is { } name)
            System.Windows.Automation.AutomationProperties.SetName(_back, name);
        if (automationHelpText is { } help)
            System.Windows.Automation.AutomationProperties.SetHelpText(_back, help);
    }

    /// <summary>
    /// Show the overlay and land on the default pane, or on the pane identified by
    /// <paramref name="paneLabelOrAutomationId"/> (its stable id, automation id, or label) when given. Addressing by
    /// automation id lets a host land on a pane language-invariantly (FreeX's localized labels change per UI
    /// language, but its automation ids — <c>BackstageInfoButton</c>, <c>BackstagePrintButton</c> — do not).
    /// </summary>
    public void Show(string? paneLabelOrAutomationId = null)
    {
        Visibility = Visibility.Visible;
        if (_session.Show(paneLabelOrAutomationId) is { } activation)
            ApplyActivation(activation);
        Focus();
    }

    /// <summary>Hide the overlay and notify the host.</summary>
    public void Hide()
    {
        _session.Hide();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    public bool TryActivateEntry(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        var entry = _session.FindEntry(idOrLabel);
        if (entry is null)
            return false;

        var rendered = FindRenderedEntry(entry);
        if (rendered is null || !rendered.Value.Button.IsVisible || !rendered.Value.Button.IsEnabled)
            return false;

        ApplyActivation(_session.Activate(entry), rendered.Value.Button);
        return true;
    }

    /// <summary>
    /// Move keyboard focus to a rail nav button identified by its <see cref="BackstageEntry.StableId"/>,
    /// <see cref="BackstageEntry.AutomationId"/>, or <see cref="BackstageEntry.Label"/>. Returns
    /// <c>false</c> when no entry matches.
    /// Used by hosts that need to land focus on a specific rail entry (e.g. FreeX's screenshot tour, which
    /// previously focused named rail buttons such as <c>SsNewNavBtn</c>). The match is case-sensitive on the
    /// automation id and case-insensitive on the label, mirroring how the rail is addressed elsewhere.
    /// </summary>
    public bool FocusEntry(string automationIdOrLabel)
    {
        var button = FindNavButton(automationIdOrLabel);
        if (button is null)
            return false;

        FocusButton(button);
        return true;
    }

    /// <summary>
    /// True when the rail nav button identified by <paramref name="automationIdOrLabel"/> currently holds
    /// keyboard focus. Lets a host assert focus returned to a specific entry without reaching into the
    /// private button list.
    /// </summary>
    public bool IsEntryFocused(string automationIdOrLabel) =>
        FindNavButton(automationIdOrLabel) is { } button && ReferenceEquals(Keyboard.FocusedElement, button);

    private Button? FindNavButton(string automationIdOrLabel)
    {
        var entry = _session.FindEntry(automationIdOrLabel);
        return entry is null ? null : FindRenderedEntry(entry)?.Button;
    }

    private Button BuildNavButton(
        BackstageEntry entry,
        SisterBackstageEntryPlan<UIElement> plan)
    {
        var button = new Button { Tag = entry };
        ApplyStyle(button, "BackstageSidebarNavButton");
        button.Padding = ToThickness(BackstageVisualContract.Frame.NavigationButtonPadding);
        button.FontSize = BackstageVisualContract.Frame.NavigationFontSize;

        // Render the label through AccessText so a mnemonic underscore (e.g. FreeX's "_Save"/"Save _As")
        // shows the access-key marker and participates in Alt-access, exactly like the hand-rolled rail did.
        // FreeW's labels carry no underscore, so AccessText renders them identically — this stays additive.
        if (entry.Icon is { } kind)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(BuildIcon(
                kind,
                BackstageVisualContract.Frame.NavigationIconSize,
                entry.IconCommandName));
            row.Children.Add(new AccessText { Text = entry.Label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
        }
        else
        {
            button.Content = new AccessText { Text = entry.Label };
        }

        // FreeX parity metadata — key-tip badge, rich-tooltip card and the accessibility tree. All
        // optional: FreeW's entries set none of these, so the guards keep its rail byte-for-byte the same.
        if (entry.KeyTip is { } keyTip)
            _chrome.SetKeyTip?.Invoke(button, keyTip);
        if (entry.TooltipTitle is { } title)
            _chrome.SetTooltipTitle?.Invoke(button, title);
        if (entry.TooltipDescription is { } description)
            _chrome.SetTooltipDescription?.Invoke(button, description);
        if (entry.AutomationId is { } automationId)
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        if (entry.AutomationName is { } automationName)
            System.Windows.Automation.AutomationProperties.SetName(button, automationName);
        if (entry.AutomationHelpText is { } automationHelpText)
            System.Windows.Automation.AutomationProperties.SetHelpText(button, automationHelpText);

        // Rail nav buttons are focusable tab-stops so Up/Down/Home/End arrow navigation can move focus
        // among them (see the KeyDown handler) the way FreeX's start-screen rail does.
        button.Focusable = true;
        KeyboardNavigation.SetTabNavigation(button, KeyboardNavigationMode.Continue);

        button.Click += (_, _) => ApplyActivation(_session.Activate(plan), button);
        return button;
    }

    private void ApplyActivation(
        BackstageFrameActivation<UIElement> activation,
        Button? button = null)
    {
        activation.Dispatch(
            paneContent =>
            {
                var targetButton = button ?? FindRenderedEntry(activation.Entry)?.Button
                    ?? throw new InvalidOperationException(
                        $"Backstage pane '{activation.Entry.Label}' is not rendered.");
                SetSelected(targetButton);
                _content.Content = paneContent;
            },
            () =>
            {
                Visibility = Visibility.Collapsed;
                Closed?.Invoke();
            });
    }

    private (BackstageEntry Entry, Button Button, SisterBackstageEntryPlan<UIElement> Plan)?
        FindRenderedEntry(SisterBackstageEntryPlan<UIElement> entry)
    {
        foreach (var item in _navButtons)
        {
            if (ReferenceEquals(item.Plan, entry))
                return item;
        }

        return null;
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

    // Builds a rail glyph. When a commandName is supplied, RibbonIcon routes through the host's command-icon
    // provider (the FreeX/Word SVG library) and recolours it white for the dark rail, falling back to the
    // kind geometry when the host has no artwork — so the backstage reuses the same Office icons the ribbon does.
    private FrameworkElement BuildIcon(BackstageIconKind kind, double size, string? commandName = null)
    {
        var icon = _chrome.CreateIcon(new BackstageIconSpec(kind, commandName), size, Brushes.White);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = kind == BackstageIconKind.Previous
            ? new Thickness(0)
            : new Thickness(0, 0, BackstageVisualContract.Frame.NavigationIconLabelGap, 0);
        return icon;
    }

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

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
