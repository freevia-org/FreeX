using System.Globalization;
using System.Printing;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed partial class PrintPreviewDialog
{
    public static string CreateTitle(string workbookName) =>
        $"Print Preview - {workbookName.Trim()}";

    public static int NormalizeCopyCount(string? text)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies))
            return 1;

        return Math.Clamp(copies, 1, 999);
    }

    private static void ShowNativePrintDialog(FixedDocument document, PrintQueue? printQueue, int copies)
    {
        var dialog = new PrintDialog();
        if (printQueue is not null)
            dialog.PrintQueue = printQueue;

        copies = NormalizeCopyCount(copies.ToString(CultureInfo.InvariantCulture));
        if (dialog.PrintTicket is not null)
            dialog.PrintTicket.CopyCount = copies;

        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(document.DocumentPaginator, "Freexcel worksheet");
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
        var copies = NormalizeCopyCount(copiesBox.Text);
        var pages = totalPages == 1 ? "1 page" : $"{totalPages} pages";
        var copyText = copies == 1 ? "1 copy" : $"{copies} copies";
        var printerName = printerBox.SelectedItem is PrintQueue queue
            ? queue.FullName
            : "Windows print dialog";

        statusText.Text = $"Ready: {printerName}; {copyText}; {pages}";
    }

    private static void NavigateToPage(DocumentViewer viewer, TextBox pageNumberBox, TextBlock pageStatusText, int totalPages)
    {
        if (!int.TryParse(pageNumberBox.Text.Trim(), out var pageNumber))
            return;

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        viewer.GoToPage(pageNumber);
        pageNumberBox.Text = pageNumber.ToString(CultureInfo.InvariantCulture);
        pageStatusText.Text = $"Page {pageNumber} of {totalPages}";
    }

    private static void FocusInitialKeyboardTarget(Button printButton)
    {
        printButton.Focus();
        Keyboard.Focus(printButton);
    }
}
