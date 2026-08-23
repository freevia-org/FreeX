using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookSaveExecutionStartOutcome
{
    Ready,
    ExternalWriteDeclined
}

public enum WorkbookSaveExecutionOutcome
{
    Succeeded,
    Canceled,
    ExternalWriteConflict,
    Failed
}

/// <summary>
/// Coordinates the portable portion of a workbook save. Renderers retain ownership of prompts,
/// progress, input gates, file-access scopes, recent files, diagnostics, and visual refresh.
/// </summary>
public static class WorkbookSaveExecutionCoordinator
{
    public static WorkbookSaveExecutionStartResult Begin(WorkbookSaveExecutionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.GetCurrentWorkbook);
        ArgumentNullException.ThrowIfNull(request.GetDirtyGeneration);
        ArgumentNullException.ThrowIfNull(request.ConfirmExternallyModifiedOverwrite);

        var fileExists = request.FileExists ?? File.Exists;
        var getLastWriteTimeUtc = request.GetLastWriteTimeUtc ?? File.GetLastWriteTimeUtc;
        var expectedLastWriteTimeUtc = ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(
            request.CurrentFilePath,
            request.Target.Path,
            request.ExpectedLastWriteTimeUtc);
        var conflictPreparation = ExternalFileWriteConflictPolicy.Prepare(
            request.Target.Path,
            expectedLastWriteTimeUtc,
            request.ConfirmExternallyModifiedOverwrite,
            fileExists,
            getLastWriteTimeUtc);
        if (!conflictPreparation.CanWrite)
        {
            return new WorkbookSaveExecutionStartResult(
                WorkbookSaveExecutionStartOutcome.ExternalWriteDeclined,
                Execution: null);
        }

        expectedLastWriteTimeUtc = conflictPreparation.ExpectedLastWriteTimeUtc;

        var execution = new WorkbookSaveExecution(
            request.Target,
            request.GetCurrentWorkbook(),
            request.GetDirtyGeneration(),
            request.GetCurrentWorkbook,
            request.GetDirtyGeneration,
            expectedLastWriteTimeUtc,
            request.CompletionDisplayName,
            fileExists,
            getLastWriteTimeUtc);

        return new WorkbookSaveExecutionStartResult(
            WorkbookSaveExecutionStartOutcome.Ready,
            execution);
    }
}

public sealed record WorkbookSaveExecutionStartRequest(
    string? CurrentFilePath,
    FileSaveTarget Target,
    DateTime? ExpectedLastWriteTimeUtc,
    Func<Workbook> GetCurrentWorkbook,
    Func<int> GetDirtyGeneration,
    Func<string, bool> ConfirmExternallyModifiedOverwrite,
    string? CompletionDisplayName = null,
    Func<string, bool>? FileExists = null,
    Func<string, DateTime>? GetLastWriteTimeUtc = null);

public sealed record WorkbookSaveExecutionStartResult(
    WorkbookSaveExecutionStartOutcome Outcome,
    WorkbookSaveExecution? Execution)
{
    public bool CanExecute => Outcome == WorkbookSaveExecutionStartOutcome.Ready && Execution is not null;
}

public sealed class WorkbookSaveExecution
{
    private readonly FileSaveTarget _target;
    private readonly Workbook _workbookAtStart;
    private readonly int _generationAtStart;
    private readonly Func<Workbook> _getCurrentWorkbook;
    private readonly Func<int> _getDirtyGeneration;
    private readonly DateTime? _expectedLastWriteTimeUtc;
    private readonly string? _completionDisplayName;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, DateTime> _getLastWriteTimeUtc;
    private int _executed;

    internal WorkbookSaveExecution(
        FileSaveTarget target,
        Workbook workbookAtStart,
        int generationAtStart,
        Func<Workbook> getCurrentWorkbook,
        Func<int> getDirtyGeneration,
        DateTime? expectedLastWriteTimeUtc,
        string? completionDisplayName,
        Func<string, bool> fileExists,
        Func<string, DateTime> getLastWriteTimeUtc)
    {
        _target = target;
        _workbookAtStart = workbookAtStart;
        _generationAtStart = generationAtStart;
        _getCurrentWorkbook = getCurrentWorkbook;
        _getDirtyGeneration = getDirtyGeneration;
        _expectedLastWriteTimeUtc = expectedLastWriteTimeUtc;
        _completionDisplayName = completionDisplayName;
        _fileExists = fileExists;
        _getLastWriteTimeUtc = getLastWriteTimeUtc;
    }

    public async Task<WorkbookSaveExecutionResult> ExecuteAsync(WorkbookSaveExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ProjectViewStateForSave);
        ArgumentNullException.ThrowIfNull(request.SaveAsync);

