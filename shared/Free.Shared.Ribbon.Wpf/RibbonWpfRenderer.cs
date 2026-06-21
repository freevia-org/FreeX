using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// WPF realization of a declarative <see cref="RibbonTab"/>. Reproduces the existing ribbon's visual
/// vocabulary (RibbonGroupPanel grids, dividers, group-label borders, <see cref="RibbonIcon"/> glyphs)
/// and honors each control's <see cref="RibbonCommandLayoutKind"/> strictly — Large = hero button with a
/// big icon, Medium = small icon + label, Small = icon-only — so controls render at their preferred size
/// rather than auto-expanding. Behavior is resolved through the command registry by <c>CommandId</c>.
///
/// Ported from FreeX's <c>RibbonWpfRenderer</c> (app-neutral: depends only on WPF + Free.Shared.Ribbon).
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
        IRibbonCommandRegistry? registry = null,
        IRibbonStateStore? stateStore = null)
    {
        var panel = new RibbonAdaptivePanel { MinHeight = 88 };

        // Group keytips for the collapsed (overflow) form are derived per tab, deduped against each
        // other (a collapsed group is reachable by a 2-letter keytip like Charts -> CH).
        var usedGroupKeyTips = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildGroupDivider(resourceHost));

            var full = (FrameworkElement)BuildGroup(group, resourceHost, registry, stateStore);
            var captured = group;
            var collapsedKeyTip = DeriveGroupKeyTip(group.Header, usedGroupKeyTips);
            panel.Children.Add(new RibbonGroupHost(
                group,
                full,
                () => (FrameworkElement)BuildGroup(captured, resourceHost, registry, stateStore),
                resourceHost,
                collapsedKeyTip,
                () => BuildCollapsedGroupMenu(captured, registry)));
            first = false;
        }

        return new Border
        {
            Background = Brush(resourceHost, "FreeXRibbonSurfaceBrush", Brushes.White),
            Padding = new Thickness(0, 4, 0, 0),
            Child = panel
        };
    }

    // Derives a unique 2-letter keytip for a collapsed group from its header (Charts -> CH, Editing ->
    // ED), falling back to G/G1.. — mirrors the original adaptive ribbon's CreateGroupKeyTip.
    private static string DeriveGroupKeyTip(string header, HashSet<string> used)
    {
        var letters = header.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        var candidates = new List<string>();
        if (letters.Length >= 2)
        {
            candidates.Add(new string(new[] { letters[0], letters[1] }));
            for (var i = 2; i < letters.Length; i++)
                candidates.Add(new string(new[] { letters[0], letters[i] }));
        }
        else if (letters.Length == 1)
        {
            candidates.Add(new string(new[] { letters[0] }));
        }

        candidates.Add("G");
        for (var i = 1; i <= 9; i++)
            candidates.Add($"G{i}");

        foreach (var candidate in candidates)
        {
            if (used.Add(candidate))
                return candidate;
        }

        return "G";
    }

    // Builds the collapsed group's dropdown: every commandable control becomes a menu item carrying
    // the control's keytip and routed through the registry, so a keytip opens the group and selects a
    // command exactly like the expanded form.
    private static ContextMenu BuildCollapsedGroupMenu(RibbonGroup group, IRibbonCommandRegistry? registry)
    {
        var menu = new ContextMenu();
        foreach (var control in group.Controls)
        {
            if (control is RibbonSeparator or RibbonRowBreak || string.IsNullOrEmpty(control.Label))
                continue;

            var menuItem = new MenuItem { Header = control.Label, Tag = control.Label };
            if (!string.IsNullOrEmpty(control.KeyTip))
                RibbonTooltip.SetKeyTip(menuItem, control.KeyTip);
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                RibbonMetadata.SetCommandName(menuItem, control.CommandId.Value);

            var nested = GetMenu(control);
            if (registry is not null && nested is not null && nested.Items.Count > 0)
            {
                AddMenuItems(menuItem.Items, nested.Items, registry);
            }
            else if (registry is not null)
            {
                var commandId = control.CommandId;
                menuItem.IsEnabled = registry.TryGet(commandId, out _);
                menuItem.Click += (sender, _) =>
                {
                    if (registry.TryGet(commandId, out var command) && command is not null)
                        command.Execute(SenderContext(sender));
                };
            }

            menu.Items.Add(menuItem);
        }

        return menu;
    }

    private static FrameworkElement BuildGroup(RibbonGroup group, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        var grid = new Grid();
        ApplyStyle(grid, resourceHost, "RibbonGroupPanel");
        RibbonMetadata.SetCatalogId(grid, group.Id);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        var content = BuildGroupContent(group, resourceHost, registry, stateStore);
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

    private static FrameworkElement BuildGroupContent(RibbonGroup group, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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
            lane.Children.Add(BuildLargeControl(controls[index], resourceHost, registry, stateStore));
            index++;
        }

        var rest = controls.Skip(index).ToList();
        if (rest.Count == 0)
            return lane;

        if (rest.Any(c => c is RibbonRowBreak))
            lane.Children.Add(BuildExplicitRows(rest, resourceHost, registry, stateStore));
        else
            BuildAutoColumns(rest, lane, resourceHost, registry, stateStore);

        return lane;
    }

    // Groups that declare RowBreaks lay out as stacked horizontal rows (e.g. Font: combos row, then B/I/U row).
    private static FrameworkElement BuildExplicitRows(IReadOnlyList<RibbonControl> controls, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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

            current.Children.Add(BuildInlineControl(control, resourceHost, registry, stateStore));
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
    private static void BuildAutoColumns(IReadOnlyList<RibbonControl> controls, StackPanel lane, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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
                    lane.Children.Add(BuildLargeControl(control, resourceHost, registry, stateStore));
                    break;
                default:
                    var isCombo = control is RibbonComboBox;
                    if (column is not null && columnIsCombo != isCombo)
                        Flush();
                    column ??= new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0) };
                    columnIsCombo = isCombo;
                    column.Children.Add(BuildInlineControl(control, resourceHost, registry, stateStore));
                    if (column.Children.Count >= MaxRowsPerColumn)
                        Flush();
                    break;
            }
        }

        Flush();
    }

    private static FrameworkElement BuildInlineControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore) =>
        control switch
        {
            RibbonSeparator => BuildInlineDivider(),
            RibbonComboBox combo => BuildComboControl(combo, resourceHost, registry, stateStore),
            RibbonCheckBox check => BuildCheckControl(check, registry, stateStore),
            { PreferredLayout: RibbonCommandLayoutKind.Large } => BuildLargeControl(control, resourceHost, registry, stateStore),
            { PreferredLayout: RibbonCommandLayoutKind.Small } => BuildIconControl(control, resourceHost, registry, stateStore),
            _ => BuildMediumControl(control, resourceHost, registry, stateStore)
        };

    private static FrameworkElement BuildCheckControl(RibbonCheckBox check, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        var box = new CheckBox
        {
            Content = check.Label,
            FontSize = 12,
            Height = SmallRowHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1)
        };
        WireMetadata(box, check, registry, stateStore);
        return box;
    }

    private static FrameworkElement BuildLargeControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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
            MaxWidth = 128
        };
        EnsureNaturalLabelWidth(caption);
        if (HasMenu(control))
            caption.Inlines.Add(new System.Windows.Documents.Run("  ▾") { FontSize = 9 });
        stack.Children.Add(caption);

        var button = NewButton(control, resourceHost, "RibbonLargeButton");
        ((ContentControl)button).Content = stack;
        WireMetadata(button, control, registry, stateStore);
        return button;
    }

    private static FrameworkElement BuildMediumControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = NewIcon(control, MediumIconSize, HorizontalAlignment.Center, VerticalAlignment.Center);
        icon.Margin = new Thickness(0, 0, 4, 0);
        content.Children.Add(icon);
        var label = new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };
        EnsureNaturalLabelWidth(label);
        content.Children.Add(label);
        if (HasMenu(control))
            content.Children.Add(Chevron());

        var button = NewButton(control, resourceHost, "RibbonBtn");
        button.Height = SmallRowHeight;
        button.MinWidth = 84;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)button).Content = content;
        WireMetadata(button, control, registry, stateStore);
        return button;
    }

    private static void EnsureNaturalLabelWidth(TextBlock label)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (label.DesiredSize.Width > 0 &&
            !double.IsInfinity(label.DesiredSize.Width) &&
            !double.IsNaN(label.DesiredSize.Width))
        {
            label.MinWidth = Math.Max(label.MinWidth, Math.Ceiling(label.DesiredSize.Width));
        }
    }

    private static FrameworkElement BuildIconControl(RibbonControl control, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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
        WireMetadata(button, control, registry, stateStore);
        return button;
    }

    private static FrameworkElement BuildComboControl(RibbonComboBox combo, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
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
        WireMetadata(box, combo, registry, stateStore);
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

    private static void WireMetadata(Control element, RibbonControl control, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
            RibbonMetadata.SetCommandName(element, control.CommandId.Value);
        if (!string.IsNullOrEmpty(control.KeyTip))
            RibbonTooltip.SetKeyTip(element, control.KeyTip);
        // The tooltip title doubles as the control's human-readable identity (keytip overlays, tests
        // that resolve a command by its visible title). Default it to the Label so every rendered
        // control reports a meaningful title even when no explicit tooltip title was authored.
        var title = !string.IsNullOrEmpty(control.TooltipTitle) ? control.TooltipTitle : control.Label;
        if (!string.IsNullOrEmpty(title))
            RibbonTooltip.SetTitle(element, title);
        if (!string.IsNullOrEmpty(control.TooltipDescription))
            RibbonTooltip.SetDescription(element, control.TooltipDescription);

        if (registry is null)
            return;

        var menu = GetMenu(control);
        var hasMenuItems = menu is not null && menu.Items.Count > 0;
        var commandIsLive = control is RibbonComboBox || hasMenuItems || registry.TryGet(control.CommandId, out _);
        element.IsEnabled = commandIsLive;

        // Bind the control to the neutral state store: its checked-ness, combo value, and enablement
        // now follow the store (the source of truth).
        // A command-only control whose command has no handler stays disabled regardless of store state;
        // editable combos are intrinsically live because their behavior is wired through input events.
        BindControlToStore(element, control.CommandId, stateStore, commandIsLive);

        if (element is not ButtonBase buttonBase)
            return;

        if (hasMenuItems)
        {
            var contextMenu = BuildContextMenu(menu!, registry);
            contextMenu.PlacementTarget = buttonBase;
            contextMenu.Placement = PlacementMode.Bottom;
            buttonBase.ContextMenu = contextMenu;
            buttonBase.Click += (_, _) => contextMenu.IsOpen = true;
        }
        else
        {
            var commandId = control.CommandId;
            buttonBase.Click += (sender, _) =>
            {
                // A real/keytip click flips a toggle's IsChecked before raising Click; push that new
                // state into the store first so field-reading handlers observe it.
                if (sender is ToggleButton toggle && stateStore is not null)
                    stateStore.SetChecked(commandId, toggle.IsChecked == true);
                if (registry.TryGet(commandId, out var command) && command is not null)
                    command.Execute(SenderContext(sender));
            };
        }
    }

    // Subscribes a rendered control to the state store so its IsEnabled, toggle IsChecked, and combo
    // value/Text follow the command's RibbonCommandState. Platform-neutral state in, WPF visuals out;
    // an Avalonia renderer binds the same store the same way.
    private static void BindControlToStore(Control element, RibbonCommandId commandId, IRibbonStateStore? stateStore, bool commandIsLive)
    {
        if (stateStore is null || string.IsNullOrEmpty(commandId.Value))
            return;

        void Apply(RibbonCommandState state)
        {
            element.IsEnabled = commandIsLive && state.IsEnabled;
            switch (element)
            {
                case ToggleButton toggle:
                    if (toggle.IsChecked != state.IsChecked)
                        toggle.IsChecked = state.IsChecked;
                    break;
                case ComboBox combo when state.Value is { } value && !string.Equals(combo.Text, value, System.StringComparison.Ordinal):
                    combo.Text = value;
                    break;
            }
        }

        stateStore.StateChanged += (_, e) =>
        {
            if (e.Id == commandId)
                Apply(e.State);
        };

        // Apply any state already set before the control existed (e.g. an initial selection refresh).
        Apply(stateStore.GetState(commandId));
    }

    // Passes the actual clicked WPF element to the command so host handlers that inspect their sender
    // (MenuItem.Tag/Header, ToggleButton.IsChecked) see the real rendered control. ReflectiveHandlerCommand
    // prefers this over its backplane sender when present.
    public const string SenderKey = "wpf.sender";

    private static RibbonCommandContext SenderContext(object? sender) =>
        sender is null
            ? RibbonCommandContext.Empty
            : new RibbonCommandContext(new Dictionary<string, object?> { [SenderKey] = sender });

    private static RibbonMenu? GetMenu(RibbonControl control) => control switch
    {
        RibbonSplitButton sb => sb.Menu,
        RibbonDropdown dd => dd.Menu,
        _ => null
    };

    private static ContextMenu BuildContextMenu(RibbonMenu menu, IRibbonCommandRegistry registry)
    {
        var contextMenu = new ContextMenu();
        AddMenuItems(contextMenu.Items, menu.Items, registry);
        return contextMenu;
    }

    private static void AddMenuItems(ItemCollection target, IReadOnlyList<RibbonMenuItem> items, IRibbonCommandRegistry registry)
    {
        foreach (var item in items)
        {
            if (item.Kind == Free.Shared.Ribbon.RibbonMenuItemKind.Separator)
            {
                target.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = item.Header,
                InputGestureText = item.InputGesture ?? string.Empty
            };
            // Keytip navigation only enters a menu whose items carry keytips, so propagate them.
            if (!string.IsNullOrEmpty(item.KeyTip))
                RibbonTooltip.SetKeyTip(menuItem, item.KeyTip);

            if (item.Children.Count > 0)
            {
                AddMenuItems(menuItem.Items, item.Children, registry);
            }
            else if (item.CommandId is { } commandId)
            {
                menuItem.IsEnabled = registry.TryGet(commandId, out _);
                // Some menu-item handlers read state off their sender. Carry the values the original
                // authored menu set as Tag so those handlers resolve against the rendered menu item.
                menuItem.Tag = item.Header;
                menuItem.Click += (sender, _) =>
                {
                    if (registry.TryGet(commandId, out var command) && command is not null)
                        command.Execute(SenderContext(sender));
                };
            }

            target.Add(menuItem);
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
        var divider = new Rectangle
        {
            Width = 1,
            Fill = Brush(resourceHost, "FreeXBorderBrush", new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0))),
            Margin = new Thickness(2, 5, 3, 18),
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
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
