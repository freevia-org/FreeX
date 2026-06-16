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
