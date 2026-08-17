namespace Free.Shared.AppServices.Printing;

public enum PortablePrintValidationIssue
{
    RequestedSelectionInvalid,
    SelectedSelectionInvalid,
}

public enum PortablePrintFailureStage
{
    Discovery,
    Selection,
    TemporaryPdf,
    Rendering,
    Submission,
}

public sealed record PortablePrintFailure(
    PortablePrintFailureStage Stage,
    string? Message = null);

public sealed record PortablePrintSelectionHandoff(
    PrintSelection PdfSelection,
    PrintSelection SubmissionSelection);

/// <summary>
/// Separates settings that must be applied while preparing the PDF from settings understood by the
/// selected platform printer backend.
/// </summary>
public static class PortablePrintSelectionHandoffPlanner
{
    public static PortablePrintSelectionHandoff Build(
        PrintSelection selection,
        PrintRangeAndOrientationHandling handling)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        return handling == PrintRangeAndOrientationHandling.PreparedPdf
            ? new PortablePrintSelectionHandoff(
                selection,
                selection with
                {
                    PageRange = PrintPageRange.All,
                    Orientation = PrintOrientation.Document,
                })
            : new PortablePrintSelectionHandoff(new PrintSelection(), selection);
    }
}

/// <summary>
/// Renderer-neutral input for a product's native print-selection surface. The shared dialog session
/// owns initial state and validation while the product retains control creation and modal lifetime.
/// </summary>
public sealed record PortablePrintSelectionIntent(
    PrinterDiscoveryResult Discovery,
    PrintSelection RequestedSelection,
    PrintDialogSession DialogSession);

public delegate Task<PrintSelection?> PortablePrintSelectionPort(
    PortablePrintSelectionIntent intent,
    CancellationToken cancellationToken);

public delegate ValueTask PortablePdfRenderPort(
    Stream output,
    PrintSelection selection,
    CancellationToken cancellationToken);

public sealed record PortablePrintExecutionResult(
    OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure> Operation,
    PrinterDiscoveryResult? Discovery = null,
    PrintSelection? Selection = null,
    PortablePrintSelectionHandoff? Handoff = null)
{
    public OperationStatus Status => Operation.Status;

    public PrintSubmissionResult? Submission => Operation.Value;

    public bool Succeeded => Operation.Succeeded;
}

/// <summary>
/// Owns the portable printer lifecycle around product PDF rendering and native selection ports.
/// Temporary output is always released after submission, cancellation, or failure.
/// </summary>
public sealed class PortablePrintSubmissionWorkflow
{
    private readonly IPlatformPrintService _printService;
    private readonly Func<TemporaryFileLease> _createTemporaryPdf;

    public PortablePrintSubmissionWorkflow(
        IPlatformPrintService printService,
        Func<TemporaryFileLease>? createTemporaryPdf = null)
    {
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _createTemporaryPdf = createTemporaryPdf ??
            (() => TemporaryFileLease.Create("portable-print-", ".pdf"));
    }

    public async Task<PortablePrintExecutionResult> ExecuteAsync(
        PortablePrintSelectionPort selectAsync,
        PortablePdfRenderPort renderPdfAsync,
        PrintSelection? requestedSelection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(renderPdfAsync);

        requestedSelection ??= new PrintSelection();
        var requestedValidation = ValidateSelection(
            requestedSelection,
            PortablePrintValidationIssue.RequestedSelectionInvalid);
        if (requestedValidation is not null)
            return requestedValidation;

        PrinterDiscoveryResult? discovery = null;
        PrintSelection? selection = null;
        PortablePrintSelectionHandoff? handoff = null;
        var failureStage = PortablePrintFailureStage.Discovery;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            discovery = _printService.IsSupported
                ? await _printService.DiscoverAsync(cancellationToken).ConfigureAwait(false)
                : new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Unavailable,
                    [],
                    requestedSelection.PrinterName);

            if (!discovery.IsAvailable)
                return FromDiscovery(discovery, requestedSelection);

            failureStage = PortablePrintFailureStage.Selection;
            var intent = new PortablePrintSelectionIntent(
                discovery,
                requestedSelection,
                PrintDialogSession.Start(discovery, requestedSelection));
            selection = await selectAsync(intent, cancellationToken).ConfigureAwait(false);
            if (selection is null)
                return Canceled(discovery, requestedSelection.PrinterName);

            var selectedValidation = ValidateSelection(
                selection,
                PortablePrintValidationIssue.SelectedSelectionInvalid,
                discovery);
            if (selectedValidation is not null)
                return selectedValidation;

            handoff = PortablePrintSelectionHandoffPlanner.Build(
                selection,
                _printService.RangeAndOrientationHandling);

