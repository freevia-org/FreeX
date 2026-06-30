using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed partial class PrintPreviewDialog
{
    public static string CreateTitle(string workbookName) =>
        UiText.Format(
            PrintPreviewDialogPlanner.TitleFormatResourceKey,
            PrintPreviewDialogPlanner.NormalizeWorkbookName(workbookName));

    public static bool TryParseCopyCount(string? text, out int copies) =>
        PrintPreviewDialogPlanner.TryParseCopyCount(text, out copies);

    public static bool TryParsePageNumber(string? text, int totalPages, out int pageNumber) =>
        PrintPreviewDialogPlanner.TryParsePageNumber(text, totalPages, out pageNumber);

    private void ShowInvalidCopiesWarning(TextBox copiesBox)
    {
        DialogFocus.ShowWarningAndFocus(this, UiText.Get("PrintPreview_InvalidCopiesMessage"), Title, copiesBox);
    }

    private void ShowInvalidPageNumberWarning(TextBox pageNumberBox, int totalPages)
    {
        DialogFocus.ShowWarningAndFocus(this, UiText.Format("PrintPreview_InvalidPageNumberMessage", totalPages), Title, pageNumberBox);
    }

    internal static DocumentPaginator ResolvePrintPaginator(
        FixedDocument document,
        PrintPreviewPageRangeMode pageRangeMode,
        int currentPage,
        ExportPageRange? pageRange = null) =>
        WpfPrintPreviewToolbarPlanner.ResolvePrintPaginator(document, pageRangeMode, currentPage, pageRange);

    internal static Duplexing ResolvePrintTicketDuplexing(PrintPreviewSidesMode mode) =>
        WpfPrintPreviewToolbarPlanner.ResolvePrintTicketDuplexing(mode);

    internal static PrintPreviewNavigationState CreateNavigationState(int currentPage, int totalPages) =>
        PrintPreviewToolbarStatePlanner.CreateNavigationState(currentPage, totalPages);

    private static PrintPreviewSidesMode ResolveSelectedSidesMode(ComboBox sidesBox) =>
        PrintPreviewToolbarStatePlanner.SidesIndexToMode(sidesBox.SelectedIndex);

    private void ShowInvalidPageRangeWarning(TextBox fromPageBox, TextBox toPageBox, string? error)
    {
        var target = string.Equals(error, UiText.Get("Export_PageRangeFromLessThanToError"), StringComparison.OrdinalIgnoreCase)
            ? toPageBox
            : fromPageBox;
        DialogFocus.ShowWarningAndFocus(this, error ?? UiText.Get("PrintPreview_InvalidPageRangeMessage"), Title, target);
    }

    private void ShowNativePrintDialog(
        DocumentPaginator paginator,
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode) =>
        NativePrintDialogService.ShowPrintDialogAndPrint(paginator, printQueue, copies, collated, sidesMode, this);

    private static void RefreshPrintStatus(TextBlock statusText, ComboBox printerBox, TextBox copiesBox, int totalPages)
    {
        var validCopies = TryParseCopyCount(copiesBox.Text, out var copies);
        var printerName = printerBox.SelectedItem is PrintQueue queue
            ? queue.FullName
            : null;

        statusText.Text = PrintPreviewToolbarStatePlanner.CreateStatusText(printerName, validCopies ? copies : null, totalPages);
    }

    private void NavigateToPage(DocumentViewer viewer, TextBox pageNumberBox, TextBlock pageStatusText, int totalPages)
    {
        if (!TryParsePageNumber(pageNumberBox.Text, totalPages, out var pageNumber))
        {
            ShowInvalidPageNumberWarning(pageNumberBox, totalPages);
            return;
        }

        viewer.GoToPage(pageNumber);
        pageNumberBox.Text = pageNumber.ToString(CultureInfo.InvariantCulture);
        pageStatusText.Text = CreateNavigationState(pageNumber, totalPages).StatusText;
    }

    private static void FocusInitialKeyboardTarget(Button printButton)
    {
        printButton.Focus();
        Keyboard.Focus(printButton);
    }

}
