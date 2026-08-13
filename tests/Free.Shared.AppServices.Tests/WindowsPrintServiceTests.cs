using System.Diagnostics;
using Free.Shared.AppServices.Printing;
using Free.Shared.AppServices.Windows;

namespace Free.Shared.AppServices.Tests;

public sealed class WindowsPrintServiceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("freew-windows-print-");

    public void Dispose() => _temporaryDirectory.Dispose();
    [Fact]
    public void WindowsBackend_DeclaresPreparedPdfRangeAndOrientationHandling()
    {
        IPlatformPrintService service = new WindowsPrintService(isSupportedOverride: true);

        service.RangeAndOrientationHandling.Should().Be(PrintRangeAndOrientationHandling.PreparedPdf);
    }

    [Fact]
    public async Task DiscoverAsync_UsesWindowsQueuesAndDefault()
    {
        var service = new WindowsPrintService(
            new FakeCatalog(["PDF", "Office", "Office"], "Office"),
            new FakeHandoff(),
            isSupportedOverride: true);

        var result = await service.DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Available);
        result.DefaultPrinter.Should().Be("Office");
        result.Printers.Select(printer => printer.Name).Should().Equal("Office", "PDF");
        result.Printers.Should().ContainSingle(printer => printer.Name == "Office" && printer.IsDefault);
    }

    [Fact]
    public async Task SubmitAsync_HandsEachCopyToSelectedWindowsQueue()
    {
        var pdfPath = await CreatePdfAsync();
        try
        {
            var handoff = new FakeHandoff();
            var service = new WindowsPrintService(
                new FakeCatalog(["Office", "PDF"], "Office"),
                handoff,
                isSupportedOverride: true);

            var result = await service.SubmitAsync(
                pdfPath,
                new PrintSelection("PDF", Copies: 2));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            handoff.Submissions.Should().Equal((pdfPath, "PDF"), (pdfPath, "PDF"));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_RejectsUnsupportedRangeWithoutStartingShellHandoff()
    {
        var pdfPath = await CreatePdfAsync();
        try
        {
            var handoff = new FakeHandoff();
            var service = new WindowsPrintService(
                new FakeCatalog(["Office"], "Office"),
                handoff,
                isSupportedOverride: true);

            var result = await service.SubmitAsync(
                pdfPath,
                new PrintSelection("Office", PageRange: PrintPageRange.Between(2, 3)));

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Message.Should().Contain("all pages");
            handoff.Submissions.Should().BeEmpty();
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_DoesNotClaimSuccessWhenShellHandoffFails()
    {
        var pdfPath = await CreatePdfAsync();
        try
        {
            var service = new WindowsPrintService(
                new FakeCatalog(["Office"], "Office"),
                new FakeHandoff(WindowsShellPdfPrintHandoffResult.Failed("No PDF handler")),
                isSupportedOverride: true);

            var result = await service.SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Message.Should().Be("No PDF handler");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CompatibilityOptionsPreserveValidatedQueueAndNativeExitCode()
    {
        var pdfPath = await CreatePdfAsync();
        try
        {
            var catalog = new FakeCatalog(["Office"], "Office");
            var service = new WindowsPrintService(
                catalog,
                new FakeHandoff(WindowsShellPdfPrintHandoffResult.HandlerExited(7)),
                new WindowsPrintServiceOptions(
                    RequirePrinterDiscoveryBeforeSubmission: false,
                    RejectNonZeroHandlerExitCode: false),
                isSupportedOverride: true);

            var result = await service.SubmitAsync(
                pdfPath,
                new PrintSelection("Office", JobTitle: "Quarterly review"));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            result.NativeExitCode.Should().Be(7);
            catalog.Calls.Should().Be(0);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_PropagatesNativeHandoffFailureDetails()
    {
        var pdfPath = await CreatePdfAsync();
        try
        {
            var service = new WindowsPrintService(
                new FakeCatalog(["Office"], "Office"),
                new FakeHandoff(WindowsShellPdfPrintHandoffResult.Failed("No PDF handler", 1155)),
                isSupportedOverride: true);

            var result = await service.SubmitAsync(pdfPath, new PrintSelection("Office"));

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Message.Should().Be("No PDF handler");
            result.NativeErrorCode.Should().Be(1155);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task DiscoverAsync_CancellationIsReportedBeforeCatalogAccess()
    {
        var catalog = new FakeCatalog(["Office"], "Office");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new WindowsPrintService(
                catalog,
                new FakeHandoff(),
                isSupportedOverride: true)
            .DiscoverAsync(cancellation.Token);

        result.Status.Should().Be(PrinterDiscoveryStatus.Cancelled);
        catalog.Calls.Should().Be(0);
    }

    [Fact]
    public void SharedCatalogResult_NormalizesOrdersAndPreservesDefaultQueueIdentity()
    {
        var result = WindowsPrinterCatalogResult.FromQueues(
            [" PDF ", "office", "PDF"],
            " Office ");

        result.Status.Should().Be(WindowsPrinterCatalogStatus.Available);
        result.Printers.Should().Equal("office", "PDF");
        result.DefaultPrinter.Should().Be("Office");
    }

    [Fact]
    public void SharedShellHandoff_BuildsQuotedHiddenPrintToInvocation()
    {
        var startInfo = WindowsShellPdfPrintHandoff.BuildStartInfo(
            @"C:\Temp\deck.pdf",
            "Office \"East\"");

        startInfo.FileName.Should().Be(@"C:\Temp\deck.pdf");
        startInfo.Verb.Should().Be("printto");
        startInfo.Arguments.Should().Be("\"Office \\\"East\\\"\"");
        startInfo.UseShellExecute.Should().BeTrue();
        startInfo.WindowStyle.Should().Be(ProcessWindowStyle.Hidden);
    }

    [Fact]
    public async Task SharedShellHandoff_TreatsAcceptanceTimeoutAsStartedWithoutKillingHandler()
    {
        var process = new FakeShellProcess(
            exitCode: 0,
            wait: cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        var handoff = new WindowsShellPdfPrintHandoff(
            new FakeShellProcessStarter(process),
            TimeSpan.Zero,
            isSupportedOverride: true);

        var result = await handoff.SubmitAsync("deck.pdf", "Office");

        result.Status.Should().Be(WindowsShellPdfPrintHandoffStatus.Accepted);
        result.Started.Should().BeTrue();
        result.ExitCode.Should().BeNull();
        process.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SharedShellHandoff_ReportsHandlerExitCodeThroughTypedOutcome()
    {
        var process = new FakeShellProcess(exitCode: 7, wait: _ => Task.CompletedTask);
        var handoff = new WindowsShellPdfPrintHandoff(
            new FakeShellProcessStarter(process),
            TimeSpan.FromSeconds(8),
            isSupportedOverride: true);

        var result = await handoff.SubmitAsync("deck.pdf", "Office");

        result.Status.Should().Be(WindowsShellPdfPrintHandoffStatus.HandlerExited);
        result.Started.Should().BeTrue();
        result.ExitCode.Should().Be(7);
        result.FailureReason.Should().Contain("code 7");
    }

    private async Task<string> CreatePdfAsync()
    {
        var path = Path.Combine(_temporaryDirectory.Path, "input.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4 test");
        return path;
    }

    private sealed class FakeCatalog(
        IReadOnlyList<string> printers,
        string? defaultPrinter) : IWindowsPrinterCatalog
    {
        public int Calls { get; private set; }

        public WindowsPrinterCatalogResult Discover()
        {
            Calls++;
            return WindowsPrinterCatalogResult.FromQueues(printers, defaultPrinter);
        }
    }

    private sealed class FakeHandoff(
        WindowsShellPdfPrintHandoffResult? result = null) : IWindowsPdfPrintHandoff
    {
        public List<(string PdfPath, string PrinterName)> Submissions { get; } = [];

        public Task<WindowsShellPdfPrintHandoffResult> SubmitAsync(
            string pdfPath,
            string printerName,
            CancellationToken cancellationToken)
        {
            Submissions.Add((pdfPath, printerName));
            return Task.FromResult(result ?? WindowsShellPdfPrintHandoffResult.HandlerExited(0));
        }
    }

    private sealed class FakeShellProcessStarter(IWindowsShellProcess process) : IWindowsShellProcessStarter
    {
        public IWindowsShellProcess? Start(ProcessStartInfo startInfo) => process;
    }

    private sealed class FakeShellProcess(
        int exitCode,
        Func<CancellationToken, Task> wait) : IWindowsShellProcess
    {
        public int ExitCode => exitCode;
        public bool Disposed { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => wait(cancellationToken);

        public void Dispose() => Disposed = true;
    }
}
