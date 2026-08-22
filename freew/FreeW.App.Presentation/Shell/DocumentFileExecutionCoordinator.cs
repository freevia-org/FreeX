using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public enum DocumentFileExecutionOutcome
{
    Succeeded,
    UnsupportedFormat,
    SaveAsRequired,
    CompatibilityDeclined,
    // r137-remediation2: the save target's on-disk write time no longer matches what the caller
    // observed when it opened/last saved the document (ExpectedLastWriteTimeUtc), and the user
    // either declined the overwrite prompt or a race let a second write land between the prompt
    // and the actual write. See DocumentSaveExecutionRequest.ExpectedLastWriteTimeUtc.
    ExternalWriteConflict,
    Canceled,
    Failed
}

public enum DocumentSaveExecutionKind
{
    Save,
    SaveCopy
}

/// <summary>
/// Coordinates portable open/save execution while renderers retain native pickers, prompts, editor
/// projection, focus, and status presentation. Callback order is part of the contract: open loads the
/// model, prepares field context, updates requested fields, then publishes saved metadata; save prepares
/// the live model, confirms compatibility, persists, then publishes saved metadata. Save Copy omits the
/// final metadata callback by design.
/// </summary>
public sealed class DocumentFileExecutionCoordinator
{
    private readonly DocumentPersistenceWorkflow _persistence;

    public DocumentFileExecutionCoordinator(DocumentPersistenceWorkflow persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public async Task<DocumentOpenExecutionResult> OpenAsync(
        DocumentOpenExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.LoadDocumentAsync);
        ArgumentNullException.ThrowIfNull(request.CompleteOpenAsync);

        if (!_persistence.CanOpenPath(request.Path))
            return DocumentOpenExecutionResult.UnsupportedFormat();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openResult = _persistence.Open(request.Path);

            cancellationToken.ThrowIfCancellationRequested();
            await request.LoadDocumentAsync(openResult.Document, cancellationToken);

            if (request.PrepareFieldContextAsync is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.PrepareFieldContextAsync(openResult.SavedPath, cancellationToken);
            }

            if (openResult.Document.UpdateFieldsOnOpen && request.UpdateFieldsAsync is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.UpdateFieldsAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await request.CompleteOpenAsync(openResult, request.SuppressRecentFiles, cancellationToken);
            return DocumentOpenExecutionResult.Success(openResult);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return DocumentOpenExecutionResult.Canceled(ex);
        }
        catch (Exception ex)
        {
            return DocumentOpenExecutionResult.Failed(ex);
        }
    }

    public async Task<DocumentSaveExecutionResult> SaveAsync(
        DocumentSaveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.Target);
        if (request.Kind == DocumentSaveExecutionKind.Save)
            ArgumentNullException.ThrowIfNull(request.CompleteSaveAsync);