        if (Interlocked.Exchange(ref _executed, 1) != 0)
            throw new InvalidOperationException("A workbook save execution can only run once.");

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            using var preparation = request.PrepareAsync is null
                ? WorkbookSaveExecutionPreparation.None()
                : await request.PrepareAsync(request.CancellationToken).ConfigureAwait(true)
                    ?? throw new InvalidOperationException("Save preparation returned no result.");

            request.CancellationToken.ThrowIfCancellationRequested();
            request.ProjectViewStateForSave();
            var warnings = await request.SaveAsync(new WorkbookSaveInvocation(
                _target,
                _workbookAtStart,
                request.CancellationToken,
                _expectedLastWriteTimeUtc)).ConfigureAwait(true);
            request.CancellationToken.ThrowIfCancellationRequested();

            var completionPlan = SaveCompletionPlanner.Plan(
                _generationAtStart,
                _getDirtyGeneration(),
                sameWorkbook: ReferenceEquals(_getCurrentWorkbook(), _workbookAtStart),
                _target.Path,
                preparation.FileAccessIdentity,
                _completionDisplayName);
            DateTime? savedLastWriteTimeUtc = _fileExists(_target.Path)
                ? _getLastWriteTimeUtc(_target.Path)
                : null;

            return WorkbookSaveExecutionResult.Success(
                warnings ?? [],
                completionPlan,
                savedLastWriteTimeUtc);
        }
        catch (WorkbookExternallyModifiedException ex)
        {
            return WorkbookSaveExecutionResult.ExternalWriteConflict(ex);
        }
        catch (OperationCanceledException ex) when (request.CancellationToken.IsCancellationRequested)
        {
            return WorkbookSaveExecutionResult.Canceled(ex);
        }
        catch (Exception ex)
        {
            return WorkbookSaveExecutionResult.Failed(ex);
        }
    }
}

public sealed record WorkbookSaveExecutionRequest(
    CancellationToken CancellationToken,
    Action ProjectViewStateForSave,
    Func<WorkbookSaveInvocation, Task<IReadOnlyList<string>>> SaveAsync,
    Func<CancellationToken, Task<WorkbookSaveExecutionPreparation>>? PrepareAsync = null);

public sealed record WorkbookSaveInvocation(
    FileSaveTarget Target,
    Workbook Workbook,
    CancellationToken CancellationToken,
    DateTime? ExpectedLastWriteTimeUtc);

public sealed class WorkbookSaveExecutionPreparation : IDisposable
{
    private readonly IDisposable? _lifetime;
    private int _disposed;

    public WorkbookSaveExecutionPreparation(
        WorkbookFileAccessIdentity? fileAccessIdentity = null,
        IDisposable? lifetime = null)
    {
        FileAccessIdentity = fileAccessIdentity;
        _lifetime = lifetime;
    }

    public WorkbookFileAccessIdentity? FileAccessIdentity { get; }

    public static WorkbookSaveExecutionPreparation None() => new();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _lifetime?.Dispose();
    }
}

public sealed record WorkbookSaveExecutionResult(
    WorkbookSaveExecutionOutcome Outcome,
    IReadOnlyList<string> Warnings,
    SaveCompletionPlan? CompletionPlan,
    DateTime? SavedLastWriteTimeUtc,
    Exception? Exception)
{
    public bool Succeeded => Outcome == WorkbookSaveExecutionOutcome.Succeeded;

    internal static WorkbookSaveExecutionResult Success(
        IReadOnlyList<string> warnings,
        SaveCompletionPlan completionPlan,
        DateTime? savedLastWriteTimeUtc) =>
        new(
            WorkbookSaveExecutionOutcome.Succeeded,
            warnings,
            completionPlan,
            savedLastWriteTimeUtc,
            Exception: null);

    internal static WorkbookSaveExecutionResult Canceled(OperationCanceledException exception) =>
        new(
            WorkbookSaveExecutionOutcome.Canceled,
            [],
            CompletionPlan: null,
            SavedLastWriteTimeUtc: null,
            Exception: exception);

    internal static WorkbookSaveExecutionResult ExternalWriteConflict(WorkbookExternallyModifiedException exception) =>
        new(
            WorkbookSaveExecutionOutcome.ExternalWriteConflict,
            [],
            CompletionPlan: null,
            SavedLastWriteTimeUtc: null,
            Exception: exception);

    internal static WorkbookSaveExecutionResult Failed(Exception exception) =>
        new(
            WorkbookSaveExecutionOutcome.Failed,
            [],
            CompletionPlan: null,
            SavedLastWriteTimeUtc: null,
            Exception: exception);
}
