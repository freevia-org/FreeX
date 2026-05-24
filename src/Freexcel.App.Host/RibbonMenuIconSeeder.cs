using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Freexcel.App.Host;

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
        if (fallback.Kind == RibbonCommandIconKind.Generic && !TryResolveGenericHeader(header, out fallback))
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

    private static bool TryResolveGenericHeader(string header, out RibbonCommandIcon fallback)
    {
        fallback = header.ToLowerInvariant() switch
        {
            "values" => new RibbonCommandIcon(RibbonCommandIconKind.Number),
            "formulas" => new RibbonCommandIcon(RibbonCommandIconKind.Function),
            "formatting" => new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter),
            "transpose" => new RibbonCommandIcon(RibbonCommandIconKind.Rotate),
            "horizontal" => new RibbonCommandIcon(RibbonCommandIconKind.Align),
            "angle counterclockwise" => new RibbonCommandIcon(RibbonCommandIconKind.Orientation),
            "angle clockwise" => new RibbonCommandIcon(RibbonCommandIconKind.Orientation),
            "vertical text" => new RibbonCommandIcon(RibbonCommandIconKind.Orientation),
            "rotate text up" => new RibbonCommandIcon(RibbonCommandIconKind.Rotate),
            "rotate text down" => new RibbonCommandIcon(RibbonCommandIconKind.Rotate),
            "greater than" => new RibbonCommandIcon(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning),
            "less than" => new RibbonCommandIcon(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning),
            "between" => new RibbonCommandIcon(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning),
            "equal to" => new RibbonCommandIcon(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning),
            "text that contains" => new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            "duplicate values" => new RibbonCommandIcon(RibbonCommandIconKind.Delete),
            "above average" => new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
            "below average" => new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
            "normal" => new RibbonCommandIcon(RibbonCommandIconKind.Grid),
            "good" => new RibbonCommandIcon(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color),
            "bad" => new RibbonCommandIcon(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning),
            "neutral" => new RibbonCommandIcon(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Theme),
            "input" => new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            "output" => new RibbonCommandIcon(RibbonCommandIconKind.Table),
            "calculation" => new RibbonCommandIcon(RibbonCommandIconKind.Function),
            "check cell" => new RibbonCommandIcon(RibbonCommandIconKind.Warning),
            "linked cell" => new RibbonCommandIcon(RibbonCommandIconKind.Link),
            "heading 1" => new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            "heading 2" => new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            "total" => new RibbonCommandIcon(RibbonCommandIconKind.Sum),
            "sum" => new RibbonCommandIcon(RibbonCommandIconKind.Sum),
            "average" => new RibbonCommandIcon(RibbonCommandIconKind.Function),
            "count numbers" => new RibbonCommandIcon(RibbonCommandIconKind.Number),
            "max" => new RibbonCommandIcon(RibbonCommandIconKind.Function),
            "min" => new RibbonCommandIcon(RibbonCommandIconKind.Function),
            "automatic" => new RibbonCommandIcon(RibbonCommandIconKind.Refresh),
            "manual" => new RibbonCommandIcon(RibbonCommandIconKind.Warning),
            "tiled" => new RibbonCommandIcon(RibbonCommandIconKind.Grid),
            "cascade" => new RibbonCommandIcon(RibbonCommandIconKind.Window),
            _ => new RibbonCommandIcon(RibbonCommandIconKind.Generic)
        };

        return fallback.Kind != RibbonCommandIconKind.Generic;
    }

    private static bool IsGallerySectionHeader(MenuItem item, string header)
    {
        if (item.IsEnabled)
            return false;

        return header is "Directional" or "Shapes" or "Indicators" or "Ratings";
    }

    private readonly record struct MenuIconSeed(string CommandName, RibbonCommandIcon Fallback);
}
