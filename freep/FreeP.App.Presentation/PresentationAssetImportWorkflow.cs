using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationAssetImportKind
{
    Picture,
    Video,
    Audio,
    EmbeddedObject,
    TransitionSound,
    PictureBullet,
    SmartArtPicture,
    ZoomCoverImage,
}

public enum PresentationAssetPickerStatus
{
    Selected,
    Cancelled,
    Unavailable,
}

public sealed record PresentationAssetImportRequest(
    PresentationAssetImportKind Kind,
    string CommandName,
    string PickerTitle)
{
    public static PresentationAssetImportRequest Create(PresentationAssetImportKind kind) =>
        kind switch
        {
            PresentationAssetImportKind.Picture => new(
                kind,
                PresentationFileTextResources.Presentation.InsertPictureCommand,
                PresentationFileTextResources.Presentation.InsertPicturePickerTitle),
            PresentationAssetImportKind.Video => new(
                kind,
                PresentationFileTextResources.InsertVideoCommand,
                PresentationFileTextResources.InsertVideoPickerTitle),
            PresentationAssetImportKind.Audio => new(
                kind,
                PresentationFileTextResources.InsertAudioCommand,
                PresentationFileTextResources.InsertAudioPickerTitle),
            PresentationAssetImportKind.EmbeddedObject => new(
                kind,
                OleInsertionPlanner.PickerTitle,
                OleInsertionPlanner.PickerTitle),
            PresentationAssetImportKind.TransitionSound => new(
                kind,
                PresentationFileTextResources.InsertAudioCommand,
                PresentationFileTextResources.InsertAudioPickerTitle),
            PresentationAssetImportKind.PictureBullet => new(
                kind,
                "Picture Bullet",
                "Choose Picture Bullet"),
            PresentationAssetImportKind.SmartArtPicture => new(
                kind,
                "Replace SmartArt picture",
                "Replace SmartArt picture"),
            PresentationAssetImportKind.ZoomCoverImage => new(
                kind,
                ZoomCoverImagePlanner.DialogTitle,
                ZoomCoverImagePlanner.DialogTitle),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}

public sealed record PresentationAssetSelection(
    string Name,
    object Source);

public sealed record PresentationAssetPickerResult(
    PresentationAssetPickerStatus Status,
    PresentationAssetSelection? Selection = null,
    string? Message = null)
{
    public static PresentationAssetPickerResult Selected(string name, object source) =>
        new(
            PresentationAssetPickerStatus.Selected,
            new PresentationAssetSelection(name, source));

    public static PresentationAssetPickerResult Cancelled { get; } =
        new(PresentationAssetPickerStatus.Cancelled);

    public static PresentationAssetPickerResult Unavailable(string message) =>
        new(PresentationAssetPickerStatus.Unavailable, Message: message);
}

public interface IPresentationAssetPickerPort
{
    Task<PresentationAssetPickerResult> PickAsync(
        PresentationAssetImportRequest request,
        CancellationToken cancellationToken);
}

public interface IPresentationAssetReaderPort
{
    Task<byte[]> ReadAsync(
        PresentationAssetSelection selection,
        CancellationToken cancellationToken);
}

public sealed record PresentationAssetImportPayload(
    PresentationAssetImportKind Kind,
    string SourceName,
    byte[] Bytes,
    string? ContentType = null,
    SlideObjectPicturePayload? Picture = null,
    SlideObjectMediaPayload? Media = null,
    PresentationPictureBulletPayload? PictureBullet = null);

public sealed record PresentationAssetImportExecutionResult(
    bool Applied,
    string? Message = null)
{
    public static PresentationAssetImportExecutionResult Success { get; } = new(true);

    public static PresentationAssetImportExecutionResult NotApplied(string? message = null) =>
        new(false, message);
}

public interface IPresentationAssetImportExecutionPort
{
    PresentationAssetImportExecutionResult Execute(PresentationAssetImportPayload payload);
}

public sealed record PresentationAssetImportExecutionCallbacks(
    Func<PresentationPictureBulletPayload, bool>? ApplyPictureBullet = null,
    Func<byte[], string, bool>? ApplySmartArtPicture = null,
    Func<byte[], string, bool>? ApplyZoomCoverImage = null,
    Action? EmbeddedObjectInserted = null);

public sealed class PresentationAssetImportExecutionPort : IPresentationAssetImportExecutionPort
{
    private readonly EditingSession _editor;
    private readonly PresentationAssetImportExecutionCallbacks _callbacks;

    public PresentationAssetImportExecutionPort(
        EditingSession editor,
        PresentationAssetImportExecutionCallbacks? callbacks = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _callbacks = callbacks ?? new PresentationAssetImportExecutionCallbacks();
    }

    public PresentationAssetImportExecutionResult Execute(PresentationAssetImportPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var applied = payload.Kind switch
        {
            PresentationAssetImportKind.Picture when payload.Picture is { } picture =>
                SlideObjectInsertionPlanner.ApplyCommand(
                    _editor,
                    SlideObjectInsertionPlanner.PictureCommandId,
                    picture) is not null,
            PresentationAssetImportKind.Video when payload.Media is { } video =>
                SlideObjectInsertionPlanner.ApplyCommand(
                    _editor,
                    SlideObjectInsertionPlanner.VideoCommandId,
                    mediaPayload: video) is not null,
            PresentationAssetImportKind.Audio when payload.Media is { } audio =>
                SlideObjectInsertionPlanner.ApplyCommand(
                    _editor,
                    SlideObjectInsertionPlanner.AudioCommandId,
                    mediaPayload: audio) is not null,
            PresentationAssetImportKind.EmbeddedObject => InsertEmbeddedObject(payload),
            PresentationAssetImportKind.TransitionSound => ApplyTransitionSound(payload),
            PresentationAssetImportKind.PictureBullet when payload.PictureBullet is { } pictureBullet =>
                _callbacks.ApplyPictureBullet?.Invoke(pictureBullet) == true,
            PresentationAssetImportKind.SmartArtPicture when payload.ContentType is { } contentType =>
                _callbacks.ApplySmartArtPicture?.Invoke(payload.Bytes, contentType) == true,
            PresentationAssetImportKind.ZoomCoverImage when payload.ContentType is { } contentType =>
                _callbacks.ApplyZoomCoverImage?.Invoke(payload.Bytes, contentType) == true,
            _ => false,
        };

        return applied
            ? PresentationAssetImportExecutionResult.Success
            : PresentationAssetImportExecutionResult.NotApplied();
    }

    private bool InsertEmbeddedObject(PresentationAssetImportPayload payload)
    {
        _editor.InsertEmbeddedObject(payload.Bytes, payload.SourceName);
        _callbacks.EmbeddedObjectInserted?.Invoke();
        return true;
    }

    private bool ApplyTransitionSound(PresentationAssetImportPayload payload)
    {
        _editor.SetCurrentSlideTransitionSound(new TransitionSound
        {
            AudioBytes = payload.Bytes,
            ContentType = payload.ContentType,
            IsBuiltIn = false,
        });
        return true;
    }
}

public enum PresentationAssetImportStatus
{
    Succeeded,
    Cancelled,
    Unavailable,
    NotApplied,
    Failed,
}

public sealed record PresentationAssetImportResult(
    PresentationAssetImportRequest Request,
    PresentationAssetImportStatus Status,
    string? SourceName = null,
    string? Message = null,
    Exception? Exception = null)
{
    public bool Succeeded => Status == PresentationAssetImportStatus.Succeeded;

    public bool Cancelled => Status == PresentationAssetImportStatus.Cancelled;
}

/// <summary>
/// Owns the renderer-neutral asset-import lifecycle. Hosts retain native pickers,
/// storage reads, command execution, and UI feedback materialization.
/// </summary>
public sealed class PresentationAssetImportWorkflow
{
    private readonly IPresentationAssetPickerPort _picker;
    private readonly IPresentationAssetReaderPort _reader;
    private readonly IPresentationAssetImportExecutionPort _execution;

    public PresentationAssetImportWorkflow(
        IPresentationAssetPickerPort picker,
        IPresentationAssetReaderPort reader,
        IPresentationAssetImportExecutionPort execution)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    public async Task<PresentationAssetImportResult> ImportAsync(
        PresentationAssetImportKind kind,
        CancellationToken cancellationToken = default) =>
        await ImportAsync(PresentationAssetImportRequest.Create(kind), cancellationToken);

    public async Task<PresentationAssetImportResult> ImportAsync(
        PresentationAssetImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PresentationAssetPickerResult picked;
        try
        {
            picked = await _picker.PickAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PresentationAssetImportResult(request, PresentationAssetImportStatus.Cancelled);
        }
        catch (Exception ex)
        {
            return Failure(request, ex);
        }

        if (picked.Status == PresentationAssetPickerStatus.Cancelled)
            return new PresentationAssetImportResult(request, PresentationAssetImportStatus.Cancelled);

        if (picked.Status == PresentationAssetPickerStatus.Unavailable)
        {
            return new PresentationAssetImportResult(
                request,
                PresentationAssetImportStatus.Unavailable,
                Message: picked.Message);
        }

        if (picked.Selection is not { } selection)
        {
            return Failure(
                request,
                new InvalidOperationException("The asset picker returned a selected result without a selection."));
        }

        try
        {
            var bytes = await _reader.ReadAsync(selection, cancellationToken);
            var payload = CreatePayload(request.Kind, selection.Name, bytes);
            var execution = _execution.Execute(payload);
            return execution.Applied
                ? new PresentationAssetImportResult(
                    request,
                    PresentationAssetImportStatus.Succeeded,
                    selection.Name,
                    execution.Message)
                : new PresentationAssetImportResult(
                    request,
                    PresentationAssetImportStatus.NotApplied,
                    selection.Name,
                    execution.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PresentationAssetImportResult(request, PresentationAssetImportStatus.Cancelled);
        }
        catch (Exception ex)
        {
            return Failure(request, ex, selection.Name);
        }
    }

    public static PresentationAssetImportPayload CreatePayload(
        PresentationAssetImportKind kind,
        string sourceName,
        byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(bytes);

        return kind switch
        {
            PresentationAssetImportKind.Picture => CreatePicturePayload(kind, sourceName, bytes),
            PresentationAssetImportKind.Video => CreateMediaPayload(kind, sourceName, bytes, isVideo: true),
            PresentationAssetImportKind.Audio => CreateMediaPayload(kind, sourceName, bytes, isVideo: false),
            PresentationAssetImportKind.EmbeddedObject => new(kind, sourceName, bytes),
            PresentationAssetImportKind.TransitionSound => new(
                kind,
                sourceName,
                bytes,
                SlideObjectInsertionPlanner.InferMediaContentType(sourceName, isVideo: false)),
            PresentationAssetImportKind.PictureBullet => CreatePictureBulletPayload(kind, sourceName, bytes),
            PresentationAssetImportKind.SmartArtPicture or PresentationAssetImportKind.ZoomCoverImage => new(
                kind,
                sourceName,
                bytes,
                SlideObjectInsertionPlanner.InferPictureContentType(sourceName)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static PresentationAssetImportPayload CreatePicturePayload(
        PresentationAssetImportKind kind,
        string sourceName,
        byte[] bytes)
    {
        var picture = SlideObjectInsertionPlanner.CreatePicturePayload(bytes, sourceName);
        return new(kind, sourceName, bytes, picture.ContentType, Picture: picture);
    }

    private static PresentationAssetImportPayload CreateMediaPayload(
        PresentationAssetImportKind kind,
        string sourceName,
        byte[] bytes,
        bool isVideo)
    {
        var media = SlideObjectInsertionPlanner.CreateMediaPayload(bytes, sourceName, isVideo);
        return new(kind, sourceName, bytes, media.ContentType, Media: media);
    }

    private static PresentationAssetImportPayload CreatePictureBulletPayload(
        PresentationAssetImportKind kind,
        string sourceName,
        byte[] bytes)
    {
        var pictureBullet = PresentationPictureBulletAuthoringPlanner.CreatePayloadFromFileName(bytes, sourceName);
        return new(kind, sourceName, bytes, pictureBullet.ContentType, PictureBullet: pictureBullet);
    }

    private static PresentationAssetImportResult Failure(
        PresentationAssetImportRequest request,
        Exception exception,
        string? sourceName = null) =>
        new(
            request,
            PresentationAssetImportStatus.Failed,
            sourceName,
            exception.Message,
            exception);
}
