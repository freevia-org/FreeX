using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// WPF realization of a declarative <see cref="RibbonTab"/>. Builds the same visual vocabulary
/// the hand-authored ribbon XAML used (RibbonGroupPanel grids, group dividers, large/small
/// buttons, group-label borders, <see cref="RibbonIcon"/> glyphs) so a generated tab reproduces
/// the existing look. Behavior is resolved through the command registry by <c>CommandId</c>.
/// </summary>
public static class RibbonWpfRenderer
{
    private const double SmallRowHeight = 22;
    private const int MaxSmallRowsPerColumn = 3;

    /// <summary>Builds the scrollable content panel for one tab (the body shown under the tab header).</summary>
    public static FrameworkElement BuildTabContent(
        RibbonTab tab,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry = null)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinHeight = 88
        };

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildDivider(resourceHost));
            panel.Children.Add(BuildGroup(group, resourceHost, registry));
            first = false;
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };

        return new Border
        {
            Background = Brush(resourceHost, "FreeXRibbonSurfaceBrush", Brushes.White),
            Padding = new Thickness(0, 4, 0, 0),
            Child = scroller
        };
    }

    private static FrameworkElement BuildGroup(
        RibbonGroup group,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry)
    {
        var grid = new Grid();
        ApplyStyle(grid, resourceHost, "RibbonGroupPanel");
        RibbonMetadata.SetCatalogId(grid, group.Id);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        var content = BuildGroupContent(group, resourceHost, registry);
        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        var labelBorder = new Border();
        ApplyStyle(labelBorder, resourceHost, "RibbonGroupLabelBorder");
        var label = new TextBlock { Text = group.Header };
        ApplyStyle(label, resourceHost, "GroupLbl");
        labelBorder.Child = label;
        Grid.SetRow(labelBorder, 1);
        grid.Children.Add(labelBorder);

        return grid;
    }

    private static FrameworkElement BuildGroupContent(
        RibbonGroup group,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 2, 0)
        };

        StackPanel? smallColumn = null;
        var columnIsCombo = false;

        void FlushColumn()
        {
            if (smallColumn is not null)
            {
                row.Children.Add(smallColumn);
                smallColumn = null;
            }
        }

        foreach (var control in group.Controls)
        {
            switch (control)
            {
                case RibbonSeparator:
                    FlushColumn();
                    row.Children.Add(BuildInlineSpacer());
                    break;

                case { PreferredLayout: RibbonCommandLayoutKind.Large }:
                    FlushColumn();
                    row.Children.Add(BuildLargeControl(control, resourceHost, registry));
                    break;

                default:
                    var isCombo = control is RibbonComboBox;
                    // Keep comboboxes and buttons in separate columns so a group reads like
                    // Excel's (e.g. Font: name+size stacked, then the format buttons beside them).
                    if (smallColumn is not null && columnIsCombo != isCombo)
                        FlushColumn();

                    smallColumn ??= NewSmallColumn();
                    columnIsCombo = isCombo;
                    smallColumn.Children.Add(BuildSmallControl(control, resourceHost, registry));
                    if (smallColumn.Children.Count >= MaxSmallRowsPerColumn)
                        FlushColumn();
                    break;
            }
        }

        FlushColumn();
        return row;
    }

    private static StackPanel NewSmallColumn() => new()
    {
        Orientation = Orientation.Vertical,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(1, 1, 1, 0)
    };

    private static FrameworkElement BuildLargeControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry)
    {
        var stack = new StackPanel();
        stack.Children.Add(new RibbonIcon
        {
            Kind = control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
            CommandName = control.CommandId.Value,
            IconSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var caption = new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 64
        };
        if (HasMenu(control))
            caption.Inlines.Add(new System.Windows.Documents.Run("  ▾") { FontSize = 9 });
        stack.Children.Add(caption);

        var button = NewButtonLike(control, resourceHost, "RibbonLargeButton");
        ((ContentControl)button).Content = stack;
        WireMetadata(button, control, registry);
        return button;
    }

    private static FrameworkElement BuildSmallControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry)
    {
        if (control is RibbonComboBox combo)
        {
            var box = new ComboBox
            {
                MinWidth = 110,
                Height = SmallRowHeight,
                Margin = new Thickness(1, 0, 1, 1),
                IsEditable = true
            };
            foreach (var item in combo.Items)
                box.Items.Add(item);
            if (combo.Items.Count > 0)
                box.SelectedIndex = 0;
            WireMetadata(box, control, registry);
            return box;
        }

        var contentStack = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        contentStack.Children.Add(new RibbonIcon
        {
            Kind = control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
            CommandName = control.CommandId.Value,
            IconSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrEmpty(control.Label))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = control.Label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0)
            });
        }
        if (HasMenu(control))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 2, 0)
            });
        }

        var button = NewButtonLike(control, resourceHost, "RibbonBtn");
        button.Height = SmallRowHeight;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)button).Content = contentStack;
        WireMetadata(button, control, registry);
        return button;
    }

    private static Control NewButtonLike(RibbonControl control, FrameworkElement resourceHost, string buttonStyleKey)
    {
        if (control is RibbonToggleButton or RibbonCheckBox)
        {
            var toggle = new ToggleButton();
            ApplyStyle(toggle, resourceHost, "RibbonToggleBtn");
            return toggle;
        }

        var button = new Button();
        ApplyStyle(button, resourceHost, buttonStyleKey);
        return button;
    }

    private static void WireMetadata(Control element, RibbonControl control, IRibbonCommandRegistry? registry)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
            RibbonMetadata.SetCommandName(element, control.CommandId.Value);
        if (!string.IsNullOrEmpty(control.KeyTip))
            RibbonTooltip.SetKeyTip(element, control.KeyTip);
        if (!string.IsNullOrEmpty(control.TooltipTitle))
            RibbonTooltip.SetTitle(element, control.TooltipTitle);
        if (!string.IsNullOrEmpty(control.TooltipDescription))
            RibbonTooltip.SetDescription(element, control.TooltipDescription);

        // No registry => preview/design mode: leave controls enabled so the layout renders fully.
        // With a registry, an unregistered command id renders disabled (never throws).
        if (registry is null)
            return;

        element.IsEnabled = registry.TryGet(control.CommandId, out _);

        if (element is ButtonBase buttonBase)
        {
            buttonBase.Click += (_, _) =>
            {
                if (registry.TryGet(control.CommandId, out var command) && command is not null)
                    command.Execute(RibbonCommandContext.Empty);
            };
        }
    }

    private static bool HasMenu(RibbonControl control) =>
        control is RibbonSplitButton or RibbonDropdown;

    private static FrameworkElement BuildInlineSpacer() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(3, 4, 3, 4),
        Fill = Brushes.Transparent
    };

    private static FrameworkElement BuildDivider(FrameworkElement resourceHost)
    {
        var divider = new Rectangle();
        ApplyStyle(divider, resourceHost, "RibbonGroupDivider");
        return divider;
    }

    private static void ApplyStyle(FrameworkElement element, FrameworkElement resourceHost, string styleKey)
    {
        if (resourceHost.TryFindResource(styleKey) is Style style)
            element.Style = style;
    }

    private static Brush Brush(FrameworkElement resourceHost, string key, Brush fallback) =>
        resourceHost.TryFindResource(key) as Brush ?? fallback;
}
