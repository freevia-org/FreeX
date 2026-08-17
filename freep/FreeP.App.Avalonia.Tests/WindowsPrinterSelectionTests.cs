namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsPrinterSelectionTests
{
    [Fact]
    public void AvaloniaPrintPaneExposesWindowsPrinterSelectorBackedBySharedDiscovery()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var presentation = Read(
            "freep",
            "FreeP.App.Presentation",
            "PresentationPrintOutputPackageExecutor.cs");

        source.Should().Contain("surface.PrinterPickerAutomationId")
            .And.Contain("surface.NativeDialogAutomationId")
            .And.NotContain("\"FreePWindowsPrinterPicker\"")
            .And.NotContain("\"FreePWindowsPrinterDialog\"")
            .And.Contain("_printService.DiscoverAsync()")
            .And.Contain("_latestPrinterDiscovery.Printers")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
        presentation.Should().Contain("PrinterPickerAutomationId: \"FreePWindowsPrinterPicker\"")
            .And.Contain("NativeDialogAutomationId: \"FreePWindowsPrinterDialog\"");
    }

    [Fact]
    public void UnknownPrinterIsRejectedAgainstTheSharedDiscoverySnapshot()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("_latestPrinterDiscovery?.Printers.FirstOrDefault")
            .And.Contain("PresentationShellTextCatalog.WindowsPrinterQueueUnavailableStatus(normalized)")
            .And.NotContain("Windows printer queue '{normalized}' is no longer available.")
            .And.NotContain("WindowsNativePrintOutput.ForPrinter(");
    }

    // sweep78-2 (round 139): AddWindowsPrinterSelector used to call
    // `_printService.DiscoverAsync().GetAwaiter().GetResult()` directly while building the print
    // backstage pane, blocking the UI thread for however long printer discovery took -- an unreachable
    // network printer or a wedged spooler had no bound at all. This is a source-contract test (not a
    // compiled/behavioral one): the entire Windows-native printer/camera surface in MainWindow.cs sits
    // behind `#if FREEP_WINDOWS_CAPTURE`, and FreeP.App.Avalonia.csproj's own
    // `Condition="'$(TargetPlatformIdentifier)' == 'Windows'"` PropertyGroup for that constant is a
    // separate, pre-existing bug (PropertyGroup conditions evaluate before the SDK has derived
    // $(TargetPlatformIdentifier) from the conditionally-set $(TargetFramework) a few lines above) that
    // leaves FREEP_WINDOWS_CAPTURE undefined in every current build -- confirmed by attempting a real
    // MainWindow(printService: <slow fake>).ShowPrintOptionsPane() behavioral test: DiscoverAsync is
    // never even called. Fixing that MSBuild bug cascades into unrelated pre-existing compile errors
    // elsewhere in this project (e.g. AvaloniaOleInPlaceHost.cs referencing a since-renamed/removed
    // AvaloniaInlineOleHostRequest type), which is its own separate, larger remediation effort outside
    // this fix's scope. A text-contract test is the same methodology the sibling tests in this file
    // already use for this exact code region, and is the only reliable signal available today.
    [Fact]
    public void AddWindowsPrinterSelectorNoLongerBlocksTheUiThreadOnPrinterDiscovery()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().NotContain(
                "_latestPrinterDiscovery = _printService.DiscoverAsync().GetAwaiter().GetResult();",
                "the print pane must never block synchronously on printer discovery -- a slow " +
                "spooler/unreachable network printer would freeze the whole window with no bound")
            .And.Contain(
                "Task.Run(() => _printService.DiscoverAsync())",
                "printer discovery must run on a background thread so a slow spooler cannot block " +
                "the caller even transiently")
            .And.Contain("StartPrinterDiscovery()")
            .And.Contain(
                "_printOptionsPaneHost?.IsVisible == true",
                "once background discovery lands, the pane must re-render so a slow probe still " +
                "reaches the user instead of being silently dropped");
    }

    // Sibling to the test above: proves the fix did not disturb the rest of the printer-selector wiring
    // this same file already contract-tests (shared discovery snapshot, automation ids, native dialog).
    [Fact]
    public void PrinterSelectorStillExposesTheSameSharedDiscoverySurface()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("surface.PrinterPickerAutomationId")
            .And.Contain("_latestPrinterDiscovery.Printers")
            .And.Contain("_latestPrinterDiscovery.DefaultPrinter")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
    }

    [Fact]
    public void SharedPlatformServiceOwnsDiscoveryAndSubmissionOnEveryPlatform()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("IPlatformPrintService")
            .And.Contain("new CupsPrintService()")
            .And.Contain("new WindowsPrintService(")
            .And.Contain("CupsPrintDialog.ShowAsync")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog")
            .And.NotContain("_portablePrintWorkflowEnabled")
            .And.NotContain("CreateNativePrintAdapter")
            .And.NotContain("ILinuxNativePrintHandoffAdapter");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
