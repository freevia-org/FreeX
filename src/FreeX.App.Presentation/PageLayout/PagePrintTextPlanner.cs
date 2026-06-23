using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public static class PagePrintTextPlanner
{
    public static string ExpandHeaderFooterText(
        string? text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        DateTime now) =>
        (text ?? "")
            .Replace("&[Page]", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", now.ToString("t", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", sheetName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Picture]", "", StringComparison.OrdinalIgnoreCase)
            .Replace("&G", "", StringComparison.OrdinalIgnoreCase)
            .Replace("&P", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&N", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&D", now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&T", now.ToString("t", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&F", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&Z", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&A", sheetName, StringComparison.OrdinalIgnoreCase);

    public static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
    {
        if (!IsErrorDisplayText(displayText))
            return displayText;

        return printErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => "",
            WorksheetPrintErrorValue.Dash => "--",
            WorksheetPrintErrorValue.NotAvailable => "#N/A",
            _ => displayText
        };
    }

    public static bool IsErrorDisplayText(string text) =>
        text is "#DIV/0!" or "#VALUE!" or "#REF!" or "#NAME?" or "#NULL!" or "#N/A" or "#NUM!";
}
