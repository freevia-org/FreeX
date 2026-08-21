using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
public sealed record RibbonWpfRendererOptions(
    bool UseExternalDropdownZones = false,
    double MediumIconSize = RibbonVisualMetrics.MediumIconSize,
    double SmallIconSize = RibbonVisualMetrics.SmallIconSize,
    double? LargeButtonWidth = null)
{
    public static RibbonWpfRendererOptions Default { get; } = new();

    public static RibbonWpfRendererOptions FreeXHost { get; } =
        new(UseExternalDropdownZones: true, MediumIconSize: 20, SmallIconSize: 20, LargeButtonWidth: 58);
}

public static class RibbonWpfRenderer
{
    private sealed class ComboExecutionState
    {
        public bool IsSynchronizing { get; set; }
    }

    private const int MaxRowsPerColumn = 3;
    private static readonly ConditionalWeakTable<ComboBox, ComboExecutionState> ComboExecutionStates = new();
    private static readonly ConditionalWeakTable<MenuItem, MenuCommandStateBinding> MenuCommandStateBindings = new();

    private sealed class MenuCommandStateBinding
    {
        internal required RibbonCommandId CommandId { get; init; }
        internal RibbonMenuItem? Definition { get; init; }
        internal RibbonControl? CollapsedControl { get; init; }
    }

    public static FrameworkElement BuildTabContent(
        RibbonTab tab,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry = null,
        IRibbonStateStore? stateStore = null,
        RibbonWpfRendererOptions? options = null)
    {
        options ??= RibbonWpfRendererOptions.Default;
        var panel = new RibbonAdaptivePanel
        {
            MinHeight = RibbonVisualMetrics.TabContentMinHeight,
            RefreshFullWidthsFromFullContent = tab.IsContextual
        };

        // Group keytips for the collapsed (overflow) form are derived per tab, deduped against each
        // other (a collapsed group is reachable by a 2-letter keytip like Charts -> CH).
        var usedGroupKeyTips = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildGroupDivider(resourceHost));

