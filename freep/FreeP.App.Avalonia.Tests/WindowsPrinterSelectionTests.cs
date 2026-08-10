using FreeP.App.Recording.Windows;

namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsPrinterSelectionTests
{
    [Fact]
    public void AvaloniaPrintPaneExposesWindowsPrinterSelector()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var presentation = File.ReadAllText(Path.Combine(
            repo,
            "freep",
            "FreeP.App.Presentation",
            "PresentationPrintOutputPackageExecutor.cs"));

        source.Should().Contain("surface.PrinterPickerAutomationId");
        source.Should().Contain("surface.NativeDialogAutomationId");
        source.Should().NotContain("\"FreePWindowsPrinterPicker\"");
        source.Should().NotContain("\"FreePWindowsPrinterDialog\"");
        presentation.Should().Contain("PrinterPickerAutomationId: \"FreePWindowsPrinterPicker\"");
        presentation.Should().Contain("NativeDialogAutomationId: \"FreePWindowsPrinterDialog\"");
        source.Should().Contain("WindowsNativePrintOutput.GetPrinters()");
        source.Should().Contain("WindowsNativePrintOutput.ForPrinter(printerName)");
        source.Should().Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
    }

    [Fact]
    public void UnknownPrinterIsNotMarkedPrintable()
    {
        var capability = WindowsNativePrintOutput.ForPrinter("printer-that-does-not-exist");

        capability.CanPrint.Should().BeFalse();
        capability.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PortablePrintWorkflowUsesSharedPlatformServiceWithoutReplacingWindowsNativeSelection()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("IPlatformPrintService");
        source.Should().Contain("CupsPrintDialog.ShowAsync");
        source.Should().Contain("!_portablePrintWorkflowEnabled || OperatingSystem.IsWindows()");
        source.Should().Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
        source.Should().Contain("WindowsNativePrintOutput.CreateAdapter(capability)");
    }
}
