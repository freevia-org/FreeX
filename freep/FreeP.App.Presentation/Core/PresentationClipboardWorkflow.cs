using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationClipboardSelectionSnapshot(
    int SlideIndex,
    IReadOnlyList<uint> SelectedShapeIds);

public sealed record PresentationClipboardWriteRequest(
    EditingSession Editor,
    PresentationClipboardContent? Content,
    PresentationClipboardSelectionSnapshot SourceSelection);

public sealed record PresentationClipboardPasteRequest(
    EditingSession Editor,
    int SlideIndex);

/// <summary>
/// Owns renderer-neutral copy, cut, and paste transitions. Desktop hosts retain only native
/// clipboard access and selection rendering.
/// </summary>
public static class PresentationClipboardWorkflow
{
    public static PresentationClipboardWriteRequest PrepareWrite(
        EditingSession editor,
        Func<Presentation, Slide, IReadOnlyList<SlideShape>, byte[]> renderPng,
        string ownerToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(renderPng);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);

        var sourceSelection = CaptureSelection(editor);
        return new PresentationClipboardWriteRequest(
            editor,
            PresentationClipboardContentFactory.CreateSelection(editor, renderPng, ownerToken),
            sourceSelection);
    }

    public static PresentationClipboardWriteRequest PrepareInternalWrite(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new PresentationClipboardWriteRequest(editor, null, CaptureSelection(editor));
    }

    public static PresentationClipboardPasteRequest PreparePaste(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new PresentationClipboardPasteRequest(editor, editor.CurrentSlideIndex);
    }

    public static void CommitCopy(PresentationClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var liveSelection = CaptureSelection(request.Editor);
        RestoreSelection(request.Editor, request.SourceSelection);
        request.Editor.CopySelectedShapes();
        RestoreSelection(request.Editor, liveSelection);
    }

    public static void CommitCut(
        PresentationClipboardWriteRequest request,
        Action? beforeDelete = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        RestoreSelection(request.Editor, request.SourceSelection);
        request.Editor.CopySelectedShapes();
        beforeDelete?.Invoke();
        request.Editor.DeleteSelected();
    }

    public static PresentationClipboardPasteSource ApplyPaste(
        PresentationClipboardPasteRequest request,
        PresentationClipboardContent content,
        bool ownCopyIsCurrent,
        bool preferSystemClipboard = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        var editor = request.Editor;
        if (editor.CurrentSlideIndex != request.SlideIndex)
            editor.SelectSlide(request.SlideIndex);

        var source = !preferSystemClipboard && editor.CanPaste
            ? PresentationClipboardPasteSource.Internal
            : DecideSource(content, editor.CanPaste, ownCopyIsCurrent);

        if (source == PresentationClipboardPasteSource.NativeSelection)
        {
            try
            {
                var shapes = PresentationClipboardSelectionCodec.Deserialize(content.SelectionBytes!);
                if (shapes.Count > 0)
                {
                    editor.PasteExternalShapes(shapes);
                    return source;
                }
            }
            catch
            {
                // Continue through the interoperable image, rich-text, XAML, and text formats.
            }

            source = DecideSource(content with { SelectionBytes = null }, editor.CanPaste, false);
        }

        if (source == PresentationClipboardPasteSource.RichText)
        {
            var payload = InCanvasRichClipboardPlanner.Deserialize(content.RichTextBytes)
                ?? ExternalRichTextClipboardPlanner.TryParseRtf(content.RtfBytes);
            if (payload is not null)
            {
                ApplyRichPayload(editor, payload);
                return source;
            }

            source = DecideSource(
                content with { SelectionBytes = null, RichTextBytes = null, RtfBytes = null },
                editor.CanPaste,
                false);
        }

        if (source == PresentationClipboardPasteSource.XamlPackage)
        {
            var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(content.XamlPackageBytes);
            if (payload is not null)
            {
                ApplyRichPayload(editor, payload);
                return source;
            }

            source = DecideSource(
                content with
                {
                    SelectionBytes = null,
                    RichTextBytes = null,
                    XamlPackageBytes = null,
                    RtfBytes = null,
                },
                editor.CanPaste,
                false);
        }

        switch (source)
        {
            case PresentationClipboardPasteSource.Image:
                editor.InsertPicture(content.PngBytes!, "image/png");
                break;
            case PresentationClipboardPasteSource.Text:
                editor.InsertTextBox(content.Text!);
                break;
            case PresentationClipboardPasteSource.Internal:
                editor.Paste();
                break;
        }

        return source;
    }

    private static PresentationClipboardPasteSource DecideSource(
        PresentationClipboardContent content,
        bool internalHasData,
        bool ownCopyIsCurrent) =>
        PresentationClipboardPastePlanner.Decide(
            content.HasSelection,
            content.HasImage,
            content.HasText,
            internalHasData,
            ownCopyIsCurrent,
            content.HasRichText,
            content.HasXamlPackage);

    private static void ApplyRichPayload(
        EditingSession editor,
        InCanvasRichClipboardPayload payload)
    {
        var images = payload.GetImagePayloads();
        var objects = payload.GetObjectPayloads();
        foreach (var image in images)
            editor.InsertPicture(image.Bytes, image.ContentType, image.WidthEmu, image.HeightEmu);
        foreach (var obj in objects)
            editor.InsertEmbeddedObject(obj.Bytes, obj.FileName, obj.ClassName);

        var slideBody = images.Count > 0 || objects.Count > 0
            ? InCanvasRichClipboardPlanner.CloneBodyForSlideFallback(payload.Body)
            : payload.Body;
        var table = payload.ContainsTable
            ? editor.InsertTableFromClipboard(
                slideBody,
                payload.TableColumnWidthsEmu,
                payload.TableCellStyles)
            : null;
        if (table is null
            && !string.IsNullOrWhiteSpace(InCanvasTextEditPlanner.ExtractPlainText(slideBody)))
        {
            editor.InsertTextBox(slideBody);
        }
    }

    private static PresentationClipboardSelectionSnapshot CaptureSelection(EditingSession editor) =>
        new(editor.CurrentSlideIndex, editor.SelectedShapeIds.ToArray());

    private static void RestoreSelection(
        EditingSession editor,
        PresentationClipboardSelectionSnapshot selection)
    {
        if (editor.CurrentSlideIndex != selection.SlideIndex)
            editor.SelectSlide(selection.SlideIndex);
        else
            editor.ClearSelection();

        foreach (var shapeId in selection.SelectedShapeIds)
        {
            if (editor.CurrentSlide is { } slide
                && SlideShapeTraversal.FindById(slide, shapeId) is not null)
            {
                editor.Select(shapeId, addToSelection: true);
            }
        }
    }
}

