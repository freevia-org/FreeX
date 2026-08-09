using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookFileOperationOutcome
{
    Succeeded,
    Canceled,
    Rejected,
    ExternalWriteConflict,
    Failed
}

public static class WorkbookFileWorkflowMessages
{
    public const string UnsupportedSaveFormat = "Unsupported save format.";
    public const string UnsupportedXlsxSave = "Save As FreeX Workbook to avoid dropping unsupported XLSX features.";
    public const string OpenCanceled = "Open canceled.";
    public const string SaveCanceled = "Save canceled.";

    public static string OpenFailed(Exception exception) => $"Open failed: {exception.Message}";

    public static string SaveFailed(Exception exception) => $"Save failed: {exception.Message}";

    public static string RecentWorkbookMissing(string path) => $"Recent workbook no longer exists: {path}";
}

public sealed record WorkbookOpenWorkflowContext(
    WorkbookOpenTarget Target,
    WorkbookOpenResult Result,
    WorkbookOpenCompletionPlan CompletionPlan);

public sealed record WorkbookOpenWorkflowRequest(
    WorkbookOpenTarget Target,
    Func<WorkbookOpenWorkflowContext, CancellationToken, Task> ApplyOpenAsync,
    bool SuppressRecentFiles = false,
    string? CompletionDisplayName = null,
    IProgress<WorkbookOpenProgressUpdate>? Progress = null,
    Func<WorkbookOpenTarget, CancellationToken, Task<WorkbookFileWorkflowPreparation>>? PrepareAsync = null,
    CancellationToken CancellationToken = default);

public sealed record WorkbookOpenWorkflowResult(
    WorkbookFileOperationOutcome Outcome,
    WorkbookOpenWorkflowContext? Context,
    string Message,
    Exception? Exception = null,
    RecentFileRegistrationResult? RecentFileRegistration = null)
{
    public bool Succeeded => Outcome == WorkbookFileOperationOutcome.Succeeded;
}

public sealed record WorkbookSaveWorkflowRequest(
    bool IsDirty,
    string? CurrentFilePath,
    FileSaveTarget Target,
    DateTime? ExpectedLastWriteTimeUtc,
    Func<Workbook> GetCurrentWorkbook,
    Func<int> GetDirtyGeneration,
    Func<string, bool> ConfirmExternallyModifiedOverwrite,
    Action ProjectViewStateForSave,
    Func<WorkbookSaveInvocation, Task<IReadOnlyList<string>>> SaveAsync,
    Action<SaveCompletionPlan> ApplyCompletion,
    CancellationToken CancellationToken = default,
    string? CompletionDisplayName = null,
    Func<FileSaveTarget, CancellationToken, Task<bool>>? ConfirmTargetAsync = null,
    Func<CancellationToken, Task<WorkbookSaveExecutionPreparation>>? PrepareAsync = null,
    Action? ExecutionStarting = null,
    Action? ExecutionCompleted = null);

public sealed record WorkbookSaveWorkflowResult(
    WorkbookFileOperationOutcome Outcome,
    FileSaveTarget Target,
    string Message,
    WorkbookSaveExecutionResult? ExecutionResult = null,
    Exception? Exception = null,
    bool SkippedCleanWrite = false,
    RecentFileRegistrationResult? RecentFileRegistration = null)
{
    public bool Succeeded => Outcome == WorkbookFileOperationOutcome.Succeeded;

    public WorkbookSaveExecutionResult RequireExecutionResult()
    {
        if (!Succeeded)
            throw new InvalidOperationException($"Save workflow outcome '{Outcome}' has no successful execution result.");

        return ExecutionResult
            ?? throw new InvalidOperationException("A successful save did not produce an execution result.");
    }

    public SaveCompletionPlan RequireCompletionPlan() =>
        RequireExecutionResult().CompletionPlan
        ?? throw new InvalidOperationException("A successful save did not produce a completion plan.");
}

public sealed class WorkbookFileWorkflowPreparation : IDisposable
{
    private readonly IDisposable? _lifetime;
    private int _disposed;

