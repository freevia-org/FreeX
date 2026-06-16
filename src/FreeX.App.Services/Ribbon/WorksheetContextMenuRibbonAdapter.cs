using Free.Shared.Ribbon;

namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Bridges the platform-neutral <see cref="WorksheetContextMenuPlanner"/> output into the shared
/// declarative <see cref="RibbonMenu"/> model, so the worksheet context menu can be rendered by the
/// same menu system as ribbon dropdowns/split-buttons (WPF today; Avalonia once the planner relocates).
/// </summary>
public static class WorksheetContextMenuRibbonAdapter
{
    public static RibbonMenu ToRibbonMenu(IReadOnlyList<WorksheetContextMenuCommand> commands)
    {
        var items = new RibbonMenuItem[commands.Count];
        for (var i = 0; i < commands.Count; i++)
            items[i] = ToItem(commands[i]);

        return new RibbonMenu(items);
    }

    private static RibbonMenuItem ToItem(WorksheetContextMenuCommand command)
    {
        if (command.IsSeparator)
            return RibbonMenuItem.Separator();

        // The Win32 access mnemonic ("Cu_t") is carried verbatim as the header — both the WPF and
        // Avalonia menu renderers interpret '_' as the access key, and stripping it recovers the
        // clean label for automation. Submenu parents carry Action.None → no command id.
        var commandId = command.Action == WorksheetContextMenuAction.None
            ? (RibbonCommandId?)null
            : new RibbonCommandId(command.Action.ToString());

        return new RibbonMenuItem(
            command.AccessHeader,
            CommandId: commandId,
            Children: command.HasChildren ? MapChildren(command.Children) : null)
        {
            IsEnabled = command.IsEnabled
        };
    }

    private static RibbonMenuItem[] MapChildren(IReadOnlyList<WorksheetContextMenuCommand> children)
    {
        var mapped = new RibbonMenuItem[children.Count];
        for (var i = 0; i < children.Count; i++)
            mapped[i] = ToItem(children[i]);

        return mapped;
    }
}
