using FreeP.App.Recording.Windows;

namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsPrinterSelectionTests
{
    [Fact]
    public void AvaloniaPrintPaneExposesWindowsPrinterSelector()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FreePWindowsPrinterPicker");
        source.Should().Contain("FreePWindowsPrinterDialog");
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
}
