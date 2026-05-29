namespace FreeX.App.Host;

public static class RibbonTopLevelKeyTipRouter
{
    public static RibbonTopLevelKeyTipAction? Resolve(string keyTip)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        return keyTip.Trim().ToUpperInvariant() switch
        {
            "F" => RibbonTopLevelKeyTipAction.BackstageFile,
            "H" => RibbonTopLevelKeyTipAction.RibbonTab("Home"),
            "N" => RibbonTopLevelKeyTipAction.RibbonTab("Insert"),
            "J" => RibbonTopLevelKeyTipAction.RibbonTab("Draw"),
            "P" => RibbonTopLevelKeyTipAction.RibbonTab("Page Layout"),
            "M" => RibbonTopLevelKeyTipAction.RibbonTab("Formulas"),
            "A" => RibbonTopLevelKeyTipAction.RibbonTab("Data"),
            "D" => RibbonTopLevelKeyTipAction.RibbonTab("Data"),
            "R" => RibbonTopLevelKeyTipAction.RibbonTab("Review"),
            "W" => RibbonTopLevelKeyTipAction.RibbonTab("View"),
            "JA" => RibbonTopLevelKeyTipAction.RibbonTab("PivotTable Analyze"),
            "JD" => RibbonTopLevelKeyTipAction.RibbonTab("Design"),
            "Y" => RibbonTopLevelKeyTipAction.RibbonTab("Help"),
            _ => null
        };
    }

    public static bool HasLongerKeyTipPrefix(string keyTipPrefix, IEnumerable<string?> keyTips)
    {
        if (string.IsNullOrWhiteSpace(keyTipPrefix))
            return false;

        var normalizedPrefix = keyTipPrefix.Trim();
        return keyTips
            .Where(keyTip => !string.IsNullOrWhiteSpace(keyTip))
            .Any(keyTip =>
                keyTip!.Trim() is { } candidate &&
                candidate.Length > normalizedPrefix.Length &&
                candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
    }
}

public readonly record struct RibbonTopLevelKeyTipAction(
    RibbonTopLevelKeyTipActionKind Kind,
    string? RibbonTabHeader)
{
    public static RibbonTopLevelKeyTipAction BackstageFile { get; } =
        new(RibbonTopLevelKeyTipActionKind.BackstageFile, null);

    public static RibbonTopLevelKeyTipAction RibbonTab(string header) =>
        new(RibbonTopLevelKeyTipActionKind.RibbonTab, header);
}

public enum RibbonTopLevelKeyTipActionKind
{
    BackstageFile,
    RibbonTab
}
