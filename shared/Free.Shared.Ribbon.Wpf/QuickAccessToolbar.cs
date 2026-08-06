using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// App-neutral description of one Quick Access Toolbar button: a stable command id/key, the label/tooltip
/// to surface, the ribbon icon glyph to draw, an optional automation id (defaults to the command id) and
/// optional initial enabled/checked state. This is pure data — no FreeX/FreeW specifics and no WPF chrome —
/// so both apps (and a future Avalonia port) can share the same descriptor and only differ in rendering.
/// </summary>
public sealed record QuickAccessToolbarItem(
    string CommandId,
    string Tooltip,
    RibbonCommandIconKind IconKind)
{
    /// <summary>Automation id for the rendered button. Defaults to <see cref="CommandId"/> when null.</summary>
    public string? AutomationId { get; init; }

    /// <summary>Initial enabled state applied when the button is built.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Initial checked (pressed/toggled) state applied when the button is built.</summary>
    public bool IsChecked { get; init; }

    /// <summary>The automation id actually used: the explicit <see cref="AutomationId"/> or the command id.</summary>
    public string ResolvedAutomationId => string.IsNullOrEmpty(AutomationId) ? CommandId : AutomationId!;
}

/// <summary>
/// Per-render options for <see cref="QuickAccessToolbarRenderer"/>. All have family-default values so a
/// host can render the FreeX/FreeW title-bar QAT look (flat white-on-navy 26x22 icon buttons using the
/// shared <c>ChromeFlatButtonStyle</c>) with no configuration, and override only where it differs.
/// </summary>
public sealed record QuickAccessToolbarRenderOptions
{
    /// <summary>Resource key of the button style resolved off the host (FindResource). Default: the shared
    /// flat chrome button style.</summary>
    public string ButtonStyleKey { get; init; } = "ChromeFlatButtonStyle";

    /// <summary>Brush key resolved off the host for the glyph foreground. When null, <see cref="Foreground"/>
    /// (default white) is used directly. Lets a host swap the icon colour per surface (e.g. dark text on a
    /// below-ribbon light panel).</summary>
    public string? ForegroundResourceKey { get; init; }

    /// <summary>Glyph foreground used when <see cref="ForegroundResourceKey"/> is null or unresolved.</summary>
    public Brush Foreground { get; init; } = Brushes.White;

    public double ButtonWidth { get; init; } = 26;
    public double ButtonHeight { get; init; } = 22;
    public double IconSize { get; init; } = 16;
    public Thickness ButtonMargin { get; init; } = new(0, 0, 1, 0);
    public Thickness ButtonPadding { get; init; } = new(0);

    /// <summary>Optional explicit font size for the button. When null the style/default applies.</summary>
    public double? FontSize { get; init; }

    /// <summary>Whether to set the WPF <see cref="FrameworkElement.ToolTip"/> to the descriptor tooltip.
    /// Default true. A host that surfaces tooltips through a richer mechanism (e.g. <see cref="RibbonTooltip"/>)
    /// sets this false and applies its own in <see cref="CustomizeButton"/>.</summary>
    public bool SetWpfToolTip { get; init; } = true;

    /// <summary>Whether each button is marked hit-test-visible in the WindowChrome caption (so clicks land
    /// while WindowChrome owns the title bar). Default true (title-bar QAT). Set false for a below-ribbon QAT.</summary>
    public bool HitTestVisibleInChrome { get; init; } = true;

    /// <summary>Whether to register the button's name via <see cref="FrameworkElement.Name"/> so it can be
    /// resolved by name. Default true.</summary>
    public bool SetElementName { get; init; } = true;

    /// <summary>Whether the renderer attaches the <c>Click</c> handler that routes to the click callback.
    /// Default true. A host that needs the raw sender/args (FreeX forwards to its existing <c>*_Click</c>
    /// handlers) sets this false and wires the click itself in <see cref="CustomizeButton"/>.</summary>
    public bool WireClick { get; init; } = true;

    /// <summary>Builds the glyph element for a button. Defaults to the shared <see cref="RibbonIcon"/>. A host
    /// can supply its own factory (e.g. FreeX's app-local RibbonIcon with its command→glyph map) so the icons
    /// match the rest of that app verbatim.</summary>
    public Func<RibbonCommandIconKind, double, Brush, FrameworkElement>? IconFactory { get; init; }

    /// <summary>Optional per-button hook invoked after the renderer wires up the common parts (style, icon,
    /// hit-test, automation, click, state). Lets a host attach app-specific decorations (key tips, metadata,
    /// context menus, history flyouts) without re-implementing the shared construction.</summary>
    public Action<QuickAccessToolbarItem, Button>? CustomizeButton { get; init; }
}

/// <summary>
/// A handle returned from <see cref="QuickAccessToolbarRenderer.Render"/> for updating the rendered buttons'
/// enabled/checked state per command id after the fact, without the host holding onto the WPF buttons itself.
/// </summary>
public sealed class QuickAccessToolbarHandle
{
    // Stores a list of buttons per command id so that duplicate CommandIds (multiple QAT descriptors
    // sharing the same id) are both tracked and updated by SetEnabled, rather than the last one
    // silently overwriting the earlier entry.
    private readonly Dictionary<string, List<Button>> _byCommandId;
    private readonly List<Button> _allButtons;

    internal QuickAccessToolbarHandle(Dictionary<string, List<Button>> byCommandId, List<Button> allButtons)
    {
        _byCommandId = byCommandId;
        _allButtons = allButtons;
    }

    /// <summary>The rendered buttons, in render order.</summary>
    public IReadOnlyCollection<Button> Buttons => _allButtons;

