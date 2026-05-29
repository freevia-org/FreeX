using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace FreeX.App.Host;

internal enum PrintPreviewPageRangeMode
{
    AllPages,
    CurrentPage,
    Pages
}

public enum PrintPreviewSidesMode
{
    OneSided,
    TwoSidedLongEdge,
    TwoSidedShortEdge
}

public sealed partial class PrintPreviewDialog
{
    public static string CreateTitle(string workbookName) =>
        $"Print Preview - {workbookName.Trim()}";

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

    private void ShowInvalidCopiesWarning(TextBox copiesBox)
    {
        DialogMessageHelper.ShowWarning(this, "Enter a copy count from 1 to 999.", Title);
        copiesBox.Focus();
        copiesBox.SelectAll();
        Keyboard.Focus(copiesBox);
    }

    private void ShowInvalidPageNumberWarning(TextBox pageNumberBox, int totalPages)
    {
        DialogMessageHelper.ShowWarning(this, $"Enter a page number from 1 to {totalPages}.", Title);
        pageNumberBox.Focus();
        pageNumberBox.SelectAll();
        Keyboard.Focus(pageNumberBox);
    }

    internal static DocumentPaginator ResolvePrintPaginator(
        FixedDocument document,
        PrintPreviewPageRangeMode pageRangeMode,
        int currentPage,
        ExportPageRange? pageRange = null) =>
        PrintPreviewToolbarPlanner.ResolvePrintPaginator(document, pageRangeMode, currentPage, pageRange);

    internal static Duplexing ResolvePrintTicketDuplexing(PrintPreviewSidesMode mode) =>
        PrintPreviewToolbarPlanner.ResolvePrintTicketDuplexing(mode);

    internal static PrintPreviewNavigationState CreateNavigationState(int currentPage, int totalPages) =>
        PrintPreviewToolbarPlanner.CreateNavigationState(currentPage, totalPages);

    private static PrintPreviewSidesMode ResolveSelectedSidesMode(ComboBox sidesBox) =>
        PrintPreviewToolbarPlanner.ResolveSelectedSidesMode(sidesBox.SelectedIndex);

    private void ShowInvalidPageRangeWarning(TextBox fromPageBox, TextBox toPageBox, string? error)
    {
        DialogMessageHelper.ShowWarning(this, error ?? "Enter a valid page range.", Title);
        var target = string.Equals(error, "From page must be less than or equal to To page.", StringComparison.OrdinalIgnoreCase)
            ? toPageBox
            : fromPageBox;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private static void ShowNativePrintDialog(
        DocumentPaginator paginator,
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode)
    {
        var dialog = new PrintDialog();
        if (printQueue is not null)
            dialog.PrintQueue = printQueue;

        if (dialog.PrintTicket is not null)
        {
            dialog.PrintTicket.CopyCount = copies;
            dialog.PrintTicket.Collation = collated ? Collation.Collated : Collation.Uncollated;
            dialog.PrintTicket.Duplexing = ResolvePrintTicketDuplexing(sidesMode);
        }

        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(paginator, "FreeX worksheet");
    }

    private static void PopulatePrinterBox(ComboBox printerBox)
    {
        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
                printerBox.Items.Add(queue);

            if (printerBox.Items.Count > 0)
            {
                printerBox.DisplayMemberPath = nameof(PrintQueue.FullName);
                printerBox.SelectedItem = printerBox.Items
                    .OfType<PrintQueue>()
                    .FirstOrDefault(queue => string.Equals(
                        queue.FullName,
                        server.DefaultPrintQueue.FullName,
                        StringComparison.OrdinalIgnoreCase));
                if (printerBox.SelectedItem is null)
                    printerBox.SelectedIndex = 0;

                return;
            }
        }
        catch (PrintSystemException)
        {
        }

        printerBox.IsEnabled = false;
        printerBox.ToolTip = "No installed printers were detected. Print opens the Windows print dialog so a printer can be chosen there.";
        AutomationProperties.SetHelpText(printerBox, "No installed printers were detected. Use Print to choose a printer in the Windows print dialog.");
    }

    private static void RefreshPrintStatus(TextBlock statusText, ComboBox printerBox, TextBox copiesBox, int totalPages)
    {
        var validCopies = TryParseCopyCount(copiesBox.Text, out var copies);
        var printerName = printerBox.SelectedItem is PrintQueue queue
            ? queue.FullName
            : null;

        statusText.Text = PrintPreviewToolbarPlanner.CreateStatusText(printerName, validCopies ? copies : null, totalPages);
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
