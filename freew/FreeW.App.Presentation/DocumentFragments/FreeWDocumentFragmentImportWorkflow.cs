using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentFragments;

public enum FreeWDocumentFragmentImportKind
{
    TextFromFile,
    EmbeddedObject,
}

public sealed record FreeWDocumentFragmentPickerPlan(
    string Title,
    IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes,
    string DefaultExtensionWithDot = "")
{
    public string BuildWpfFilter() => string.Join(
        '|',
        FileTypes.Select(fileType =>
            $"{fileType.DisplayName}|{string.Join(';', fileType.Patterns)}"));
}

public sealed record FreeWDocumentFragmentImportRequest(
    FreeWDocumentFragmentImportKind Kind,
    string CommandName,
    FreeWDocumentFragmentPickerPlan PickerPlan);

public static class FreeWDocumentFragmentImportPlanner
{
    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static FreeWDocumentFragmentImportRequest CreateTextFromFileRequest() => new(
        FreeWDocumentFragmentImportKind.TextFromFile,
        FreeWFileTextResources.InsertTextCommand,
        new FreeWDocumentFragmentPickerPlan(
            InsertDialogTextResources.TextFromFilePickerTitle,
            [
                new FileDialogPickerTypeDescriptor(
                    FreeWFileTextResources.TextFromFileTypeName,
                    ["*.docx", "*.txt"],
                    [DocxMimeType, "text/plain"]),
            ],
            DefaultExtensionWithDot: ".docx"));

    public static FreeWDocumentFragmentImportRequest CreateEmbeddedObjectRequest() => new(
        FreeWDocumentFragmentImportKind.EmbeddedObject,
        "Insert object",
        new FreeWDocumentFragmentPickerPlan(
            "Insert Object",
            [new FileDialogPickerTypeDescriptor("All files (*.*)", ["*.*"])]));
}

public sealed record FreeWDocumentFragmentImportSelection(
    string Name,
    string LocalPath,
    object Source);

public sealed record FreeWDocumentFragmentPickerResult
{
    private FreeWDocumentFragmentPickerResult(
        PickerOutcome<FreeWDocumentFragmentImportSelection> outcome)
    {
        Outcome = outcome;
    }

    public PickerOutcome<FreeWDocumentFragmentImportSelection> Outcome { get; }
    public OperationStatus Status => Outcome.Status;
    public FreeWDocumentFragmentImportSelection? Selection => Outcome.Selection;
    public string? Message => Outcome.Message;

    public static FreeWDocumentFragmentPickerResult Selected(
        string name,
        string localPath,
        object source) =>
        new(PickerOutcome<FreeWDocumentFragmentImportSelection>.Selected(
            new FreeWDocumentFragmentImportSelection(name, localPath, source)));

    public static FreeWDocumentFragmentPickerResult Cancelled { get; } =
        new(PickerOutcome<FreeWDocumentFragmentImportSelection>.Cancelled);

    public static FreeWDocumentFragmentPickerResult Unavailable(string message) =>
        new(PickerOutcome<FreeWDocumentFragmentImportSelection>.Unavailable(message));
}

public interface IFreeWDocumentFragmentPickerPort
{
    Task<FreeWDocumentFragmentPickerResult> PickAsync(
        FreeWDocumentFragmentImportRequest request,
        CancellationToken cancellationToken);
}

public interface IFreeWDocumentFragmentSourceReaderPort
{
    Task<byte[]> ReadBytesAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken);

    Task<string> ReadTextAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken);

    void ResolveLinkedImagePreviews(
        FreeWDocumentFragmentImportSelection selection,
        TextDocument document);
}

/// <summary>
/// Shared local-file reading for renderer adapters. Native hosts retain linked-image preview
/// realization because that depends on renderer-specific image support.
/// </summary>
public abstract class FreeWDocumentFragmentFileSourceReaderPort :
    IFreeWDocumentFragmentSourceReaderPort
{
    public Task<byte[]> ReadBytesAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        FileByteReadWorkflow.ReadLocalPathBytesAsync(
            (string)selection.Source,
            cancellationToken);

    public Task<string> ReadTextAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        File.ReadAllTextAsync((string)selection.Source, cancellationToken);

    public abstract void ResolveLinkedImagePreviews(
        FreeWDocumentFragmentImportSelection selection,
        TextDocument document);
}

public enum FreeWDocumentFragmentInsertionKind
{
    Document,
    PlainText,
    EmbeddedObject,
}

