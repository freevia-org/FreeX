using Free.Shared.AppServices.Printing;

namespace FreeW.App.Avalonia.Printing;

internal sealed partial class CupsPrintDialog
{
    internal static CupsPrintDialog CreateForVisualHarness()
    {
        var discovery = new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.NoPrinters,
            [],
            null,
            "No printers are installed or available.");
        _ = PrintSelectionPlanner.Build(discovery, requested: null);
        return new CupsPrintDialog();
    }
}
