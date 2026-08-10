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

// Toolkit projection helpers retained for the native WPF adapter tests. Runtime access goes
// through WpfPlatformClipboard and IPlatformClipboard.
public static class WpfOsClipboard
{
    internal const string SelectionFormat = PresentationClipboardFormats.Selection;
    internal const string OwnerTokenFormat = PresentationClipboardFormats.OwnerToken;
    internal const string RichTextFormat = PresentationClipboardFormats.RichText;
    internal const string WindowsXamlPackageFormat = PresentationClipboardFormats.WindowsXamlPackage;
    internal const string AvaloniaApplicationFormatPrefix =
        PresentationClipboardPlatformMapper.LegacyAvaloniaApplicationFormatPrefix;
    internal const string LegacyAvaloniaSelectionFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.Selection;
    internal const string LegacyAvaloniaOwnerTokenFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.OwnerToken;

    internal static DataObject BuildDataObject(PresentationClipboardContent content) =>
        WpfPlatformClipboard.BuildDataObject(
            PresentationClipboardPlatformMapper.ToPlatformContent(content));

    internal static PresentationClipboardContent ReadDataObject(IDataObject? data)
    {
        var read = WpfPlatformClipboard.ReadDataObject(
            data,
            PresentationClipboardPlatformMapper.ReadRequest);
        return read.Status == PlatformClipboardReadStatus.Success
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();
    }
}

/// <summary>Coordinates FreeP clipboard policy with the shared platform boundary.</summary>
public sealed class OsClipboardService
{
    private readonly IPlatformClipboard _clipboard;
    private readonly IShapeRenderer _renderer;
    private readonly PresentationClipboardOwnershipTracker _ownership = new();

    internal bool OwnCopyIsCurrentOnOs =>
        _ownership.HasCurrentPlatformIdentity(CurrentSequenceIdentity());

    public int RenderWidthPx { get; set; } = 1280;
    public int RenderHeightPx { get; set; } = 720;

    public OsClipboardService(IPlatformClipboard clipboard, IShapeRenderer renderer)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public void PlaceSelectionOnOsClipboard(EditingSession editor) =>
        TryPlaceSelectionOnOsClipboard(editor);

    internal bool TryPlaceSelectionOnOsClipboard(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return TryWrite(PrepareWrite(editor));
    }

    internal PresentationClipboardWriteRequest PrepareWrite(EditingSession editor) =>
        PresentationClipboardWorkflow.PrepareWrite(
            editor,
            (presentation, slide, shapes) => _renderer.RenderShapesToPng(
                presentation,
                slide,
                shapes,
                RenderWidthPx,
                RenderHeightPx),
            Guid.NewGuid().ToString("N"));

    internal bool TryWrite(PresentationClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Content is null)
            return false;

        var result = _clipboard.WriteAsync(
                PresentationClipboardPlatformMapper.ToPlatformContent(request.Content))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (result.IsSuccess)
        {
            _ownership.RecordSuccessfulWrite(request.Content, CurrentSequenceIdentity());
            return true;
        }

        _ownership.Invalidate();
        return false;
    }

    public void Paste(EditingSession editor, bool preferOsClipboard = true) =>
        PasteWithResult(editor, preferOsClipboard);

    internal PresentationClipboardPasteSource PasteWithResult(
        EditingSession editor,
        bool preferOsClipboard = true)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var read = _clipboard.ReadAsync(PresentationClipboardPlatformMapper.ReadRequest)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        var content = read.Status == PlatformClipboardReadStatus.Success
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();

        var ownCopy = preferOsClipboard
            && _ownership.IsCurrent(content, CurrentSequenceIdentity(), editor.CanPaste);
        return PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            content,
            ownCopy,
            preferOsClipboard);
    }

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

    public static PasteAction DecidePasteAction(
        bool osHasImage,
        bool osHasText,
        bool internalHasData,
        bool preferOsClipboard = true,
        bool ownCopyIsCurrentOnOs = false,
        bool osHasRichText = false,
        bool osHasXamlPackage = false)
    {
        var source = !preferOsClipboard && internalHasData
            ? PresentationClipboardPasteSource.Internal
            : PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                hasImage: osHasImage,
                hasText: osHasText,
                internalHasData: internalHasData,
                ownCopyIsCurrent: ownCopyIsCurrentOnOs,
                hasRichText: osHasRichText,
                hasXamlPackage: osHasXamlPackage);
        return source switch
        {
            PresentationClipboardPasteSource.Image => PasteAction.OsImage,
            PresentationClipboardPasteSource.RichText => PasteAction.OsText,
            PresentationClipboardPasteSource.XamlPackage => PasteAction.OsText,
            PresentationClipboardPasteSource.Text => PasteAction.OsText,
            PresentationClipboardPasteSource.Internal => PasteAction.Internal,
            _ => PasteAction.Nothing,
        };
    }

    internal static string ExtractText(IEnumerable<SlideShape> shapes) =>
        PresentationClipboardContentFactory.ExtractText(shapes) ?? string.Empty;

    private string? CurrentSequenceIdentity() => _clipboard.TryGetChangeIdentity();
}

public enum PasteAction
{
    OsImage,
    OsText,
    Internal,
    Nothing,
}
