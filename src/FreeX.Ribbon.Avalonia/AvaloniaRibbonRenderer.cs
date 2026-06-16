using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeX.Ribbon.Avalonia;

/// <summary>
/// Avalonia (cross-platform) realization of a declarative <see cref="RibbonTab"/>.
/// Mirrors the WPF renderer's visual vocabulary: a horizontal strip of groups, each a content
/// row over a header label, controls laid out by <see cref="RibbonControl.PreferredLayout"/>
/// (Large = icon-above-label hero, Medium/Small = compact icon+label rows packed into columns),
/// dropdown/split buttons opening a <see cref="MenuFlyout"/> built from the control's
/// <see cref="RibbonMenu"/>, separators as vertical rules, and combos as <see cref="ComboBox"/>.
/// Behavior is resolved through an <see cref="IRibbonCommandRegistry"/> keyed by command id.
/// </summary>
public static class AvaloniaRibbonRenderer
{
    private const double SmallRowHeight = 22;
    private const int MaxSmallRowsPerColumn = 3;

    private static readonly IBrush SurfaceBrush = Brushes.White;
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static readonly IBrush GroupLabelBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

    /// <summary>Builds the content panel for one tab (the body shown under the tab header).</summary>
    public static Control BuildTabContent(RibbonTab tab, IRibbonCommandRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinHeight = 88,
        };

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildDivider());
            panel.Children.Add(BuildGroup(group, registry));
            first = false;
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
        };

        return new Border
        {
            Background = SurfaceBrush,
            Padding = new Thickness(0, 4, 0, 0),
            Child = scroller,
        };
    }

    /// <summary>Builds a <see cref="TabControl"/> over a whole definition's visible tabs.</summary>
    public static Control BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tabControl = new TabControl
        {
            Background = SurfaceBrush,
        };

        foreach (var tab in definition.VisibleTabs)
        {
            tabControl.Items.Add(new TabItem
            {
                Header = tab.Header,
                Content = BuildTabContent(tab, registry),
                Tag = tab.Id,
            });
        }

        if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = 0;

        return tabControl;
    }

    private static Control BuildGroup(RibbonGroup group, IRibbonCommandRegistry? registry)
    {
        var grid = new Grid
        {
            Tag = group.Id,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(18)),
            },
        };

        var content = BuildGroupContent(group, registry);
        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        var labelBorder = new Border
        {
            Padding = new Thickness(4, 0, 4, 2),
            Child = new TextBlock
            {
                Text = group.Header,
                FontSize = 11,
                Foreground = GroupLabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Grid.SetRow(labelBorder, 1);
        grid.Children.Add(labelBorder);

        return grid;
    }

    private static Control BuildGroupContent(RibbonGroup group, IRibbonCommandRegistry? registry)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 2, 0),
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
                    row.Children.Add(BuildVerticalSeparator());
                    break;

                case { PreferredLayout: RibbonCommandLayoutKind.Large }:
                    FlushColumn();
                    row.Children.Add(BuildLargeControl(control, registry));
                    break;

                default:
                    var isCombo = control is RibbonComboBox;
                    // Keep comboboxes and buttons in separate columns so a group reads like
                    // Excel's (e.g. Font: name+size stacked, then the format buttons beside them).
                    if (smallColumn is not null && columnIsCombo != isCombo)
                        FlushColumn();

                    smallColumn ??= NewSmallColumn();
                    columnIsCombo = isCombo;
                    smallColumn.Children.Add(BuildSmallControl(control, registry));
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
        Margin = new Thickness(1, 1, 1, 0),
    };

    private static Control BuildLargeControl(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(AvaloniaRibbonIcons.Build(control.Icon?.Kind ?? RibbonCommandIconKind.Generic, 22));

        var captionText = control.Label;
        if (HasMenu(control))
            captionText += "  ▾";

        stack.Children.Add(new TextBlock
        {
            Text = captionText,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 64,
        });

        var button = NewButtonLike(control);
        button.Padding = new Thickness(6, 4);
        ((ContentControl)button).Content = stack;
        WireControl(button, control, registry);
        return button;
    }

    private static Control BuildSmallControl(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        if (control is RibbonComboBox combo)
        {
            var box = new ComboBox
            {
                MinWidth = 110,
                Height = SmallRowHeight,
                Margin = new Thickness(1, 0, 1, 1),
                Tag = control.CommandId.Value,
            };
            foreach (var item in combo.Items)
                box.Items.Add(item);
            if (combo.Items.Count > 0)
                box.SelectedIndex = 0;
            ApplyEnablement(box, control, registry);
            return box;
        }

        var contentStack = new StackPanel { Orientation = Orientation.Horizontal };
        contentStack.Children.Add(AvaloniaRibbonIcons.Build(control.Icon?.Kind ?? RibbonCommandIconKind.Generic, 16));

        if (!string.IsNullOrEmpty(control.Label))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = control.Label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
            });
        }

        if (HasMenu(control))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 2, 0),
            });
        }

        var button = NewButtonLike(control);
        button.Height = SmallRowHeight;
        button.Padding = new Thickness(4, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)button).Content = contentStack;
        WireControl(button, control, registry);
        return button;
    }

    private static ContentControl NewButtonLike(RibbonControl control)
    {
        if (control is RibbonToggleButton or RibbonCheckBox)
            return new ToggleButton { Tag = control.CommandId.Value };

        return new Button { Tag = control.CommandId.Value };
    }

    /// <summary>
    /// Attaches the menu flyout (for dropdown/split buttons), click routing, and enablement.
    /// </summary>
    private static void WireControl(ContentControl element, RibbonControl control, IRibbonCommandRegistry? registry)
    {
        if (BuildMenu(control) is { } menu && element is Button menuButton)
        {
            menuButton.Flyout = menu.BuildFlyout(registry);
        }
        else if (element is Button button)
        {
            button.Click += (_, _) => Execute(control.CommandId, registry);
        }
        else if (element is ToggleButton toggle)
        {
            toggle.Click += (_, _) => Execute(control.CommandId, registry);
        }

        ApplyEnablement(element, control, registry);
    }

    private static void ApplyEnablement(Control element, RibbonControl control, IRibbonCommandRegistry? registry)
    {
        // No registry => preview/design mode: leave controls enabled so the layout renders fully.
        // With a registry, an unregistered command id renders disabled (never throws).
        if (registry is null)
            return;
        if (string.IsNullOrEmpty(control.CommandId.Value))
            return;
        element.IsEnabled = registry.TryGet(control.CommandId, out _);
    }

    private static RibbonMenu? BuildMenu(RibbonControl control) => control switch
    {
        RibbonSplitButton split => split.Menu,
        RibbonDropdown dropdown => dropdown.Menu,
        _ => null,
    };

    private static MenuFlyout BuildFlyout(this RibbonMenu menu, IRibbonCommandRegistry? registry)
    {
        var flyout = new MenuFlyout();
        foreach (var item in menu.Items)
            flyout.Items.Add(BuildMenuItem(item, registry));
        return flyout;
    }

    private static Control BuildMenuItem(RibbonMenuItem item, IRibbonCommandRegistry? registry)
    {
        if (item.Kind == RibbonMenuItemKind.Separator)
            return new Separator();

        var menuItem = new MenuItem
        {
            Header = item.Header,
            InputGesture = null,
            Tag = item.CommandId?.Value,
        };

        if (!string.IsNullOrEmpty(item.InputGesture))
            menuItem.InputGesture = TryParseGesture(item.InputGesture);

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                menuItem.Items.Add(BuildMenuItem(child, registry));
        }
        else if (item.CommandId is { } commandId)
        {
            menuItem.Click += (_, _) => Execute(commandId, registry);
            ApplyEnablement(menuItem, commandId, registry);
        }

        return menuItem;
    }

    private static global::Avalonia.Input.KeyGesture? TryParseGesture(string gesture)
    {
        try
        {
            return global::Avalonia.Input.KeyGesture.Parse(gesture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ApplyEnablement(MenuItem item, RibbonCommandId commandId, IRibbonCommandRegistry? registry)
    {
        if (registry is null || string.IsNullOrEmpty(commandId.Value))
            return;
        item.IsEnabled = registry.TryGet(commandId, out _);
    }

    private static void Execute(RibbonCommandId commandId, IRibbonCommandRegistry? registry)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }

    private static bool HasMenu(RibbonControl control) =>
        control is RibbonSplitButton or RibbonDropdown;

    private static Control BuildVerticalSeparator() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(4),
        Fill = DividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private static Control BuildDivider() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(3, 4, 3, 4),
        Fill = DividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
}
