namespace FreeP.App.Host.Tests;

public sealed class WindowsNativePrintHandoffAdapterTests
{
    [Fact]
    public void Windows_recording_layer_keeps_only_the_native_queue_selection_surface()
    {
        var source = Read(
            "freep",
            "FreeP.App.Recording.Windows",
            "WindowsNativePrintHandoff.cs");

        source.Should().Contain("TryShowPrinterSelectionDialog(")
            .And.Contain("PrintDlgEx(ref dialog)")
            .And.NotContain("WindowsNativePrintHandoffAdapter")
            .And.NotContain("ILinuxNativePrintHandoffAdapter")
            .And.NotContain("LinuxNativePrintCapability")
            .And.NotContain("LinuxNativePrintResult")
            .And.NotContain("IPlatformPrintService")
            .And.NotContain("SubmitAsync(");
    }

    [Fact]
    public void Avalonia_print_workflow_uses_shared_services_and_owns_the_temporary_pdf()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("IPlatformPrintService _printService")
            .And.Contain("new CupsPrintService()")
            .And.Contain("new WindowsPrintService(")
            .And.Contain("PresentationPrintOutputPackageExecutor.ValidatePackage(package)")
            .And.Contain("TemporaryFileLease.Create(\"freep-print-\", \".pdf\")")
            .And.Contain("_printService.SubmitAsync(")
            .And.NotContain("LinuxNativePrintHandoffAdapter")
            .And.NotContain("WindowsNativePrintHandoffAdapter");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
