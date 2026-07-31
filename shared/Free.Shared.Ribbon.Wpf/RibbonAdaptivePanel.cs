using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Hosts one ribbon group and can swap between its full content and a collapsed single-button form
/// (icon + group label + chevron) that opens the full group in a popup. Collapsing is driven by
/// <see cref="RibbonAdaptivePanel"/> based on available width.
/// </summary>
public sealed class RibbonGroupHost : ContentControl
{
    /// <summary>Approximate width of the collapsed single-button form (button + spacing).</summary>
    public const double CollapsedWidth = 64;

    public int Priority { get; }
    public double FullWidth { get; set; }
    internal double LayoutWidth { get; set; }

    /// <summary>The group's display name (header). Exposed so group-discovery queries and test harnesses
    /// can identify the host without reaching into its private group model.</summary>
    public string GroupName => _group.Header;

    /// <summary>The full (expanded) group grid this host renders. Always the same instance regardless
    /// of whether the host is currently showing the collapsed button, so discovery can find the group
    /// even while collapsed.</summary>
    public FrameworkElement GroupContent => _full;

    public double MeasureFullWidth(Size availableSize)
    {
        _full.Measure(availableSize);
        return _full.DesiredSize.Width;
    }

    private readonly RibbonGroup _group;
    private readonly FrameworkElement _full;
    private readonly System.Func<FrameworkElement> _popupContentFactory;
    private readonly FrameworkElement _resourceHost;
    private readonly string? _collapsedKeyTip;
    private readonly System.Func<ContextMenu>? _collapsedMenuFactory;
    private FrameworkElement? _collapsedButton;
    private bool _collapsed;

