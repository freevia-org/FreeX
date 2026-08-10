using Free.Shared.AppServices.Windows;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Tests;

public sealed class WindowsNativePrintHandoffAdapterTests
{
    [Fact]
    public async Task Adapter_TranslatesAcceptedSharedHandoffAndDeletesTemporaryPdf()
    {
        var handoff = new FakeHandoff(WindowsShellPdfPrintHandoffResult.Accepted());
        var adapter = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            handoff);

        var result = await adapter.PrintAsync("%PDF-1.4 test"u8.ToArray(), "Quarterly review");

        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().BeNull();
        handoff.PrinterName.Should().Be("Office");
        handoff.PdfPath.Should().NotBeNull();
        File.Exists(handoff.PdfPath!).Should().BeFalse();
    }

    [Fact]
    public async Task Adapter_PreservesFreePResultTranslationForExitedAndFailedHandlers()
    {
        var exited = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            new FakeHandoff(WindowsShellPdfPrintHandoffResult.HandlerExited(7)));
        var failed = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            new FakeHandoff(WindowsShellPdfPrintHandoffResult.Failed("No PDF handler", 1155)));

        var exitedResult = await exited.PrintAsync("%PDF-1.4 test"u8.ToArray(), "Quarterly review");
        var failedResult = await failed.PrintAsync("%PDF-1.4 test"u8.ToArray(), "Quarterly review");

        exitedResult.Succeeded.Should().BeTrue();
        exitedResult.ExitCode.Should().Be(7);
        failedResult.Succeeded.Should().BeFalse();
        failedResult.FailureReason.Should().Be("No PDF handler");
        failedResult.ExitCode.Should().Be(1155);
    }

    private static LinuxNativePrintCapability AvailableCapability() =>
        new(
            CanPrint: true,
            ExecutablePath: "windows-shell-print",
            PrinterName: "Office",
            Reason: "Available");

    private sealed class FakeHandoff(
        WindowsShellPdfPrintHandoffResult result) : IWindowsPdfPrintHandoff
    {
        public string? PdfPath { get; private set; }
        public string? PrinterName { get; private set; }

        public Task<WindowsShellPdfPrintHandoffResult> SubmitAsync(
            string pdfPath,
            string printerName,
            CancellationToken cancellationToken = default)
        {
            PdfPath = pdfPath;
            PrinterName = printerName;
            return Task.FromResult(result);
        }
    }
}
