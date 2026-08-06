namespace FreeX.App.Services;

public enum WorkbookExportExecutionOutcome
{
    Succeeded,
    Canceled,
    ValidationFailed,
    Failed
}

public sealed record WorkbookExportExecutionResult(
    WorkbookExportExecutionOutcome Outcome,
    ExportRequest Request,
    string Message,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == WorkbookExportExecutionOutcome.Succeeded;
}

/// <summary>
/// Owns export option normalization, validation, cancellation, and failure capture. Renderers provide
/// the framework-specific document renderer and destination writer.
/// </summary>
public static class WorkbookExportWorkflow
{
    public static async Task<WorkbookExportExecutionResult> ExecuteAsync(
        ExportRequest request,
        Func<ExportRequest, CancellationToken, Task> exportAsync,
        ExportPlannerTextResolver? textResolver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exportAsync);

        var effectiveRequest = request with
        {
            Options = ExportPlanner.CreateEffectiveOptionsForFormat(request.Options, request.Format)
        };
        if (!ExportPlanner.TryValidatePublishOptions(
                effectiveRequest.Options,
                effectiveRequest.Format,
                out var validationMessage,
                textResolver))
        {
            return new WorkbookExportExecutionResult(
                WorkbookExportExecutionOutcome.ValidationFailed,
                effectiveRequest,
                validationMessage ?? "Export options are not supported.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await exportAsync(effectiveRequest, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkbookExportExecutionResult(
                WorkbookExportExecutionOutcome.Succeeded,
                effectiveRequest,
                Message: "");
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new WorkbookExportExecutionResult(
                WorkbookExportExecutionOutcome.Canceled,
                effectiveRequest,
                "Export canceled.",
                ex);
        }
        catch (Exception ex)
        {
            return new WorkbookExportExecutionResult(
                WorkbookExportExecutionOutcome.Failed,
                effectiveRequest,
                $"Export failed: {ex.Message}",
                ex);
        }
    }
}
