using Free.Shared.Ribbon.KeyTips;

namespace FreeX.App.Host;

public static class RibbonTopLevelKeyTipRouter
{
    public static RibbonTopLevelKeyTipAction? Resolve(
        string keyTip,
        IEnumerable<RibbonTopLevelKeyTipEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        var normalizedKeyTip = RibbonKeyTipText.Normalize(keyTip);
        if (normalizedKeyTip is null)
            return null;

        var candidates = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Header) &&
                            !string.IsNullOrWhiteSpace(entry.KeyTip))
            .ToList();

        foreach (var entry in candidates)
        {
            if (string.Equals(RibbonKeyTipText.Normalize(entry.KeyTip!), normalizedKeyTip, StringComparison.OrdinalIgnoreCase))
                return CreateAction(entry.Header);
        }

        if (string.Equals(normalizedKeyTip, "D", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in candidates)
            {
                if (!string.Equals(entry.Header, "Data", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(entry.Header))
                    return RibbonTopLevelKeyTipAction.RibbonTab(entry.Header);
                break;
            }
        }

        return null;
    }

    public static bool HasLongerKeyTipPrefix(string keyTipPrefix, IEnumerable<string?> keyTips)
    {
        if (string.IsNullOrWhiteSpace(keyTipPrefix))
            return false;

        var normalizedPrefix = RibbonKeyTipText.Normalize(keyTipPrefix);
        if (normalizedPrefix is null)
            return false;

        return keyTips
            .Where(keyTip => !string.IsNullOrWhiteSpace(keyTip))
            .Any(keyTip =>
                RibbonKeyTipText.Normalize(keyTip!) is { } candidate &&
                candidate.Length > normalizedPrefix.Length &&
                candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static RibbonTopLevelKeyTipAction CreateAction(string header) =>
        string.Equals(header, "File", StringComparison.OrdinalIgnoreCase)
            ? RibbonTopLevelKeyTipAction.BackstageFile
            : RibbonTopLevelKeyTipAction.RibbonTab(header);

}

public readonly record struct RibbonTopLevelKeyTipEntry(string Header, string? KeyTip);

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
