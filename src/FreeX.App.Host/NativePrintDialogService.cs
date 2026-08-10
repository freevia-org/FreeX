using System.Drawing.Printing;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using FreeX.App.Presentation.PageLayout;
using Forms = System.Windows.Forms;

namespace FreeX.App.Host;

internal static class NativePrintDialogService
{
    public static void ShowPrinterOptionsDialog(Window? owner = null)
    {
        using var document = CreatePrinterSelectionDocument(null, copies: 1, collated: true, PrintPreviewSidesMode.OneSided);
        using var dialog = CreatePrinterSelectionDialog(document);
        ShowDialog(dialog, owner);
    }

    public static void ShowPrintDialogAndPrint(
        DocumentPaginator paginator,
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode,
        Window? owner = null)
    {
        using var document = CreatePrinterSelectionDocument(printQueue, copies, collated, sidesMode, paginator);
        using var dialog = CreatePrinterSelectionDialog(document);
        if (ShowDialog(dialog, owner) != Forms.DialogResult.OK)
            return;

        // A printer failure here (offline/removed printer, stopped spooler, driver fault,
        // invalid PrintTicket, access-denied on a network queue) must never crash the whole
        // app -- match the ExportAsPdf/ExportAsXps pattern (MainWindow.PrintExport.cs) of
        // catching and showing an owned error dialog instead of letting the exception reach
        // the WPF dispatcher unhandled.
        try
        {
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowPrintFailedMessage(ex, owner);
        }
    }

    private static void ShowPrintFailedMessage(Exception ex, Window? owner)
    {
        var presentation = PageLayoutMessagePresentationCatalog
            .DescribeNativePrintFailure(ex.Message)
            .Resolve(UiText.Get, UiText.Format);
        DialogMessageHelper.ShowMessage(
            owner,
            presentation.Message,
            presentation.Title,
            presentation.Buttons,
            presentation.Kind);
    }

    private static PrintDocument CreatePrinterSelectionDocument(
        PrintQueue? printQueue,
        int copies,
        bool collated,
        PrintPreviewSidesMode sidesMode,
        DocumentPaginator? previewPaginator = null)
    {
        var settings = new PrinterSettings
        {
            Copies = (short)Math.Clamp(copies, 1, 999),
            Collate = collated,
            Duplex = ToPrinterSettingsDuplex(sidesMode)
        };
        if (printQueue is not null)
            settings.PrinterName = printQueue.FullName;

        var document = new PrintDocument
        {
            DocumentName = "FreeX worksheet",
            PrinterSettings = settings
        };

        // Render the worksheet pages through the PrintDocument so the OS print-dialog
        // preview works. Without a PrintPage handler the document has no content and the
        // Windows print dialog reports "this app doesn't support print preview".
        if (previewPaginator is not null)
            WirePreviewRendering(document, previewPaginator);

        return document;
    }

    private static void WirePreviewRendering(PrintDocument document, DocumentPaginator paginator)
    {
        var pageIndex = 0;
        document.BeginPrint += (_, _) => pageIndex = 0;
        document.PrintPage += (_, e) =>
        {
            try
            {
                var pageCount = paginator.IsPageCountValid ? paginator.PageCount : int.MaxValue;
                if (pageIndex >= pageCount)
                {
                    e.HasMorePages = false;
                    return;
                }

                using var pageImage = RenderPaginatorPageToImage(paginator, pageIndex);
                if (pageImage is not null && e.Graphics is not null)
                {
                    e.Graphics.DrawImage(pageImage, e.PageBounds);
                }

                pageIndex++;
                e.HasMorePages = paginator.IsPageCountValid
                    ? pageIndex < paginator.PageCount
                    : pageImage is not null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A preview render failure must never crash the print flow; end the document.
                e.HasMorePages = false;
            }
        };
    }

    // Renders a single WPF page from the paginator to a GDI+ bitmap so it can be drawn
    // onto the print/preview Graphics. Drawing into e.PageBounds scales to the page
    // regardless of the source DPI, so the image aspect (page DIP size) is all that matters.
    private static System.Drawing.Image? RenderPaginatorPageToImage(DocumentPaginator paginator, int pageIndex)
    {
        if (pageIndex < 0 || (paginator.IsPageCountValid && pageIndex >= paginator.PageCount))
            return null;

        var page = paginator.GetPage(pageIndex);
        if (page?.Visual is not { } visual)
            return null;

        var sizeDip = page.Size;
        if (sizeDip.Width <= 0 || sizeDip.Height <= 0)
            return null;

        // Render at ~150 DPI for a crisp preview without excessive memory.
        const double renderDpi = 150.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(sizeDip.Width / 96.0 * renderDpi));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(sizeDip.Height / 96.0 * renderDpi));

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            pixelWidth, pixelHeight, renderDpi, renderDpi, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
        var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return System.Drawing.Image.FromStream(stream);
    }

    private static Forms.PrintDialog CreatePrinterSelectionDialog(PrintDocument document)
    {
        return new Forms.PrintDialog
        {
            AllowCurrentPage = false,
            AllowPrintToFile = true,
            AllowSelection = false,
            AllowSomePages = false,
            Document = document,
            PrinterSettings = document.PrinterSettings,
            ShowNetwork = true,
            UseEXDialog = false
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

    private static Forms.DialogResult ShowDialog(Forms.PrintDialog dialog, Window? owner)
    {
        var handle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        return handle == IntPtr.Zero
            ? dialog.ShowDialog()
            : dialog.ShowDialog(new WindowHandleOwner(handle));
    }

    private sealed class WindowHandleOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
