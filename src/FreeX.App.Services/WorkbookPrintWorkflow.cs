using Free.Shared.AppServices.Printing;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookPrintExecutionOutcome
{
    Succeeded,
    Canceled,
    NotReady,
    Failed
}

public sealed record WorkbookPrintFallbackResult(
    WorkbookPrintExecutionOutcome Outcome,
    string StatusText,
    string? Path = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == WorkbookPrintExecutionOutcome.Succeeded;

    public static WorkbookPrintFallbackResult Success(string statusText, string path) =>
        new(WorkbookPrintExecutionOutcome.Succeeded, statusText, path);

    public static WorkbookPrintFallbackResult Canceled(string statusText = "Print canceled.") =>
        new(WorkbookPrintExecutionOutcome.Canceled, statusText);

    public static WorkbookPrintFallbackResult Failure(string statusText, Exception? exception = null) =>
        new(WorkbookPrintExecutionOutcome.Failed, statusText, Exception: exception);
}

public sealed record WorkbookPrintRenderResult(
    byte[] DocumentBytes,
    IReadOnlyList<string> ImageDiagnostics);

public sealed record WorkbookPrintWorkflowPlan(
    PrintExportHostReadinessPlan Readiness,
    PortablePdfExportPlan? PortablePdfPlan)
{
    public bool IsReady => Readiness.NativePrintPlan.JobPlan.IsReady;
}

public sealed record WorkbookPrintExecutionResult(
    WorkbookPrintExecutionOutcome Outcome,
    WorkbookPrintWorkflowPlan Plan,
    string StatusText,
    PrintSubmissionResult? Submission = null,
    WorkbookPrintFallbackResult? Fallback = null,
    WorkbookPrintRenderResult? RenderedDocument = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == WorkbookPrintExecutionOutcome.Succeeded;
}

/// <summary>
/// Plans and routes portable print execution. Native dialogs, PDF rendering, printer APIs, and fallback
/// pickers remain host delegates; this type owns readiness, render-before-route ordering, and outcomes.
/// </summary>
public static class WorkbookPrintWorkflow
{
    public static WorkbookPrintWorkflowPlan CreatePlan(
        Workbook workbook,
        bool hasSelection,
        PrintJobRequest request,
        PrintExportHostCapabilities host)
    {
        var readiness = PrintExportHostReadinessPlanner.Create(workbook, hasSelection, request, host);
        var portablePlan = readiness.NativePrintPlan.JobPlan.IsReady &&
            readiness.NativePrintPlan.RouteKind != PrintExportNativePrintRouteKind.NativePrintDialog
                ? PortablePdfExportPlanner.CreatePlan(readiness.NativePrintPlan.JobPlan.ExportPrintPlan)
                : null;
        return new WorkbookPrintWorkflowPlan(readiness, portablePlan);
    }

    public static async Task<WorkbookPrintExecutionResult> ExecutePortableAsync(
        WorkbookPrintWorkflowPlan plan,
        string? printerId,
        string jobTitle,
        Func<PortablePdfExportPlan, CancellationToken, Task<WorkbookPrintRenderResult>> renderPdfAsync,
        Func<string, PrintSelection, CancellationToken, Task<PrintSubmissionResult>> submitAsync,
        Func<byte[], CancellationToken, Task<WorkbookPrintFallbackResult>> saveFallbackAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(renderPdfAsync);
        ArgumentNullException.ThrowIfNull(submitAsync);
        ArgumentNullException.ThrowIfNull(saveFallbackAsync);

        if (!plan.IsReady)
        {
            return new WorkbookPrintExecutionResult(
                WorkbookPrintExecutionOutcome.NotReady,
                plan,
                plan.Readiness.NativePrintPlan.StatusText);
        }

        if (plan.PortablePdfPlan is not { IsReady: true } portablePlan)
        {
            return new WorkbookPrintExecutionResult(
                WorkbookPrintExecutionOutcome.NotReady,
                plan,
                plan.PortablePdfPlan?.StatusText ?? "Portable print output is not available.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderedDocument = await renderPdfAsync(portablePlan, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (plan.Readiness.NativePrintPlan.RouteKind == PrintExportNativePrintRouteKind.PlatformPrinter)
            {
                var job = plan.Readiness.NativePrintPlan.JobPlan;
                using var temporaryFile = TemporaryFileLease.Create("freex-print-", ".pdf");
                await temporaryFile.WriteAllBytesAsync(renderedDocument.DocumentBytes, cancellationToken)
                    .ConfigureAwait(true);
                var selection = new PrintSelection(
                    PrinterName: string.IsNullOrWhiteSpace(printerId) ? null : printerId.Trim(),
                    Copies: job.Copies,
                    PageRange: job.FirstPage == job.LastPage
                        ? PrintPageRange.Single(job.FirstPage)
                        : PrintPageRange.Between(job.FirstPage, job.LastPage),
                    Collate: job.Collate,
                    JobTitle: jobTitle);
                var submission = await submitAsync(temporaryFile.Path, selection, cancellationToken)
                    .ConfigureAwait(true);
                var statusText = FormatSubmissionStatus(submission);
                return new WorkbookPrintExecutionResult(
                    submission.Succeeded
                        ? WorkbookPrintExecutionOutcome.Succeeded
                        : submission.Status == PrintSubmissionStatus.Cancelled
                            ? WorkbookPrintExecutionOutcome.Canceled
                            : WorkbookPrintExecutionOutcome.Failed,
                    plan,
                    statusText,
                    Submission: submission,
                    RenderedDocument: renderedDocument);
            }

            var fallback = await saveFallbackAsync(renderedDocument.DocumentBytes, cancellationToken).ConfigureAwait(true);
            return new WorkbookPrintExecutionResult(
                fallback.Outcome,
                plan,
                fallback.StatusText,
                Fallback: fallback,
                RenderedDocument: renderedDocument,
                Exception: fallback.Exception);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new WorkbookPrintExecutionResult(
                WorkbookPrintExecutionOutcome.Canceled,
                plan,
                "Print canceled.",
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new WorkbookPrintExecutionResult(
                WorkbookPrintExecutionOutcome.Failed,
                plan,
                $"Print failed: {ex.Message}",
                Exception: ex);
        }
    }

    private static string FormatSubmissionStatus(PrintSubmissionResult submission)
    {
        if (submission.Succeeded)
        {
            var target = string.IsNullOrWhiteSpace(submission.PrinterName)
                ? "the default printer"
                : submission.PrinterName;
            return $"Sent to {target}.";
        }

        return string.IsNullOrWhiteSpace(submission.Message)
            ? "Printing failed."
            : submission.Message;
    }
}
