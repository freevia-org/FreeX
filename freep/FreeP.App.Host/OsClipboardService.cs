using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public interface IShapeRenderer
{
    byte[] RenderShapesToPng(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes,
        int widthPx,
        int heightPx);
}

/// <summary>Coordinates FreeP clipboard policy with the shared platform boundary.</summary>
public sealed class OsClipboardService
{
    private readonly IShapeRenderer _renderer;
    private readonly PresentationPlatformClipboardSession _session;

    internal bool OwnCopyIsCurrentOnOs =>
        _session.OwnCopyHasCurrentPlatformIdentity;

    /// <summary>
    /// The message from the most recent failed OS-clipboard write (<see
    /// cref="TryPlaceSelectionOnOsClipboard"/>), or null if the most recent write succeeded (or none
    /// has run yet). Copy/Cut callers read this after a false result so the failure reaches the user
    /// instead of vanishing silently.
    /// </summary>
    public string? LastWriteFailureMessage => _session.LastWriteFailureMessage;

    public int RenderWidthPx { get; set; } = 1280;
    public int RenderHeightPx { get; set; } = 720;

    public OsClipboardService(IPlatformClipboard clipboard, IShapeRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _session = new PresentationPlatformClipboardSession(
            clipboard,
            RenderSelection,
            static content => PresentationClipboardPlatformMapper.ToPlatformContent(content),
            PresentationClipboardPlatformIdentityStrategy.ChangeIdentity);
    }

    public void PlaceSelectionOnOsClipboard(EditingSession editor) =>
        TryPlaceSelectionOnOsClipboard(editor);

    internal bool TryPlaceSelectionOnOsClipboard(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return TryWrite(PrepareWrite(editor));
    }

    internal PresentationClipboardWriteRequest PrepareWrite(EditingSession editor) =>
        _session.PrepareWrite(editor);

    internal bool TryWrite(PresentationClipboardWriteRequest request) =>
        _session.WriteAsync(request)
            .GetAwaiter()
            .GetResult();

    public void Copy(
        EditingSession editor,
        Action<string>? onWriteFailed = null)
    {
        var written = _session.CopyAsync(editor).GetAwaiter().GetResult();
        ReportFailure(written, onWriteFailed);
    }

    public void Cut(
        EditingSession editor,
        Action<string>? onWriteFailed = null)
    {
        var written = _session.CutAsync(editor).GetAwaiter().GetResult();
        ReportFailure(written, onWriteFailed);
    }

    public void Paste(EditingSession editor, bool preferOsClipboard = true) =>
        PasteWithResult(editor, preferOsClipboard);

    internal PresentationClipboardPasteSource PasteWithResult(
        EditingSession editor,
        bool preferOsClipboard = true) =>
        _session.PasteAsync(editor, preferOsClipboard)
            .GetAwaiter()
            .GetResult();

    internal DataObject BuildDataObject(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes)
    {
        var content = PresentationClipboardContentFactory.CreateSelection(
            presentation,
            slide,
            shapes,
            (sourcePresentation, sourceSlide, sourceShapes) => _renderer.RenderShapesToPng(
                sourcePresentation,
                sourceSlide,
                sourceShapes,
                RenderWidthPx,
                RenderHeightPx),
            Guid.NewGuid().ToString("N"));
        return WpfPlatformClipboard.BuildDataObject(
            PresentationClipboardPlatformMapper.ToPlatformContent(content));
    }

    private byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes) =>
        _renderer.RenderShapesToPng(
            presentation,
            slide,
            shapes,
            RenderWidthPx,
            RenderHeightPx);

    private void ReportFailure(bool written, Action<string>? onWriteFailed)
    {
        if (!written && LastWriteFailureMessage is { } error)
            onWriteFailed?.Invoke(error);
    }
}
