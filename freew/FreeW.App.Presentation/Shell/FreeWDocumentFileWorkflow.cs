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
    Action<string?>? SetCurrentFileName = null,
    // r137-remediation2: asks the user whether to overwrite a save target that was modified by
    // another program since it was opened/last saved. Null (the default) means the host wired
    // nothing, which SaveTargetAsync treats as "always decline" -- never silently overwrite.
    Func<string, CancellationToken, ValueTask<bool>>? ConfirmExternallyModifiedOverwriteAsync = null);

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

    // r137-remediation2: the write time observed on the CURRENT document's source file, captured at
    // open (DocumentOpenResult.SourceLastWriteTimeUtc) and rebased after each successful save to the
    // path it just wrote. SaveTargetAsync only forwards this as the external-modification guard's
    // expected time when the save target is the SAME path this field tracks (PlatformPathIdentity-
    // Comparer) -- a Save-As/Save-Copy to a different path establishes a new identity with nothing to
    // compare, so the guard is naturally skipped there without any extra branching. Never explicitly
    // reset on File>New: New clears _lifecycle.CurrentPath to null, and the path-identity gate above
    // is already false against a null CurrentPath, so a stale value here is inert.
    private DateTime? _currentFileSourceLastWriteTimeUtc;

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
            // Recompute the guard baseline from the ORIGINAL path's current on-disk write time, not
            // the snapshot's -- comparing against the snapshot would fire a spurious "modified by
            // someone else" warning on every ordinary recover-then-save (matches FreeX's
            // SetCurrentFilePathForRecovery rationale). No original file (moved/deleted since the
            // crash) means nothing to compare against, so the guard stays off until the next save.
            _currentFileSourceLastWriteTimeUtc =
                result.TargetPath is not null && File.Exists(result.TargetPath)
                    ? File.GetLastWriteTimeUtc(result.TargetPath)
                    : null;
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

        // Only guard a save that overwrites the SAME path this document's identity already tracks
        // (_lifecycle.CurrentPath). Save-As/Save-Copy to a different path -- or the first save of a
        // never-saved document, where CurrentPath is null -- has nothing to compare against, so the
        // comparer's null-safe Equals naturally turns the guard off rather than needing a separate
        // Kind check.
        var expectedLastWriteTimeUtc = PlatformPathIdentityComparer.Current.Equals(
            _lifecycle.CurrentPath,
            target.Path)
                ? _currentFileSourceLastWriteTimeUtc
                : null;

        var result = await _execution.SaveAsync(new DocumentSaveExecutionRequest(
            _ports.GetDocument(),
            target,
            kind,
            PrepareDocumentAsync: _ports.PrepareDocumentAsync,
            ConfirmCompatibilityAsync: _ports.ConfirmSaveCompatibilityAsync,
            ExpectedLastWriteTimeUtc: expectedLastWriteTimeUtc,
            ConfirmExternallyModifiedOverwriteAsync: _ports.ConfirmExternallyModifiedOverwriteAsync,
            CompleteSaveAsync: (savedTarget, _) =>
            {
                PublishSavedDocument(savedTarget.Path);
                return ValueTask.CompletedTask;
            }), cancellationToken);

        return new(result.Outcome, target, result.CompatibilityPlan, result.Exception);
    }

    private void PublishOpenedDocument(DocumentOpenResult result, bool suppressRecentFiles)
    {
        _currentFileSourceLastWriteTimeUtc = result.SourceLastWriteTimeUtc;

        if (result.SavedPath is null)
        {
            _lifecycle.MarkSavedWithoutPath();
            return;
        }

        _lifecycle.MarkSavedWithPath(
            result.SavedPath,
            suppressRecentFiles);
    }

    private void PublishSavedDocument(string path)
    {
        // Rebase the guard to the write this save just produced (kind == Save only -- Save Copy
        // never calls this callback, by design, since it doesn't change the document's identity).
        _currentFileSourceLastWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        _lifecycle.MarkSavedWithPath(
            path,
            suppressRecentFiles: false,
            () => _ports.SetCurrentFileName?.Invoke(FileName(path)));
    }

    private static string? FileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
}
