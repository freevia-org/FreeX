using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ExportReadinessPlan(
    bool IsReady,
    string StatusText);

public static class ExportReadinessPlanner
{
    private const string ExportOptionSummary =
        "Options include page range, standard/minimum-size PDF quality, ignore print areas, document properties, PDF bookmarks/language/view choices, XPS routing with PDF-only choices called out, and open after publishing. PDF/A and tagged PDF are exposed as unsupported and rejected rather than emitted as normal PDFs.";

    public static ExportReadinessPlan Create(Workbook workbook, bool hasSelection = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (!HasVisibleWorksheet(workbook))
        {
            return new ExportReadinessPlan(
                false,
                "No visible worksheets are available for local PDF/XPS export.");
        }

        return CreateForAvailableWorkbook(hasSelection);
    }

    public static ExportReadinessPlan CreateForAvailableWorkbook(bool hasSelection = false)
    {
        var scopeSummary = hasSelection
            ? "Active sheet, selected range, and entire visible workbook export scopes are available."
            : "Active sheet and entire visible workbook export scopes are available; select a range to enable selected-range export.";

        return new ExportReadinessPlan(
            true,
            $"Ready for local PDF/XPS export to a chosen local path. {scopeSummary} {ExportOptionSummary} No Microsoft account or cloud service is required.");
    }

    private static bool HasVisibleWorksheet(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.IsHidden)
                return true;
        }

        return false;
    }
}
