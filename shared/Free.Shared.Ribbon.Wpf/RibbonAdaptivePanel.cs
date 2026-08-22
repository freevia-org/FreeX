using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Hosts one ribbon group and can swap through the shared adaptive presentations before reaching a
/// collapsed single-button form (icon + group label + chevron) that opens the full group in a popup.
/// State selection is driven by <see cref="RibbonAdaptivePanel"/> based on available width.
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
    /// of whether the host is currently showing a compact or collapsed presentation, so discovery can
    /// find the group's authored full surface even while responsive layout is active.</summary>
    public FrameworkElement GroupContent => _full;

    public double MeasureWidth(RibbonAdaptiveGroupState state, Size availableSize)
    {
        if (!Supports(state))
            state = RibbonAdaptiveGroupState.Full;

        if (state == RibbonAdaptiveGroupState.Collapsed)
            return CollapsedWidth;

        var presentation = GetPresentation(state);
        presentation.Measure(availableSize);
        return presentation.DesiredSize.Width;
    }

    private readonly RibbonGroup _group;
    private readonly FrameworkElement _full;
    private readonly System.Func<RibbonAdaptiveGroupState, FrameworkElement> _presentationFactory;
    private readonly Dictionary<RibbonAdaptiveGroupState, FrameworkElement> _presentations = new();
    private readonly System.Func<FrameworkElement> _popupContentFactory;
    private readonly FrameworkElement _resourceHost;
    private readonly string? _collapsedKeyTip;
    private readonly System.Func<ContextMenu>? _collapsedMenuFactory;
    private FrameworkElement? _collapsedButton;
    private RibbonAdaptiveGroupState _layoutState;

    public RibbonGroupHost(
        RibbonGroup group,
        FrameworkElement full,
        System.Func<RibbonAdaptiveGroupState, FrameworkElement> presentationFactory,
        System.Func<FrameworkElement> popupContentFactory,
        FrameworkElement resourceHost,
        string? collapsedKeyTip = null,
        System.Func<ContextMenu>? collapsedMenuFactory = null)
    {
        _group = group;
        _full = full;
        _presentationFactory = presentationFactory;
        _presentations[RibbonAdaptiveGroupState.Full] = full;
        _popupContentFactory = popupContentFactory;
        _resourceHost = resourceHost;
        _collapsedKeyTip = collapsedKeyTip;
        _collapsedMenuFactory = collapsedMenuFactory;
        Priority = group.Priority;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = full;
    }

    public RibbonAdaptiveGroupState LayoutState
    {
        get => _layoutState;
        set
        {
            if (_layoutState == value)
                return;
            _layoutState = value;
            if (value == RibbonAdaptiveGroupState.Collapsed)
            {
                Width = CollapsedWidth;
                MinWidth = CollapsedWidth;
            }
            else
            {
                ClearValue(WidthProperty);
                ClearValue(MinWidthProperty);
            }
            Content = value == RibbonAdaptiveGroupState.Collapsed
                ? (_collapsedButton ??= BuildCollapsedButton())
                : GetPresentation(value);
        }
    }

    public bool Collapsed
    {
        get => LayoutState == RibbonAdaptiveGroupState.Collapsed;
        set => LayoutState = value ? RibbonAdaptiveGroupState.Collapsed : RibbonAdaptiveGroupState.Full;
    }

    private FrameworkElement GetPresentation(RibbonAdaptiveGroupState state)
    {
        if (state == RibbonAdaptiveGroupState.Full)
            return _full;

        if (_presentations.TryGetValue(state, out var presentation))
            return presentation;

        presentation = _presentationFactory(state);
        _presentations.Add(state, presentation);
        return presentation;
    }

    internal RibbonAdaptiveGroupState NextFallbackState()
    {
        foreach (var state in new[]
                 {
                     RibbonAdaptiveGroupState.SmallWithLabels,
                     RibbonAdaptiveGroupState.IconOnly,
                     RibbonAdaptiveGroupState.Collapsed
                 })
        {
            if ((int)state > (int)LayoutState && Supports(state))
                return state;
        }

        return LayoutState;
    }

    private bool Supports(RibbonAdaptiveGroupState state) =>
        state is RibbonAdaptiveGroupState.Full or RibbonAdaptiveGroupState.Collapsed ||
        _group.Sizing.EnableCompactPresentation &&
        _group.Sizing.SupportedVariants.Contains(state);

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
            Text = "\u25BE",
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
            RibbonWpfPopupAdapter.Configure(contextMenu, button, _resourceHost);
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

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        var grid = new Grid { Width = CollapsedWidth, MinWidth = CollapsedWidth };
        grid.Children.Add(element);
        return grid;
    }
}

