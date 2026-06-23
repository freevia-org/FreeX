using FreeX.App.Presentation.PageLayout;

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
        PagePrintTextPlanner.ExpandHeaderFooterText(
            text,
            pageNumber,
            totalPages,
            workbookName,
            sheetName,
            now);
}