    public WorkbookFileWorkflowPreparation(IDisposable? lifetime = null) => _lifetime = lifetime;

    public static WorkbookFileWorkflowPreparation None() => new();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _lifetime?.Dispose();
    }
}

/// <summary>
/// Owns FreeX's renderer-neutral workbook file choreography. Hosts retain native pickers, prompts,
/// storage scopes, progress controls, workbook-view realization, and platform filesystem APIs.
/// </summary>
public sealed class WorkbookFileWorkflow
{
    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly WorkbookOpenService _openService;
    private readonly Func<RecentFileRegistrationRequest, RecentFileRegistrationResult> _registerRecentFile;
    private readonly Action? _recentFilesChanged;
    private readonly Func<FileSaveTarget, string?>? _validateSaveTarget;

    public WorkbookFileWorkflow(
        IEnumerable<IFileAdapter> adapters,
        WorkbookOpenService? openService = null,
        Func<RecentFileRegistrationRequest, RecentFileRegistrationResult>? registerRecentFile = null,
        Action? recentFilesChanged = null,
        Func<FileSaveTarget, string?>? validateSaveTarget = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _adapters = adapters.ToList();
        _openService = openService ?? new WorkbookOpenService();
        _registerRecentFile = registerRecentFile ?? SkipRecentRegistration;
        _recentFilesChanged = recentFilesChanged;
        _validateSaveTarget = validateSaveTarget;
    }

    public IReadOnlyList<IFileAdapter> Adapters => _adapters;

    public IReadOnlyList<FileFormatDescriptor> OpenFormats =>
        _adapters.SelectMany(adapter => adapter.Formats).Where(format => format.CanOpen).ToList();

    public IReadOnlyList<FileFormatDescriptor> SaveFormats =>
        _adapters.SelectMany(adapter => adapter.Formats).Where(format => format.CanSave).ToList();

    public bool TryResolveOpenTarget(
        string path,
        out WorkbookOpenTarget? target,
        out string message) =>
        TryResolveOpenTarget(path, fileAccessIdentity: null, out target, out message);

    public bool TryResolveOpenTarget(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity,
        out WorkbookOpenTarget? target,
        out string message) =>
        WorkbookOpenTargetPlanner.TryCreateOpenTarget(
            _adapters,
            path,
            fileAccessIdentity,
            out target,
            out message);

    public bool TryResolveSaveTarget(
        string path,
        out FileSaveTarget? target,
        out string message,
        int filterIndex = 0)
    {
        if (!WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(_adapters, path, filterIndex, out target) ||
            target is null)
        {
            message = WorkbookFileWorkflowMessages.UnsupportedSaveFormat;
            return false;
        }

        message = _validateSaveTarget?.Invoke(target) ?? "";
        if (!string.IsNullOrWhiteSpace(message))
        {
            target = null;
            return false;
        }

        return true;
    }

    public FileSaveTarget? ResolveExistingSaveTarget(string? currentFilePath) =>
        !string.IsNullOrWhiteSpace(currentFilePath) &&
        TryResolveSaveTarget(currentFilePath, out var target, out _)
            ? target
            : null;

    public bool ShouldSkipSaveTargetWrite(
        bool isDirty,
        string? currentFilePath,
        FileSaveTarget target) =>
        WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(isDirty, currentFilePath, target)
            == WorkbookSaveTargetIntent.SkipCleanCurrentPath;

    public WorkbookSavePathNormalizationPlan PlanSavePathNormalization(
        string selectedPath,
        string defaultExtension,
        Func<string, bool> fileExists) =>
        WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(
            selectedPath,
            defaultExtension,
            fileExists);

    public async Task<WorkbookOpenWorkflowResult> OpenAsync(WorkbookOpenWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.ApplyOpenAsync);

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            using var preparation = request.PrepareAsync is null
                ? WorkbookFileWorkflowPreparation.None()
                : await request.PrepareAsync(request.Target, request.CancellationToken).ConfigureAwait(true)
                    ?? throw new InvalidOperationException("Open preparation returned no result.");

