using Free.Shared.AppServices.Printing;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

public sealed class CupsPlatformPrinterTests
{
    [Fact]
    public async Task GetPrintersAsync_UsesSharedRunnerResults()
    {
        var runner = new QueueProcessRunner(
            new ProcessResult(0, "Office\nPDF\n", ""),
            new ProcessResult(0, "system default destination: PDF\n", ""));
        var printer = new CupsPlatformPrinter(runner, canPrintOverride: true);

        var printers = await printer.GetPrintersAsync();

        printers.Should().Equal(
            new PrinterDescriptor("PDF", "PDF", IsDefault: true),
            new PrinterDescriptor("Office", "Office", IsDefault: false));
        runner.Invocations.Select(invocation => invocation.FileName)
            .Should().Equal("lpstat", "lpstat");
        runner.Invocations.Select(invocation => invocation.Arguments.Single())
            .Should().Equal("-e", "-d");
    }

    [Fact]
    public async Task SubmitAsync_TranslatesRunnerErrorAndDeletesTemporaryDocument()
    {
        var runner = new QueueProcessRunner(new ProcessResult(9, "", "queue stopped\n"));
        var printer = new CupsPlatformPrinter(runner, canPrintOverride: true);

        var result = await printer.SubmitAsync(CreateSubmission());

        result.Should().Be(PrintSubmissionResult.Failure("Printing failed: queue stopped"));
        var invocation = runner.Invocations.Should().ContainSingle().Which;
        invocation.FileName.Should().Be("lp");
        invocation.Arguments.Should().ContainInOrder("-d", "Office", "-n", "2", "-P", "1-3");
        File.Exists(invocation.Arguments[^1]).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_TranslatesLinkedTimeoutCancellation()
    {
        var runner = new BlockingProcessRunner();
        var printer = new CupsPlatformPrinter(
            runner,
            commandTimeout: TimeSpan.FromMilliseconds(50),
            canPrintOverride: true);

        var result = await printer.SubmitAsync(CreateSubmission());

        result.Should().Be(PrintSubmissionResult.Failure("Printing failed: the CUPS command timed out."));
        runner.CancellationObserved.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_TranslatesMissingCupsUtility()
    {
        var runner = new ThrowingProcessRunner(new System.ComponentModel.Win32Exception("missing"));
        var printer = new CupsPlatformPrinter(runner, canPrintOverride: true);

        var result = await printer.SubmitAsync(CreateSubmission());

        result.Should().Be(PrintSubmissionResult.Failure(
            "Printing failed: the CUPS 'lp' utility is not installed on this host."));
    }

    [Fact]
    public async Task SubmitAsync_DoesNotTranslateCallerCancellationAsTimeout()
    {
        var runner = new BlockingProcessRunner();
        var printer = new CupsPlatformPrinter(
            runner,
            commandTimeout: TimeSpan.FromSeconds(10),
            canPrintOverride: true);
        using var cancellation = new CancellationTokenSource();

        var submission = printer.SubmitAsync(CreateSubmission(), cancellation.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        var result = await submission;

        result.Succeeded.Should().BeFalse();
        result.StatusText.Should().StartWith("Printing failed:");
        result.StatusText.Should().NotContain("timed out");
        runner.CancellationObserved.Should().BeTrue();
    }

    private static PrintJobSubmission CreateSubmission() =>
        new(
            PrinterId: "Office",
            DocumentBytes: [0x25, 0x50, 0x44, 0x46],
            Copies: 2,
            Collate: true,
            FirstPage: 1,
            LastPage: 3,
            JobTitle: "Quarterly report");

    private sealed class QueueProcessRunner(params ProcessResult[] results) : IProcessRunner
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

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking runner unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class ThrowingProcessRunner(Exception exception) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProcessResult>(exception);
    }
}