public static class PresentationClipboardContentIdentity
{
    public static string Compute(
        PresentationClipboardContent content,
        Func<byte[]?, byte[]?>? normalizePng = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendBytes(hash, content.SelectionBytes);
        AppendBytes(
            hash,
            normalizePng is null ? content.PngBytes : normalizePng(content.PngBytes));
        AppendBytes(hash, content.Text is null ? null : Encoding.UTF8.GetBytes(content.Text));
        AppendBytes(hash, content.OwnerToken is null
            ? null
            : Encoding.UTF8.GetBytes(content.OwnerToken));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendBytes(IncrementalHash hash, byte[]? bytes)
    {
        Span<byte> length = stackalloc byte[4];
        if (bytes is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class PresentationClipboardOwnershipTracker
{
    private string? _ownerToken;
    private string? _platformIdentity;

    public void RecordSuccessfulWrite(
        PresentationClipboardContent content,
        string? platformIdentity)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrEmpty(content.OwnerToken) || string.IsNullOrEmpty(platformIdentity))
        {
            Invalidate();
            return;
        }

        _ownerToken = content.OwnerToken;
        _platformIdentity = platformIdentity;
    }

    public bool HasCurrentPlatformIdentity(string? platformIdentity) =>
        !string.IsNullOrEmpty(_ownerToken)
        && !string.IsNullOrEmpty(_platformIdentity)
        && string.Equals(_platformIdentity, platformIdentity, StringComparison.Ordinal);

    public bool IsCurrent(
        PresentationClipboardContent content,
        string? platformIdentity,
        bool internalHasData)
    {
        ArgumentNullException.ThrowIfNull(content);
        return internalHasData
            && HasCurrentPlatformIdentity(platformIdentity)
            && string.Equals(content.OwnerToken, _ownerToken, StringComparison.Ordinal);
    }

    public void Invalidate()
    {
        _ownerToken = null;
        _platformIdentity = null;
    }
}

public sealed class PresentationClipboardOperationQueue
{
    public Task Completion { get; private set; } = Task.CompletedTask;

    public Task Enqueue(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Completion = RunAsync(Completion, operation);
        return Completion;
    }

    private static async Task RunAsync(Task preceding, Func<Task> operation)
    {
        try
        {
            await preceding;
        }
        catch
        {
            // One failed native operation must not prevent later clipboard commands.
        }

        await operation();
    }
}
