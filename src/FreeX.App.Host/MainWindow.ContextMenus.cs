using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Pivot field-area context menu ─────────────────────────────────────────
    // Builds the right-click menu shared by the PivotTable field-area ListBoxes from the neutral
    // PivotFieldContextMenuPlanner so the menu's labels, order, keytips, and command names are single-sourced
    // with the Avalonia port instead of duplicated five times in XAML. The visible menu is identical to the
    // previous XAML ContextMenus (the four bucket lists carry the trailing "Remove" item; the available-fields
    // list omits it). Field-target resolution is unchanged: each list's PreviewMouseRightButtonDown selects the
    // hovered item, and the dispatched handlers read that selection exactly as before.

    private void PivotFieldList_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        var includeRemove = !ReferenceEquals(list, PivotAvailableFieldsList);
        list.ContextMenu = BuildPivotFieldContextMenu(includeRemove);
    }

    private ContextMenu BuildPivotFieldContextMenu(bool includeRemove)
    {
        var menu = new ContextMenu();
        foreach (var command in PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove))
            AddPivotFieldContextMenuItem(menu.Items, command);

        return menu;
    }

    private void AddPivotFieldContextMenuItem(ItemCollection target, PivotFieldContextMenuCommand command)
    {
        if (command.IsSeparator)
        {
            target.Add(new Separator());
            return;
        }

        var menuItem = new MenuItem
        {
            Header = UiText.Get(command.ResourceKey),
            IsEnabled = command.IsEnabled
        };

        if (!string.IsNullOrEmpty(command.KeyTip))
            RibbonTooltip.SetKeyTip(menuItem, command.KeyTip);
        if (!string.IsNullOrEmpty(command.CommandName))
            RibbonMetadata.SetCommandName(menuItem, command.CommandName);

        if (ResolvePivotFieldContextMenuHandler(command.Action) is { } handler)
            menuItem.Click += handler;

        target.Add(menuItem);
    }

    // Maps neutral planner actions to the existing pivot-field Click handlers. Every action routes to the
    // same handler the hand-authored ContextMenu wired, so dispatch resolves the field through the list
    // selection (GetSelectedPivotFieldListItem) exactly as before.
    private RoutedEventHandler? ResolvePivotFieldContextMenuHandler(PivotFieldContextMenuAction action) =>
        action switch
        {
            PivotFieldContextMenuAction.SortAscending => PivotFieldSortAscendingMenuItem_Click,
            PivotFieldContextMenuAction.SortDescending => PivotFieldSortDescendingMenuItem_Click,
            PivotFieldContextMenuAction.SelectItems => PivotFieldSelectItemsMenuItem_Click,
            PivotFieldContextMenuAction.LabelFilter => PivotFieldLabelFilterMenuItem_Click,
            PivotFieldContextMenuAction.ValueFilter => PivotFieldValueFilterMenuItem_Click,
            PivotFieldContextMenuAction.ClearFilter => PivotFieldClearFilterMenuItem_Click,
            PivotFieldContextMenuAction.ValueFieldSettings => PivotFieldValueSettingsMenuItem_Click,
            PivotFieldContextMenuAction.Remove => PivotFieldRemoveBtn_Click,
            _ => null
        };

    // ── Status-bar "Customize Status Bar" context menu ────────────────────────
    // Builds the status-bar right-click customize menu from the neutral StatusBarCustomizeContextMenuPlanner.
    // The toggles still persist through StatusBarCustomizeMenuItem_Click (resolved by the carried Tag), and the
    // live checked state is refreshed on open by StatusBarCustomizeMenu_Opened against the named menu items.

    private void StatusBarRoot_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is null)
            element.ContextMenu = BuildStatusBarCustomizeContextMenu();

        RefreshKeyLockIndicators();
    }

    private ContextMenu BuildStatusBarCustomizeContextMenu()
    {
        var menu = new ContextMenu();
        AutomationProperties.SetName(menu, UiText.Get("StatusBar_CustomizeStatusBar"));
        menu.Opened += StatusBarCustomizeMenu_Opened;

        foreach (var command in StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands())
            AddStatusBarCustomizeMenuItem(menu.Items, command);

        return menu;
    }

    private void AddStatusBarCustomizeMenuItem(ItemCollection target, StatusBarCustomizeMenuCommand command)
    {
        if (command.IsSeparator)
        {
            target.Add(new Separator());
            return;
        }

        var menuItem = new MenuItem
        {
            Header = UiText.Get(command.ResourceKey),
            IsEnabled = command.IsEnabled,
            IsCheckable = command.IsCheckable
        };

        if (!string.IsNullOrEmpty(command.KeyTip))
            RibbonTooltip.SetKeyTip(menuItem, command.KeyTip);
        if (!string.IsNullOrEmpty(command.AutomationId))
        {
            AutomationProperties.SetAutomationId(menuItem, command.AutomationId);
            // Preserve the previous XAML x:Name (== AutomationId) so name-based lookups (e.g. UI test harnesses)
            // keep resolving these items after the move to runtime construction.
            menuItem.Name = command.AutomationId;
        }

        if (command.IsCheckable && !string.IsNullOrEmpty(command.OptionTag))
        {
            menuItem.Tag = command.OptionTag;
            menuItem.Click += StatusBarCustomizeMenuItem_Click;
            RegisterStatusBarCustomizeMenuItem(command.OptionTag, menuItem);
        }

        target.Add(menuItem);
    }

    // ── Backstage recent/pinned file context menus ────────────────────────────
    // Builds the recent/pinned file right-click menus from the neutral BackstageRecentFileContextMenuPlanner.
    // Each menu is attached at load time to the per-item file Button; the menu's DataContext flows from
    // PlacementTarget.DataContext (the RecentFileViewModel), exactly as the previous XAML did, so per-item
    // automation Name/HelpText bindings still describe the specific file and the existing handlers
    // (Ss{Pin,Unpin,RemoveRecent}Item_Click) resolve the right file through PlacementTarget.DataContext.

    private void SsRecentFileItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        ApplyBackstageRecentFileRowDescriptor(element, FreeXBackstageRecentFileRowKind.Recent);
        if (element.ContextMenu is null)
            element.ContextMenu = BuildBackstageRecentFileContextMenu(pinned: false);
    }

    private void SsPinnedFileItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        ApplyBackstageRecentFileRowDescriptor(element, FreeXBackstageRecentFileRowKind.Pinned);
        if (element.ContextMenu is null)
            element.ContextMenu = BuildBackstageRecentFileContextMenu(pinned: true);
    }

    private ContextMenu BuildBackstageRecentFileContextMenu(bool pinned)
    {
        var menu = new ContextMenu();

        // Mirror the XAML's `DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}"`
        // so each item's automation Name/HelpText bind against the placement target's RecentFileViewModel.
        menu.SetBinding(FrameworkElement.DataContextProperty, new Binding("PlacementTarget.DataContext")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self)
        });

        var commands = pinned
            ? BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands()
            : BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands();
        foreach (var command in commands)
            AddBackstageRecentFileMenuItem(menu.Items, command);

        return menu;
    }

    private void AddBackstageRecentFileMenuItem(ItemCollection target, BackstageRecentFileMenuCommand command)
    {
        var menuItem = new MenuItem { Header = UiText.Get(command.ResourceKey) };

        if (!string.IsNullOrEmpty(command.KeyTip))
            RibbonTooltip.SetKeyTip(menuItem, command.KeyTip);
        if (!string.IsNullOrEmpty(command.CommandName))
            RibbonMetadata.SetCommandName(menuItem, command.CommandName);
        if (!string.IsNullOrEmpty(command.AutomationId))
            AutomationProperties.SetAutomationId(menuItem, command.AutomationId);
        if (!string.IsNullOrEmpty(command.AutomationNamePath))
            menuItem.SetBinding(AutomationProperties.NameProperty, new Binding(command.AutomationNamePath));
        if (!string.IsNullOrEmpty(command.AutomationHelpTextPath))
            menuItem.SetBinding(AutomationProperties.HelpTextProperty, new Binding(command.AutomationHelpTextPath));

        if (ResolveBackstageRecentFileHandler(command.Action) is { } handler)
            menuItem.Click += handler;

        target.Add(menuItem);
    }

    private RoutedEventHandler? ResolveBackstageRecentFileHandler(BackstageRecentFileMenuAction action) =>
        action switch
        {
            BackstageRecentFileMenuAction.Pin => SsPinItem_Click,
            BackstageRecentFileMenuAction.Unpin => SsUnpinItem_Click,
            BackstageRecentFileMenuAction.Remove => SsRemoveRecentItem_Click,
            _ => null
        };
}
