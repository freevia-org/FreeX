using Free.Shared.AppServices.Printing;
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
        RecordingPrintService service,
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
}
