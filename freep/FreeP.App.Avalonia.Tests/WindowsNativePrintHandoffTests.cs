using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsNativePrintHandoffTests
{
    [Fact]
    public void AvaloniaWindowRoutesWindowsBuildsToNativePrintOutput()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("WindowsNativePrintOutput.Detect()");
        source.Should().Contain("WindowsNativePrintOutput.CreateAdapter(capability)");
        source.Should().Contain("FREEP_WINDOWS_CAPTURE");
    }

    [Fact]
    public async Task WindowsAdapterRejectsNonPdfBeforeStartingShell()
    {
        var capability = new LinuxNativePrintCapability(
            CanPrint: true,
            ExecutablePath: "windows-shell-print",
            PrinterName: "test-printer",
            Reason: "ready");
        var adapter = new WindowsNativePrintHandoffAdapter(capability);

        var result = await adapter.PrintAsync([1, 2, 3], "test");

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty PDF");
    }
}
