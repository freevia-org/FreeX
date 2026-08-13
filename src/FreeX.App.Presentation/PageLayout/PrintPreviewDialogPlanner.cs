using System.Globalization;
using Free.Shared.Localization;

namespace FreeX.App.Presentation.PageLayout;

public enum PrintPreviewToolbarCommand
{
    Print,
    FirstPage,
    PreviousPage,
    NextPage,
    LastPage,
    Margins,
    PageSetup,
    Close
}

public enum PrintPreviewValidationFocusTarget
{
    Copies,
    PageNumber,
    FromPage,
    ToPage
}

public sealed record PrintPreviewToolbarCommandPlan(
    PrintPreviewToolbarCommand Command,
    string AutomationId,
    string ContentResourceKey,
    string AutomationNameResourceKey,
    string HelpTextResourceKey,
    string? ToolTipResourceKey = null);

public static class PrintPreviewDialogPlanner
{
    public static PrintPreviewToolbarCommand InitialFocusCommand => PrintPreviewToolbarCommand.Print;

    public const string TitleFormatResourceKey = "PrintPreview_TitleFormat";
    public const string DialogAutomationId = "PrintPreviewWindow";
    public const string PageHostAutomationId = "PrintPreviewPageHost";
    public const string PageCanvasAutomationId = "PrintPreviewPageCanvas";
    public const string PageLabelAutomationId = "PrintPreviewPageLabel";
    public const string PrintButtonAutomationId = "PrintPreviewPrintButton";
    public const string FirstPageButtonAutomationId = "PrintPreviewFirstPageButton";
    public const string PreviousPageButtonAutomationId = "PrintPreviewPreviousPageButton";
    public const string NextPageButtonAutomationId = "PrintPreviewNextPageButton";
    public const string LastPageButtonAutomationId = "PrintPreviewLastPageButton";
    public const string PreviousButtonAutomationId = "PrintPreviewPrevButton";
    public const string NextButtonAutomationId = "PrintPreviewNextButton";
    public const string ExportPdfButtonAutomationId = "PrintPreviewExportPdfButton";
    public const string MarginsButtonAutomationId = "PrintPreviewMarginsButton";
    public const string PageSetupButtonAutomationId = "PrintPreviewPageSetupButton";
    public const string CloseButtonAutomationId = "PrintPreviewCloseButton";
    public const string PageNumberBoxAutomationId = "PrintPreviewPageNumberBox";
    public const string PageStatusTextAutomationId = "PrintPreviewPageStatusText";
    public const string ZoomBoxAutomationId = "PrintPreviewZoomBox";
    public const string SettingsSummaryTextAutomationId = "PrintPreviewSettingsSummaryText";

    public const double WindowWidth = 1120;
    public const double WindowHeight = 700;
    public const double MinWindowWidth = 520;
    public const double MinWindowHeight = 480;

    public static IReadOnlyList<PrintPreviewToolbarCommandPlan> CreateNavigationCommandPlans() =>
    [
        CreateToolbarCommandPlan(PrintPreviewToolbarCommand.FirstPage),
        CreateToolbarCommandPlan(PrintPreviewToolbarCommand.PreviousPage),
        CreateToolbarCommandPlan(PrintPreviewToolbarCommand.NextPage),
        CreateToolbarCommandPlan(PrintPreviewToolbarCommand.LastPage)
    ];

    public static PrintPreviewToolbarCommandPlan CreateToolbarCommandPlan(PrintPreviewToolbarCommand command) =>
        command switch
        {
            PrintPreviewToolbarCommand.Print => new(
                command,
                PrintButtonAutomationId,
                "PrintPreview_PrintButton",
                "PrintPreview_PrintAutomationName",
                "PrintPreview_PrintHelpText",
                "PrintPreview_PrintToolTip"),
            PrintPreviewToolbarCommand.FirstPage => new(
                command,
                FirstPageButtonAutomationId,
                "PrintPreview_FirstPageButton",
                "PrintPreview_FirstPageAutomationName",
                "PrintPreview_FirstPageHelpText"),
            PrintPreviewToolbarCommand.PreviousPage => new(
                command,
                PreviousPageButtonAutomationId,
                "PrintPreview_PreviousPageButton",
                "PrintPreview_PreviousPageAutomationName",
                "PrintPreview_PreviousPageHelpText"),
            PrintPreviewToolbarCommand.NextPage => new(
                command,
                NextPageButtonAutomationId,
                "PrintPreview_NextPageButton",
                "PrintPreview_NextPageAutomationName",
                "PrintPreview_NextPageHelpText"),
            PrintPreviewToolbarCommand.LastPage => new(
                command,
                LastPageButtonAutomationId,
                "PrintPreview_LastPageButton",
                "PrintPreview_LastPageAutomationName",
                "PrintPreview_LastPageHelpText"),
            PrintPreviewToolbarCommand.Margins => new(
                command,
                MarginsButtonAutomationId,
                "PrintPreview_MarginsButton",
                "PrintPreview_MarginsAutomationName",
                "PrintPreview_MarginsHelpText",
                "PrintPreview_MarginsToolTip"),
            PrintPreviewToolbarCommand.PageSetup => new(
                command,
                PageSetupButtonAutomationId,
                "PrintPreview_PageSetupButton",
                "PrintPreview_PageSetupAutomationName",
                "PrintPreview_PageSetupHelpText",
                "PrintPreview_PageSetupToolTip"),
            PrintPreviewToolbarCommand.Close => new(
                command,
                CloseButtonAutomationId,
                "PrintPreview_CloseButton",
                "PrintPreview_CloseAutomationName",
                "PrintPreview_CloseHelpText",
                "PrintPreview_CloseToolTip"),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

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

    public static ValidationPresentationDescriptor<PrintPreviewValidationFocusTarget> DescribeInvalidCopies() =>
        new(
            LocalizedTextDescriptor.Resource("PrintPreview_InvalidCopiesMessage"),
            PrintPreviewValidationFocusTarget.Copies);

    public static ValidationPresentationDescriptor<PrintPreviewValidationFocusTarget> DescribeInvalidPageNumber(int totalPages) =>
        new(
            LocalizedTextDescriptor.Resource("PrintPreview_InvalidPageNumberMessage", totalPages),
            PrintPreviewValidationFocusTarget.PageNumber);

    public static ValidationPresentationDescriptor<PrintPreviewValidationFocusTarget> DescribeInvalidPageRange(
        string? resolvedMessage,
        PrintPreviewValidationFocusTarget focusTarget) =>
        new(
            resolvedMessage is null
                ? LocalizedTextDescriptor.Resource("PrintPreview_InvalidPageRangeMessage")
                : LocalizedTextDescriptor.Literal(resolvedMessage),
            focusTarget);
}
