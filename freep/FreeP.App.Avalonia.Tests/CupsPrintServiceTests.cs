using Free.Shared.AppServices.Printing;
using FreeP.App.Avalonia.Printing;

namespace FreeP.App.Avalonia.Tests;

public sealed class CupsPrintServiceTests
{
    [Fact]
    public void CupsPrintService_ImplementsSharedPlatformBoundary()
    {
        IPlatformPrintService service = new CupsPrintService(new FakeProcessRunner());

        service.IsSupported.Should().Be(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public async Task DiscoverAsync_ParsesPrintersAndDefault()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, "printer Office is idle.\nprinter PDF is idle.\n", ""),
            new ProcessResult(0, "system default destination: Office\n", ""));

        var result = await new CupsPrintService(runner, isSupportedOverride: true).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Available);
        result.DefaultPrinter.Should().Be("Office");
        result.Printers.Should().ContainSingle(printer => printer.Name == "Office" && printer.IsDefault);
        result.Printers.Should().ContainSingle(printer => printer.Name == "PDF" && !printer.IsDefault);
    }

    [Fact]
    public async Task SubmitAsync_BuildsCupsArgumentsForPrinterCopiesRangeCollationAndOrientation()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freep-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var runner = new FakeProcessRunner(
                new ProcessResult(0, "printer Office is idle.\n", ""),
                new ProcessResult(0, "system default destination: Office\n", ""),
                new ProcessResult(0, "request id is Office-17 (1 file(s))\n", ""));
            var result = await new CupsPrintService(runner, isSupportedOverride: true).SubmitAsync(
                pdfPath,
                new PrintSelection(
                    Copies: 2,
                    PageRange: PrintPageRange.Between(2, 4),
                    Orientation: PrintOrientation.Landscape,
                    Collate: false));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            var invocation = runner.Invocations.Last();
            invocation.FileName.Should().Be("lp");
            invocation.Arguments.Should().ContainInOrder(
                "-d", "Office", "-n", "2", "-o", "collate=false", "-P", "2-4",
                "-o", "orientation-requested=4", pdfPath);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task DiscoverAsync_NoPrintersReturnsWithoutClaimingAvailability()
    {
        var result = await new CupsPrintService(
            new FakeProcessRunner(new ProcessResult(0, "", "")),
            isSupportedOverride: true).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.NoPrinters);
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_CancellationIsReported()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new CupsPrintService(new FakeProcessRunner(), isSupportedOverride: true)
            .DiscoverAsync(cancellation.Token);

        result.Status.Should().Be(PrinterDiscoveryStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    private sealed class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
