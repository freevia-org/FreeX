using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// The hyperlink navigation planner/plan/kind live in the shared FreeX.App.Services tier
// (FreeX.App.Services.HyperlinkNavigationPlanner) as a superset that also resolves local-file
// targets; every shell consumes that single source. Only the WPF-specific dialog prefill,
// which depends on SpreadsheetDisplayFormatter, remains here.

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
