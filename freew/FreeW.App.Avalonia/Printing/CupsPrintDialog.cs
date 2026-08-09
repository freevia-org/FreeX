using Avalonia.Controls;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Printing;

internal sealed class CupsPrintDialog : FreeWDialogWindow
{
    private static readonly AvaloniaPrintDialogOptions Options = new()
    {
        Width = 480,
        ChoiceMinWidth = 220,
        Collation = AvaloniaPrintDialogCollation.Fixed(true),
        ApplyCompactActionButtonChrome = true,
    };

    private CupsPrintDialog()
    {
    }

    public static Task<PrintSelection?> ShowAsync(
        Window owner,
        PrinterDiscoveryResult discovery,
        PrintSelection? requested = null,
        CancellationToken cancellationToken = default) =>
        AvaloniaPrintDialogWorkflow.ShowAsync(
            owner,
            discovery,
            static () => new CupsPrintDialog(),
            Options,
            requested,
            cancellationToken);
}