public sealed record FreeWDocumentFragmentInsertionRequest(
    FreeWDocumentFragmentInsertionKind Kind,
    TextDocument? Document = null,
    string? PlainText = null,
    EmbeddedObject? EmbeddedObject = null)
{
    public static FreeWDocumentFragmentInsertionRequest ForDocument(TextDocument document) =>
        new(FreeWDocumentFragmentInsertionKind.Document, Document: document);

    public static FreeWDocumentFragmentInsertionRequest ForPlainText(string text) =>
        new(FreeWDocumentFragmentInsertionKind.PlainText, PlainText: text);

    public static FreeWDocumentFragmentInsertionRequest ForEmbeddedObject(EmbeddedObject embeddedObject) =>
        new(FreeWDocumentFragmentInsertionKind.EmbeddedObject, EmbeddedObject: embeddedObject);
}

public sealed record FreeWDocumentFragmentInsertionResult(bool Applied, string? Message = null)
{
    public static FreeWDocumentFragmentInsertionResult Success { get; } = new(true);

    public static FreeWDocumentFragmentInsertionResult NotApplied(string? message = null) =>
        new(false, message);
}

public interface IFreeWDocumentFragmentInsertionPort
{
    FreeWDocumentFragmentInsertionResult Insert(FreeWDocumentFragmentInsertionRequest request);
}

public enum FreeWDocumentFragmentImportStatus
{
    Succeeded,
    Cancelled,
    Unavailable,
    UnsupportedFormat,
    NotApplied,
    Failed,
}

public sealed record FreeWDocumentFragmentImportResult(
    FreeWDocumentFragmentImportRequest Request,
    FreeWDocumentFragmentImportStatus Status,
    string? SourceName = null,
    string? SourceExtension = null,
    FreeWDocumentFragmentInsertionRequest? Insertion = null,
    string? Message = null,
    Exception? Exception = null);

public enum FreeWDocumentFragmentImportFailureSurface
{
    AvaloniaStatus,
    WpfModalError,
    None,
}

public sealed record FreeWDocumentFragmentImportOutcomePresentation(
    string? StatusText = null,
    string? ModalTitle = null,
    string? ModalMessage = null)
{
    public static FreeWDocumentFragmentImportOutcomePresentation Empty { get; } = new();
}

public static class FreeWDocumentFragmentImportOutcomePlanner
{
    public static FreeWDocumentFragmentImportOutcomePresentation Plan(
        FreeWDocumentFragmentImportResult result,
        SisterAppFileTextSpec fileText,
        FreeWDocumentFragmentImportFailureSurface failureSurface)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fileText);

        if (result.Status is FreeWDocumentFragmentImportStatus.Succeeded
            or FreeWDocumentFragmentImportStatus.Cancelled
            or FreeWDocumentFragmentImportStatus.NotApplied
            || failureSurface == FreeWDocumentFragmentImportFailureSurface.None)
        {
            return FreeWDocumentFragmentImportOutcomePresentation.Empty;
        }

        var reason = result.Message ?? string.Empty;
        if (failureSurface == FreeWDocumentFragmentImportFailureSurface.WpfModalError)
        {
            var subject = result.Request.Kind == FreeWDocumentFragmentImportKind.TextFromFile
                ? "file"
                : "object";
            return new FreeWDocumentFragmentImportOutcomePresentation(
                ModalTitle: "FreeW",
                ModalMessage: $"Could not insert the {subject}:\n{reason}");
        }

        if (failureSurface != FreeWDocumentFragmentImportFailureSurface.AvaloniaStatus)
            throw new ArgumentOutOfRangeException(nameof(failureSurface), failureSurface, null);

        if (result.Request.Kind == FreeWDocumentFragmentImportKind.EmbeddedObject)
        {
            return new FreeWDocumentFragmentImportOutcomePresentation(
                StatusText: $"Could not insert the object: {reason}");
        }

        return new FreeWDocumentFragmentImportOutcomePresentation(
            StatusText: result.Status switch
            {
                FreeWDocumentFragmentImportStatus.UnsupportedFormat =>
                    SisterAppFileTextPlanner.FormatUnsupportedFileType(
                        fileText,
                        result.Request.CommandName,
                        result.SourceExtension ?? string.Empty),
                FreeWDocumentFragmentImportStatus.Unavailable =>
                    SisterAppFileTextPlanner.FormatCommandUnavailable(fileText, result.Request.CommandName),
                _ => SisterAppFileTextPlanner.FormatCommandFailed(
                    fileText,
                    result.Request.CommandName,
                    reason),
            });
    }
}

