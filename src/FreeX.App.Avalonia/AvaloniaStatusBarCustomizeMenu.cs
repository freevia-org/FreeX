using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds the Avalonia "Customize Status Bar" right-click <see cref="ContextMenu"/> from the neutral
/// <see cref="StatusBarCustomizeContextMenuPlanner"/> — the same declarative plan the WPF host renders.
/// Title/separator entries are passed through; each checkable toggle becomes a checkbox
/// <see cref="MenuItem"/> whose live state is read from <paramref name="getOption"/> and whose click flips
/// the option through <paramref name="onToggle"/>. Factored out of the shell so it can be exercised
/// headlessly.
/// </summary>
internal static class AvaloniaStatusBarCustomizeMenu
{
    /// <summary>
    /// Builds the customize menu. <paramref name="registeredItems"/>, when supplied, is populated with the
    /// checkable toggle items keyed by their OptionTag so the caller can refresh their checked state on
    /// open (mirroring the WPF host's registry).
    /// </summary>
    public static ContextMenu Build(
        Func<string, bool> getOption,
        Action<string, bool> onToggle,
        IDictionary<string, MenuItem>? registeredItems = null)
    {
        ArgumentNullException.ThrowIfNull(getOption);
        ArgumentNullException.ThrowIfNull(onToggle);

        registeredItems?.Clear();

        var menu = new ContextMenu();
        AutomationProperties.SetName(menu, UiText.Get("StatusBar_CustomizeStatusBar"));

        var items = new List<Control>();
        foreach (var command in StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands())
            items.Add(BuildItem(command, getOption, onToggle, registeredItems));

        menu.ItemsSource = items;
        return menu;
    }

    private static Control BuildItem(
        StatusBarCustomizeMenuCommand command,
        Func<string, bool> getOption,
        Action<string, bool> onToggle,
        IDictionary<string, MenuItem>? registeredItems)
    {
        if (command.IsSeparator)
            return new Separator();

        var menuItem = new MenuItem
        {
            Header = UiText.Get(ResolveResourceKey(command.ResourceKey)),
            IsEnabled = command.IsEnabled,
        };

        if (!string.IsNullOrEmpty(command.AutomationId))
            AutomationProperties.SetAutomationId(menuItem, command.AutomationId);

        if (command.IsCheckable && !string.IsNullOrEmpty(command.OptionTag))
        {
            var optionTag = command.OptionTag;
            menuItem.ToggleType = MenuItemToggleType.CheckBox;
            menuItem.IsChecked = getOption(optionTag);
            menuItem.Tag = optionTag;
            menuItem.Click += (_, _) => onToggle(optionTag, menuItem.IsChecked);
            registeredItems?.Add(optionTag, menuItem);
        }

        return menuItem;
    }

    private static string ResolveResourceKey(string resourceKey) =>
        resourceKey == StatusBarCustomizeResourceKeys.Zoom
            ? "MainWindow_Text_Zoom"
            : resourceKey;
}
