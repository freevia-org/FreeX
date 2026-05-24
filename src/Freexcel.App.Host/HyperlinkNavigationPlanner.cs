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
