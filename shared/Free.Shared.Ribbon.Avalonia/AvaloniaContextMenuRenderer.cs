using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Renders a platform-neutral declarative <see cref="RibbonMenu"/> into an Avalonia
/// <see cref="ContextMenu"/> / <see cref="MenuItem"/> tree.
///
/// This is the Avalonia counterpart to the WPF context-menu renderer: it reuses the same shared
/// menu model (separators, nested submenus, state-driven <see cref="RibbonMenuItem.IsEnabled"/>) so
/// the worksheet cell context menu is built from exactly the same neutral plan as the ribbon.
///
/// Headers are passed through verbatim — Avalonia's <see cref="MenuItem.Header"/> already treats
/// <c>_</c> as the access-key marker, matching the Win32 mnemonic convention carried by the planner
/// (e.g. <c>"Cu_t"</c>). Leaf items whose <see cref="RibbonMenuItem.CommandId"/> is non-null route
/// their click to the supplied <paramref name="dispatch"/> callback; submenu parents (command id
/// <c>null</c>) only host children and never dispatch.
/// </summary>
public static class AvaloniaContextMenuRenderer
{
    /// <summary>Builds a <see cref="ContextMenu"/> from <paramref name="menu"/>.</summary>
    /// <param name="menu">The neutral menu tree to render.</param>
    /// <param name="dispatch">Invoked with a leaf item's command id when it is clicked.</param>
    public static ContextMenu BuildContextMenu(RibbonMenu menu, Action<RibbonCommandId> dispatch)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(dispatch);

        var contextMenu = new ContextMenu();
        contextMenu.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Escape)
                    return;
                contextMenu.Close();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        foreach (var item in menu.Items)
            contextMenu.Items.Add(BuildItem(item, dispatch));

        return contextMenu;
    }

    private static Control BuildItem(RibbonMenuItem item, Action<RibbonCommandId> dispatch)
    {
        if (item.Kind == RibbonMenuItemKind.Separator)
            return new Separator();

        var presentation = RibbonMenuItemPresentationPlanner.Plan(item);

        var menuItem = new MenuItem
        {
            Header = presentation.Header,
            InputGesture = TryParseGesture(presentation.InputGestureText),
            Tag = item.CommandId?.Value,
            IsEnabled = item.IsEnabled,
        };
        if (item.IsChecked is { } isChecked)
        {
            menuItem.ToggleType = MenuItemToggleType.CheckBox;
            menuItem.IsChecked = isChecked;
        }

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                menuItem.Items.Add(BuildItem(child, dispatch));
        }
        else if (item.CommandId is { } commandId)
        {
            menuItem.Click += (_, _) => dispatch(commandId);
        }

        return menuItem;
    }

    private static KeyGesture? TryParseGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return null;

        try
        {
            return KeyGesture.Parse(gesture);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
