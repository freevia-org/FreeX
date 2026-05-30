namespace FreeX.App.Host;

public static class RibbonTopLevelKeyTipRouter
{
    public static RibbonTopLevelKeyTipAction? Resolve(
        string keyTip,
        IEnumerable<RibbonTopLevelKeyTipEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        var normalizedKeyTip = NormalizeKeyTip(keyTip);
        var candidates = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Header) &&
                            !string.IsNullOrWhiteSpace(entry.KeyTip))
            .ToList();

        foreach (var entry in candidates)
        {
            if (string.Equals(NormalizeKeyTip(entry.KeyTip!), normalizedKeyTip, StringComparison.OrdinalIgnoreCase))
                return CreateAction(entry.Header);
        }

        if (string.Equals(normalizedKeyTip, "D", StringComparison.OrdinalIgnoreCase))
        {
            var dataEntry = candidates.FirstOrDefault(entry =>
                string.Equals(entry.Header, "Data", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(dataEntry.Header))
                return RibbonTopLevelKeyTipAction.RibbonTab(dataEntry.Header);
        }

        return null;
    }

    public static bool HasLongerKeyTipPrefix(string keyTipPrefix, IEnumerable<string?> keyTips)
    {
        if (string.IsNullOrWhiteSpace(keyTipPrefix))
            return false;

        var normalizedPrefix = keyTipPrefix.Trim();
        return keyTips
            .Where(keyTip => !string.IsNullOrWhiteSpace(keyTip))
            .Any(keyTip =>
                NormalizeKeyTip(keyTip!) is { } candidate &&
                candidate.Length > normalizedPrefix.Length &&
                candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static RibbonTopLevelKeyTipAction CreateAction(string header) =>
        string.Equals(header, "File", StringComparison.OrdinalIgnoreCase)
            ? RibbonTopLevelKeyTipAction.BackstageFile
            : RibbonTopLevelKeyTipAction.RibbonTab(header);

    private static string NormalizeKeyTip(string keyTip) =>
        keyTip.Trim().ToUpperInvariant();
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
