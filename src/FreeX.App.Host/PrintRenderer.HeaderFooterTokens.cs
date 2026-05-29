using System.Globalization;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    internal static string ExpandHeaderFooterText(
        string text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        DateTime now) =>
        text
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
}
