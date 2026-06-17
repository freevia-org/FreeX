using Free.Shared.Ribbon;

namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Bridges the platform-neutral <see cref="WaterfallChartContextMenuPlanner"/> output into the shared
/// declarative <see cref="RibbonMenu"/> model, so the waterfall-chart point context menu renders through
/// the same menu system as the worksheet context menu and ribbon dropdowns. The single "Set as Total"
/// item is checkable: its <see cref="RibbonMenuItem.IsChecked"/> carries the toggle state and its
/// <see cref="RibbonMenuItem.CommandId"/> is the <see cref="ToggleTotalCommandId"/> the host dispatches to
/// <c>ToggleWaterfallTotalPoint</c>.
/// </summary>
public static class WaterfallChartContextMenuRibbonAdapter
{
    /// <summary>Command id carried by the checkable "Set as Total" item.</summary>
    public static readonly RibbonCommandId ToggleTotalCommandId = new("WaterfallSetAsTotal");

    public static RibbonMenu ToRibbonMenu(IReadOnlyList<WaterfallChartContextMenuCommand> commands)
    {
        var items = new RibbonMenuItem[commands.Count];
        for (var i = 0; i < commands.Count; i++)
            items[i] = ToItem(commands[i]);

        return new RibbonMenu(items);
    }

    private static RibbonMenuItem ToItem(WaterfallChartContextMenuCommand command) =>
        // The Win32 access mnemonic ("_Set as Total") is carried verbatim as the header; the menu renderers
        // interpret '_' as the access key and strip it for automation. IsChecked makes the item checkable.
        new(command.AccessHeader, CommandId: ToggleTotalCommandId)
        {
            IsEnabled = command.IsEnabled,
            IsChecked = command.IsChecked
        };
}
