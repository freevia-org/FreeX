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
            .And.Contain("Windows printer queue '{normalized}' is no longer available.")
            .And.NotContain("WindowsNativePrintOutput.ForPrinter(");
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
