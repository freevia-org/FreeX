using System.Printing;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Free.Shared.Shell.Wpf;
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
            ? WpfPageRangeDocumentPaginator.CreateValidatedInclusive(
                document.DocumentPaginator,
                plan.FromPage,
                plan.ToPage)
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

        var discovery = WpfPrintQueueCatalog.Discover();
        foreach (var queue in discovery.Queues)
            printerBox.Items.Add(queue);

        if (printerBox.Items.Count > 0)
        {
            printerBox.DisplayMemberPath = nameof(PrintQueue.FullName);
            if (discovery.DefaultQueue is not null)
                printerBox.SelectedItem = discovery.DefaultQueue;
            else
                printerBox.SelectedIndex = 0;

            return;
        }

        printerBox.IsEnabled = false;
        printerBox.ToolTip = noInstalledPrintersToolTip;
        AutomationProperties.SetHelpText(printerBox, noInstalledPrintersHelpText);
    }
}
