using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

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
        var dialog = new CupsPrintDialog();
        AvaloniaPrintDialogWorkflow.ConfigureForVisualHarness(dialog, discovery, Options);
        return dialog;
    }
}
