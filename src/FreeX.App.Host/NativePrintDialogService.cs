using System.Drawing.Printing;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using Forms = System.Windows.Forms;

namespace FreeX.App.Host;

internal static class NativePrintDialogService
{
    public static void ShowPrinterOptionsDialog()
    {
        using var dialog = CreatePrinterSelectionDialog(null, copies: 1, collated: true, PrintPreviewSidesMode.OneSided);
        dialog.ShowDialog();
    }

    public static void ShowPrintDialogAndPrint(
        DocumentPaginator paginator,
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode)
    {
        using var dialog = CreatePrinterSelectionDialog(printQueue, copies, collated, sidesMode);
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        var selectedQueue = ResolvePrintQueue(dialog.PrinterSettings.PrinterName) ?? printQueue;
        var documentPrinter = new PrintDialog();
        if (selectedQueue is not null)
            documentPrinter.PrintQueue = selectedQueue;

        if (documentPrinter.PrintTicket is not null)
        {
            documentPrinter.PrintTicket.CopyCount = Math.Clamp((int)dialog.PrinterSettings.Copies, 1, 999);
            documentPrinter.PrintTicket.Collation = dialog.PrinterSettings.Collate
                ? Collation.Collated
                : Collation.Uncollated;
            documentPrinter.PrintTicket.Duplexing = ResolveDuplexing(dialog.PrinterSettings.Duplex, sidesMode);
        }

        documentPrinter.PrintDocument(paginator, "FreeX worksheet");
    }

    private static Forms.PrintDialog CreatePrinterSelectionDialog(
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode)
    {
        var settings = new PrinterSettings
        {
            Copies = (short)Math.Clamp(copies, 1, 999),
            Collate = collated,
            Duplex = ToPrinterSettingsDuplex(sidesMode)
        };
        if (printQueue is not null)
            settings.PrinterName = printQueue.FullName;

        return new Forms.PrintDialog
        {
            AllowCurrentPage = false,
            AllowPrintToFile = true,
            AllowSelection = false,
            AllowSomePages = false,
            PrinterSettings = settings,
            ShowNetwork = true,
            UseEXDialog = true
        };
    }

    private static PrintQueue? ResolvePrintQueue(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return null;

        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
            {
                if (string.Equals(queue.FullName, printerName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(queue.Name, printerName, StringComparison.OrdinalIgnoreCase))
                    return queue;
            }

            return new PrintQueue(server, printerName);
        }
        catch (PrintSystemException)
        {
            return null;
        }
    }

    private static Duplex ToPrinterSettingsDuplex(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => Duplex.Vertical,
            PrintPreviewSidesMode.TwoSidedShortEdge => Duplex.Horizontal,
            _ => Duplex.Simplex
        };

    private static Duplexing ResolveDuplexing(Duplex duplex, PrintPreviewSidesMode fallbackMode) =>
        duplex switch
        {
            Duplex.Vertical => Duplexing.TwoSidedLongEdge,
            Duplex.Horizontal => Duplexing.TwoSidedShortEdge,
            Duplex.Simplex => Duplexing.OneSided,
            _ => PrintPreviewDialog.ResolvePrintTicketDuplexing(fallbackMode)
        };
}
