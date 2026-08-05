using FluentAssertions;
using Free.Shared.AppServices.Printing;
using SharedCupsPrintCommandPlanner = Free.Shared.AppServices.Printing.CupsPrintCommandPlanner;

namespace FreeX.App.Services.Tests;

public sealed class SharedCupsPrintServiceTests
{
    [Fact]
    public void Service_OwnershipIsSharedAndPlatformSupportIsReported()
    {
        IPlatformPrintService service = new CupsPrintService(new FakeProcessRunner());

        service.GetType().Assembly.GetName().Name.Should().Be("Free.Shared.AppServices");
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
    public async Task SubmitAsync_BuildsSupersetCupsArguments()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = SuccessfulSubmissionRunner();
            var result = await new CupsPrintService(runner, isSupportedOverride: true).SubmitAsync(
                pdfPath,
                new PrintSelection(
                    Copies: 2,
                    PageRange: PrintPageRange.Between(2, 4),
                    Orientation: PrintOrientation.Landscape,
                    Collate: false));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            runner.Invocations.Last().Should().BeEquivalentTo(
                new ProcessInvocation(
                    "lp",
                    [
                        "-d", "Office", "-n", "2", "-o", "collate=false", "-P", "2-4",
                        "-o", "orientation-requested=4", pdfPath,
                    ]));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_NoPrintersReturnsWithoutCallingLp()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new FakeProcessRunner(new ProcessResult(0, "", ""));

            var result = await new CupsPrintService(runner, isSupportedOverride: true)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.NoPrinters);
            runner.Invocations.Should().ContainSingle();
            runner.Invocations[0].Should().BeEquivalentTo(SharedCupsPrintCommandPlanner.ListPrinters());
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
    public async Task DiscoverAsync_UnsupportedBackendDoesNotInvokeCommands()
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
    }

    [Fact]
    public async Task SubmitAsync_LpFailureIsReportedWithoutClaimingSuccess()
    {
        var pdfPath = await CreatePdfStubAsync();
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
    public async Task SubmitAsync_CancellationDuringLpIsReported()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new CancellingSubmitProcessRunner();

            var result = await new CupsPrintService(runner, isSupportedOverride: true)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
            runner.Invocations.Select(invocation => invocation.FileName)
                .Should().Equal("lpstat", "lpstat", "lp");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CancellationBeforeSpoolingIsReported()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new CupsPrintService(new FakeProcessRunner())
            .SubmitAsync("unused.pdf", new PrintSelection(), cancellation.Token);

        result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
    }

    private static FakeProcessRunner SuccessfulSubmissionRunner() => new(
        new ProcessResult(0, "printer Office is idle.\n", ""),
        new ProcessResult(0, "system default destination: Office\n", ""),
        new ProcessResult(0, "request id is Office-17 (1 file(s))\n", ""));

    private static async Task<string> CreatePdfStubAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"free-shared-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46]);
        return path;
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

    private sealed class CancellingSubmitProcessRunner : IProcessRunner
    {
        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            if (Invocations.Count == 3)
                throw new OperationCanceledException(cancellationToken);

            return Task.FromResult(Invocations.Count == 1
                ? new ProcessResult(0, "printer Office is idle.\n", "")
                : new ProcessResult(0, "system default destination: Office\n", ""));
        }
    }
}
