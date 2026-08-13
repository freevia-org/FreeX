using Free.Shared.AppServices;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWDocumentFilePorts(
    Func<TextDocument> GetDocument,
    Func<TextDocument, CancellationToken, ValueTask> LoadDocumentAsync,
    Func<CancellationToken, ValueTask>? PrepareDocumentAsync = null,
    Func<DocumentSaveCompatibilityPlan, CancellationToken, ValueTask<bool>>? ConfirmSaveCompatibilityAsync = null,
    Func<CancellationToken, ValueTask>? UpdateFieldsAsync = null,
    Action<string?>? SetCurrentFileName = null);

public sealed record DocumentOpenWorkflowResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentOpenResult? OpenResult = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;
}

public sealed record DocumentSaveWorkflowResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentSaveTarget? Target = null,
    DocumentSaveCompatibilityPlan? CompatibilityPlan = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;

    public bool RequiresSaveAs => Outcome == DocumentFileExecutionOutcome.SaveAsRequired;
}

public sealed record DocumentImportWorkflowResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentImportResult? ImportResult = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;
}

public sealed record DocumentSnapshotWorkflowResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentSnapshotOpenResult? SnapshotResult = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;
}

/// <summary>
/// Owns FreeW's renderer-neutral document file execution and lifecycle publication. Native hosts keep
/// file pickers, compatibility dialogs, status/error presentation, controls, focus, and editor realization.
/// </summary>
public sealed class FreeWDocumentFileWorkflow
{
    private readonly FileCommandWorkflow _lifecycle;
    private readonly DocumentPersistenceWorkflow _persistence;
    private readonly DocumentFileExecutionCoordinator _execution;
    private readonly FreeWDocumentFilePorts _ports;

    public FreeWDocumentFileWorkflow(
        FileCommandWorkflow lifecycle,
        DocumentPersistenceWorkflow persistence,
        FreeWDocumentFilePorts ports)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        ArgumentNullException.ThrowIfNull(ports.GetDocument);
        ArgumentNullException.ThrowIfNull(ports.LoadDocumentAsync);
        _execution = new DocumentFileExecutionCoordinator(persistence);
    }

    public DocumentPersistenceWorkflow Persistence => _persistence;

    public async Task<DocumentOpenWorkflowResult> OpenPathAsync(
        string path,
        bool suppressRecentFiles = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _execution.OpenAsync(new DocumentOpenExecutionRequest(
            path,
            suppressRecentFiles,
            _ports.LoadDocumentAsync,
            CompleteOpenAsync: (openResult, suppressRecent, _) =>
            {
                PublishOpenedDocument(openResult, suppressRecent);
                return ValueTask.CompletedTask;
            },
            PrepareFieldContextAsync: (savedPath, _) =>
            {
                _ports.SetCurrentFileName?.Invoke(FileName(savedPath));
                return ValueTask.CompletedTask;
            },
            UpdateFieldsAsync: _ports.UpdateFieldsAsync), cancellationToken);

        return new(result.Outcome, result.OpenResult, result.Exception);
    }

    public async Task<DocumentOpenWorkflowResult> ApplyOpenResultAsync(
        DocumentOpenResult result,
        bool suppressRecentFiles = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _ports.LoadDocumentAsync(result.Document, cancellationToken);
            _ports.SetCurrentFileName?.Invoke(FileName(result.SavedPath));

            if (result.Document.UpdateFieldsOnOpen && _ports.UpdateFieldsAsync is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _ports.UpdateFieldsAsync(cancellationToken);
            }

            PublishOpenedDocument(result, suppressRecentFiles);
            return new(DocumentFileExecutionOutcome.Succeeded, result);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new(DocumentFileExecutionOutcome.Canceled, Exception: ex);
        }
        catch (Exception ex)
        {
            return new(DocumentFileExecutionOutcome.Failed, Exception: ex);
        }
    }

    public async Task<DocumentImportWorkflowResult> ImportPdfTextPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _persistence.ImportPdfText(path);
            await _ports.LoadDocumentAsync(result.Document, cancellationToken);
            _ports.SetCurrentFileName?.Invoke(null);
            _lifecycle.MarkDirtyWithPath(null);
            return new(DocumentFileExecutionOutcome.Succeeded, result);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new(DocumentFileExecutionOutcome.Canceled, Exception: ex);
        }
        catch (Exception ex)
        {
            return new(DocumentFileExecutionOutcome.Failed, Exception: ex);
        }
    }

    public async Task<DocumentSnapshotWorkflowResult> OpenSnapshotAsync(
        string snapshotPath,
        string? originalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _persistence.OpenSnapshot(snapshotPath, originalPath);
            await _ports.LoadDocumentAsync(result.Document, cancellationToken);
            _ports.SetCurrentFileName?.Invoke(FileName(result.TargetPath));
            _lifecycle.MarkDirtyWithPath(result.TargetPath);
            return new(DocumentFileExecutionOutcome.Succeeded, result);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new(DocumentFileExecutionOutcome.Canceled, Exception: ex);
        }
        catch (Exception ex)
        {
            return new(DocumentFileExecutionOutcome.Failed, Exception: ex);
        }
    }

    public Task<DocumentSaveWorkflowResult> SaveCurrentPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _persistence.TryResolveCurrentSaveTarget(path, out var target)
            ? SaveTargetAsync(target, DocumentSaveExecutionKind.Save, cancellationToken)
            : Task.FromResult(new DocumentSaveWorkflowResult(DocumentFileExecutionOutcome.SaveAsRequired));
    }

    public Task<DocumentSaveWorkflowResult> SavePathAsync(
        string path,
        int filterIndex = 0,
        DocumentSaveExecutionKind kind = DocumentSaveExecutionKind.Save,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _persistence.TryResolveSaveTarget(path, filterIndex, out var target)
            ? SaveTargetAsync(target, kind, cancellationToken)
            : Task.FromResult(new DocumentSaveWorkflowResult(DocumentFileExecutionOutcome.UnsupportedFormat));
    }

    public async Task<DocumentSaveWorkflowResult> SaveTargetAsync(
        DocumentSaveTarget target,
        DocumentSaveExecutionKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = await _execution.SaveAsync(new DocumentSaveExecutionRequest(
            _ports.GetDocument(),
            target,
            kind,
            PrepareDocumentAsync: _ports.PrepareDocumentAsync,
            ConfirmCompatibilityAsync: _ports.ConfirmSaveCompatibilityAsync,
            CompleteSaveAsync: (savedTarget, _) =>
            {
                PublishSavedDocument(savedTarget.Path);
                return ValueTask.CompletedTask;
            }), cancellationToken);

        return new(result.Outcome, target, result.CompatibilityPlan, result.Exception);
    }

    private void PublishOpenedDocument(DocumentOpenResult result, bool suppressRecentFiles)
    {
        if (result.SavedPath is null)
        {
            _lifecycle.MarkSavedWithoutPath();
            return;
        }

        _lifecycle.MarkSavedWithPath(
            result.SavedPath,
            suppressRecentFiles);
    }

    private void PublishSavedDocument(string path) =>
        _lifecycle.MarkSavedWithPath(
            path,
            suppressRecentFiles: false,
            () => _ports.SetCurrentFileName?.Invoke(FileName(path)));

    private static string? FileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
}
