using Avalonia.Controls;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Printing;

/// <summary>
/// FreeP adapter for the shared portable print surface. Presentation-specific layout context and
/// automation identifiers stay here; document planning and queue submission remain upstream.
/// </summary>
internal sealed class CupsPrintDialog : Window
{
    private static readonly AvaloniaPrintDialogAutomationIds AutomationIds = new(
        Printer: "FreePPortablePrinterPicker",
        Copies: "FreePPortablePrintCopies",
        PageRange: "FreePPortablePrintPageRange",
        Orientation: "FreePPortablePrintOrientation",
        Collation: "FreePPortablePrintCollation",
        Submit: "FreePPortablePrintSubmit");

    private CupsPrintDialog()
    {
    }

    public static Task<PrintSelection?> ShowAsync(
        Window owner,
        PrinterDiscoveryResult discovery,
        PrintSelection? requested = null,
        string? layoutSummary = null,
        CancellationToken cancellationToken = default) =>
        AvaloniaPrintDialogWorkflow.ShowAsync(
            owner,
            discovery,
            static () => new CupsPrintDialog(),
            new AvaloniaPrintDialogOptions
            {
                Width = 500,
                ChoiceMinWidth = 240,
                LayoutSummary = layoutSummary,
                AutomationIds = AutomationIds,
                Collation = AvaloniaPrintDialogCollation.Selectable,
                ApplyCompactActionButtonChrome = false,
                Text = BuildText(),
            },
            requested,
            cancellationToken);

    private static AvaloniaPrintDialogText BuildText()
    {
        static string Text(string suffix) => PresentationShellTextCatalog.PrintDialogText("Print_Dialog_" + suffix);

        return new AvaloniaPrintDialogText(
            Text("Title"),
            Text("PrinterLabel"),
            Text("CopiesLabel"),
            Text("PagesLabel"),
            Text("FirstPageLabel"),
            Text("LastPageLabel"),
            Text("OrientationLabel"),
            Text("LayoutLabel"),
            [Text("AllPages"), Text("SinglePage"), Text("PageRange")],
            [Text("DocumentOrientation"), Text("PortraitOrientation"), Text("LandscapeOrientation")],
            Text("CollateCopies"),
            Text("Submit"),
            Text("Cancel"),
            new PrintDialogText(
                Text("ReadyStatus"),
                Text("UnavailableStatus"),
                Text("CopiesOutOfRange"),
                Text("FirstPageInvalid"),
                Text("LastPageBeforeFirstPage")));
    }
}
