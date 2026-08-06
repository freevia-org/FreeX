using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public enum DocumentFileExecutionOutcome
{
    Succeeded,
    UnsupportedFormat,
    CompatibilityDeclined,
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
                    return DocumentSaveExecutionResult.CompatibilityDeclined(compatibility);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            _persistence.Save(request.Document, request.Target);

            if (request.Kind == DocumentSaveExecutionKind.Save)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.CompleteSaveAsync!(request.Target, cancellationToken);
            }

            return DocumentSaveExecutionResult.Success(compatibility);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return DocumentSaveExecutionResult.Canceled(ex);
        }
        catch (Exception ex)
        {
            return DocumentSaveExecutionResult.Failed(ex);
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

public sealed record DocumentOpenExecutionResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentOpenResult? OpenResult,
    Exception? Exception)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;

    internal static DocumentOpenExecutionResult Success(DocumentOpenResult result) =>
        new(DocumentFileExecutionOutcome.Succeeded, result, Exception: null);

    internal static DocumentOpenExecutionResult UnsupportedFormat() =>
        new(DocumentFileExecutionOutcome.UnsupportedFormat, OpenResult: null, Exception: null);

    internal static DocumentOpenExecutionResult Canceled(OperationCanceledException exception) =>
        new(DocumentFileExecutionOutcome.Canceled, OpenResult: null, exception);

    internal static DocumentOpenExecutionResult Failed(Exception exception) =>
        new(DocumentFileExecutionOutcome.Failed, OpenResult: null, exception);
}

public sealed record DocumentSaveExecutionRequest(
    TextDocument Document,
    DocumentSaveTarget Target,
    DocumentSaveExecutionKind Kind,
    Func<CancellationToken, ValueTask>? PrepareDocumentAsync = null,
    Func<DocumentSaveCompatibilityPlan, CancellationToken, ValueTask<bool>>? ConfirmCompatibilityAsync = null,
    Func<DocumentSaveTarget, CancellationToken, ValueTask>? CompleteSaveAsync = null);

public sealed record DocumentSaveExecutionResult(
    DocumentFileExecutionOutcome Outcome,
    DocumentSaveCompatibilityPlan? CompatibilityPlan,
    Exception? Exception)
{
    public bool Succeeded => Outcome == DocumentFileExecutionOutcome.Succeeded;

    internal static DocumentSaveExecutionResult Success(DocumentSaveCompatibilityPlan compatibilityPlan) =>
        new(DocumentFileExecutionOutcome.Succeeded, compatibilityPlan, Exception: null);

    internal static DocumentSaveExecutionResult CompatibilityDeclined(
        DocumentSaveCompatibilityPlan compatibilityPlan) =>
        new(DocumentFileExecutionOutcome.CompatibilityDeclined, compatibilityPlan, Exception: null);

    internal static DocumentSaveExecutionResult Canceled(OperationCanceledException exception) =>
        new(DocumentFileExecutionOutcome.Canceled, CompatibilityPlan: null, exception);

    internal static DocumentSaveExecutionResult Failed(Exception exception) =>
        new(DocumentFileExecutionOutcome.Failed, CompatibilityPlan: null, exception);
}
