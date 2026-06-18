using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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

    // Ribbon palette — a polished, Excel-like surface adapted to macOS conventions (light, low-contrast
    // chrome with a single brand accent on the active tab). Exposed internally so the theme is unit-testable.
    internal static readonly Color SurfaceColor = Color.FromRgb(0xF5, 0xF6, 0xF7);
    internal static readonly Color AccentColor = Color.FromRgb(0x21, 0x73, 0x46);   // workbook brand green
    internal static readonly Color DividerColor = Color.FromRgb(0xDA, 0xDC, 0xDF);
    internal static readonly Color GroupLabelColor = Color.FromRgb(0x60, 0x60, 0x60);
    internal static readonly Color HoverColor = Color.FromRgb(0xE6, 0xF2, 0xEC);     // light accent tint

    private static readonly IBrush SurfaceBrush = new SolidColorBrush(SurfaceColor);
    private static readonly IBrush AccentBrush = new SolidColorBrush(AccentColor);
    private static readonly IBrush DividerBrush = new SolidColorBrush(DividerColor);
    private static readonly IBrush GroupLabelBrush = new SolidColorBrush(GroupLabelColor);
    private static readonly IBrush HoverBrush = new SolidColorBrush(HoverColor);

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
            // A thin brand-accent rule under the tab strip, mirroring the desktop ribbon's active band.
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(0, 4, 0, 0),
            Child = scroller,
        };
    }

    /// <summary>Builds a single <see cref="TabItem"/> for a tab (header + content), tagged with the tab id.</summary>
    private static TabItem BuildTabItem(RibbonTab tab, IRibbonCommandRegistry? registry) => new()
    {
        Header = tab.Header,
        Content = BuildTabContent(tab, registry),
        Tag = tab.Id,
    };

    /// <summary>
    /// Builds a <see cref="TabControl"/> over a whole definition's tabs. When a
    /// <paramref name="contextSource"/> is supplied, the visible tab set is resolved from its current
    /// context (normal tabs plus any contextual tab whose activation key is active) and the strip is
    /// re-synced whenever the source raises <see cref="IRibbonContextSource.ContextChanged"/>:
    /// newly-active contextual tabs are inserted in declaration order, deactivated ones removed, and the
    /// previously-selected tab preserved if it is still visible (otherwise the first tab is selected).
    /// With no source, the strip is the definition's non-contextual tabs (back-compat).
    /// </summary>
    public static Control BuildRibbon(
        RibbonDefinition definition,
        IRibbonCommandRegistry? registry = null,
        IRibbonContextSource? contextSource = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tabControl = new TabControl
        {
            Background = SurfaceBrush,
        };
        ApplyRibbonTheme(tabControl);

        var initialTabs = contextSource is null
            ? (IReadOnlyList<RibbonTab>)definition.VisibleTabs.ToArray()
            : RibbonContextResolver.Resolve(definition, contextSource.Current);

        foreach (var tab in initialTabs)
            tabControl.Items.Add(BuildTabItem(tab, registry));

        if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = 0;

        if (contextSource is not null)
            contextSource.ContextChanged += (_, _) => SyncContextualTabs(tabControl, definition, registry, contextSource);

        return tabControl;
    }

    /// <summary>
    /// Reconciles the tab strip with the source's current context: the resolver yields the exact ordered
    /// set of tabs that should be visible; we diff by tab id (the <see cref="TabItem.Tag"/>), inserting
    /// missing tabs at their resolved position and removing stale ones, preserving the user's selection.
    /// </summary>
    private static void SyncContextualTabs(
        TabControl tabControl,
        RibbonDefinition definition,
        IRibbonCommandRegistry? registry,
        IRibbonContextSource contextSource)
    {
        var desired = RibbonContextResolver.Resolve(definition, contextSource.Current);
        var selectedId = (tabControl.SelectedItem as TabItem)?.Tag as string;

        // Remove tabs no longer desired.
        var desiredIds = new HashSet<string>(desired.Select(t => t.Id), StringComparer.Ordinal);
        for (var i = tabControl.Items.Count - 1; i >= 0; i--)
        {
            if (tabControl.Items[i] is TabItem item && item.Tag is string id && !desiredIds.Contains(id))
                tabControl.Items.RemoveAt(i);
        }

        // Insert missing tabs at their resolved (declaration-order) index.
        for (var i = 0; i < desired.Count; i++)
        {
            var tab = desired[i];
            var existingIndex = IndexOfTab(tabControl, tab.Id);
            if (existingIndex < 0)
                tabControl.Items.Insert(Math.Min(i, tabControl.Items.Count), BuildTabItem(tab, registry));
        }

        // Preserve selection if still visible; otherwise select the first tab.
        var restoreIndex = selectedId is null ? -1 : IndexOfTab(tabControl, selectedId);
        if (restoreIndex >= 0)
            tabControl.SelectedIndex = restoreIndex;
        else if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = 0;
    }

    private static int IndexOfTab(TabControl tabControl, string tabId)
    {
        for (var i = 0; i < tabControl.Items.Count; i++)
            if (tabControl.Items[i] is TabItem item && item.Tag is string id && string.Equals(id, tabId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>
    /// Applies the ribbon theme styles to the tab control: the active tab takes the brand accent and a
    /// semibold header, and ribbon buttons get a subtle accent-tinted hover — matching the desktop ribbon's
    /// feedback while staying within the platform theme. Returns the styles applied (for tests/inspection).
    /// </summary>
    internal static void ApplyRibbonTheme(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        var selectedTab = new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, AccentBrush),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
            },
        };

        var buttonHover = new Style(x => x.OfType<Button>().Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, HoverBrush) },
        };

        var toggleHover = new Style(x => x.OfType<ToggleButton>().Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, HoverBrush) },
        };

        tabControl.Styles.Add(selectedTab);
        tabControl.Styles.Add(buttonHover);
        tabControl.Styles.Add(toggleHover);
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

            // A user pick executes the control's command, passing the chosen value so the host applies it
            // (e.g. font size). The initial programmatic SelectedIndex is suppressed by a ready flag.
            var ready = false;
            box.SelectionChanged += (_, _) =>
            {
                if (ready)
                    ExecuteWithValue(control.CommandId, registry, box.SelectedItem as string);
            };
            ready = true;

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

    private static void ExecuteWithValue(RibbonCommandId commandId, IRibbonCommandRegistry? registry, string? value)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.ForSelectedValue(value));
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