    public RibbonGroupHost(
        RibbonGroup group,
        FrameworkElement full,
        System.Func<FrameworkElement> popupContentFactory,
        FrameworkElement resourceHost,
        string? collapsedKeyTip = null,
        System.Func<ContextMenu>? collapsedMenuFactory = null)
    {
        _group = group;
        _full = full;
        _popupContentFactory = popupContentFactory;
        _resourceHost = resourceHost;
        _collapsedKeyTip = collapsedKeyTip;
        _collapsedMenuFactory = collapsedMenuFactory;
        Priority = group.Priority;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = full;
    }

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
                return;
            _collapsed = value;
            if (value)
            {
                Width = CollapsedWidth;
                MinWidth = CollapsedWidth;
            }
            else
            {
                ClearValue(WidthProperty);
                ClearValue(MinWidthProperty);
            }
            Content = value ? (_collapsedButton ??= BuildCollapsedButton()) : _full;
        }
    }

    private FrameworkElement BuildCollapsedButton()
    {
        // Use the shared representative icon choice so collapsed groups tell the same story across renderers.
        var representativeIcon = RibbonCollapsedGroupPresentationPlanner.GetRepresentativeIcon(_group);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new RibbonIcon
        {
            Kind = representativeIcon.Icon.Kind,
            CommandName = representativeIcon.CommandName ?? string.Empty,
            IconSize = 36,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var caption = new TextBlock
        {
            Text = _group.Header,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 58,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(caption);
        stack.Children.Add(new TextBlock
        {
            Text = "▾",
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Opacity = 0.85
        });

        // Keep the rendered width just under RibbonGroupHost.CollapsedWidth (the value the fit decision
        // budgets per collapsed group) so the strip never edges over the viewport.
        var button = new Button { Width = 58, Content = stack };
        if (_resourceHost.TryFindResource("RibbonBtn") is Style style)
            button.Style = style;

        // Mark the collapsed button so keytip systems can treat it as a group overflow: it carries the
        // group's derived keytip + title and a menu of the group's commands.
        RibbonMetadata.SetRole(button, RibbonMetadataRole.CollapsedGroupButton);
        if (!string.IsNullOrEmpty(_collapsedKeyTip))
            RibbonTooltip.SetKeyTip(button, _collapsedKeyTip);
        if (!string.IsNullOrEmpty(_group.Header))
            RibbonTooltip.SetTitle(button, _group.Header);
        if (_collapsedMenuFactory is not null)
        {
            var contextMenu = _collapsedMenuFactory();
            ConfigureCollapsedGroupMenu(contextMenu, button, _resourceHost);
            button.ContextMenu = contextMenu;
            button.Click += (_, _) => contextMenu.IsOpen = true;
            return Wrap(button);
        }

        var popup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PlacementTarget = button };
        button.Click += (_, _) =>
        {
            popup.Child = new Border
            {
                Background = _resourceHost.TryFindResource("FreeXRibbonSurfaceBrush") as Brush ?? Brushes.White,
                BorderBrush = _resourceHost.TryFindResource("FreeXBorderBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = _popupContentFactory()
            };
            popup.IsOpen = true;
        };

        var container = new Grid { Width = CollapsedWidth, MinWidth = CollapsedWidth };
        container.Children.Add(button);
        container.Children.Add(popup);
        return container;
    }

    private static void ConfigureCollapsedGroupMenu(
        ContextMenu contextMenu,
        Button anchor,
        FrameworkElement resourceHost)
    {
        var contract = RibbonPopupInteractionContract.CollapsedGroup;
        var chrome = RibbonVisualMetrics.PopupChrome;
        contextMenu.PlacementTarget = anchor;
        contextMenu.Placement = contract.RepositionAtScreenEdge
            ? PlacementMode.Custom
            : contract.Placement switch
        {
            RibbonPopupPlacement.BelowAnchor => PlacementMode.Bottom,
            RibbonPopupPlacement.AboveAnchor => PlacementMode.Top,
            _ => PlacementMode.Bottom,
        };
        contextMenu.MinWidth = chrome.MinWidth;
        contextMenu.MaxWidth = chrome.MaxWidth;
        contextMenu.Padding = ToThickness(chrome.PopupPadding);
        contextMenu.Background = FindBrush(resourceHost, "ThemeRibbonSurfaceBrush", "FreeXRibbonSurfaceBrush", Brushes.White);
        contextMenu.BorderBrush = FindBrush(resourceHost, "ThemeRibbonBorderBrush", "FreeXBorderBrush", Brushes.Gray);
        contextMenu.BorderThickness = new Thickness(chrome.BorderThickness);
        contextMenu.Foreground = FindBrush(resourceHost, "ThemeNeutralTextBrush", "FreeXTextBrush", Brushes.Black);
        contextMenu.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 270,
            ShadowDepth = chrome.ShadowDepth,
            BlurRadius = chrome.ShadowBlurRadius,
            Opacity = chrome.ShadowOpacity,
        };
        contextMenu.SnapsToDevicePixels = true;
        foreach (var item in contextMenu.Items.OfType<MenuItem>())
        {
            item.MinHeight = chrome.ItemMinHeight;
            item.Padding = ToThickness(chrome.ItemPadding);
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        if (contract.RepositionAtScreenEdge)
        {
            contextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var screenAnchor = anchor.PointToScreen(new Point(0, 0));
                var result = RibbonPopupPlacementPlanner.Plan(
                    new RibbonPopupRect(screenAnchor.X, screenAnchor.Y, targetSize.Width, targetSize.Height),
                    new RibbonPopupRect(0, 0, popupSize.Width, popupSize.Height),
                    new RibbonPopupRect(
                        SystemParameters.WorkArea.Left,
                        SystemParameters.WorkArea.Top,
                        SystemParameters.WorkArea.Width,
                        SystemParameters.WorkArea.Height),
                    contract);
                return
                [
                    new CustomPopupPlacement(
                        new Point(result.X - screenAnchor.X, result.Y - screenAnchor.Y),
                        PopupPrimaryAxis.None),
                ];
            };
        }
        contextMenu.StaysOpen = false;
        contextMenu.Opened += (_, _) =>
        {
            if (!contract.FocusFirstEnabledItemOnOpen)
                return;

            var items = contextMenu.Items.OfType<MenuItem>().ToArray();
            var states = items
                .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
                .ToArray();
            var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
            if (index >= 0)
            {
                // ContextMenu opens in a separate Popup focus scope. Defer until that scope has been
                // presented so the first menu item receives real keyboard focus, not just logical focus.
                contextMenu.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        if (contextMenu.IsOpen)
                            Keyboard.Focus(items[index]);
                    }));
            }
        };
        contextMenu.Closed += (_, _) =>
        {
            if (contract.RestoreFocusToAnchorOnClose)
                anchor.Focus();
        };
        contextMenu.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Key == Key.Escape && contract.DismissOnEscape)
                {
                    contextMenu.IsOpen = false;
                    args.Handled = true;
                    return;
                }

                if (!contract.TraverseEnabledItems || args.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
                    return;

                var items = contextMenu.Items.OfType<MenuItem>().ToArray();
                var currentIndex = Array.FindIndex(items, item => ReferenceEquals(Keyboard.FocusedElement, item));
                if (currentIndex < 0)
                    return;

                var states = items
                    .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
                    .ToArray();
                var targetIndex = args.Key switch
                {
                    Key.Home => RibbonPopupInteractionPlanner.FindFirstFocusableItem(states),
                    Key.End => RibbonPopupInteractionPlanner.FindLastFocusableItem(states),
                    Key.Up => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, -1),
                    Key.Down => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, 1),
                    _ => -1,
                };
                if (targetIndex >= 0 && items[targetIndex].Focus())
                    args.Handled = true;
            }),
            handledEventsToo: true);
    }

    private static Thickness ToThickness(RibbonPopupInsets insets) =>
        new(insets.Left, insets.Top, insets.Right, insets.Bottom);

    private static Brush FindBrush(
        FrameworkElement resourceHost,
        string primaryKey,
        string fallbackKey,
        Brush fallback)
    {
        return resourceHost.TryFindResource(primaryKey) as Brush ??
            resourceHost.TryFindResource(fallbackKey) as Brush ??
            fallback;
    }

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        var grid = new Grid { Width = CollapsedWidth, MinWidth = CollapsedWidth };
        grid.Children.Add(element);
        return grid;
    }
}

