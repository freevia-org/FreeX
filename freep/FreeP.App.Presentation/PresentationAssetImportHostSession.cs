using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral composition of native asset ports, editor execution callbacks, and
/// user-facing outcome materialization. Desktop hosts supply only native picker/reader adapters,
/// status text setters, and their message service.
/// </summary>
public sealed class PresentationAssetImportHostSession
{
    private readonly IPresentationAssetPickerPort _picker;
    private readonly IPresentationAssetReaderPort _reader;
    private readonly EditingSession _editor;
    private readonly PresentationAssetImportExecutionCallbacks _callbacks;

    public PresentationAssetImportHostSession(
        IPresentationAssetPickerPort picker,
        IPresentationAssetReaderPort reader,
        EditingSession editor,
        PresentationAssetImportExecutionCallbacks? callbacks = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _callbacks = callbacks ?? new PresentationAssetImportExecutionCallbacks();
    }

    public Task<PresentationAssetImportResult> ImportAsync(
        PresentationAssetImportKind kind,
        Func<byte[], string, bool>? applyZoomCoverImage = null,
        CancellationToken cancellationToken = default)
    {
        var callbacks = applyZoomCoverImage is null
            ? _callbacks
            : _callbacks with { ApplyZoomCoverImage = applyZoomCoverImage };
        var workflow = new PresentationAssetImportWorkflow(
            _picker,
            _reader,
            new PresentationAssetImportExecutionPort(_editor, callbacks));
        return workflow.ImportAsync(kind, cancellationToken);
    }

    public async ValueTask<PresentationAssetImportOutcomePresentation> MaterializeOutcomeAsync(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportOutcomePolicy policy,
        Action<string> setDefaultStatus,
        IUserMessageService messageService,
        Action<string>? statusTarget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fileText);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(setDefaultStatus);
        ArgumentNullException.ThrowIfNull(messageService);

        var presentation = PresentationAssetImportOutcomePlanner.Plan(result, fileText, policy);
        if (presentation.StatusText is { } statusText)
            (statusTarget ?? setDefaultStatus)(statusText);

        if (presentation.Message is { } message)
            _ = await messageService.ShowMessageAsync(message, cancellationToken);

        return presentation;
    }
}
