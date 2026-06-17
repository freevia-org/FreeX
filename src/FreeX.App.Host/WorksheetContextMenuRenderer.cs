using System;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Services.Ribbon;
using Free.Shared.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// Renders the worksheet cell context menu from the shared declarative <see cref="RibbonMenu"/> model,
/// so context menus and ribbon dropdowns share one neutral menu model. The planner output is bridged to
/// <see cref="RibbonMenu"/> by <see cref="WorksheetContextMenuRibbonAdapter"/>; this renderer turns those
/// <see cref="RibbonMenuItem"/>s into WPF <see cref="MenuItem"/>s, reproducing the worksheet menu's
/// automation contract (clean Name, "WorksheetContextMenu_*" AutomationId) and dispatching leaf clicks
/// back through <see cref="WorksheetContextMenuAction"/>.
/// </summary>
internal static class WorksheetContextMenuRenderer
{
    public static void AddItems(
        ItemCollection target,
        System.Collections.Generic.IReadOnlyList<RibbonMenuItem> items,
        Action<WorksheetContextMenuAction> dispatch)
    {
        foreach (var item in items)
            AddItem(target, item, dispatch);
    }

    /// <summary>
    /// Renders <see cref="RibbonMenuItem"/>s into WPF <see cref="MenuItem"/>s, dispatching leaf clicks by the
    /// item's raw <see cref="RibbonCommandId"/>. Checkable items (<see cref="RibbonMenuItem.IsChecked"/> non-null)
    /// render with <c>IsCheckable=true</c> and the carried check state. Used by menus (e.g. the waterfall-chart
    /// point menu) whose dispatch is not the worksheet <see cref="WorksheetContextMenuAction"/> enum.
    /// </summary>
    public static void AddItemsByCommandId(
        ItemCollection target,
        System.Collections.Generic.IReadOnlyList<RibbonMenuItem> items,
        Action<RibbonCommandId> dispatch)
    {
        foreach (var item in items)
            AddItem(target, item, dispatch);
    }

    private static void AddItem(
        ItemCollection target,
        RibbonMenuItem item,
        Action<RibbonCommandId> dispatch)
    {
        if (item.Kind == Free.Shared.Ribbon.RibbonMenuItemKind.Separator)
        {
            target.Add(new Separator());
            return;
        }

        var accessHeader = item.Header;
        var cleanHeader = StripAccessMnemonic(accessHeader);

        var menuItem = new MenuItem { Header = accessHeader, IsEnabled = item.IsEnabled };
        ApplyCheckable(menuItem, item);
        AutomationProperties.SetName(menuItem, cleanHeader);

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                AddItem(menuItem.Items, child, dispatch);
        }
        else if (item.CommandId is { } commandId)
        {
            menuItem.Click += (_, _) => dispatch(commandId);
        }

        target.Add(menuItem);
    }

    // Makes the WPF item checkable iff the shared model carries a check state. A null IsChecked leaves the
    // item as a plain command (preserving the existing worksheet cell-menu behavior verbatim).
    private static void ApplyCheckable(MenuItem menuItem, RibbonMenuItem item)
    {
        if (item.IsChecked is not { } isChecked)
            return;

        menuItem.IsCheckable = true;
        menuItem.IsChecked = isChecked;
    }

    private static void AddItem(
        ItemCollection target,
        RibbonMenuItem item,
        Action<WorksheetContextMenuAction> dispatch)
    {
        if (item.Kind == Free.Shared.Ribbon.RibbonMenuItemKind.Separator)
        {
            target.Add(new Separator());
            return;
        }

        // RibbonMenuItem.Header carries the access mnemonic verbatim (e.g. "Cu_t"); stripping the single
        // '_' recovers the clean label the automation Name used before this rendered from the shared model.
        var accessHeader = item.Header;
        var cleanHeader = StripAccessMnemonic(accessHeader);
        var action = ResolveAction(item.CommandId);

        var menuItem = new MenuItem { Header = accessHeader, IsEnabled = item.IsEnabled };
        ApplyCheckable(menuItem, item);
        AutomationProperties.SetName(menuItem, cleanHeader);
        AutomationProperties.SetAutomationId(
            menuItem,
            action == WorksheetContextMenuAction.None
                ? $"WorksheetContextMenu_{NormalizeAutomationId(cleanHeader)}"
                : $"WorksheetContextMenu_{action}");

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                AddItem(menuItem.Items, child, dispatch);
        }
        else
        {
            menuItem.Click += (_, _) => dispatch(action);
        }

        target.Add(menuItem);
    }

    private static WorksheetContextMenuAction ResolveAction(RibbonCommandId? commandId) =>
        commandId is { } id
            ? Enum.Parse<WorksheetContextMenuAction>(id.Value)
            : WorksheetContextMenuAction.None;

    // Recovers the clean label by removing the single access-key marker ('_') the planner inserts.
    private static string StripAccessMnemonic(string accessHeader)
    {
        var index = accessHeader.IndexOf('_');
        return index < 0 ? accessHeader : accessHeader.Remove(index, 1);
    }

    private static string NormalizeAutomationId(string header)
    {
        var builder = new System.Text.StringBuilder(header.Length);
        foreach (var character in header)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.Length == 0 ? "Item" : builder.ToString();
    }
}