/// <summary>
/// Lays ribbon group hosts left-to-right and, when the available width is insufficient, collapses the
/// lowest-priority groups to popup buttons first (Office behavior). Realtime: WPF re-measures on resize.
/// </summary>
public sealed class RibbonAdaptivePanel : Panel
{
    private const double GroupSpacing = 6;
    private const double MinimumUnusedWidthForReclaim = 320;
    private const double MinimumUnusedWidthRatioForReclaim = 0.35;

    // Contextual tabs are shown after the initial ribbon warm-up; refresh their full-width budget from
    // the preserved expanded group so a previously collapsed surface cannot under-plan and clip.
    public bool RefreshFullWidthsFromFullContent { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Cast<UIElement>().ToList();
        var hosts = children.OfType<RibbonGroupHost>().ToList();
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        var spacing = GroupSpacing * Math.Max(0, children.Count - 1);

        // Measure every child in its current state first so non-host chrome is current, then measure each
        // group's full content directly. The full-width budget must not depend on whether a previous pass
        // happened to leave that host collapsed or expanded; otherwise the same tab/width can produce
        // different collapse sets after different resize sequences.
        foreach (var child in children)
            child.Measure(infinite);
        foreach (var host in hosts)
        {
            host.FullWidth = host.MeasureFullWidth(infinite);
            host.LayoutWidth = host.Collapsed ? RibbonGroupHost.CollapsedWidth : host.DesiredSize.Width;
        }

        var nonHostWidth = children
            .Where(c => c is not RibbonGroupHost)
            .Sum(c => c.DesiredSize.Width);
        var available = ResolveAvailableWidth(this, availableSize.Width);
        var fitAvailable = double.IsInfinity(available) ? available : Math.Max(0, available - 4);

        // Decide the collapse set from the refreshed full widths through the shared renderer-neutral
        // full/collapsed policy, then apply only the groups whose state flips.
        if (!double.IsInfinity(fitAvailable))
        {
            var decisions = RibbonAdaptiveCollapsePolicy.Plan(
                fitAvailable,
                hosts
                    .Select(host => new RibbonAdaptiveCollapseGroup(
                        host.GroupName,
                        host.FullWidth,
                        RibbonGroupHost.CollapsedWidth,
                        host.Priority))
                    .ToList(),
                fixedChromeWidth: nonHostWidth + spacing);

            for (var index = 0; index < hosts.Count; index++)
                hosts[index].Collapsed = decisions[index].IsCollapsed;
        }

        // Re-measure the groups whose state just flipped (unchanged ones short-circuit).
        foreach (var child in children)
        {
            if (child is RibbonGroupHost { Collapsed: true } collapsedHost)
            {
                collapsedHost.Measure(new Size(RibbonGroupHost.CollapsedWidth, availableSize.Height));
                collapsedHost.LayoutWidth = RibbonGroupHost.CollapsedWidth;
            }
            else
            {
                child.Measure(infinite);
                if (child is RibbonGroupHost expandedHost)
                    expandedHost.LayoutWidth = expandedHost.DesiredSize.Width;
            }
        }

        var width = children.Sum(GetChildLayoutWidth) + spacing;
        if (!double.IsInfinity(fitAvailable))
        {
            foreach (var host in EnumerateCollapseCandidates(hosts).Where(h => !h.Collapsed))
            {
                if (width <= fitAvailable)
                    break;

                var previousWidth = GetChildLayoutWidth(host);
                host.Collapsed = true;
                host.Measure(new Size(RibbonGroupHost.CollapsedWidth, availableSize.Height));
                host.LayoutWidth = RibbonGroupHost.CollapsedWidth;
                width += RibbonGroupHost.CollapsedWidth - previousWidth;
            }

            foreach (var host in hosts
                         .OrderByDescending(h => h.Priority)
                         .Where(h => h.Collapsed))
            {
                if (!HasSevereUnusedWidth(width, fitAvailable))
                    break;

                var previousWidth = GetChildLayoutWidth(host);
                var remainingWidth = Math.Max(0, fitAvailable - (width - previousWidth));
                host.Collapsed = false;
                host.Measure(new Size(remainingWidth, availableSize.Height));
                host.LayoutWidth = host.DesiredSize.Width;

                var expandedWidth = GetChildLayoutWidth(host);
                if (width + expandedWidth - previousWidth <= fitAvailable)
                {
                    width += expandedWidth - previousWidth;
                    continue;
                }

                host.Collapsed = true;
                host.Measure(new Size(RibbonGroupHost.CollapsedWidth, availableSize.Height));
                host.LayoutWidth = RibbonGroupHost.CollapsedWidth;
            }
        }

        var height = children.Count > 0 ? children.Max(c => c.DesiredSize.Height) : 0;
        return new Size(double.IsInfinity(available) ? width : Math.Min(width, available), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var child in Children.Cast<UIElement>())
        {
            var w = GetChildLayoutWidth(child);
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + GroupSpacing;
        }

        return finalSize;
    }