            failureStage = PortablePrintFailureStage.TemporaryPdf;
            using var temporaryPdf = _createTemporaryPdf() ??
                throw new InvalidOperationException("The temporary PDF factory returned null.");
            await using (var output = temporaryPdf.OpenWrite(useAsync: true))
            {
                failureStage = PortablePrintFailureStage.Rendering;
                await renderPdfAsync(output, handoff.PdfSelection, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            failureStage = PortablePrintFailureStage.Submission;
            var submission = await _printService.SubmitAsync(
                temporaryPdf.Path,
                handoff.SubmissionSelection,
                cancellationToken).ConfigureAwait(false);
            if (submission.SourceFileMayStillBeInUse)
            {
                // The platform backend accepted the handoff but could not confirm the external
                // reader finished consuming the file (e.g. the Windows shell "printto" verb was
                // accepted but the handler process never exited within the acceptance window).
                // Deleting the temp PDF here would race that still-reading process, so relinquish
                // cleanup ownership instead of guessing at a safe deletion time with a delay.
                temporaryPdf.Keep();
            }
            return FromSubmission(submission, discovery, selection, handoff);
        }
        catch (OperationCanceledException ex)
        {
            var canceled = new PrintSubmissionResult(
                PrintSubmissionStatus.Cancelled,
                selection?.PrinterName ?? requestedSelection.PrinterName);
            return new PortablePrintExecutionResult(
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Cancel(canceled, exception: ex),
                discovery,
                selection,
                handoff);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var failed = new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selection?.PrinterName ?? requestedSelection.PrinterName,
                Message: ex.Message);
            return new PortablePrintExecutionResult(
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Failure(new PortablePrintFailure(failureStage, ex.Message), ex, failed),
                discovery,
                selection,
                handoff);
        }
    }

    private static PortablePrintExecutionResult? ValidateSelection(
        PrintSelection selection,
        PortablePrintValidationIssue issue,
        PrinterDiscoveryResult? discovery = null)
    {
        try
        {
            selection.Validate();
            return null;
        }
        catch (ArgumentException ex)
        {
            var failed = new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: ex.Message);
            return new PortablePrintExecutionResult(
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .ValidationFailure(
                        issue,
                        new PortablePrintFailure(PortablePrintFailureStage.Selection, ex.Message),
                        ex,
                        failed),
                discovery,
                selection);
        }
    }

    private static PortablePrintExecutionResult FromDiscovery(
        PrinterDiscoveryResult discovery,
        PrintSelection requestedSelection)
    {
        var status = discovery.Status == PrinterDiscoveryStatus.Available
            ? PrintSubmissionStatus.NoPrinters
            : discovery.Status switch
            {
                PrinterDiscoveryStatus.Cancelled => PrintSubmissionStatus.Cancelled,
                PrinterDiscoveryStatus.NoPrinters => PrintSubmissionStatus.NoPrinters,
                PrinterDiscoveryStatus.Unavailable => PrintSubmissionStatus.Unavailable,
                _ => PrintSubmissionStatus.Failed,
            };
        var submission = new PrintSubmissionResult(
            status,
            requestedSelection.PrinterName ?? discovery.DefaultPrinter,
            Message: discovery.Message);

        var operation = status switch
        {
            PrintSubmissionStatus.Cancelled =>
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Cancel(submission),
            PrintSubmissionStatus.NoPrinters or PrintSubmissionStatus.Unavailable =>
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Unavailable(submission),
            _ => OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                .Failure(
                    new PortablePrintFailure(PortablePrintFailureStage.Discovery, discovery.Message),
                    new InvalidOperationException(discovery.Message ?? "Printer discovery failed."),
                    submission),
        };
        return new PortablePrintExecutionResult(operation, discovery);
    }

    private static PortablePrintExecutionResult FromSubmission(
        PrintSubmissionResult submission,
        PrinterDiscoveryResult discovery,
        PrintSelection selection,
        PortablePrintSelectionHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var operation = submission.Status switch
        {
            PrintSubmissionStatus.Submitted =>
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Completed(submission),
            PrintSubmissionStatus.Cancelled =>
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Cancel(submission),
            PrintSubmissionStatus.NoPrinters or PrintSubmissionStatus.Unavailable =>
                OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                    .Unavailable(submission),
            _ => OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                .Failure(
                    new PortablePrintFailure(PortablePrintFailureStage.Submission, submission.Message),
                    new InvalidOperationException(submission.Message ?? "Print submission failed."),
                    submission),
        };
        return new PortablePrintExecutionResult(operation, discovery, selection, handoff);
    }

    private static PortablePrintExecutionResult Canceled(
        PrinterDiscoveryResult discovery,
        string? printerName)
    {
        var submission = new PrintSubmissionResult(PrintSubmissionStatus.Cancelled, printerName);
        return new PortablePrintExecutionResult(
            OperationOutcome<PrintSubmissionResult, PortablePrintValidationIssue, PortablePrintFailure>
                .Cancel(submission),
            discovery);
    }
}
