using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia.Printing;

namespace FreeW.App.Avalonia.Tests.Printing;

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
            var result = await new CupsPrintService(runner, isSupportedOverride: true).SubmitAsync(
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
            var result = await new CupsPrintService(runner, isSupportedOverride: true).SubmitAsync(pdfPath, new PrintSelection());

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
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new CupsPrintService(new FakeProcessRunner(), isSupportedOverride: true)
            .DiscoverAsync(cancellation.Token);

        result.Status.Should().Be(PrinterDiscoveryStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task DiscoverAsync_UnsupportedBackendIsReportedWithoutInvokingCommands()
    {
        var runner = new FakeProcessRunner();

        var result = await new CupsPrintService(runner, isSupportedOverride: false).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Unavailable);
        result.Printers.Should().BeEmpty();
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_CommandFailureIsReportedAsUnavailable()
    {
        var runner = new FakeProcessRunner(new ProcessResult(1, "", "lpstat: not found"));

        var result = await new CupsPrintService(runner, isSupportedOverride: true).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Unavailable);
        result.Message.Should().Contain("lpstat");
        runner.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task SubmitAsync_LpFailureIsReportedWithoutClaimingSuccess()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var runner = new FakeProcessRunner(
                new ProcessResult(0, "printer Office is idle.\n", ""),
                new ProcessResult(0, "system default destination: Office\n", ""),
                new ProcessResult(1, "", "lp: backend rejected job"));

            var result = await new CupsPrintService(runner, isSupportedOverride: true)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Succeeded.Should().BeFalse();
            result.Message.Should().Contain("backend rejected");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CancellationDuringLpIsReportedWithoutClaimingSuccess()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var runner = new CancellingSubmitProcessRunner();
            var result = await new CupsPrintService(runner, isSupportedOverride: true)
                .SubmitAsync(pdfPath, new PrintSelection(), CancellationToken.None);

            result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
            result.Succeeded.Should().BeFalse();
            runner.Invocations.Select(invocation => invocation.FileName)
                .Should().Equal("lpstat", "lpstat", "lp");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CancellationIsReportedBeforeSpooling()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await new CupsPrintService(new FakeProcessRunner())
                .SubmitAsync(pdfPath, new PrintSelection(), cancellation.Token);

            result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
            result.Message.Should().Contain("cancelled");
        }
        finally
        {
            File.Delete(pdfPath);
        }
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

    private sealed class CancellingSubmitProcessRunner : IProcessRunner
    {
        private int _callCount;

        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            _callCount++;
            if (_callCount == 3)
                throw new OperationCanceledException(cancellationToken);

            return Task.FromResult(_callCount == 1
                ? new ProcessResult(0, "printer Office is idle.\n", "")
                : new ProcessResult(0, "system default destination: Office\n", ""));
        }
    }
}