    private static IEnumerable<RibbonGroupHost> EnumerateCollapseCandidates(IReadOnlyList<RibbonGroupHost> hosts) =>
        hosts
            .Select((Host, Index) => new { Host, Index })
            .OrderBy(entry => entry.Host.Priority)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Host);

    private static double GetChildLayoutWidth(UIElement child)
    {
        if (child is RibbonGroupHost host)
        {
            if (host.Collapsed)
                return RibbonGroupHost.CollapsedWidth;

            if (host.LayoutWidth > 0)
                return host.LayoutWidth;

            if (host.FullWidth > 0)
                return host.FullWidth;
        }

        if (child is System.Windows.Shapes.Rectangle { Width: var width and > 0 } &&
            !double.IsNaN(width) &&
            !double.IsInfinity(width))
        {
            return width;
        }

        return child.DesiredSize.Width;
    }

    private static bool HasSevereUnusedWidth(double currentWidth, double fitAvailable)
    {
        var unusedWidth = fitAvailable - currentWidth;
        var threshold = Math.Max(MinimumUnusedWidthForReclaim, fitAvailable * MinimumUnusedWidthRatioForReclaim);
        return unusedWidth >= threshold;
    }

    private static double ResolveAvailableWidth(FrameworkElement element, double measuredWidth)
    {
        if (!double.IsInfinity(measuredWidth))
            return measuredWidth;

        if (element.ActualWidth > 0)
            return element.ActualWidth;

        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is ScrollViewer { ViewportWidth: > 0 } scrollViewer)
                return scrollViewer.ViewportWidth;

            current = VisualTreeHelper.GetParent(current);
        }

        return double.PositiveInfinity;
    }
}
