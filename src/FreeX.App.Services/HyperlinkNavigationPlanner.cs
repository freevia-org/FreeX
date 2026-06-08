using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum HyperlinkNavigationKind
{
    External,
    WorksheetCell
}

public sealed record HyperlinkNavigationPlan(
    HyperlinkNavigationKind Kind,
    string Target,
    CellAddress? Address);

public static class HyperlinkNavigationPlanner
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "ftp"
    };

    public static bool IsAllowedScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return AllowedSchemes.Contains(uri.Scheme);
    }

    public static bool TryCreatePlan(Sheet? sheet, CellAddress address, out HyperlinkNavigationPlan? plan)
    {
        plan = null;
        if (sheet is null ||
            !sheet.Hyperlinks.TryGetValue(address, out var target) ||
            string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
        var kind = metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage;
        var normalizedTarget = target.Trim();

        plan = kind == HyperlinkTargetKind.PlaceInThisDocument
            ? new HyperlinkNavigationPlan(HyperlinkNavigationKind.WorksheetCell, normalizedTarget, null)
            : new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, normalizedTarget, null);
        return true;
    }
}
