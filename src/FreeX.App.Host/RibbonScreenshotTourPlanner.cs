namespace FreeX.App.Host;

internal static class RibbonScreenshotTourPlanner
{
    public static IReadOnlyList<(string Header, string FileName)> DefaultTabs { get; } =
    [
        ("Home", "Home"),
        ("Insert", "Insert"),
        ("Draw", "Draw"),
        ("Page Layout", "Page_Layout"),
        ("Formulas", "Formulas"),
        ("Data", "Data"),
        ("Review", "Review"),
        ("View", "View"),
        ("Help", "Help")
    ];

    public static IReadOnlyList<(string Header, string FileName)> FilterTabs(
        IReadOnlyList<(string Header, string FileName)> tabs,
        string? requestedTabs)
    {
        if (string.IsNullOrWhiteSpace(requestedTabs))
            return tabs;

        var requested = requestedTabs
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tabs
            .Where(tab => requested.Contains(tab.Header) || requested.Contains(tab.FileName))
            .ToList();
    }

    public static IReadOnlyList<double> ParseWidths(string? requestedWidths)
    {
        if (string.IsNullOrWhiteSpace(requestedWidths))
            return [];

        var widths = new List<double>();
        foreach (var value in requestedWidths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var width) &&
                width > 0)
            {
                widths.Add(width);
            }
        }

        return widths;
    }
}
