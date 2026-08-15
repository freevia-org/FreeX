using Free.Shared.IO;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum ScenarioMergeWorkflowOutcome
{
    Succeeded,
    Canceled,
    OpenFailed,
    ApplyFailed
}

public sealed record ScenarioMergeWorkflowResult(
    ScenarioMergeWorkflowOutcome Outcome,
    WorkbookOpenWorkflowResult? OpenResult = null)
{
    public bool Succeeded => Outcome == ScenarioMergeWorkflowOutcome.Succeeded;
}

/// <summary>
/// Native host ports for scenario merge. Renderers retain file-dialog ownership, command-error
/// presentation, and visual refresh while the workflow owns portable file and merge choreography.
/// </summary>
public sealed record ScenarioMergeWorkflowHost(
    Func<FileOpenDialogPlan, CancellationToken, ValueTask<string?>> PickSourcePathAsync,
    Func<IReadOnlyList<WorkbookScenario>, bool> ApplyMerge,
    Action ReportOpenFailure,
    Action ApplySucceeded);

/// <summary>
/// Loads scenarios from a selected workbook, remaps workbook-local sheet identifiers by sheet
/// name, and submits the resulting scenarios through the host's domain mutation port.
/// </summary>
public sealed class ScenarioMergeWorkflow
{
    private readonly WorkbookFileWorkflow _fileWorkflow;

    public ScenarioMergeWorkflow(IEnumerable<IFileAdapter> adapters, WorkbookOpenService? openService = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _fileWorkflow = new WorkbookFileWorkflow(adapters, openService);
    }

    public async Task<ScenarioMergeWorkflowResult> RunAsync(
        Workbook targetWorkbook,
        ScenarioMergeWorkflowHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetWorkbook);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(host.PickSourcePathAsync);
        ArgumentNullException.ThrowIfNull(host.ApplyMerge);
        ArgumentNullException.ThrowIfNull(host.ReportOpenFailure);
        ArgumentNullException.ThrowIfNull(host.ApplySucceeded);

        var pickerPlan = WorkbookFilePickerPlanner.BuildOpenDialogPlan(_fileWorkflow.Adapters);
        string? selectedPath;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            selectedPath = await host.PickSourcePathAsync(pickerPlan, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ScenarioMergeWorkflowResult(ScenarioMergeWorkflowOutcome.Canceled);
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
            return new ScenarioMergeWorkflowResult(ScenarioMergeWorkflowOutcome.Canceled);

        if (!_fileWorkflow.TryResolveOpenTarget(selectedPath, out var target, out _))
        {
            host.ReportOpenFailure();
            return new ScenarioMergeWorkflowResult(ScenarioMergeWorkflowOutcome.OpenFailed);
        }

        var applied = false;
        var openResult = await _fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (context, _) =>
            {
                var scenarios = ScenarioManagerPlanner.RemapScenariosBySheetName(
                    context.Result.Workbook,
                    targetWorkbook);
                applied = host.ApplyMerge(scenarios);
                return Task.CompletedTask;
            },
            SuppressRecentFiles: true,
            CancellationToken: cancellationToken)).ConfigureAwait(true);

        if (!openResult.Succeeded)
        {
            if (openResult.Outcome != WorkbookFileOperationOutcome.Canceled)
                host.ReportOpenFailure();

            return new ScenarioMergeWorkflowResult(
                openResult.Outcome == WorkbookFileOperationOutcome.Canceled
                    ? ScenarioMergeWorkflowOutcome.Canceled
                    : ScenarioMergeWorkflowOutcome.OpenFailed,
                openResult);
        }

        if (!applied)
            return new ScenarioMergeWorkflowResult(ScenarioMergeWorkflowOutcome.ApplyFailed, openResult);

        host.ApplySucceeded();
        return new ScenarioMergeWorkflowResult(ScenarioMergeWorkflowOutcome.Succeeded, openResult);
    }
}
