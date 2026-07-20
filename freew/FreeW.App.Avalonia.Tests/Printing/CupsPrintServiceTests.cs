using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia.Printing;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class CupsPrintServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ParsesPrintersAndDefault()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, "printer Office is idle.\nprinter PDF is idle.\n", ""),
            new ProcessResult(0, "system default destination: Office\n", ""));

        var result = await new CupsPrintService(runner).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Available);
        result.DefaultPrinter.Should().Be("Office");
        result.Printers.Should().ContainSingle(printer => printer.Name == "Office" && printer.IsDefault);
        result.Printers.Should().ContainSingle(printer => printer.Name == "PDF" && !printer.IsDefault);
        runner.Invocations.Select(invocation => invocation.FileName).Should().Equal("lpstat", "lpstat");
    }

    [Fact]
    public async Task SubmitAsync_BuildsCupsArgumentsForPrinterCopiesRangeAndOrientation()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var runner = new FakeProcessRunner(
                new ProcessResult(0, "printer Office is idle.\n", ""),
                new ProcessResult(0, "system default destination: Office\n", ""),
                new ProcessResult(0, "request id is Office-17 (1 file(s))\n", ""));
            var result = await new CupsPrintService(runner).SubmitAsync(
                pdfPath,
                new PrintSelection(Copies: 2, PageRange: PrintPageRange.Between(2, 4), Orientation: PrintOrientation.Landscape));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            var invocation = runner.Invocations.Last();
            invocation.FileName.Should().Be("lp");
            invocation.Arguments.Should().ContainInOrder("-d", "Office", "-n", "2", "-P", "2-4", "-o", "orientation-requested=4", pdfPath);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_NoPrintersReturnsWithoutCallingLp()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var runner = new FakeProcessRunner(new ProcessResult(0, "", ""));
            var result = await new CupsPrintService(runner).SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.NoPrinters);
            runner.Invocations.Should().ContainSingle();
            runner.Invocations[0].Arguments.Should().Equal("-p");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task DiscoverAsync_CancellationIsReported()
    {
        var runner = new FakeProcessRunner(cancellation: true);

        var result = await new CupsPrintService(runner).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results;
        private readonly bool _cancellation;

        public FakeProcessRunner(params ProcessResult[] results)
        {
            _results = new Queue<ProcessResult>(results);
        }

        public FakeProcessRunner(bool cancellation)
        {
            _results = new Queue<ProcessResult>();
            _cancellation = cancellation;
        }

        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            if (_cancellation)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
