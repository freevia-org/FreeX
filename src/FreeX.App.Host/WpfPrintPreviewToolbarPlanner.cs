using System.Printing;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class WpfPrintPreviewToolbarPlanner
{
    public static DocumentPaginator ResolvePrintPaginator(
        FixedDocument document,
        PrintPreviewPageRangeMode pageRangeMode,
        int currentPage,
        ExportPageRange? pageRange = null)
    {
        var range = PrintPreviewToolbarStatePlanner.ResolvePageRange(
            pageRangeMode,
            currentPage,
            pageRange?.FromPage,
            pageRange?.ToPage);

        return range is { } plan
            ? PageRangeDocumentPaginator.Create(
                document.DocumentPaginator,
                new ExportPageRange(plan.FromPage, plan.ToPage))
            : document.DocumentPaginator;
    }

    public static Duplexing ResolvePrintTicketDuplexing(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => Duplexing.TwoSidedLongEdge,
            PrintPreviewSidesMode.TwoSidedShortEdge => Duplexing.TwoSidedShortEdge,
            _ => Duplexing.OneSided
        };

    public static void PopulatePrinterBox(
        ComboBox printerBox,
        string noInstalledPrintersToolTip,
        string noInstalledPrintersHelpText,
        string? fixturePrinterName = null)
    {
        if (!string.IsNullOrWhiteSpace(fixturePrinterName))
        {
            printerBox.Items.Add(fixturePrinterName.Trim());
            printerBox.SelectedIndex = 0;
            return;
        }

        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
                printerBox.Items.Add(queue);

            if (printerBox.Items.Count > 0)
            {
                printerBox.DisplayMemberPath = nameof(PrintQueue.FullName);
                printerBox.SelectedItem = null;
                foreach (var item in printerBox.Items)
                {
                    if (item is not PrintQueue queue)
                        continue;

                    if (string.Equals(
                        queue.FullName,
                        server.DefaultPrintQueue.FullName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        printerBox.SelectedItem = queue;
                        break;
                    }
                }

                if (printerBox.SelectedItem is null)
                    printerBox.SelectedIndex = 0;

                return;
            }
        }
        catch (PrintSystemException)
        {
        }

        printerBox.IsEnabled = false;
        printerBox.ToolTip = noInstalledPrintersToolTip;
        AutomationProperties.SetHelpText(printerBox, noInstalledPrintersHelpText);
    }
}