/// <summary>
/// Lays ribbon group hosts left-to-right and, when the available width is insufficient, steps their
/// command presentations down before collapsing the lowest-priority groups to popup buttons. Realtime:
/// WPF re-measures on resize.
/// </summary>
public sealed class RibbonAdaptivePanel : Panel
{
    private const double GroupSpacing = 6;

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
        // group's adaptive presentations directly. The budget must not depend on which state a previous
        // pass selected; otherwise the same tab/width can produce different results after resizing.
        foreach (var child in children)
            child.Measure(infinite);
        foreach (var host in hosts)
        {
            host.FullWidth = host.MeasureWidth(RibbonAdaptiveGroupState.Full, infinite);
            host.LayoutWidth = host.MeasureWidth(host.LayoutState, infinite);
        }

        var nonHostWidth = children
            .Where(c => c is not RibbonGroupHost)
            .Sum(c => c.DesiredSize.Width);
        var available = ResolveMeasuredAvailableWidth(this, availableSize.Width);
        var fitAvailable = double.IsInfinity(available) ? available : Math.Max(0, available - 4);

        // Decide the complete adaptive state set from measured presentation widths through the shared,
        // renderer-neutral planner, then apply only the groups whose state flips.
        if (!double.IsInfinity(fitAvailable))
        {
            var orderedHosts = hosts
                .Select((host, index) => new { Host = host, Index = index })
                .OrderByDescending(entry => entry.Host.Priority)
                .ThenBy(entry => entry.Index)
                .ToList();
            var orderedStates = RibbonAdaptiveLayoutPlanner.Plan(
                fitAvailable,
                orderedHosts
                    .Select(entry => new RibbonAdaptiveGroup(
                        entry.Host.GroupName,
                        entry.Host.FullWidth,
                        entry.Host.MeasureWidth(RibbonAdaptiveGroupState.SmallWithLabels, infinite),
                        entry.Host.MeasureWidth(RibbonAdaptiveGroupState.IconOnly, infinite),
                        RibbonGroupHost.CollapsedWidth,
                        entry.Host.GroupName))
                    .ToList(),
                fixedChromeWidth: nonHostWidth + spacing);

            for (var index = 0; index < orderedHosts.Count; index++)
                orderedHosts[index].Host.LayoutState = orderedStates[index];
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
                    expandedHost.LayoutWidth = expandedHost.MeasureWidth(expandedHost.LayoutState, infinite);
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
                host.LayoutState = host.NextFallbackState();
                host.Measure(new Size(host.LayoutState == RibbonAdaptiveGroupState.Collapsed
                    ? RibbonGroupHost.CollapsedWidth
                    : double.PositiveInfinity, availableSize.Height));
                host.LayoutWidth = host.MeasureWidth(host.LayoutState, infinite);
                width += host.LayoutWidth - previousWidth;
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

    private static double ResolveMeasuredAvailableWidth(FrameworkElement element, double measuredWidth)
    {
        if (!double.IsInfinity(measuredWidth))
            return measuredWidth;

        if (element.ActualWidth > 0)
            return element.ActualWidth;

        for (var current = VisualTreeHelper.GetParent(element);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer { ViewportWidth: > 0 } scrollViewer)
                return scrollViewer.ViewportWidth;
        }

        return double.PositiveInfinity;
    }
}