/// <summary>
/// Owns text/object selection policy, parsing, package creation, insertion requests, and outcomes.
/// Native hosts retain picker, file access, editor, focus, dialog, and status realization.
/// </summary>
public sealed class FreeWDocumentFragmentImportWorkflow
{
    private readonly IReadOnlyList<IDocumentFileAdapter> _documentAdapters;
    private readonly IFreeWDocumentFragmentPickerPort _picker;
    private readonly IFreeWDocumentFragmentSourceReaderPort _reader;
    private readonly IFreeWDocumentFragmentInsertionPort _insertion;

    public FreeWDocumentFragmentImportWorkflow(
        IEnumerable<IDocumentFileAdapter> documentAdapters,
        IFreeWDocumentFragmentPickerPort picker,
        IFreeWDocumentFragmentSourceReaderPort reader,
        IFreeWDocumentFragmentInsertionPort insertion)
    {
        ArgumentNullException.ThrowIfNull(documentAdapters);
        _documentAdapters = documentAdapters.ToArray();
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _insertion = insertion ?? throw new ArgumentNullException(nameof(insertion));
    }

    public async Task<FreeWDocumentFragmentImportResult> ImportAsync(
        FreeWDocumentFragmentImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pickerResult = await _picker.PickAsync(request, cancellationToken);
            if (pickerResult.Status == OperationStatus.Cancelled)
                return new FreeWDocumentFragmentImportResult(request, FreeWDocumentFragmentImportStatus.Cancelled);
            if (pickerResult.Status == OperationStatus.Unavailable)
            {
                return new FreeWDocumentFragmentImportResult(
                    request,
                    FreeWDocumentFragmentImportStatus.Unavailable,
                    Message: pickerResult.Message);
            }

            var selection = pickerResult.Selection
                ?? throw new InvalidOperationException("The document-fragment picker did not return a selection.");
            var extension = Path.GetExtension(selection.LocalPath);
            var insertionRequest = request.Kind switch
            {
                FreeWDocumentFragmentImportKind.TextFromFile =>
                    await BuildTextInsertionAsync(selection, extension, cancellationToken),
                FreeWDocumentFragmentImportKind.EmbeddedObject =>
                    await BuildObjectInsertionAsync(selection, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, null),
            };

            if (insertionRequest is null)
            {
                return new FreeWDocumentFragmentImportResult(
                    request,
                    FreeWDocumentFragmentImportStatus.UnsupportedFormat,
                    selection.Name,
                    extension);
            }

            var insertionResult = _insertion.Insert(insertionRequest);
            return new FreeWDocumentFragmentImportResult(
                request,
                insertionResult.Applied
                    ? FreeWDocumentFragmentImportStatus.Succeeded
                    : FreeWDocumentFragmentImportStatus.NotApplied,
                selection.Name,
                extension,
                insertionRequest,
                insertionResult.Message);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new FreeWDocumentFragmentImportResult(
                request,
                FreeWDocumentFragmentImportStatus.Cancelled,
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new FreeWDocumentFragmentImportResult(
                request,
                FreeWDocumentFragmentImportStatus.Failed,
                Message: ex.Message,
                Exception: ex);
        }
    }

    private async Task<FreeWDocumentFragmentInsertionRequest?> BuildTextInsertionAsync(
        FreeWDocumentFragmentImportSelection selection,
        string extension,
        CancellationToken cancellationToken)
    {
        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = await _reader.ReadTextAsync(selection, cancellationToken);
            return FreeWDocumentFragmentInsertionRequest.ForPlainText(text);
        }

        var adapter = DocumentFileFormatResolver.FindOpenAdapter(
            _documentAdapters,
            extension,
            out _);
        if (adapter is null)
            return null;

        var bytes = await _reader.ReadBytesAsync(selection, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var document = adapter.Load(stream);
        _reader.ResolveLinkedImagePreviews(selection, document);
        return FreeWDocumentFragmentInsertionRequest.ForDocument(document);
    }

    private async Task<FreeWDocumentFragmentInsertionRequest> BuildObjectInsertionAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken)
    {
        var bytes = await _reader.ReadBytesAsync(selection, cancellationToken);
        var payload = OlePackagePayloadBuilder.Create(selection.Name, selection.LocalPath, bytes);
        return FreeWDocumentFragmentInsertionRequest.ForEmbeddedObject(
            EmbeddedObject.Create(payload, OlePackagePayloadBuilder.ProgId));
    }
}