            var full = (FrameworkElement)BuildGroup(group, resourceHost, registry, stateStore, options);
            var captured = group;
            var collapsedKeyTip = RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip(
                group.Header,
                usedGroupKeyTips);
            panel.Children.Add(new RibbonGroupHost(
                group,
                full,
                () => (FrameworkElement)BuildGroup(captured, resourceHost, registry, stateStore, options),
                resourceHost,
                collapsedKeyTip,
                () => BuildCollapsedGroupMenu(captured, registry, stateStore)));
            first = false;
        }

        return new Border
        {
            Background = Brush(resourceHost, "FreeXRibbonSurfaceBrush", Brushes.White),
            Padding = new Thickness(0, RibbonVisualMetrics.TabContentTopPadding, 0, 0),
            Child = panel
        };
    }

    // Builds the collapsed group's dropdown: every commandable control becomes a menu item carrying
    // the control's keytip and routed through the registry, so a keytip opens the group and selects a
    // command exactly like the expanded form.
    private static ContextMenu BuildCollapsedGroupMenu(
        RibbonGroup group,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore)
    {
        var menu = new ContextMenu();
        foreach (var control in RibbonCollapsedGroupPresentationPlanner.GetOverflowControls(group))
        {
            if (control is RibbonSplitButton splitButton)
            {
                AddCollapsedSplitButtonItems(menu.Items, splitButton, registry, stateStore);
                continue;
            }

            var menuItem = new MenuItem { Header = control.Label, Tag = control.Label };
            if (!string.IsNullOrEmpty(control.KeyTip))
                RibbonTooltip.SetKeyTip(menuItem, control.KeyTip);
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                RibbonMetadata.SetCommandName(menuItem, control.CommandId.Value);

            var nested = GetMenu(control);
            if (registry is not null && nested is not null && nested.Items.Count > 0)
            {
                AddMenuItems(menuItem.Items, nested.Items, registry, stateStore);
            }
            else if (registry is not null)
            {
                var commandId = control.CommandId;
                menuItem.Click += (sender, _) =>
                {
                    if (registry.TryGet(commandId, out var command) && command is not null)
                        ExecuteGuarded(command, commandId, SenderContext(sender));
                };
                BindCollapsedGroupCommandState(menuItem, control, registry, stateStore);
            }

            menu.Items.Add(menuItem);
        }

        menu.Opened += (_, _) => RefreshMenuCommandStates(menu, registry, stateStore);

        // Opened is raised once the popup is actually shown, which needs a dispatcher turn. A
        // caller that sets IsOpen and immediately reads the projections -- as the collapsed group
        // button does -- would see the state the menu was built with rather than the current one.
        // Refresh on the IsOpen transition too, which happens synchronously with the assignment.
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(ContextMenu.IsOpenProperty, typeof(ContextMenu))
            .AddValueChanged(menu, (_, _) =>
            {
                if (menu.IsOpen)
                    RefreshMenuCommandStates(menu, registry, stateStore);
            });

        return menu;
    }

    private static void AddCollapsedSplitButtonItems(
        ItemCollection target,
        RibbonSplitButton splitButton,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore)
    {
        var primary = new MenuItem
        {
            Header = splitButton.Label,
            Tag = splitButton.CommandId.Value,
            IsEnabled = registry?.TryGet(splitButton.CommandId, out _) == true
        };
        if (!string.IsNullOrEmpty(splitButton.KeyTip))
            RibbonTooltip.SetKeyTip(primary, splitButton.KeyTip);
        RibbonMetadata.SetCommandName(primary, splitButton.CommandId.Value);
        primary.Click += (sender, _) =>
        {
            if (registry?.TryGet(splitButton.CommandId, out var command) == true && command is not null)
                ExecuteGuarded(command, splitButton.CommandId, SenderContext(sender));
        };
        BindCollapsedGroupCommandState(primary, splitButton, registry, stateStore);
        target.Add(primary);

        if (registry is null)
            return;

        foreach (var item in splitButton.Menu.Items)
        {
            if (item.Kind != RibbonMenuItemKind.Separator &&
                item.CommandId is { } commandId &&
                (commandId == splitButton.CommandId ||
                 string.Equals(item.Header, splitButton.Label, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddMenuItem(target, item, registry, stateStore);
        }
    }

    private static FrameworkElement BuildGroup(
        RibbonGroup group,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        var grid = new Grid();
        ApplyStyle(grid, resourceHost, "RibbonGroupPanel");
        RibbonMetadata.SetCatalogId(grid, group.Id);
        RibbonMetadata.SetRole(grid, RibbonMetadataRole.RibbonGroup);
        if (!string.IsNullOrEmpty(group.Header))
            RibbonMetadata.SetGroupName(grid, group.Header);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RibbonVisualMetrics.GroupLabelHeight) });

        var content = BuildGroupContent(group, resourceHost, registry, stateStore, options);
        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        var labelBorder = new Border();
        ApplyStyle(labelBorder, resourceHost, "RibbonGroupLabelBorder");
        var labelPanel = new Grid();
        labelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (group.Launcher is not null)
            labelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = group.Header };
        ApplyStyle(label, resourceHost, "GroupLbl");
        labelPanel.Children.Add(label);

        if (group.Launcher is { } launcher)
        {
            var launcherControl = new RibbonButton(launcher.CommandId, launcher.TooltipTitle)
            {
                KeyTip = launcher.KeyTip,
                TooltipTitle = launcher.TooltipTitle,
                TooltipDescription = launcher.TooltipDescription
            };
            var launcherButton = new Button();
            ApplyStyle(launcherButton, resourceHost, "RibbonGroupDialogLauncher");
            launcherButton.Content = new TextBlock { Text = "\u2197" };
            WireMetadata(launcherButton, launcherControl, registry, stateStore, options, attachMenu: false, resourceHost: resourceHost);
            Grid.SetColumn(launcherButton, 1);
            labelPanel.Children.Add(launcherButton);
        }

        labelBorder.Child = labelPanel;
        Grid.SetRow(labelBorder, 1);
        grid.Children.Add(labelBorder);

        return grid;
    }

    private static FrameworkElement BuildGroupContent(
        RibbonGroup group,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
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
            lane.Children.Add(BuildLargeControl(controls[index], resourceHost, registry, stateStore, options));
            index++;
        }

        var rest = controls.Skip(index).ToList();
        if (rest.Count == 0)
            return lane;

        if (rest.Any(c => c is RibbonRowBreak))
            lane.Children.Add(BuildExplicitRows(rest, resourceHost, registry, stateStore, options));
        else
            BuildAutoColumns(rest, lane, resourceHost, registry, stateStore, options);

        return lane;
    }

    // Groups that declare RowBreaks lay out as stacked horizontal rows (e.g. Font: combos row, then B/I/U row).
    private static FrameworkElement BuildExplicitRows(
        IReadOnlyList<RibbonControl> controls,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
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

            current.Children.Add(BuildInlineControl(control, resourceHost, registry, stateStore, options));
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
    private static void BuildAutoColumns(
        IReadOnlyList<RibbonControl> controls,
        StackPanel lane,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
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
                    lane.Children.Add(BuildLargeControl(control, resourceHost, registry, stateStore, options));
                    break;
                default:
                    var isCombo = control is RibbonComboBox;
                    if (column is not null && columnIsCombo != isCombo)
                        Flush();
                    column ??= new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0) };
                    columnIsCombo = isCombo;
                    column.Children.Add(BuildInlineControl(control, resourceHost, registry, stateStore, options));
                    if (column.Children.Count >= MaxRowsPerColumn)
                        Flush();
                    break;
            }
        }

        Flush();
    }

    private static FrameworkElement BuildInlineControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options) =>
        control switch
        {
            RibbonSeparator => BuildInlineDivider(),
            RibbonComboBox combo => BuildComboControl(combo, resourceHost, registry, stateStore),
            RibbonCheckBox check => BuildCheckControl(check, registry, stateStore),
            { PreferredLayout: RibbonCommandLayoutKind.Large } => BuildLargeControl(control, resourceHost, registry, stateStore, options),
            { PreferredLayout: RibbonCommandLayoutKind.Small } => BuildIconControl(control, resourceHost, registry, stateStore, options),
            _ => BuildMediumControl(control, resourceHost, registry, stateStore, options)
        };

    private static FrameworkElement BuildCheckControl(RibbonCheckBox check, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        var box = new CheckBox
        {
            Content = check.Label,
            FontSize = 12,
            Height = RibbonVisualMetrics.SmallRowHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1)
        };
        WireMetadata(box, check, registry, stateStore, RibbonWpfRendererOptions.Default);
        return box;
    }

    private static FrameworkElement BuildLargeControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildSplitControl(splitButton, resourceHost, registry, stateStore, options);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(NewIcon(control, RibbonVisualMetrics.LargeIconSize, HorizontalAlignment.Center));

        var caption = new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = LargeCaptionWidth(options, fallback: 128)
        };
        EnsureNaturalLargeLabelWidth(caption, options);
        RibbonMetadata.SetRole(caption, RibbonMetadataRole.CommandLabel);
        if (HasMenu(control) && !options.UseExternalDropdownZones)
            caption.Inlines.Add(new System.Windows.Documents.Run("  ▾") { FontSize = 9 });
        stack.Children.Add(caption);
        if (options.UseExternalDropdownZones)
            RibbonMetadata.SetCommandContentLayout(stack, RibbonCommandContentLayout.Large);

        var button = NewButton(control, resourceHost, "RibbonLargeButton");
        if (options.LargeButtonWidth is { } largeButtonWidth)
            button.Width = largeButtonWidth;
        ((ContentControl)button).Content = stack;
        WireMetadata(button, control, registry, stateStore, options);
        return button;
    }

    private static FrameworkElement BuildMediumControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildSplitControl(splitButton, resourceHost, registry, stateStore, options);

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = NewIcon(control, options.MediumIconSize, HorizontalAlignment.Center, VerticalAlignment.Center);
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
        RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
        content.Children.Add(label);
        if (HasMenu(control) && !options.UseExternalDropdownZones)
            content.Children.Add(Chevron());
        if (options.UseExternalDropdownZones)
            RibbonMetadata.SetCommandContentLayout(content, RibbonCommandContentLayout.Medium);

        var button = NewButton(control, resourceHost, "RibbonBtn");
        button.Height = RibbonVisualMetrics.SmallRowHeight;
        button.MinWidth = 84;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)button).Content = content;
        WireMetadata(button, control, registry, stateStore, options);
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

    // Host renderers that deliberately use narrow Office-style large tiles need their captions to wrap
    // within the tile. Giving those captions their natural minimum width makes WPF arrange them wider
    // than the button and crops the first/last characters instead.
    private static double LargeCaptionWidth(RibbonWpfRendererOptions options, double fallback) =>
        options.LargeButtonWidth is { } width
            ? Math.Max(0, width - 6)
            : fallback;

    private static void EnsureNaturalLargeLabelWidth(TextBlock label, RibbonWpfRendererOptions options)
    {
        if (options.LargeButtonWidth is null)
            EnsureNaturalLabelWidth(label);
    }

    private static FrameworkElement BuildIconControl(
        RibbonControl control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildSplitControl(splitButton, resourceHost, registry, stateStore, options);

        var hasMenu = HasMenu(control);
        FrameworkElement content;
        if (hasMenu && !options.UseExternalDropdownZones)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(NewIcon(control, options.SmallIconSize, HorizontalAlignment.Center, VerticalAlignment.Center));
            stack.Children.Add(Chevron());
            content = stack;
        }
        else
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(NewIcon(control, options.SmallIconSize, HorizontalAlignment.Center, VerticalAlignment.Center));
            if (options.UseExternalDropdownZones)
                RibbonMetadata.SetCommandContentLayout(grid, RibbonCommandContentLayout.IconOnly);
            content = grid;
        }

        var isToggle = control is RibbonToggleButton or RibbonCheckBox;
        var button = NewButton(control, resourceHost, isToggle ? "RibbonIconToggleButton" : "RibbonIconButton");
        if (hasMenu && !options.UseExternalDropdownZones)
            button.Width = 34;
        ((ContentControl)button).Content = content;
        WireMetadata(button, control, registry, stateStore, options);
        return button;
    }

    private static FrameworkElement BuildSplitControl(
        RibbonSplitButton control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options) =>
        control.PreferredLayout switch
        {
            RibbonCommandLayoutKind.Large => BuildLargeSplitControl(control, resourceHost, registry, stateStore, options),
            RibbonCommandLayoutKind.Small => BuildIconSplitControl(control, resourceHost, registry, stateStore, options),
            _ => BuildMediumSplitControl(control, resourceHost, registry, stateStore, options)
        };

    private static FrameworkElement BuildLargeSplitControl(
        RibbonSplitButton control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        var primaryContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        primaryContent.Children.Add(NewIcon(control, RibbonVisualMetrics.LargeIconSize, HorizontalAlignment.Center));
        var caption = new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = LargeCaptionWidth(options, fallback: 74)
        };
        EnsureNaturalLargeLabelWidth(caption, options);
        RibbonMetadata.SetRole(caption, RibbonMetadataRole.CommandLabel);
        primaryContent.Children.Add(caption);
        RibbonMetadata.SetCommandContentLayout(primaryContent, RibbonCommandContentLayout.Large);

        var primary = NewButton(control, resourceHost, "RibbonLargeButton");
        var largeButtonWidth = options.LargeButtonWidth ?? 80;
        primary.Width = largeButtonWidth;
        primary.HorizontalContentAlignment = HorizontalAlignment.Center;
        primary.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)primary).Content = primaryContent;
        WireMetadata(primary, control, registry, stateStore, options, attachMenu: false, includeKeyTip: false);

        var dropdown = BuildSplitDropdownButton(
            control,
            resourceHost,
            registry,
            stateStore,
            options,
            RibbonCommandContentLayout.Large,
            width: largeButtonWidth,
            height: 20);

        var split = new Grid
        {
            Width = largeButtonWidth,
            Height = 76,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(20) }
            }
        };
        Grid.SetRow(primary, 0);
        Grid.SetRow(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    private static FrameworkElement BuildMediumSplitControl(
        RibbonSplitButton control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = NewIcon(control, options.MediumIconSize, HorizontalAlignment.Center, VerticalAlignment.Center);
        icon.Margin = new Thickness(0, 0, 4, 0);
        content.Children.Add(icon);
        var label = new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        EnsureNaturalLabelWidth(label);
        RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
        content.Children.Add(label);
        RibbonMetadata.SetCommandContentLayout(content, RibbonCommandContentLayout.Medium);

        var primary = NewButton(control, resourceHost, "RibbonBtn");
        primary.Height = RibbonVisualMetrics.SmallRowHeight;
        primary.MinWidth = 84;
        primary.HorizontalContentAlignment = HorizontalAlignment.Left;
        ((ContentControl)primary).Content = content;
        WireMetadata(primary, control, registry, stateStore, options, attachMenu: false, includeKeyTip: false);

        var dropdown = BuildSplitDropdownButton(
            control,
            resourceHost,
            registry,
            stateStore,
            options,
            RibbonCommandContentLayout.Medium,
            width: 20,
            height: RibbonVisualMetrics.SmallRowHeight);

        var split = new Grid { MinWidth = 104 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        Grid.SetColumn(primary, 0);
        Grid.SetColumn(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    private static FrameworkElement BuildIconSplitControl(
        RibbonSplitButton control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options)
    {
        var primary = NewButton(control, resourceHost, "RibbonIconButton");
        primary.Width = 30;
        primary.Height = RibbonVisualMetrics.SmallRowHeight;
        primary.HorizontalContentAlignment = HorizontalAlignment.Center;
        primary.VerticalContentAlignment = VerticalAlignment.Center;
        var primaryContent = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        primaryContent.Children.Add(NewIcon(control, options.SmallIconSize, HorizontalAlignment.Center, VerticalAlignment.Center));
        RibbonMetadata.SetCommandContentLayout(primaryContent, RibbonCommandContentLayout.IconOnly);
        ((ContentControl)primary).Content = primaryContent;
        WireMetadata(primary, control, registry, stateStore, options, attachMenu: false, includeKeyTip: false);

        var dropdown = BuildSplitDropdownButton(
            control,
            resourceHost,
            registry,
            stateStore,
            options,
            RibbonCommandContentLayout.IconOnly,
            width: 14,
            height: RibbonVisualMetrics.SmallRowHeight);

        var split = new Grid { Width = 44, MinWidth = 44 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        Grid.SetColumn(primary, 0);
        Grid.SetColumn(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    private static Button BuildSplitDropdownButton(
        RibbonSplitButton control,
        FrameworkElement resourceHost,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options,
        RibbonCommandContentLayout layout,
        double width,
        double height)
    {
        var dropdown = (Button)NewButton(control, resourceHost, "RibbonBtn");
        dropdown.Width = width;
        dropdown.MinWidth = width;
        dropdown.Height = height;
        dropdown.Padding = new Thickness(0);
        dropdown.HorizontalContentAlignment = HorizontalAlignment.Center;
        dropdown.VerticalContentAlignment = VerticalAlignment.Center;
        var chevron = Chevron();
        RibbonMetadata.SetCommandContentLayout(chevron, layout);
        dropdown.Content = chevron;
        WireMetadata(
            dropdown,
            control,
            registry,
            stateStore,
            options,
            commandNameOverride: $"{control.CommandId.Value}.Dropdown");
        return dropdown;
    }

    private static FrameworkElement BuildComboControl(RibbonComboBox combo, FrameworkElement resourceHost, IRibbonCommandRegistry? registry, IRibbonStateStore? stateStore)
    {
        ComboBox box = combo.PresentationKind == RibbonComboBoxPresentationKind.Gallery
            ? new RibbonGalleryComboBox()
            : new ComboBox();
        box.Width = combo.Width ?? 110;
        box.Height = RibbonVisualMetrics.SmallRowHeight;
        box.Margin = new Thickness(1, 0, 1, 0);
        box.IsEditable = true;
        box.Background = Brushes.White;
        if (combo.Choices.Count > 0)
        {
            box.DisplayMemberPath = nameof(RibbonComboBoxChoice.Label);
            foreach (var choice in combo.Choices)
                box.Items.Add(choice);
            if (box is RibbonGalleryComboBox gallery)
                gallery.SetGalleryChoices(combo.Choices);
        }
        else
        {
            foreach (var item in combo.Items)
                box.Items.Add(item);
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
        var executionState = ComboExecutionStates.GetOrCreateValue(box);
        WireMetadata(box, combo, registry, stateStore, RibbonWpfRendererOptions.Default);
        if (registry is not null)
        {
            var commandId = combo.CommandId;
            box.SelectionChanged += (_, _) =>
            {
                if (!executionState.IsSynchronizing)
                    ExecuteComboValue(box, commandId, registry);
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                ExecuteComboValue(box, commandId, registry);
                e.Handled = true;
            };
        }
        return box;
    }

    private static void ExecuteComboValue(ComboBox box, RibbonCommandId commandId, IRibbonCommandRegistry registry)
    {
        if (!registry.TryGet(commandId, out var command) || command is null)
            return;

        var value = ResolveComboValue(box);
        if (string.IsNullOrWhiteSpace(value))
            value = box.Text;
        ExecuteGuarded(command, commandId, RibbonCommandContext.ForSelectedValue(value));
    }

    private static string? ResolveComboValue(ComboBox box)
    {
        if (box.SelectedItem is RibbonComboBoxChoice choice)
            return choice.Value;

        if (box.SelectedItem is not null)
            return box.SelectedItem.ToString();

        var typedChoice = box.Items
            .OfType<RibbonComboBoxChoice>()
            .FirstOrDefault(item =>
                string.Equals(item.Label, box.Text, System.StringComparison.Ordinal) ||
                string.Equals(item.Value, box.Text, System.StringComparison.Ordinal));
        return typedChoice?.Value ?? box.Text;
    }

    /// <summary>
    /// Invokes a ribbon command from a WPF event handler, containing anything it throws.
    /// The hosts' DispatcherUnhandledException handler records the fault but never sets
    /// <c>Handled</c>, so without this an exception from any one of the several hundred registered
    /// command delegates terminates the whole app rather than failing that single ribbon action.
    /// </summary>
    private static void ExecuteGuarded(IRibbonCommand command, RibbonCommandId commandId, RibbonCommandContext context)
    {
        try
        {
            command.Execute(context);
        }
        catch (Exception ex)
        {
            RibbonCommandFaultReporter.Report(ex, commandId.Value);
        }
    }

    private static RibbonIcon NewIcon(RibbonControl control, double size, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Center)
    {
        var icon = new RibbonIcon
        {
            Kind = control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
            CommandName = control.CommandId.Value,
            IconSize = size,
            HorizontalAlignment = h,
            VerticalAlignment = v
        };
        RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
        return icon;
    }

    private static TextBlock Chevron()
    {
        var chevron = new TextBlock
        {
            Text = "\u25BE",
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        RibbonMetadata.SetRole(chevron, RibbonMetadataRole.DropdownChevron);
        return chevron;
    }

    private static Control NewButton(RibbonControl control, FrameworkElement resourceHost, string styleKey)
    {
        if (control is RibbonDropdown { PresentationKind: RibbonDropdownPresentationKind.CellStyleGallery })
        {
            var galleryButton = new RibbonCellStyleGalleryButton();
            ApplyStyle(galleryButton, resourceHost, styleKey);
            return galleryButton;
        }

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

    private static void WireMetadata(
        Control element,
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore,
        RibbonWpfRendererOptions options,
        bool attachMenu = true,
        bool includeKeyTip = true,
        string? commandNameOverride = null,
        FrameworkElement? resourceHost = null)
    {
        var commandName = commandNameOverride ?? control.CommandId.Value;
        if (!string.IsNullOrEmpty(commandName))
            RibbonMetadata.SetCommandName(element, commandName);
        if (includeKeyTip && !string.IsNullOrEmpty(control.KeyTip))
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

        var menu = attachMenu ? GetMenu(control) : null;
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

        if (attachMenu && HasMenu(control) && options.UseExternalDropdownZones)
            RibbonMetadata.SetDropdownMenuButton(buttonBase, true);

        if (hasMenuItems)
        {
            if (control is RibbonDropdown { PresentationKind: RibbonDropdownPresentationKind.CellStyleGallery } &&
                buttonBase is RibbonCellStyleGalleryButton galleryButton)
            {
                galleryButton.SetMenu(menu!, (commandId, sender) =>
                {
                    if (registry.TryGet(commandId, out var command) && command is not null)
                        ExecuteGuarded(command, commandId, SenderContext(sender));
                });
                galleryButton.Click += (_, _) => galleryButton.OpenGallery();
                return;
            }

            var contextMenu = BuildContextMenu(menu!, registry, stateStore);
            RibbonWpfPopupAdapter.Configure(contextMenu, buttonBase, resourceHost ?? element);
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
                    ExecuteGuarded(command, commandId, SenderContext(sender));
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
                case ComboBox combo when state.Value is { } value:
                    SetComboValueWithoutExecuting(combo, value);
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

    private static void SetComboValueWithoutExecuting(ComboBox combo, string value)
    {
        var executionState = ComboExecutionStates.GetOrCreateValue(combo);
        executionState.IsSynchronizing = true;
        try
        {
            var matchingChoice = combo.Items
                .OfType<RibbonComboBoxChoice>()
                .FirstOrDefault(choice => string.Equals(choice.Value, value, System.StringComparison.Ordinal));
            if (matchingChoice is not null)
            {
                if (!ReferenceEquals(combo.SelectedItem, matchingChoice))
                    combo.SelectedItem = matchingChoice;
                if (!string.Equals(combo.Text, matchingChoice.Label, System.StringComparison.Ordinal))
                    combo.Text = matchingChoice.Label;
                return;
            }

            var matchingItem = combo.Items
                .OfType<string>()
                .FirstOrDefault(item => string.Equals(item, value, System.StringComparison.Ordinal));
            if (matchingItem is not null)
            {
                if (!Equals(combo.SelectedItem, matchingItem))
                    combo.SelectedItem = matchingItem;
                if (!string.Equals(combo.Text, matchingItem, System.StringComparison.Ordinal))
                    combo.Text = matchingItem;
                return;
            }

            combo.SelectedIndex = -1;
            if (!string.Equals(combo.Text, value, System.StringComparison.Ordinal))
                combo.Text = value;
        }
        finally
        {
            executionState.IsSynchronizing = false;
        }
    }

    // Passes the actual clicked WPF element to the command so host handlers that inspect their sender
    // (MenuItem.Tag/Header, ToggleButton.IsChecked) see the real rendered control. WPF command adapters
    // prefer this over their fallback sender when present.
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

    private static ContextMenu BuildContextMenu(
        RibbonMenu menu,
        IRibbonCommandRegistry registry,
        IRibbonStateStore? stateStore)
    {
        var contextMenu = new ContextMenu();
        AddMenuItems(contextMenu.Items, menu.Items, registry, stateStore);
        contextMenu.Opened += (_, _) => RefreshMenuCommandStates(contextMenu, registry, stateStore);
        return contextMenu;
    }

    private static void AddMenuItems(
        ItemCollection target,
        IReadOnlyList<RibbonMenuItem> items,
        IRibbonCommandRegistry registry,
        IRibbonStateStore? stateStore)
    {
        foreach (var item in items)
        {
            AddMenuItem(target, item, registry, stateStore);
        }
    }

    private static void AddMenuItem(
        ItemCollection target,
        RibbonMenuItem item,
        IRibbonCommandRegistry registry,
        IRibbonStateStore? stateStore)
    {
        if (item.Kind == Free.Shared.Ribbon.RibbonMenuItemKind.Separator)
        {
            target.Add(new Separator());
            return;
        }

        var presentation = RibbonMenuItemPresentationPlanner.Plan(item);

        var menuItem = new MenuItem
        {
            Header = presentation.Header,
            InputGestureText = presentation.InputGestureText,
            Icon = item.Icon is null
                ? null
                : RibbonIconFactory.CreateCommandIcon(
                    item.Header,
                    item.Icon,
                    RibbonVisualMetrics.SmallIconSize,
                    Brushes.Black),
            IsEnabled = item.IsEnabled,
            IsCheckable = item.IsChecked.HasValue,
            IsChecked = item.IsChecked ?? false,
        };
        // Keytip navigation only enters a menu whose items carry keytips, so propagate them.
        if (!string.IsNullOrEmpty(presentation.KeyTip))
            RibbonTooltip.SetKeyTip(menuItem, presentation.KeyTip);

        if (item.Children.Count > 0)
        {
            AddMenuItems(menuItem.Items, item.Children, registry, stateStore);
        }
        else if (item.CommandId is { } commandId)
        {
            RibbonMetadata.SetCommandName(menuItem, commandId.Value);
            MenuCommandStateBindings.Add(menuItem, new MenuCommandStateBinding
            {
                CommandId = commandId,
                Definition = item,
            });
            ApplyMenuCommandState(menuItem, registry, stateStore);
            // Some menu-item handlers read state off their sender. Carry the values the original
            // authored menu set as Tag so those handlers resolve against the rendered menu item.
            menuItem.Tag = item.Header;
            menuItem.Click += (sender, _) =>
            {
                if (registry.TryGet(commandId, out var command) && command is not null)
                    ExecuteGuarded(command, commandId, SenderContext(sender));
            };
        }

        target.Add(menuItem);
    }

    private static void BindCollapsedGroupCommandState(
        MenuItem item,
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore)
    {
        MenuCommandStateBindings.Add(item, new MenuCommandStateBinding
        {
            CommandId = control.CommandId,
            CollapsedControl = control,
        });
        ApplyMenuCommandState(item, registry, stateStore);
    }

    private static void RefreshMenuCommandStates(
        ContextMenu menu,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore)
    {
        var pending = new Stack<MenuItem>(menu.Items.OfType<MenuItem>().Reverse());
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            ApplyMenuCommandState(item, registry, stateStore);
            foreach (var child in item.Items.OfType<MenuItem>().Reverse())
                pending.Push(child);
        }
    }

    private static void ApplyMenuCommandState(
        MenuItem item,
        IRibbonCommandRegistry? registry,
        IRibbonStateStore? stateStore)
    {
        if (!MenuCommandStateBindings.TryGetValue(item, out var binding))
            return;

        IRibbonCommand? command = null;
        var commandAvailable = registry is not null
            && registry.TryGet(binding.CommandId, out command);
        RibbonCommandState? commandState = command is IRibbonStatefulCommand stateful
            ? stateful.GetState()
            : stateStore?.TryGetState(binding.CommandId, out var storedState) == true
                ? storedState
                : null;
        var plan = binding.Definition is { } definition
            ? RibbonMenuCommandStatePlanner.Plan(definition, commandAvailable, commandState)
            : RibbonMenuCommandStatePlanner.PlanCollapsedControl(
                binding.CollapsedControl
                    ?? throw new InvalidOperationException("Collapsed menu state binding has no control."),
                commandAvailable,
                commandState);
        item.IsEnabled = plan.IsEnabled;
        item.IsCheckable = plan.IsChecked.HasValue;
        if (plan.IsChecked is { } isChecked)
            item.IsChecked = isChecked;
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
