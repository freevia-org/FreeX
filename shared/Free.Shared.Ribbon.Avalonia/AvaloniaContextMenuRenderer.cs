using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
        var topLevelItems = new List<MenuItem>();
        contextMenu.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Source is MenuItem)
                    return;

                var key = args.Key switch
                {
                    Key.Escape => RibbonPopupKeyboardKey.Escape,
                    Key.Left => RibbonPopupKeyboardKey.Left,
                    _ => (RibbonPopupKeyboardKey?)null,
                };
                if (key is not null &&
                    RibbonPopupInteractionPlanner.PlanKey(
                        key.Value,
                        Array.Empty<RibbonPopupFocusItem>(),
                        currentIndex: -1,
                        hasChildren: false,
                        isNestedSubmenu: false).Action == RibbonPopupKeyboardAction.ClosePopup)
                {
                    contextMenu.Close();
                    args.Handled = true;
                }
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        foreach (var item in menu.Items)
            contextMenu.Items.Add(BuildItem(item, dispatch));

        topLevelItems.AddRange(contextMenu.Items.OfType<MenuItem>());
        foreach (var item in topLevelItems)
            ConfigureMenuItem(item, parent: null, topLevelItems, contextMenu);

        contextMenu.Opened += (_, _) =>
        {
            var states = topLevelItems
                .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
                .ToArray();
            var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
            if (index >= 0)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (contextMenu.IsOpen)
                            topLevelItems[index].Focus(NavigationMethod.Tab);
                    },
                    DispatcherPriority.Input);
            }
        };
        contextMenu.Closed += (_, _) =>
        {
            contextMenu.PlacementTarget?.Focus(NavigationMethod.Tab);
        };

        return contextMenu;
    }

    private static void ConfigureMenuItem(
        MenuItem item,
        MenuItem? parent,
        IReadOnlyList<MenuItem> siblings,
        ContextMenu contextMenu)
    {
        var children = item.Items.OfType<MenuItem>().ToArray();
        foreach (var child in children)
            ConfigureMenuItem(child, item, children, contextMenu);

        item.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => HandleMenuItemKey(contextMenu, item, parent, siblings, children, args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private static void HandleMenuItemKey(
        ContextMenu contextMenu,
        MenuItem currentItem,
        MenuItem? parent,
        IReadOnlyList<MenuItem> siblings,
        IReadOnlyList<MenuItem> children,
        KeyEventArgs args)
    {
        if (args.Handled || !ReferenceEquals(args.Source, currentItem))
            return;

        var key = args.Key switch
        {
            Key.Escape => RibbonPopupKeyboardKey.Escape,
            Key.Left => RibbonPopupKeyboardKey.Left,
            Key.Right => RibbonPopupKeyboardKey.Right,
            Key.Up => RibbonPopupKeyboardKey.Up,
            Key.Down => RibbonPopupKeyboardKey.Down,
            Key.Home => RibbonPopupKeyboardKey.Home,
            Key.End => RibbonPopupKeyboardKey.End,
            _ => (RibbonPopupKeyboardKey?)null,
        };
        if (key is null)
            return;

        var currentIndex = -1;
        for (var index = 0; index < siblings.Count; index++)
        {
            if (ReferenceEquals(siblings[index], currentItem))
            {
                currentIndex = index;
                break;
            }
        }

        var states = siblings
            .Select(candidate => new RibbonPopupFocusItem(candidate.Focusable, candidate.IsEnabled))
            .ToArray();
        var decision = RibbonPopupInteractionPlanner.PlanKey(
            key.Value,
            states,
            currentIndex,
            children.Count > 0,
            parent is not null);
        switch (decision.Action)
        {
            case RibbonPopupKeyboardAction.OpenSubmenu:
                currentItem.IsSubMenuOpen = true;
                FocusFirstEnabledChild(currentItem, children);
                args.Handled = true;
                return;
            case RibbonPopupKeyboardAction.CloseSubmenu when parent is not null:
                parent.IsSubMenuOpen = false;
                parent.Focusable = true;
                parent.IsSelected = true;
                parent.Focus(NavigationMethod.Tab);
                args.Handled = true;
                return;
            case RibbonPopupKeyboardAction.ClosePopup:
                contextMenu.Close();
                args.Handled = true;
                return;
            case RibbonPopupKeyboardAction.FocusItem when decision.TargetIndex >= 0:
                siblings[decision.TargetIndex].Focus(NavigationMethod.Directional);
                args.Handled = true;
                return;
        }
    }

    private static void FocusFirstEnabledChild(
        MenuItem parent,
        IReadOnlyList<MenuItem> children)
    {
        var states = children
            .Select(child => new RibbonPopupFocusItem(child.Focusable, child.IsEnabled))
            .ToArray();
        var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
        if (index < 0)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (parent.IsSubMenuOpen)
                    children[index].Focus(NavigationMethod.Directional);
            },
            DispatcherPriority.Input);
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
            menuItem.Click += (_, _) =>
            {
                // See RibbonCommandFaultReporter: an exception escaping an Avalonia click handler
                // terminates the process, so context-menu dispatch is contained here.
                try
                {
                    dispatch(commandId);
                }
                catch (Exception ex)
                {
                    RibbonCommandFaultReporter.Report(ex, commandId.Value);
                }
            };
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
