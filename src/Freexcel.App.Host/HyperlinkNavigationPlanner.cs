using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
    // Scheme whitelist for external hyperlink navigation.
    // "file:" is intentionally excluded to prevent local filesystem access via crafted spreadsheets.
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "ftp"
    };

    /// <summary>
    /// Returns true only if <paramref name="url"/> is an absolute URI with an allowed scheme.
    /// Rejects javascript:, data:, vbscript:, file:, and relative URLs.
    /// </summary>
    public static bool IsAllowedScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return AllowedSchemes.Contains(uri.Scheme);
    }

    public static bool TryCreatePlan(Sheet? sheet, CellAddress address, out HyperlinkNavigationPlan? plan)
    {
        plan = null;
        if (sheet is null || !sheet.Hyperlinks.TryGetValue(address, out var target) || string.IsNullOrWhiteSpace(target))
            return false;

        sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
        var kind = metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage;
        var normalizedTarget = target.Trim();

        if (kind == HyperlinkTargetKind.PlaceInThisDocument)
        {
            plan = new HyperlinkNavigationPlan(HyperlinkNavigationKind.WorksheetCell, normalizedTarget, null);
            return true;
        }

        plan = new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, normalizedTarget, null);
        return true;
    }
}

public sealed record HyperlinkDialogPrefill(string Target, string DisplayText)
{
    public static HyperlinkDialogPrefill FromCell(Sheet? sheet, CellAddress address)
    {
        var target = "https://";
        var displayText = "";
        if (sheet is null)
            return new HyperlinkDialogPrefill(target, displayText);

        if (sheet.Hyperlinks.TryGetValue(address, out var existingTarget) &&
            !string.IsNullOrWhiteSpace(existingTarget))
        {
            target = existingTarget;
        }

        displayText = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(address)?.Value);
        return new HyperlinkDialogPrefill(target, displayText);
    }
}
