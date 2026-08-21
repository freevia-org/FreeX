using System;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    internal const string SearchMenuItemTag = "WorksheetContextMenuSearch";

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

    /// <summary>
    /// Adds Excel's familiar <c>Search the menus</c> affordance above a worksheet context menu.
    /// Filtering only changes the live WPF presentation; the shared command tree and its action
    /// routing remain intact.
    /// </summary>
    public static TextBox AddSearchBox(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var searchBox = new TextBox
        {
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = UiText.Get("WorksheetContextMenu_SearchToolTip")
        };
        var searchLabel = UiText.Get("WorksheetContextMenu_SearchLabel");
        AutomationProperties.SetName(searchBox, searchLabel);
        AutomationProperties.SetHelpText(searchBox, UiText.Get("WorksheetContextMenu_SearchHelpText"));

        var searchHeader = new Grid { Width = 180, Margin = new Thickness(2) };
        searchHeader.Children.Add(searchBox);
        var watermark = new TextBlock
        {
            Text = searchLabel,
            Margin = new Thickness(9, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
            IsHitTestVisible = false
        };
        searchHeader.Children.Add(watermark);

        var searchItem = new MenuItem
        {
            Header = searchHeader,
            Tag = SearchMenuItemTag,
            StaysOpenOnClick = true,
            Focusable = false,
            Padding = new Thickness(3),
            IsEnabled = true
        };
        AutomationProperties.SetName(searchItem, searchLabel);
        menu.Items.Insert(0, searchItem);
        menu.Items.Insert(1, new Separator { Tag = SearchMenuItemTag });

        searchBox.TextChanged += (_, _) =>
        {
            watermark.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplySearchFilter(menu, searchBox.Text);
        };
        return searchBox;
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

    internal static bool IsSearchMenuItem(object? item) =>
        item is FrameworkElement { Tag: SearchMenuItemTag };

    private static void ApplySearchFilter(ContextMenu menu, string? query)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        foreach (var item in menu.Items)
        {
            if (IsSearchMenuItem(item))
                continue;

            if (item is MenuItem menuItem)
                menuItem.Visibility = !hasQuery || Matches(menuItem, query!)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        UpdateSeparatorVisibility(menu.Items);
    }

    private static bool Matches(MenuItem item, string query)
    {
        var name = AutomationProperties.GetName(item);
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return item.Items.OfType<MenuItem>().Any(child => Matches(child, query));
    }

    private static void UpdateSeparatorVisibility(ItemCollection items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not Separator separator || IsSearchMenuItem(separator))
                continue;

            var hasVisibleCommandBefore = items.Cast<object>().Take(index).OfType<MenuItem>()
                .Any(item => !IsSearchMenuItem(item) && item.Visibility == Visibility.Visible);
            var hasVisibleCommandAfter = items.Cast<object>().Skip(index + 1).OfType<MenuItem>()
                .Any(item => item.Visibility == Visibility.Visible);
            separator.Visibility = hasVisibleCommandBefore && hasVisibleCommandAfter
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

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
