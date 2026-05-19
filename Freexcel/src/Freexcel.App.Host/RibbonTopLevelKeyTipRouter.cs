namespace Freexcel.App.Host;

public static class RibbonTopLevelKeyTipRouter
{
    public static RibbonTopLevelKeyTipAction? Resolve(string keyTip)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        return keyTip.ToUpperInvariant() switch
        {
            "F" => RibbonTopLevelKeyTipAction.BackstageFile,
            "H" => RibbonTopLevelKeyTipAction.RibbonTab("Home"),
            "N" => RibbonTopLevelKeyTipAction.RibbonTab("Insert"),
            "J" => RibbonTopLevelKeyTipAction.RibbonTab("Draw"),
            "P" => RibbonTopLevelKeyTipAction.RibbonTab("Page Layout"),
            "M" => RibbonTopLevelKeyTipAction.RibbonTab("Formulas"),
            "A" => RibbonTopLevelKeyTipAction.RibbonTab("Data"),
            "R" => RibbonTopLevelKeyTipAction.RibbonTab("Review"),
            "W" => RibbonTopLevelKeyTipAction.RibbonTab("View"),
            "Y" => RibbonTopLevelKeyTipAction.RibbonTab("Help"),
            _ => null
        };
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
