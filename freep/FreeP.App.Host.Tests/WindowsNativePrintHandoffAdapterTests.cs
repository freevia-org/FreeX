using System.IO;
using Free.Shared.AppServices.Printing;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Tests;

public sealed class WindowsNativePrintHandoffAdapterTests
{
    [Fact]
    public async Task Adapter_TranslatesAcceptedSharedHandoffAndDeletesTemporaryPdf()
    {
        var printService = new FakePrintService(new PrintSubmissionResult(
            PrintSubmissionStatus.Submitted,
            "Office"));
        var adapter = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            printService);

        var result = await adapter.PrintAsync("%PDF-1.4 test"u8.ToArray(), "Quarterly review");

        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().BeNull();
        printService.Selection.Should().Be(new PrintSelection("Office", JobTitle: "Quarterly review"));
        printService.PdfPath.Should().NotBeNull();
        File.Exists(printService.PdfPath!).Should().BeFalse();
    }

    [Fact]
    public async Task Adapter_PreservesFreePResultTranslationForExitedAndFailedHandlers()
    {
        var exited = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            new FakePrintService(new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                "Office",
                NativeExitCode: 7)));
        var failed = new WindowsNativePrintHandoffAdapter(
            AvailableCapability(),
            new FakePrintService(new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                "Office",
                Message: "No PDF handler",
                NativeErrorCode: 1155)));

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

    private sealed class FakePrintService(
        PrintSubmissionResult result) : IPlatformPrintService
    {
        public string? PdfPath { get; private set; }
        public PrintSelection? Selection { get; private set; }

        public bool IsSupported => true;

        public Task<PrinterDiscoveryResult> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Available,
                [new PrinterInfo("Office", IsDefault: true)],
                "Office"));

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default)
        {
            PdfPath = pdfPath;
            Selection = selection;
            return Task.FromResult(result);
        }
    }
}