            var result = await _openService.LoadAsync(
                request.Target.Path,
                request.Target.Adapter,
                request.Target.Extension,
                request.Target.Format,
                request.Progress,
                request.CancellationToken).ConfigureAwait(true);
            request.CancellationToken.ThrowIfCancellationRequested();

            var completionPlan = WorkbookFileCompletionPlanner.PlanOpen(
                request.Target,
                result,
                request.SuppressRecentFiles,
                request.CompletionDisplayName);
            var context = new WorkbookOpenWorkflowContext(request.Target, result, completionPlan);
            await request.ApplyOpenAsync(context, request.CancellationToken).ConfigureAwait(true);
            request.CancellationToken.ThrowIfCancellationRequested();

            var registration = RegisterRecentFile(completionPlan.RecentFileRegistration);
            return new WorkbookOpenWorkflowResult(
                WorkbookFileOperationOutcome.Succeeded,
                context,
                completionPlan.Status,
                RecentFileRegistration: registration);
        }
        catch (OperationCanceledException ex) when (request.CancellationToken.IsCancellationRequested)
        {
            return new WorkbookOpenWorkflowResult(
                WorkbookFileOperationOutcome.Canceled,
                Context: null,
                WorkbookFileWorkflowMessages.OpenCanceled,
                ex);
        }
        catch (Exception ex)
        {
            return new WorkbookOpenWorkflowResult(
                WorkbookFileOperationOutcome.Failed,
                Context: null,
                WorkbookFileWorkflowMessages.OpenFailed(ex),
                ex);
        }
    }

    public async Task<WorkbookSaveWorkflowResult> SaveTargetAsync(WorkbookSaveWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);

        try
        {
            if (WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(
                    request.IsDirty,
                    request.CurrentFilePath,
                    request.Target) == WorkbookSaveTargetIntent.SkipCleanCurrentPath)
            {
                return new WorkbookSaveWorkflowResult(
                    WorkbookFileOperationOutcome.Succeeded,
                    request.Target,
                    Message: "",
                    SkippedCleanWrite: true);
            }

            var targetValidationMessage = _validateSaveTarget?.Invoke(request.Target);
            if (!string.IsNullOrWhiteSpace(targetValidationMessage))
            {
                return new WorkbookSaveWorkflowResult(
                    WorkbookFileOperationOutcome.Rejected,
                    request.Target,
                    targetValidationMessage);
            }

            if (request.ConfirmTargetAsync is not null &&
                !await request.ConfirmTargetAsync(request.Target, request.CancellationToken).ConfigureAwait(true))
            {
                return new WorkbookSaveWorkflowResult(
                    WorkbookFileOperationOutcome.Rejected,
                    request.Target,
                    WorkbookFileWorkflowMessages.SaveCanceled);
            }

            var executionStart = WorkbookSaveExecutionCoordinator.Begin(new WorkbookSaveExecutionStartRequest(
                request.CurrentFilePath,
                request.Target,
                request.ExpectedLastWriteTimeUtc,
                request.GetCurrentWorkbook,
                request.GetDirtyGeneration,
                request.ConfirmExternallyModifiedOverwrite,
                request.CompletionDisplayName));
            if (!executionStart.CanExecute)
            {
                return new WorkbookSaveWorkflowResult(
                    WorkbookFileOperationOutcome.Rejected,
                    request.Target,
                    WorkbookFileWorkflowMessages.SaveCanceled);
            }

            var executionStarted = false;
            try
            {
                request.ExecutionStarting?.Invoke();
                executionStarted = true;

                var executionResult = await executionStart.Execution!.ExecuteAsync(new WorkbookSaveExecutionRequest(
                    request.CancellationToken,
                    request.ProjectViewStateForSave,
                    request.SaveAsync,
                    request.PrepareAsync)).ConfigureAwait(true);

                if (!executionResult.Succeeded)
                    return FromSaveExecutionFailure(request.Target, executionResult);

                var completionPlan = executionResult.CompletionPlan
                    ?? throw new InvalidOperationException("A successful save did not produce a completion plan.");
                request.ApplyCompletion(completionPlan);

                RecentFileRegistrationResult? registration = null;
                if (completionPlan.ApplyFileContext && completionPlan.FileContext is { } fileContext)
                    registration = RegisterRecentFile(fileContext.RecentFileRegistration);

                return new WorkbookSaveWorkflowResult(
                    WorkbookFileOperationOutcome.Succeeded,
                    request.Target,
                    Message: "",
                    executionResult,
                    RecentFileRegistration: registration);
            }
            finally
            {
                if (executionStarted)
                    request.ExecutionCompleted?.Invoke();
            }
        }
        catch (OperationCanceledException ex) when (request.CancellationToken.IsCancellationRequested)
        {
            return new WorkbookSaveWorkflowResult(
                WorkbookFileOperationOutcome.Canceled,
                request.Target,
                WorkbookFileWorkflowMessages.SaveCanceled,
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new WorkbookSaveWorkflowResult(
                WorkbookFileOperationOutcome.Failed,
                request.Target,
                WorkbookFileWorkflowMessages.SaveFailed(ex),
                Exception: ex);
        }
    }

    public Task<bool> SaveResolvedAsync(
        bool isDirty,
        string? currentFilePath,
        Func<FileSaveTarget?> resolveCurrentTarget,
        Func<FileSaveTarget, Task<bool>> saveTargetAsync,
        Func<Task<bool>> saveAsAsync) =>
        WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty,
            currentFilePath,
            resolveCurrentTarget,
            saveTargetAsync,
            saveAsAsync);

    public Task<SaveChangesConfirmation> ConfirmBeforeDestructiveActionAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync) =>
        WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty,
            promptSaveChangesAsync,
            saveCurrentAsync);

    public Task<bool> CanProceedAfterDirtyGateWithCleanSaveAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync,
        Func<bool> isDirtyNow) =>
        WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(
            isDirty,
            promptSaveChangesAsync,
            saveCurrentAsync,
            isDirtyNow);

    public RecentFileRegistrationResult RegisterRecentFile(RecentFileRegistrationRequest request)
    {
        var result = _registerRecentFile(request);
        if (result.Registered)
            _recentFilesChanged?.Invoke();
        return result;
    }

    private static WorkbookSaveWorkflowResult FromSaveExecutionFailure(
        FileSaveTarget target,
        WorkbookSaveExecutionResult result) =>
        result.Outcome switch
        {
            WorkbookSaveExecutionOutcome.Canceled => new WorkbookSaveWorkflowResult(
                WorkbookFileOperationOutcome.Canceled,
                target,
                WorkbookFileWorkflowMessages.SaveCanceled,
                result,
                result.Exception),
            WorkbookSaveExecutionOutcome.ExternalWriteConflict => new WorkbookSaveWorkflowResult(
                WorkbookFileOperationOutcome.ExternalWriteConflict,
                target,
                result.Exception?.Message ?? "The workbook changed outside FreeX.",
                result,
                result.Exception),
            _ => new WorkbookSaveWorkflowResult(
                WorkbookFileOperationOutcome.Failed,
                target,
                result.Exception is null
                    ? "Save failed: Unknown error."
                    : WorkbookFileWorkflowMessages.SaveFailed(result.Exception),
                result,
                result.Exception)
        };

    private static RecentFileRegistrationResult SkipRecentRegistration(
        RecentFileRegistrationRequest request) =>
        new(
            FileLifecyclePlanner.PlanRecentRegistration(request.FilePath, request.SuppressRecentFiles),
            Registered: false);
}

public static class WorkbookSaveTargetPolicy
{
    public static string? BlockUnsupportedXlsxFeatures(
        FileSaveTarget target,
        XlsxFeatureReport? featureReport) =>
        string.Equals(Path.GetExtension(target.Path), ".xlsx", StringComparison.OrdinalIgnoreCase) &&
        featureReport?.HasUnsupportedFeatures == true
            ? WorkbookFileWorkflowMessages.UnsupportedXlsxSave
            : null;
}
