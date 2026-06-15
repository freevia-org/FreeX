using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// WPF realization of a declarative <see cref="RibbonTab"/>. Reproduces the existing ribbon's visual
/// vocabulary (RibbonGroupPanel grids, dividers, group-label borders, <see cref="RibbonIcon"/> glyphs)
/// and honors each control's <see cref="RibbonCommandLayoutKind"/> strictly — Large = hero button with a
/// big icon, Medium = small icon + label, Small = icon-only — so controls render at their preferred size
/// rather than auto-expanding. Behavior is resolved through the command registry by <c>CommandId</c>.
/// </summary>
public static class RibbonWpfRenderer
{
    private const double SmallRowHeight = 22;
    private const double LargeIconSize = 32;
    private const double MediumIconSize = 16;
    private const double SmallIconSize = 18;
    private const int MaxRowsPerColumn = 3;

    public static FrameworkElement BuildTabContent(
        RibbonTab tab,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 88 };

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildGroupDivider(resourceHost));
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

    private static FrameworkElement BuildGroup(RibbonGroup group, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
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

    private static FrameworkElement BuildGroupContent(RibbonGroup group, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var lane = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 2, 0)
        };

        var controls = group.Controls;
        var index = 0;

        // Leading large "hero" buttons each occupy their own full-height column.
        while (index < controls.Count && controls[index].PreferredLayout == RibbonCommandLayoutKind.Large)
        {
            lane.Children.Add(BuildLargeControl(controls[index], resourceHost, registry));
            index++;
        }

        var rest = controls.Skip(index).ToList();
        if (rest.Count == 0)
            return lane;

        if (rest.Any(c => c is RibbonRowBreak))
            lane.Children.Add(BuildExplicitRows(rest, resourceHost, registry));
        else
            BuildAutoColumns(rest, lane, resourceHost, registry);

        return lane;
    }

    // Groups that declare RowBreaks lay out as stacked horizontal rows (e.g. Font: combos row, then B/I/U row).
    private static FrameworkElement BuildExplicitRows(IReadOnlyList<RibbonControl> controls, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };
        var current = NewRow(isFirst: true);

        foreach (var control in controls)
        {
            if (control is RibbonRowBreak)
            {
                rows.Children.Add(current);
                current = NewRow(isFirst: false);
                continue;
            }

            current.Children.Add(BuildInlineControl(control, resourceHost, registry));
        }

        rows.Children.Add(current);
        return rows;
    }

    private static StackPanel NewRow(bool isFirst) => new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, isFirst ? 0 : 2, 0, 0)
    };

    // Groups without explicit rows pack medium/small/combo controls into columns of up to three.
    private static void BuildAutoColumns(IReadOnlyList<RibbonControl> controls, StackPanel lane, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        StackPanel? column = null;
        var columnIsCombo = false;

        void Flush()
        {
            if (column is not null)
            {
                lane.Children.Add(column);
                column = null;
            }
        }

        foreach (var control in controls)
        {
            switch (control)
            {
                case RibbonSeparator:
                    Flush();
                    lane.Children.Add(BuildInlineDivider());
                    break;
                case { PreferredLayout: RibbonCommandLayoutKind.Large }:
                    Flush();
                    lane.Children.Add(BuildLargeControl(control, resourceHost, registry));
                    break;
                default:
                    var isCombo = control is RibbonComboBox;
                    if (column is not null && columnIsCombo != isCombo)
                        Flush();
                    column ??= new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0) };
                    columnIsCombo = isCombo;
                    column.Children.Add(BuildInlineControl(control, resourceHost, registry));
                    if (column.Children.Count >= MaxRowsPerColumn)
                        Flush();
                    break;
            }
        }

        Flush();
    }

    private static FrameworkElement BuildInlineControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry) =>
        control switch
        {
            RibbonSeparator => BuildInlineDivider(),
            RibbonComboBox combo => BuildComboControl(combo, resourceHost, registry),
            { PreferredLayout: RibbonCommandLayoutKind.Large } => BuildLargeControl(control, resourceHost, registry),
            { PreferredLayout: RibbonCommandLayoutKind.Small } => BuildIconControl(control, resourceHost, registry),
            _ => BuildMediumControl(control, resourceHost, registry)
        };

    private static FrameworkElement BuildLargeControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var stack = new StackPanel();
        stack.Children.Add(NewIcon(control, LargeIconSize, HorizontalAlignment.Center));

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

        var button = NewButton(control, resourceHost, "RibbonLargeButton");
        ((ContentControl)button).Content = stack;
        WireMetadata(button, control, registry);
        return button;
    }

    private static FrameworkElement BuildMediumControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(NewIcon(control, MediumIconSize, HorizontalAlignment.Center, VerticalAlignment.Center));
        content.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0)
        });
        if (HasMenu(control))
            content.Children.Add(Chevron());

        var button = NewButton(control, resourceHost, "RibbonBtn");
        button.Height = SmallRowHeight;
        button.MinWidth = 84;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)button).Content = content;
        WireMetadata(button, control, registry);
        return button;
    }

    private static FrameworkElement BuildIconControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var hasMenu = HasMenu(control);
        FrameworkElement content;
        if (hasMenu)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(NewIcon(control, SmallIconSize, HorizontalAlignment.Center, VerticalAlignment.Center));
            stack.Children.Add(Chevron());
            content = stack;
        }
        else
        {
            content = NewIcon(control, SmallIconSize, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        var isToggle = control is RibbonToggleButton or RibbonCheckBox;
        var button = NewButton(control, resourceHost, isToggle ? "RibbonIconToggleButton" : "RibbonIconButton");
        if (hasMenu)
            button.Width = 34;
        ((ContentControl)button).Content = content;
        WireMetadata(button, control, registry);
        return button;
    }

    private static FrameworkElement BuildComboControl(RibbonComboBox combo, FrameworkElement resourceHost, IRibbonCommandRegistry? registry)
    {
        var box = new ComboBox
        {
            Width = combo.Width ?? 110,
            Height = SmallRowHeight,
            Margin = new Thickness(1, 0, 1, 0),
            IsEditable = true,
            Background = Brushes.White
        };
        foreach (var item in combo.Items)
            box.Items.Add(item);
        if (combo.Items.Count > 0)
            box.SelectedIndex = 0;
        WireMetadata(box, combo, registry);
        return box;
    }

    private static RibbonIcon NewIcon(RibbonControl control, double size, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Center) => new()
    {
        Kind = control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
        CommandName = control.CommandId.Value,
        IconSize = size,
        HorizontalAlignment = h,
        VerticalAlignment = v
    };

    private static TextBlock Chevron() => new()
    {
        Text = "▾",
        FontSize = 9,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(1, 0, 1, 0)
    };

    private static Control NewButton(RibbonControl control, FrameworkElement resourceHost, string styleKey)
    {
        if (control is RibbonToggleButton or RibbonCheckBox)
        {
            var toggle = new ToggleButton();
            ApplyStyle(toggle, resourceHost, styleKey.Contains("Toggle") ? styleKey : "RibbonToggleBtn");
            return toggle;
        }

        var button = new Button();
        ApplyStyle(button, resourceHost, styleKey);
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

    private static FrameworkElement BuildInlineDivider() => new Rectangle
    {
        Width = 1,
        Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(3, 3, 3, 3)
    };

    private static FrameworkElement BuildGroupDivider(FrameworkElement resourceHost)
    {
        var divider = new Rectangle();
        ApplyStyle(divider, resourceHost, "RibbonGroupDivider");
        return divider;
    }

    private static bool HasMenu(RibbonControl control) =>
        control is RibbonSplitButton or RibbonDropdown;

    private static void ApplyStyle(FrameworkElement element, FrameworkElement resourceHost, string styleKey)
    {
        if (resourceHost.TryFindResource(styleKey) is Style style)
            element.Style = style;
    }

    private static Brush Brush(FrameworkElement resourceHost, string key, Brush fallback) =>
        resourceHost.TryFindResource(key) as Brush ?? fallback;
}