        try
        {
            if (request.PrepareDocumentAsync is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.PrepareDocumentAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var compatibility = _persistence.BuildSaveCompatibilityPlan(request.Document, request.Target);
            if (compatibility.RequiresConfirmation)
            {
                if (request.ConfirmCompatibilityAsync is null
                    || !await request.ConfirmCompatibilityAsync(compatibility, cancellationToken))
                {
                    return DocumentSaveExecutionResult.CompatibilityDeclined(
                        compatibility,
                        request.Target.Path);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var conflictPreparation = await ExternalFileWriteConflictPolicy.PrepareAsync(
                request.Target.Path,
                request.ExpectedLastWriteTimeUtc,
                request.ConfirmExternallyModifiedOverwriteAsync,
                cancellationToken);
            if (!conflictPreparation.CanWrite)
            {
                return DocumentSaveExecutionResult.ExternalWriteConflict(request.Target.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _persistence.Save(
                    request.Document,
                    request.Target,
                    conflictPreparation.ExpectedLastWriteTimeUtc);
            }
            catch (DocumentExternallyModifiedException)
            {
                // A second writer landed between the check above and this write (race). Report the
                // same conflict outcome rather than re-prompting for a version the user never saw.
                return DocumentSaveExecutionResult.ExternalWriteConflict(request.Target.Path);
            }

            if (request.Kind == DocumentSaveExecutionKind.Save)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.CompleteSaveAsync!(request.Target, cancellationToken);
            }

            return DocumentSaveExecutionResult.Success(compatibility, request.Target.Path);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return DocumentSaveExecutionResult.Canceled(ex, request.Target.Path);
        }
        catch (Exception ex)
        {
            return DocumentSaveExecutionResult.Failed(ex, request.Target.Path);
        }
    }
}

public sealed record DocumentOpenExecutionRequest(
    string Path,
    bool SuppressRecentFiles,
    Func<TextDocument, CancellationToken, ValueTask> LoadDocumentAsync,
    Func<DocumentOpenResult, bool, CancellationToken, ValueTask> CompleteOpenAsync,
    Func<string?, CancellationToken, ValueTask>? PrepareFieldContextAsync = null,
    Func<CancellationToken, ValueTask>? UpdateFieldsAsync = null);

public sealed record DocumentOpenExecutionResult
{
    private DocumentOpenExecutionResult(
        OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome> operation)
    {
        Operation = operation;
    }

    public DocumentOpenExecutionResult(
        DocumentFileExecutionOutcome Outcome,
        DocumentOpenResult? OpenResult,
        Exception? Exception)
        : this(DocumentFileExecutionOutcomeMapper.MapOpen(Outcome, OpenResult, Exception))
    {
    }

    public OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
        Operation { get; }

    public DocumentFileExecutionOutcome Outcome => DocumentFileExecutionOutcomeMapper.MapOpen(Operation);
    public DocumentOpenResult? OpenResult => Operation.Value;
    public Exception? Exception => Operation.Exception;
    public bool Succeeded => Operation.Succeeded;

    public void Deconstruct(
        out DocumentFileExecutionOutcome outcome,
        out DocumentOpenResult? openResult,
        out Exception? exception)
    {
        outcome = Outcome;
        openResult = OpenResult;
        exception = Exception;
    }

    internal static DocumentOpenExecutionResult Success(DocumentOpenResult result) =>
        new(OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
            .Completed(result, result.SavedPath));

    internal static DocumentOpenExecutionResult UnsupportedFormat() =>
        new(OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
            .ValidationFailure(DocumentFileExecutionOutcome.UnsupportedFormat));

    internal static DocumentOpenExecutionResult Canceled(OperationCanceledException exception) =>
        new(OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
            .Cancel(exception: exception));

    internal static DocumentOpenExecutionResult Failed(Exception exception) =>
        new(OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
            .Failure(DocumentFileExecutionOutcome.Failed, exception));
}

public sealed record DocumentSaveExecutionRequest(
    TextDocument Document,
    DocumentSaveTarget Target,
    DocumentSaveExecutionKind Kind,
    Func<CancellationToken, ValueTask>? PrepareDocumentAsync = null,
    Func<DocumentSaveCompatibilityPlan, CancellationToken, ValueTask<bool>>? ConfirmCompatibilityAsync = null,
    Func<DocumentSaveTarget, CancellationToken, ValueTask>? CompleteSaveAsync = null,
    // r137-remediation2: the write time the caller observed on Target.Path when it last opened or
    // saved this document (DocumentOpenResult.SourceLastWriteTimeUtc, rebased on each successful
    // save). Null disables the external-modification guard entirely -- used for Save-As/Save-Copy
    // targets that differ from the path the caller is tracking, where there is nothing to compare.
    DateTime? ExpectedLastWriteTimeUtc = null,
    Func<string, CancellationToken, ValueTask<bool>>? ConfirmExternallyModifiedOverwriteAsync = null);

public sealed record DocumentSaveExecutionResult
{
    private DocumentSaveExecutionResult(
        OperationOutcome<DocumentSaveCompatibilityPlan, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>
            operation)
    {
        Operation = operation;
    }

    public DocumentSaveExecutionResult(
        DocumentFileExecutionOutcome Outcome,
        DocumentSaveCompatibilityPlan? CompatibilityPlan,
        Exception? Exception)
        : this(DocumentFileExecutionOutcomeMapper.MapSave(Outcome, CompatibilityPlan, Exception))
    {
    }

    public OperationOutcome<
        DocumentSaveCompatibilityPlan,
        DocumentFileExecutionOutcome,
        DocumentFileExecutionOutcome> Operation { get; }

    public DocumentFileExecutionOutcome Outcome => DocumentFileExecutionOutcomeMapper.MapSave(Operation);
    public DocumentSaveCompatibilityPlan? CompatibilityPlan => Operation.Value;
    public Exception? Exception => Operation.Exception;
    public bool Succeeded => Operation.Succeeded;

    public void Deconstruct(
        out DocumentFileExecutionOutcome outcome,
        out DocumentSaveCompatibilityPlan? compatibilityPlan,
        out Exception? exception)
    {
        outcome = Outcome;
        compatibilityPlan = CompatibilityPlan;
        exception = Exception;
    }

    internal static DocumentSaveExecutionResult Success(
        DocumentSaveCompatibilityPlan compatibilityPlan,
        string path) =>
        new(OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>
            .Completed(compatibilityPlan, path));

    internal static DocumentSaveExecutionResult CompatibilityDeclined(
        DocumentSaveCompatibilityPlan compatibilityPlan,
        string path) =>
        new(OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>
            .Decline(compatibilityPlan, path));

    internal static DocumentSaveExecutionResult ExternalWriteConflict(string path) =>
        new(OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>
            .ValidationFailure(DocumentFileExecutionOutcome.ExternalWriteConflict, path: path));

    internal static DocumentSaveExecutionResult Canceled(OperationCanceledException exception, string path) =>
        new(OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>
            .Cancel(path: path, exception: exception));

    internal static DocumentSaveExecutionResult Failed(Exception exception, string path) =>
        new(OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>
            .Failure(DocumentFileExecutionOutcome.Failed, exception, path: path));
}

internal static class DocumentFileExecutionOutcomeMapper
{
    internal static OperationOutcome<
        DocumentOpenResult,
        DocumentFileExecutionOutcome,
        DocumentFileExecutionOutcome> MapOpen(
        DocumentFileExecutionOutcome outcome,
        DocumentOpenResult? result,
        Exception? exception) => outcome switch
    {
        DocumentFileExecutionOutcome.Succeeded => OperationOutcome<
            DocumentOpenResult,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Completed(result, result?.SavedPath),
        DocumentFileExecutionOutcome.UnsupportedFormat or DocumentFileExecutionOutcome.SaveAsRequired =>
            OperationOutcome<
                DocumentOpenResult,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>.ValidationFailure(outcome, result, result?.SavedPath),
        DocumentFileExecutionOutcome.CompatibilityDeclined => OperationOutcome<
            DocumentOpenResult,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Decline(result, result?.SavedPath),
        DocumentFileExecutionOutcome.Canceled => OperationOutcome<
            DocumentOpenResult,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Cancel(result, result?.SavedPath, exception),
        DocumentFileExecutionOutcome.Failed => OperationOutcome<
            DocumentOpenResult,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Failure(
                outcome,
                exception ?? new InvalidOperationException("The document operation failed."),
                result,
                result?.SavedPath),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported open outcome."),
    };

    internal static DocumentFileExecutionOutcome MapOpen(
        OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome> operation) =>
        operation.Status switch
        {
            OperationStatus.Completed => DocumentFileExecutionOutcome.Succeeded,
            OperationStatus.Cancelled => DocumentFileExecutionOutcome.Canceled,
            OperationStatus.Declined => DocumentFileExecutionOutcome.CompatibilityDeclined,
            OperationStatus.ValidationFailed => operation.Validation!.Detail,
            OperationStatus.Failed => operation.Error!.Detail,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Status,
                "Unsupported shared open outcome."),
        };

    internal static OperationOutcome<
        DocumentSaveCompatibilityPlan,
        DocumentFileExecutionOutcome,
        DocumentFileExecutionOutcome> MapSave(
        DocumentFileExecutionOutcome outcome,
        DocumentSaveCompatibilityPlan? compatibilityPlan,
        Exception? exception) => outcome switch
    {
        DocumentFileExecutionOutcome.Succeeded => OperationOutcome<
            DocumentSaveCompatibilityPlan,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Completed(compatibilityPlan),
        DocumentFileExecutionOutcome.CompatibilityDeclined => OperationOutcome<
            DocumentSaveCompatibilityPlan,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Decline(compatibilityPlan),
        DocumentFileExecutionOutcome.UnsupportedFormat
            or DocumentFileExecutionOutcome.SaveAsRequired
            or DocumentFileExecutionOutcome.ExternalWriteConflict =>
            OperationOutcome<
                DocumentSaveCompatibilityPlan,
                DocumentFileExecutionOutcome,
                DocumentFileExecutionOutcome>.ValidationFailure(outcome, compatibilityPlan),
        DocumentFileExecutionOutcome.Canceled => OperationOutcome<
            DocumentSaveCompatibilityPlan,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Cancel(compatibilityPlan, exception: exception),
        DocumentFileExecutionOutcome.Failed => OperationOutcome<
            DocumentSaveCompatibilityPlan,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome>.Failure(
                outcome,
                exception ?? new InvalidOperationException("The document operation failed."),
                compatibilityPlan),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported save outcome."),
    };

    internal static DocumentFileExecutionOutcome MapSave(
        OperationOutcome<
            DocumentSaveCompatibilityPlan,
            DocumentFileExecutionOutcome,
            DocumentFileExecutionOutcome> operation) => operation.Status switch
        {
            OperationStatus.Completed => DocumentFileExecutionOutcome.Succeeded,
            OperationStatus.Cancelled => DocumentFileExecutionOutcome.Canceled,
            OperationStatus.Declined => DocumentFileExecutionOutcome.CompatibilityDeclined,
            OperationStatus.ValidationFailed => operation.Validation!.Detail,
            OperationStatus.Failed => operation.Error!.Detail,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Status,
                "Unsupported shared save outcome."),
        };
}
