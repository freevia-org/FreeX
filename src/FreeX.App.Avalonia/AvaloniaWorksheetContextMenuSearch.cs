using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace FreeX.App.Avalonia;

/// <summary>
/// Adds the menu search affordance of the WPF host's worksheet context menu
/// (<c>WorksheetContextMenuRenderer.AddSearchBox</c>): a text box row above the commands, followed
/// by a separator. Typing filters the live menu rows by their captions (a parent stays when any of
/// its children matches); the shared command tree and its dispatch are untouched.
/// </summary>
internal static class AvaloniaWorksheetContextMenuSearch
{
    /// <summary>Marks the search row and its separator, as the WPF renderer's tag does.</summary>
    internal const string SearchMenuItemTag = "WorksheetContextMenuSearch";

    // WPF: searchHeader Grid Width=180 Margin=2; the hosting MenuItem Padding=3.
    internal const double SearchHeaderWidth = 180;
    internal static readonly Thickness SearchHeaderMargin = new(2);
    internal static readonly Thickness SearchItemPadding = new(3);

    // WPF: TextBox Padding=6,3,6,3.
    internal static readonly Thickness SearchBoxPadding = new(6, 3, 6, 3);

    // WPF TextBox border (#ABADB3) on a white field; 12px menu text gives a 24px box.
    private static readonly IBrush SearchBoxBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
    private const double SearchBoxHeight = 24;

    /// <summary>
    /// Inserts the search row and its separator at the top of <paramref name="menu"/> and returns
    /// the search text box.
    /// </summary>
    public static TextBox AddSearchBox(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var searchLabel = UiText.Get("WorksheetContextMenu_SearchLabel");
        var searchBox = new TextBox
        {
            Padding = SearchBoxPadding,
            MinHeight = SearchBoxHeight,
            Height = SearchBoxHeight,
            MinWidth = 0,
            FontSize = AvaloniaContextMenuChrome.FontSize,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = SearchBoxBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            PlaceholderText = searchLabel,
        };
        ToolTip.SetTip(searchBox, UiText.Get("WorksheetContextMenu_SearchToolTip"));
        AutomationProperties.SetName(searchBox, searchLabel);
        AutomationProperties.SetHelpText(searchBox, UiText.Get("WorksheetContextMenu_SearchHelpText"));

        var searchHeader = new Grid
        {
            Width = SearchHeaderWidth,
            Margin = SearchHeaderMargin,
            Children = { searchBox },
        };

        var searchItem = new MenuItem
        {
            Header = searchHeader,
            Tag = SearchMenuItemTag,
            StaysOpenOnClick = true,
            Focusable = false,
            Padding = SearchItemPadding,
        };
        // The row hosts an editor, so it never shows the command highlight.
        searchItem.Resources["MenuFlyoutItemBackgroundPointerOver"] = Brushes.Transparent;
        searchItem.Resources["MenuFlyoutItemBackgroundPressed"] = Brushes.Transparent;
        AutomationProperties.SetName(searchItem, searchLabel);

        var searchSeparator = new Separator { Tag = SearchMenuItemTag };
        if (menu.Items.OfType<Separator>().FirstOrDefault() is { } commandSeparator)
            searchSeparator.Margin = commandSeparator.Margin;

        menu.Items.Insert(0, searchItem);
        menu.Items.Insert(1, searchSeparator);

        searchBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                ApplySearchFilter(menu, searchBox.Text);
        };
        searchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Down || args.KeyModifiers != KeyModifiers.None)
                return;

            var first = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => !IsSearchMenuItem(item) && item.IsVisible && item.IsEnabled);
            if (first is null)
                return;

            first.Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
        return searchBox;
    }

    /// <summary>Returns the search text box of <paramref name="menu"/>, when it has one.</summary>
    public static TextBox? FindSearchBox(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        return menu.Items
            .OfType<MenuItem>()
            .Where(IsSearchMenuItem)
            .Select(item => (item.Header as Panel)?.Children.OfType<TextBox>().FirstOrDefault())
            .FirstOrDefault(box => box is not null);
    }

    internal static bool IsSearchMenuItem(object? item) =>
        item is Control { Tag: string tag } && string.Equals(tag, SearchMenuItemTag, StringComparison.Ordinal);

    internal static void ApplySearchFilter(ContextMenu menu, string? query)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        foreach (var item in menu.Items)
        {
            if (IsSearchMenuItem(item) || item is not MenuItem menuItem)
                continue;

            var visible = !hasQuery || Matches(menuItem, query!);
            menuItem.IsVisible = visible;
            // Arrow-key traversal skips rows that are not focusable, so a filtered-out row is
            // never a navigation stop.
            menuItem.Focusable = visible;
        }

        UpdateSeparatorVisibility(menu.Items);
    }

    private static bool Matches(MenuItem item, string query)
    {
        if (CleanHeader(item).Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return item.Items.OfType<MenuItem>().Any(child => Matches(child, query));
    }

    // The caption without its access-key marker, which is the name the WPF filter matches.
    private static string CleanHeader(MenuItem item)
    {
        var header = item.Header as string ?? string.Empty;
        var index = header.IndexOf('_', StringComparison.Ordinal);
        return index < 0 ? header : header.Remove(index, 1);
    }

    private static void UpdateSeparatorVisibility(ItemCollection items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not Separator separator || IsSearchMenuItem(separator))
                continue;

            var hasVisibleCommandBefore = items
                .Take(index)
                .OfType<MenuItem>()
                .Any(item => !IsSearchMenuItem(item) && item.IsVisible);
            var hasVisibleCommandAfter = items
                .Skip(index + 1)
                .OfType<MenuItem>()
                .Any(item => item.IsVisible);
            separator.IsVisible = hasVisibleCommandBefore && hasVisibleCommandAfter;
        }
    }
}
