using Free.Shared.AppServices.Printing;
using Free.Shared.AppServices.Windows;
using System.Text;

namespace Free.Shared.AppServices.Tests;

public sealed class PortablePrintSubmissionWorkflowTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "portable-print-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_OwnsDiscoverySelectionRenderingSubmissionAndCleanup()
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(PrintSubmissionStatus.Submitted, "Office"),
        };
        var workflow = CreateWorkflow(service);
        PortablePrintSelectionIntent? capturedIntent = null;
        PrintSelection? renderSelection = null;

        var result = await workflow.ExecuteAsync(
            (intent, _) =>
            {
                capturedIntent = intent;
                return Task.FromResult<PrintSelection?>(new(
                    "Office",
                    Copies: 2,
                    PageRange: PrintPageRange.Between(2, 4),
                    Orientation: PrintOrientation.Landscape,
                    Collate: false,
                    JobTitle: "Quarterly report"));
            },
            async (output, selection, token) =>
            {
                renderSelection = selection;
                await output.WriteAsync(Encoding.ASCII.GetBytes("%PDF-test"), token);
            },
            new PrintSelection(PrinterName: "Office", Copies: 3));

        result.Succeeded.Should().BeTrue();
        result.Status.Should().Be(OperationStatus.Completed);
        result.Submission.Should().Be(service.Submission);
        capturedIntent.Should().NotBeNull();
        capturedIntent!.DialogSession.State.PrinterNames.Should().Equal("Office", "Archive");
        capturedIntent.DialogSession.State.SelectedPrinterIndex.Should().Be(0);
        renderSelection.Should().Be(new PrintSelection());
        service.SubmittedSelection.Should().Be(result.Selection);
        service.SubmittedBytes.Should().Equal(Encoding.ASCII.GetBytes("%PDF-test"));
        service.SubmittedPath.Should().NotBeNull();
        File.Exists(service.SubmittedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_PreparedPdfAppliesRangeAndOrientationOnlyWhileRendering()
    {
        var selection = new PrintSelection(
            "Office",
            Copies: 4,
            PageRange: PrintPageRange.Single(7),
            Orientation: PrintOrientation.Portrait,
            Collate: false,
            JobTitle: "Slides");
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Handling = PrintRangeAndOrientationHandling.PreparedPdf,
        };
        var workflow = CreateWorkflow(service);
        PrintSelection? renderSelection = null;

        var result = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(selection),
            (_, renderedSelection, _) =>
            {
                renderSelection = renderedSelection;
                return ValueTask.CompletedTask;
            });

        result.Succeeded.Should().BeTrue();
        renderSelection.Should().Be(selection);
        result.Handoff!.SubmissionSelection.Should().Be(selection with
        {
            PageRange = PrintPageRange.All,
            Orientation = PrintOrientation.Document,
        });
        service.SubmittedSelection.Should().Be(result.Handoff.SubmissionSelection);
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedServiceStopsBeforeDiscoveryOrProductPorts()
    {
        var service = new RecordingPrintService { IsSupported = false };
        var selected = false;
        var rendered = false;

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) =>
            {
                selected = true;
                return Task.FromResult<PrintSelection?>(null);
            },
            (_, _, _) =>
            {
                rendered = true;
                return ValueTask.CompletedTask;
            });

        result.Status.Should().Be(OperationStatus.Unavailable);
        result.Submission!.Status.Should().Be(PrintSubmissionStatus.Unavailable);
        service.DiscoveryCalls.Should().Be(0);
        selected.Should().BeFalse();
        rendered.Should().BeFalse();
    }

    [Theory]
    [InlineData(PrinterDiscoveryStatus.NoPrinters, OperationStatus.Unavailable, PrintSubmissionStatus.NoPrinters)]
    [InlineData(PrinterDiscoveryStatus.Unavailable, OperationStatus.Unavailable, PrintSubmissionStatus.Unavailable)]
    [InlineData(PrinterDiscoveryStatus.Cancelled, OperationStatus.Cancelled, PrintSubmissionStatus.Cancelled)]
    [InlineData(PrinterDiscoveryStatus.Failed, OperationStatus.Failed, PrintSubmissionStatus.Failed)]
    public async Task ExecuteAsync_MapsDiscoveryOutcomesAndStops(
        PrinterDiscoveryStatus discoveryStatus,
        OperationStatus expectedStatus,
        PrintSubmissionStatus expectedSubmissionStatus)
    {
        var service = new RecordingPrintService
        {
            Discovery = new PrinterDiscoveryResult(discoveryStatus, [], null, "discovery detail"),
        };
        var selected = false;

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) =>
            {
                selected = true;
                return Task.FromResult<PrintSelection?>(null);
            },
            (_, _, _) => ValueTask.CompletedTask);

        result.Status.Should().Be(expectedStatus);
        result.Submission!.Status.Should().Be(expectedSubmissionStatus);
        selected.Should().BeFalse();
        service.SubmissionCalls.Should().Be(0);
        if (expectedStatus == OperationStatus.Failed)
            result.Operation.Error!.Detail.Stage.Should().Be(PortablePrintFailureStage.Discovery);
    }

    [Fact]
    public async Task ExecuteAsync_AvailableDiscoveryWithoutPrintersMapsToNoPrinters()
    {
        var service = new RecordingPrintService
        {
            Discovery = new PrinterDiscoveryResult(PrinterDiscoveryStatus.Available, [], null),
        };

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(null),
            (_, _, _) => ValueTask.CompletedTask);

        result.Status.Should().Be(OperationStatus.Unavailable);
        result.Submission!.Status.Should().Be(PrintSubmissionStatus.NoPrinters);
    }

    [Fact]
    public async Task ExecuteAsync_SelectionDismissalMapsToCancellationWithoutCreatingPdf()
    {
        var service = new RecordingPrintService { Discovery = AvailableDiscovery() };

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(null),
            (_, _, _) => throw new InvalidOperationException("must not render"));

        result.Status.Should().Be(OperationStatus.Cancelled);
        result.Submission!.Status.Should().Be(PrintSubmissionStatus.Cancelled);
        Directory.Exists(_temporaryRoot).Should().BeFalse();
        service.SubmissionCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true, PortablePrintValidationIssue.RequestedSelectionInvalid, 0)]
    [InlineData(false, PortablePrintValidationIssue.SelectedSelectionInvalid, 1)]
    public async Task ExecuteAsync_MapsInvalidSelectionsToValidationOutcomes(
        bool invalidRequestedSelection,
        PortablePrintValidationIssue expectedIssue,
        int expectedDiscoveryCalls)
    {
        var service = new RecordingPrintService { Discovery = AvailableDiscovery() };
        var invalid = new PrintSelection(Copies: 0);

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(invalid),
            (_, _, _) => ValueTask.CompletedTask,
            invalidRequestedSelection ? invalid : null);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Operation.Validation!.Detail.Should().Be(expectedIssue);
        result.Operation.Error!.Detail.Stage.Should().Be(PortablePrintFailureStage.Selection);
        service.DiscoveryCalls.Should().Be(expectedDiscoveryCalls);
        service.SubmissionCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(PrintSubmissionStatus.Submitted, OperationStatus.Completed)]
    [InlineData(PrintSubmissionStatus.Cancelled, OperationStatus.Cancelled)]
    [InlineData(PrintSubmissionStatus.NoPrinters, OperationStatus.Unavailable)]
    [InlineData(PrintSubmissionStatus.Unavailable, OperationStatus.Unavailable)]
    [InlineData(PrintSubmissionStatus.Failed, OperationStatus.Failed)]
    public async Task ExecuteAsync_MapsSubmissionOutcomesAndAlwaysDeletesPdf(
        PrintSubmissionStatus submissionStatus,
        OperationStatus expectedStatus)
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(submissionStatus, "Office", Message: "submission detail"),
        };

        var result = await CreateWorkflow(service).ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 1, 2, 3 }, token));

        result.Status.Should().Be(expectedStatus);
        result.Submission.Should().Be(service.Submission);
        service.SubmittedPath.Should().NotBeNull();
        File.Exists(service.SubmittedPath!).Should().BeFalse();
        if (expectedStatus == OperationStatus.Failed)
            result.Operation.Error!.Detail.Stage.Should().Be(PortablePrintFailureStage.Submission);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTemporaryPdfWhenSubmissionCannotConfirmHandlerFinishedReading()
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                "Office",
                SourceFileMayStillBeInUse: true),
        };
        string? renderedPath = null;
        var workflow = CreateWorkflow(service, path => renderedPath = path);

        var result = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 1, 2, 3 }, token));

        result.Status.Should().Be(OperationStatus.Completed);
        renderedPath.Should().NotBeNull();
        File.Exists(renderedPath!).Should().BeTrue(
            "the platform backend could not confirm the external reader finished consuming the " +
            "file, so the workflow must not delete it out from under a still-reading process");
    }

    [Fact]
    public async Task ExecuteAsync_DeletesTemporaryPdfWhenSubmissionConfirmsHandlerFinishedReading()
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                "Office",
                SourceFileMayStillBeInUse: false),
        };
        string? renderedPath = null;
        var workflow = CreateWorkflow(service, path => renderedPath = path);

        var result = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 1, 2, 3 }, token));

        result.Status.Should().Be(OperationStatus.Completed);
        renderedPath.Should().NotBeNull();
        File.Exists(renderedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SweepsAStaleKeptTemporaryPdfBeforeReservingTheNextOneButLeavesAFreshOneAlone()
    {
        // Regression test for the r138 fix that turned "temp PDF deleted while the handler was
        // still reading it" into a permanent leak by calling Keep() with nothing to ever release
        // it again. This drives the real, unmodified entry point (ExecuteAsync) exactly the way
        // FreeW/FreeP's Avalonia print flow does -- no test-only hook is supplied.
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                "Office",
                SourceFileMayStillBeInUse: true),
        };
        var workflow = new PortablePrintSubmissionWorkflow(
            service,
            staleTemporaryPdfDirectoryPath: _temporaryRoot,
            staleTemporaryPdfAge: TimeSpan.FromMinutes(30));

        var firstResult = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 1, 2, 3 }, token));
        var firstPath = service.SubmittedPath!;
        firstResult.Status.Should().Be(OperationStatus.Completed);
        File.Exists(firstPath).Should().BeTrue(
            "the workflow must not delete a file the handler may still be reading");

        // Simulate the passage of real time (a crash, or simply the user not printing again for a
        // while) by backdating the kept file well past the configured stale-after window, instead
        // of actually sleeping in the test.
        File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow - TimeSpan.FromHours(2));

        var secondResult = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 4, 5, 6 }, token));
        var secondPath = service.SubmittedPath!;

        secondResult.Status.Should().Be(OperationStatus.Completed);
        secondPath.Should().NotBe(firstPath);
        File.Exists(firstPath).Should().BeFalse(
            "a kept temporary PDF old enough that any reader has had ample time to finish must " +
            "eventually be reaped so it does not leak forever");
        File.Exists(secondPath).Should().BeTrue(
            "the file this very call just produced (and might still be handed to an external " +
            "reader) must not be swept out from under it");
    }

    [Fact]
    public async Task ExecuteAsync_WindowsHandoffAcceptedWithoutConfirmedExitLeavesTemporaryPdfOnDisk()
    {
        // Mirrors the real FreeW/FreeP Avalonia wiring on Windows: PortablePrintSubmissionWorkflow
        // driving the actual WindowsPrintService (not a stand-in for it), so this proves the flag
        // WindowsPrintService.SubmitAsync sets on an unconfirmed handoff actually reaches the
        // workflow that owns temp-file cleanup, end to end.
        var handoff = new AcceptingOnlyHandoff();
        var windowsPrintService = new WindowsPrintService(
            new StaticWindowsPrinterCatalog(["Office"], "Office"),
            handoff,
            isSupportedOverride: true);
        string? renderedPath = null;
        var workflow = CreateWorkflow(windowsPrintService, path => renderedPath = path);

        var result = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            async (output, _, token) => await output.WriteAsync(new byte[] { 1, 2, 3 }, token));

        result.Submission!.Status.Should().Be(PrintSubmissionStatus.Submitted);
        handoff.Calls.Should().Be(1);
        renderedPath.Should().NotBeNull();
        File.Exists(renderedPath!).Should().BeTrue(
            "the shell 'printto' verb was accepted but the PDF handler never confirmed it exited, " +
            "so the workflow must not have deleted the file the handler may still be reading");
    }

    [Theory]
    [InlineData(PortablePrintFailureStage.Discovery)]
    [InlineData(PortablePrintFailureStage.Selection)]
    [InlineData(PortablePrintFailureStage.Rendering)]
    [InlineData(PortablePrintFailureStage.Submission)]
    public async Task ExecuteAsync_MapsPortExceptionsAndCleansTemporaryPdf(
        PortablePrintFailureStage stage)
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            ThrowOnDiscovery = stage == PortablePrintFailureStage.Discovery,
            ThrowOnSubmission = stage == PortablePrintFailureStage.Submission,
        };
        string? renderedPath = null;
        var workflow = CreateWorkflow(service, path => renderedPath = path);

        var result = await workflow.ExecuteAsync(
            (_, _) => stage == PortablePrintFailureStage.Selection
                ? throw new InvalidOperationException("selection failed")
                : Task.FromResult<PrintSelection?>(new("Office")),
            (output, _, _) => stage == PortablePrintFailureStage.Rendering
                ? throw new InvalidOperationException("render failed")
                : ValueTask.CompletedTask);

        result.Status.Should().Be(OperationStatus.Failed);
        result.Operation.Error!.Detail.Stage.Should().Be(stage);
        if (renderedPath is not null)
            File.Exists(renderedPath).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringRenderingReturnsCanceledAndDeletesPdf()
    {
        var service = new RecordingPrintService { Discovery = AvailableDiscovery() };
        using var cancellation = new CancellationTokenSource();
        string? renderedPath = null;
        var workflow = CreateWorkflow(service, path => renderedPath = path);

        var result = await workflow.ExecuteAsync(
            (_, _) => Task.FromResult<PrintSelection?>(new("Office")),
            (_, _, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            },
            cancellationToken: cancellation.Token);

        result.Status.Should().Be(OperationStatus.Cancelled);
        result.Operation.Exception.Should().BeOfType<OperationCanceledException>();
        service.SubmissionCalls.Should().Be(0);
        renderedPath.Should().NotBeNull();
        File.Exists(renderedPath!).Should().BeFalse();
    }

    [Fact]
    public void HandoffPlanner_RejectsInvalidSelections()
    {
        var action = () => PortablePrintSelectionHandoffPlanner.Build(
            new PrintSelection(Copies: 1000),
            PrintRangeAndOrientationHandling.PrinterSubmission);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private PortablePrintSubmissionWorkflow CreateWorkflow(
        IPlatformPrintService service,
        Action<string>? created = null) =>
        new(
            service,
            () =>
            {
                var lease = TemporaryFileLease.Create("print-", ".pdf", _temporaryRoot);
                created?.Invoke(lease.Path);
                return lease;
            });

    private static PrinterDiscoveryResult AvailableDiscovery() => new(
        PrinterDiscoveryStatus.Available,
        [new PrinterInfo("Office", IsDefault: true), new PrinterInfo("Archive")],
        "Office");

    private sealed class RecordingPrintService : IPlatformPrintService
    {
        public bool IsSupported { get; init; } = true;

        public PrintRangeAndOrientationHandling Handling { get; init; } =
            PrintRangeAndOrientationHandling.PrinterSubmission;

        public PrintRangeAndOrientationHandling RangeAndOrientationHandling => Handling;

        public PrinterDiscoveryResult Discovery { get; init; } = AvailableDiscovery();

        public PrintSubmissionResult Submission { get; init; } =
            new(PrintSubmissionStatus.Submitted, "Office");

        public bool ThrowOnDiscovery { get; init; }

        public bool ThrowOnSubmission { get; init; }

        public int DiscoveryCalls { get; private set; }

        public int SubmissionCalls { get; private set; }

        public string? SubmittedPath { get; private set; }

        public PrintSelection? SubmittedSelection { get; private set; }

        public byte[]? SubmittedBytes { get; private set; }

        public Task<PrinterDiscoveryResult> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            DiscoveryCalls++;
            return ThrowOnDiscovery
                ? throw new InvalidOperationException("discovery failed")
                : Task.FromResult(Discovery);
        }

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default)
        {
            SubmissionCalls++;
            if (ThrowOnSubmission)
                throw new InvalidOperationException("submission failed");

            SubmittedPath = pdfPath;
            SubmittedSelection = selection;
            SubmittedBytes = File.ReadAllBytes(pdfPath);
            return Task.FromResult(Submission);
        }
    }

    private sealed class AcceptingOnlyHandoff : IWindowsPdfPrintHandoff
    {
        public int Calls { get; private set; }

        public Task<WindowsShellPdfPrintHandoffResult> SubmitAsync(
            string pdfPath,
            string printerName,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(WindowsShellPdfPrintHandoffResult.Accepted());
        }
    }

    private sealed class StaticWindowsPrinterCatalog(
        IReadOnlyList<string> printers,
        string? defaultPrinter) : IWindowsPrinterCatalog
    {
        public WindowsPrinterCatalogResult Discover() =>
            WindowsPrinterCatalogResult.FromQueues(printers, defaultPrinter);
    }
}
