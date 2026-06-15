using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeX.Ribbon;

namespace FreeX.App.Host;

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

    private readonly RibbonGroup _group;
    private readonly FrameworkElement _full;
    private readonly System.Func<FrameworkElement> _popupContentFactory;
    private readonly FrameworkElement _resourceHost;
    private FrameworkElement? _collapsedButton;
    private bool _collapsed;

    public RibbonGroupHost(RibbonGroup group, FrameworkElement full, System.Func<FrameworkElement> popupContentFactory, FrameworkElement resourceHost)
    {
        _group = group;
        _full = full;
        _popupContentFactory = popupContentFactory;
        _resourceHost = resourceHost;
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
            Content = value ? (_collapsedButton ??= BuildCollapsedButton()) : _full;
        }
    }

    private FrameworkElement BuildCollapsedButton()
    {
        var firstIcon = _group.Controls.FirstOrDefault(c => c.Icon is not null)?.Icon?.Kind ?? RibbonCommandIconKind.Generic;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new RibbonIcon { Kind = firstIcon, IconSize = 22, HorizontalAlignment = HorizontalAlignment.Center });
        var caption = new TextBlock
        {
            Text = _group.Header,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 56,
            Margin = new Thickness(0, 2, 0, 0)
        };
        caption.Inlines.Add(new System.Windows.Documents.Run(" ▾") { FontSize = 8 });
        stack.Children.Add(caption);

        var button = new Button { Width = 60, Content = stack };
        if (_resourceHost.TryFindResource("RibbonBtn") is Style style)
            button.Style = style;

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

        var container = new Grid();
        container.Children.Add(button);
        container.Children.Add(popup);
        return container;
    }
}

/// <summary>
/// Lays ribbon group hosts left-to-right and, when the available width is insufficient, collapses the
/// lowest-priority groups to popup buttons first (Office behavior). Realtime: WPF re-measures on resize.
/// </summary>
public sealed class RibbonAdaptivePanel : Panel
{
    private const double GroupSpacing = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Cast<UIElement>().ToList();
        var hosts = children.OfType<RibbonGroupHost>().ToList();

        foreach (var host in hosts)
            host.Collapsed = false;

        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in children)
            child.Measure(infinite);
        foreach (var host in hosts)
            host.FullWidth = host.DesiredSize.Width;

        var total = children.Sum(c => c.DesiredSize.Width) + GroupSpacing * Math.Max(0, children.Count - 1);

        var available = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        foreach (var host in hosts.OrderBy(h => h.Priority))
        {
            if (total <= available)
                break;
            host.Collapsed = true;
            total += RibbonGroupHost.CollapsedWidth - host.FullWidth;
        }

        // Re-measure the controls whose collapsed/full state just changed, for arrangement.
        foreach (var child in children)
            child.Measure(infinite);

        var width = children.Sum(c => c.DesiredSize.Width) + GroupSpacing * Math.Max(0, children.Count - 1);
        var height = children.Count > 0 ? children.Max(c => c.DesiredSize.Height) : 0;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var child in Children.Cast<UIElement>())
        {
            var w = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + GroupSpacing;
        }

        return finalSize;
    }
}
