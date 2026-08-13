using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeX.App.Host;

public static class RibbonMenuIconSeeder
{
    private const double MenuIconSize = 18;
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnContextMenuOpened));
        _registered = true;
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            SeedMenuItems(menu.Items.OfType<MenuItem>());
    }

    private static void SeedMenuItems(IEnumerable<MenuItem> menuItems)
    {
        foreach (var item in menuItems)
        {
            if (item.Icon is null && TryResolveIcon(item, out var icon))
            {
                item.Icon = RibbonIconFactory.CreateCommandIcon(
                    icon.CommandName,
                    icon.Fallback,
                    MenuIconSize,
                    Brushes.Black);
            }

            SeedMenuItems(item.Items.OfType<MenuItem>());
        }
    }

    private static bool TryResolveIcon(MenuItem item, out MenuIconSeed icon)
    {
        icon = default;

        var header = CleanHeader(item.Header);
        if (string.IsNullOrWhiteSpace(header) || IsGallerySectionHeader(item, header))
            return false;

        var commandName = NormalizeCommandName(header);
        var fallback = RibbonCommandPresentationPlanner.GetIcon(commandName);
        if (fallback.Kind == RibbonCommandIconKind.Generic)
            return false;

        icon = new MenuIconSeed(commandName, fallback);
        return true;
    }

    private static string CleanHeader(object? header)
    {
        var text = header switch
        {
            string value => value,
            TextBlock textBlock => textBlock.Text,
            _ => header?.ToString() ?? string.Empty
        };

        return text
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("…", string.Empty, StringComparison.Ordinal)
            .Replace("...", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeCommandName(string header)
    {
        var normalized = header;

        if (normalized.Equals("Values", StringComparison.OrdinalIgnoreCase))
            return "Paste Values";
        if (normalized.Equals("Formulas", StringComparison.OrdinalIgnoreCase))
            return "Paste Formulas";
        if (normalized.Equals("Formatting", StringComparison.OrdinalIgnoreCase))
            return "Paste Formatting";
        if (normalized.Equals("Transpose", StringComparison.OrdinalIgnoreCase))
            return "Transpose Paste";
        if (normalized.Equals("More", StringComparison.OrdinalIgnoreCase))
            return "More Functions";
        if (normalized.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            return "More";

        return normalized;
    }

    private static bool IsGallerySectionHeader(MenuItem item, string header)
    {
        if (item.IsEnabled)
            return false;

        return string.Equals(header, "Directional", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Shapes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Indicators", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Ratings", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct MenuIconSeed(string CommandName, RibbonCommandIcon Fallback);
}