    /// <summary>
    /// Returns the first button registered for <paramref name="commandId"/>, or false if none.
    /// For the common (unique-id) case this is always the only button.
    /// </summary>
    public bool TryGetButton(string commandId, out Button button)
    {
        if (_byCommandId.TryGetValue(commandId, out var list) && list.Count > 0)
        {
            button = list[0];
            return true;
        }
        button = null!;
        return false;
    }

    /// <summary>Sets the enabled state of ALL buttons for <paramref name="commandId"/> (no-op if unknown).</summary>
    public void SetEnabled(string commandId, bool isEnabled)
    {
        if (!_byCommandId.TryGetValue(commandId, out var list))
            return;
        foreach (var button in list)
        {
            if (button.IsEnabled != isEnabled)
                button.IsEnabled = isEnabled;
        }
    }
}

/// <summary>
/// Shared WPF render helper for the title-bar Quick Access Toolbar. Takes a host <see cref="Panel"/>, a list
/// of neutral <see cref="QuickAccessToolbarItem"/> descriptors and an <c>Action&lt;string commandId&gt;</c>
/// click callback, then builds the QAT <see cref="Button"/>s with the shared chrome flat-button style, the
/// requested glyph, hit-test-in-chrome, and automation id/name — the construction FreeX and FreeW previously
/// duplicated. The descriptor/model stays platform-neutral; only this renderer is WPF.
/// </summary>
public static class QuickAccessToolbarRenderer
{
    public static QuickAccessToolbarHandle Render(
        Panel host,
        FrameworkElement resourceHost,
        IEnumerable<QuickAccessToolbarItem> items,
        Action<string>? onClick,
        QuickAccessToolbarRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(resourceHost);
        ArgumentNullException.ThrowIfNull(items);
        options ??= new QuickAccessToolbarRenderOptions();

        var byCommandId = new Dictionary<string, List<Button>>(StringComparer.Ordinal);
        var allButtons = new List<Button>();
        foreach (var item in items)
        {
            var button = BuildButton(resourceHost, item, onClick, options);
            host.Children.Add(button);
            allButtons.Add(button);
            if (!byCommandId.TryGetValue(item.CommandId, out var list))
            {
                list = new List<Button>(1);
                byCommandId[item.CommandId] = list;
            }
            list.Add(button);
        }

        return new QuickAccessToolbarHandle(byCommandId, allButtons);
    }

    /// <summary>
    /// Builds (but does not add to any panel) a single QAT button from a descriptor, wiring the shared
    /// construction: style, glyph, dimensions, hit-test-in-chrome, automation id/name, name registration,
    /// initial enabled/checked state and the click callback. A host that owns more elaborate placement
    /// (FreeX adds a history flyout button beside Undo/Redo) can call this for the primary button and add
    /// its own extras around it.
    /// </summary>
    public static Button BuildButton(
        FrameworkElement resourceHost,
        QuickAccessToolbarItem item,
        Action<string>? onClick,
        QuickAccessToolbarRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resourceHost);
        ArgumentNullException.ThrowIfNull(item);
        options ??= new QuickAccessToolbarRenderOptions();

        var foreground = ResolveForeground(resourceHost, options);
        var iconFactory = options.IconFactory ?? DefaultIconFactory;

        var button = new Button
        {
            Name = item.ResolvedAutomationId,
            Width = options.ButtonWidth,
            Height = options.ButtonHeight,
            Margin = options.ButtonMargin,
            Padding = options.ButtonPadding,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = item.IsEnabled,
            Content = iconFactory(item.IconKind, options.IconSize, foreground)
        };

        if (options.SetWpfToolTip)
            button.ToolTip = item.Tooltip;
        if (options.FontSize is { } fontSize)
            button.FontSize = fontSize;

        if (resourceHost.TryFindResource(options.ButtonStyleKey) is Style style)
            button.Style = style;

        WindowChrome.SetIsHitTestVisibleInChrome(button, options.HitTestVisibleInChrome);
        AutomationProperties.SetAutomationId(button, item.ResolvedAutomationId);
        AutomationProperties.SetName(button, item.Tooltip);

        if (options.SetElementName)
            TryRegisterName(resourceHost, item.ResolvedAutomationId, button);

        if (options.WireClick && onClick is not null)
            button.Click += (_, _) => onClick(item.CommandId);

        options.CustomizeButton?.Invoke(item, button);
        return button;
    }

    private static Brush ResolveForeground(FrameworkElement resourceHost, QuickAccessToolbarRenderOptions options)
    {
        if (!string.IsNullOrEmpty(options.ForegroundResourceKey) &&
            resourceHost.TryFindResource(options.ForegroundResourceKey) is Brush brush)
            return brush;

        return options.Foreground;
    }

    private static FrameworkElement DefaultIconFactory(RibbonCommandIconKind kind, double size, Brush brush) =>
        new RibbonIcon
        {
            Kind = kind,
            IconSize = size,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    // Register the button name on the resource host's namescope when one exists, so it can be resolved by
    // name later. Swallows the duplicate-name / no-namescope cases the host already guards against.
    private static void TryRegisterName(FrameworkElement resourceHost, string name, FrameworkElement element)
    {
        if (string.IsNullOrEmpty(name))
            return;

        var scope = NameScope.GetNameScope(resourceHost);
        if (scope is null)
            return;

        try
        {
            if (scope.FindName(name) is null)
                scope.RegisterName(name, element);
        }
        catch (ArgumentException)
        {
            // Name already registered elsewhere or invalid for this scope — leave it to the host.
        }
    }
}
