using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

internal static class AvaloniaManagedContextMenu
{
    private static readonly object Marker = new();

    public static ContextMenu Attach(Control anchor, Func<IReadOnlyList<Control>> buildItems)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(buildItems);

        if (anchor.ContextMenu is { Tag: var tag } existing && ReferenceEquals(tag, Marker))
            return existing;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Tag = Marker,
        };

        void Populate()
        {
            menu.Items.Clear();
            foreach (var item in buildItems())
                menu.Items.Add(item);
        }

        Populate();
        menu.Opened += (_, _) =>
        {
            Populate();
            Dispatcher.UIThread.Post(() =>
                menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)?.Focus());
        };
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => anchor.Focus());
        menu.KeyDown += (_, args) =>
        {
            if (!menu.IsOpen)
                return;

            if (args.Key == Key.Escape)
            {
                menu.Close();
                args.Handled = true;
                return;
            }

            if (args.Handled || args.Source is not Control)
                return;

            var item = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(candidate =>
                    candidate.IsEnabled &&
                    candidate.InputGesture is KeyGesture gesture &&
                    gesture.Key == args.Key &&
                    gesture.KeyModifiers == args.KeyModifiers);
            if (item is null)
                return;

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            menu.Close();
            args.Handled = true;
        };
        anchor.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(anchor).Properties.IsRightButtonPressed)
                return;

            menu.Open(anchor);
            args.Handled = true;
        };
        anchor.KeyDown += (_, args) =>
        {
            if (!IsKeyboardInvocation(args.Key, args.KeyModifiers))
                return;

            menu.Open(anchor);
            args.Handled = true;
        };
        anchor.ContextMenu = menu;
        return menu;
    }

    internal static bool IsKeyboardInvocation(Key key, KeyModifiers modifiers) =>
        key == Key.Apps || key == Key.F10 && modifiers == KeyModifiers.Shift;
}

internal static class AvaloniaBackstageRecentFileContextMenu
{
    public static IReadOnlyList<Control> BuildItems(
        bool isPinned,
        string fileName,
        Func<string, string> resolveHeader,
        Action<BackstageRecentFileMenuAction> dispatch)
    {
        var commands = isPinned
            ? BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands()
            : BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands();

        return commands.Select(command =>
        {
            var item = new MenuItem
            {
                Header = resolveHeader(command.ResourceKey),
                Tag = command.Action,
            };
            AutomationProperties.SetAutomationId(item, command.AutomationId);
            AutomationProperties.SetName(
                item,
                UiText.Format("RecentFile_AutomationNameFormat", command.CommandName, fileName));
            AutomationProperties.SetHelpText(
                item,
                UiText.Format("RecentFile_HelpTextFormat", command.CommandName, fileName));
            item.Click += (_, _) => dispatch(command.Action);
            return (Control)item;
        }).ToArray();
    }
}

internal static class AvaloniaQuickAccessToolbarContextMenu
{
    public static IReadOnlyList<Control> BuildCustomizationItems(
        QuickAccessToolbarCustomizationMenuState state,
        Func<string, string> resolveHeader,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        BuildItems(
            QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(state),
            resolveHeader,
            dispatch);

    public static IReadOnlyList<Control> BuildHistoryItems(
        QuickAccessToolbarHistoryMenuState state,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        BuildItems(
            QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(state),
            resourceKey => resourceKey,
            dispatch);

    private static IReadOnlyList<Control> BuildItems(
        IReadOnlyList<QuickAccessToolbarMenuCommand> commands,
        Func<string, string> resolveHeader,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        commands.Select(command =>
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrEmpty(command.Header)
                    ? resolveHeader(command.ResourceKey)
                    : command.Header,
                IsEnabled = command.IsEnabled,
                Tag = command.Action,
            };
            if (!string.IsNullOrEmpty(command.AutomationId))
                AutomationProperties.SetAutomationId(item, command.AutomationId);
            if (command.Action != QuickAccessToolbarMenuAction.None)
                item.Click += (_, _) => dispatch(command);
            return (Control)item;
        }).ToArray();
}
