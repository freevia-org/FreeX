using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia.Printing;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class WindowsPrintServiceTests
{
    [Fact]
    public void Factory_SelectsWindowsBackendOnWindows()
    {
        var service = PlatformPrintServiceFactory.Create();

        if (OperatingSystem.IsWindows())
            service.Should().BeOfType<WindowsPrintService>();
        else
            service.Should().BeOfType<CupsPrintService>();
    }

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
                new FakeHandoff(new WindowsPrintHandoffResult(false, Message: "No PDF handler")),
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

    private static async Task<string> CreatePdfAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"freew-windows-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4 test");
        return path;
    }

    private sealed class FakeCatalog(
        IReadOnlyList<string> printers,
        string? defaultPrinter) : IWindowsPrinterCatalog
    {
        public int Calls { get; private set; }

        public WindowsPrinterSnapshot Discover()
        {
            Calls++;
            return new WindowsPrinterSnapshot(printers, defaultPrinter);
        }
    }

    private sealed class FakeHandoff(
        WindowsPrintHandoffResult? result = null) : IWindowsPdfPrintHandoff
    {
        public List<(string PdfPath, string PrinterName)> Submissions { get; } = [];

        public Task<WindowsPrintHandoffResult> SubmitAsync(
            string pdfPath,
            string printerName,
            CancellationToken cancellationToken)
        {
            Submissions.Add((pdfPath, printerName));
            return Task.FromResult(result ?? new WindowsPrintHandoffResult(true, 0));
        }
    }
}
