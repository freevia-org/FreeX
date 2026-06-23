using System.Globalization;

namespace FreeX.App.Presentation.PageLayout;

public static class PrintPreviewDialogPlanner
{
    public const string TitleFormatResourceKey = "PrintPreview_TitleFormat";
    public const string DialogAutomationId = "PrintPreviewWindow";
    public const string PageHostAutomationId = "PrintPreviewPageHost";
    public const string PageCanvasAutomationId = "PrintPreviewPageCanvas";
    public const string PageLabelAutomationId = "PrintPreviewPageLabel";
    public const string PreviousButtonAutomationId = "PrintPreviewPrevButton";
    public const string NextButtonAutomationId = "PrintPreviewNextButton";
    public const string ExportPdfButtonAutomationId = "PrintPreviewExportPdfButton";
    public const string CloseButtonAutomationId = "PrintPreviewCloseButton";

    public const double WindowWidth = 1120;
    public const double WindowHeight = 700;
    public const double MinWindowWidth = 520;
    public const double MinWindowHeight = 480;

    public static string NormalizeWorkbookName(string? workbookName) =>
        string.IsNullOrWhiteSpace(workbookName) ? "Book1" : workbookName.Trim();

    public static bool TryParseCopyCount(string? text, out int copies)
    {
        copies = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 1 or > 999)
            return false;

        copies = parsed;
        return true;
    }

    public static bool TryParsePageNumber(string? text, int totalPages, out int pageNumber)
    {
        pageNumber = 0;
        if (totalPages < 1
            || !int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1
            || parsed > totalPages)
            return false;

        pageNumber = parsed;
        return true;
    }
}
