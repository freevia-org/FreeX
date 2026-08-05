using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal interface IPresentationSystemClipboard
{
    Task WriteAsync(PresentationClipboardContent content);
    Task<PresentationClipboardContent> ReadAsync();
}

internal interface IPresentationClipboardShapeRenderer
{
    byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes);
}

internal sealed class AvaloniaPresentationSystemClipboard(Func<IClipboard?> getClipboard)
    : IPresentationSystemClipboard
{
    internal static readonly DataFormat<byte[]> SelectionFormat =
        DataFormat.CreateBytesApplicationFormat(PresentationClipboardFormats.Selection);
    internal static readonly DataFormat<string> OwnerTokenFormat =
        DataFormat.CreateStringApplicationFormat(PresentationClipboardFormats.OwnerToken);
    internal static readonly DataFormat<byte[]> SelectionPlatformFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.Selection);
    internal static readonly DataFormat<string> OwnerTokenPlatformFormat =
        DataFormat.CreateStringPlatformFormat(PresentationClipboardFormats.OwnerToken);
    internal static readonly DataFormat<byte[]> RichTextFormat =
        DataFormat.CreateBytesApplicationFormat(PresentationClipboardFormats.RichText);
    internal static readonly DataFormat<byte[]> RichTextPlatformFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.RichText);
    internal static readonly DataFormat<byte[]> ExternalXamlPackageWindowsFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.WindowsXamlPackage);
    internal static readonly DataFormat<byte[]> ExternalXamlPackageLinuxFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.LinuxXamlPackage);
    internal static readonly DataFormat<byte[]> ExternalRtfWindowsFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.WindowsRtf);
    internal static readonly DataFormat<byte[]> ExternalRtfLinuxFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.LinuxRtf);

    public async Task WriteAsync(PresentationClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var clipboard = getClipboard()
            ?? throw new InvalidOperationException("The window does not have a system clipboard.");

        var transfer = BuildDataTransfer(content, out var bitmap);
        try
        {
            await clipboard.SetDataAsync(transfer);
        }
        catch
        {
            bitmap?.Dispose();
            ((IDisposable)transfer).Dispose();
            throw;
        }

        // SetDataAsync transfers ownership. Flush is useful on Windows and a no-op on
        // other supported hosts; failure here must not invalidate a successful write.
        try
        {
            await clipboard.FlushAsync();
        }
        catch
        {
        }
    }

    internal static DataTransfer BuildDataTransfer(
        PresentationClipboardContent content,
        out Bitmap? bitmap)
    {
        var item = new DataTransferItem();
        if (OperatingSystem.IsWindows())
        {
            // Public system names let WPF and Avalonia exchange raw clipboard payloads
            // without depending on Avalonia's private application-format prefix.
            item.Set(SelectionPlatformFormat, content.SelectionBytes);
            item.Set(OwnerTokenPlatformFormat, content.OwnerToken);
            if (content.RichTextBytes is { Length: > 0 })
                item.Set(RichTextPlatformFormat, content.RichTextBytes);
            if (content.XamlPackageBytes is { Length: > 0 })
                item.Set(ExternalXamlPackageWindowsFormat, content.XamlPackageBytes);
        }
        else
        {
            item.Set(SelectionFormat, content.SelectionBytes);
            item.Set(OwnerTokenFormat, content.OwnerToken);
            if (content.RichTextBytes is { Length: > 0 })
                item.Set(RichTextFormat, content.RichTextBytes);
            if (content.XamlPackageBytes is { Length: > 0 })
                item.Set(ExternalXamlPackageLinuxFormat, content.XamlPackageBytes);
        }
        item.SetText(content.Text);
        bitmap = null;
        if (content.PngBytes is { Length: > 0 })
        {
            try
            {
                bitmap = new Bitmap(new MemoryStream(content.PngBytes, writable: false));
                item.SetBitmap(bitmap);
            }
            catch
            {
                // Keep the native selection and text formats when image decoding fails.
            }
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    public async Task<PresentationClipboardContent> ReadAsync()
    {
        var clipboard = getClipboard()
            ?? throw new InvalidOperationException("The window does not have a system clipboard.");
        using var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
            return new PresentationClipboardContent();

        return await ReadDataTransferAsync(transfer);
    }

    internal static async Task<PresentationClipboardContent> ReadDataTransferAsync(
        IAsyncDataTransfer transfer)
    {
        byte[]? selection = null;
        string? ownerToken = null;
        byte[]? richText = null;
        byte[]? xamlPackage = null;
        string? text = null;
        selection = await TryGetValueAsync(transfer, SelectionPlatformFormat)
            ?? await TryGetValueAsync(transfer, SelectionFormat);
        ownerToken = await TryGetValueAsync(transfer, OwnerTokenPlatformFormat)
            ?? await TryGetValueAsync(transfer, OwnerTokenFormat);
        richText = await TryGetValueAsync(transfer, RichTextPlatformFormat)
            ?? await TryGetValueAsync(transfer, RichTextFormat);
        var rtf = await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows() ? ExternalRtfWindowsFormat : ExternalRtfLinuxFormat)
            ?? await TryGetValueAsync(
                transfer,
                OperatingSystem.IsWindows() ? ExternalRtfLinuxFormat : ExternalRtfWindowsFormat);
        xamlPackage = await TryGetValueAsync(transfer, ExternalXamlPackageWindowsFormat)
            ?? await TryGetValueAsync(transfer, ExternalXamlPackageLinuxFormat);
        try { text = await transfer.TryGetTextAsync(); }
        catch { }

        byte[]? png = null;
        try
        {
            using var bitmap = await transfer.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                png = stream.ToArray();
            }
        }
        catch
        {
        }

        return new PresentationClipboardContent(selection, png, text, ownerToken, richText, xamlPackage, rtf);
    }

    private static async Task<T?> TryGetValueAsync<T>(
        IAsyncDataTransfer transfer,
        DataFormat<T> format)
        where T : class
    {
        try
        {
            return await transfer.TryGetValueAsync(format);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class AvaloniaClipboardShapeRenderer : IPresentationClipboardShapeRenderer
{
    private const int WidthPx = 1280;
    private const int HeightPx = 720;

    public byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes)
    {
        if (shapes.Count == 0)
            return [];

        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = slide,
            SlideIndex = presentation.Slides.IndexOf(slide),
        };
        var fullSize = new Size(WidthPx, HeightPx);
        canvas.Measure(fullSize);
        canvas.Arrange(new Rect(fullSize));

        using var full = new RenderTargetBitmap(new PixelSize(WidthPx, HeightPx));
        full.Render(canvas);

        var crop = CalculateCrop(presentation, shapes);
        if (crop.Width == WidthPx && crop.Height == HeightPx)
            return Save(full);

        var cropped = new CroppedBitmap(full, crop);
        var image = new Image
        {
            Source = cropped,
            Width = crop.Width,
            Height = crop.Height,
            Stretch = Stretch.Fill,
        };
        var cropSize = new Size(crop.Width, crop.Height);
        image.Measure(cropSize);
        image.Arrange(new Rect(cropSize));

        using var output = new RenderTargetBitmap(new PixelSize(crop.Width, crop.Height));
        output.Render(image);
        return Save(output);
    }

    private static PixelRect CalculateCrop(
        Presentation presentation,
        IReadOnlyList<SlideShape> shapes)
    {
        if (presentation.SlideSizeCxEmu <= 0 || presentation.SlideSizeCyEmu <= 0)
            return new PixelRect(0, 0, WidthPx, HeightPx);

        var scaleX = WidthPx / (double)presentation.SlideSizeCxEmu;
        var scaleY = HeightPx / (double)presentation.SlideSizeCyEmu;
        var left = shapes.Min(shape => shape.OffsetXEmu * scaleX);
        var top = shapes.Min(shape => shape.OffsetYEmu * scaleY);
        var right = shapes.Max(shape => (shape.OffsetXEmu + shape.ExtentCxEmu) * scaleX);
        var bottom = shapes.Max(shape => (shape.OffsetYEmu + shape.ExtentCyEmu) * scaleY);

        var x = Math.Clamp((int)Math.Floor(left), 0, WidthPx - 1);
        var y = Math.Clamp((int)Math.Floor(top), 0, HeightPx - 1);
        var width = Math.Clamp((int)Math.Ceiling(right) - x, 1, WidthPx - x);
        var height = Math.Clamp((int)Math.Ceiling(bottom) - y, 1, HeightPx - y);
        return new PixelRect(x, y, width, height);
    }

    private static byte[] Save(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}

internal sealed record PreparedClipboardWrite(
    EditingSession Editor,
    PresentationClipboardContent? Content,
    int SlideIndex,
    IReadOnlyList<uint> SelectedShapeIds);

internal sealed record PreparedClipboardPaste(EditingSession Editor, int SlideIndex);

internal sealed record ClipboardSelectionSnapshot(
    int SlideIndex,
    IReadOnlyList<uint> SelectedShapeIds);

internal sealed class AvaloniaPresentationClipboardService(
    IPresentationSystemClipboard systemClipboard,
    IPresentationClipboardShapeRenderer renderer)
{
    private string? _lastOwnerToken;
    private string? _lastContentIdentity;

    public Task<bool> CopyAsync(EditingSession editor) =>
        ExecuteCopyAsync(PrepareWrite(editor));

    public Task<bool> CutAsync(EditingSession editor) =>
        ExecuteCutAsync(PrepareWrite(editor));

    public Task<PresentationClipboardPasteSource> PasteAsync(EditingSession editor) =>
        ExecutePasteAsync(PreparePaste(editor));

    internal PreparedClipboardWrite PrepareWrite(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var selectedShapeIds = editor.SelectedShapeIds.ToArray();
        var content = Capture(editor);

        return new PreparedClipboardWrite(
            editor,
            content,
            editor.CurrentSlideIndex,
            selectedShapeIds);
    }

    internal PreparedClipboardPaste PreparePaste(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new PreparedClipboardPaste(editor, editor.CurrentSlideIndex);
    }

    internal async Task<bool> ExecuteCopyAsync(PreparedClipboardWrite request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Keep internal clipboard mutation in the same serialized order as the native write.
        var liveSelection = CaptureSelection(request.Editor);
        RestoreSelection(request.Editor, request.SlideIndex, request.SelectedShapeIds);
        request.Editor.CopySelectedShapes();
        RestoreSelection(request.Editor, liveSelection.SlideIndex, liveSelection.SelectedShapeIds);
        return request.Content is not null && await TryWriteAsync(request.Content);
    }

    internal async Task<bool> ExecuteCutAsync(PreparedClipboardWrite request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Start exporting while the source selection still exists. The cut mutation is then
        // committed before awaiting the OS clipboard so a later user selection is not restored
        // over the top of the UI after an asynchronous write.
        var writeTask = request.Content is not null
            ? TryWriteAsync(request.Content)
            : Task.FromResult(false);

        RestoreSelection(request.Editor, request.SlideIndex, request.SelectedShapeIds);
        request.Editor.CopySelectedShapes();
        request.Editor.DeleteSelected();
        return await writeTask;
    }

    internal async Task<PresentationClipboardPasteSource> ExecutePasteAsync(
        PreparedClipboardPaste request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PresentationClipboardContent content;
        try
        {
            content = await systemClipboard.ReadAsync();
        }
        catch
        {
            content = new PresentationClipboardContent();
        }

        if (request.Editor.CurrentSlideIndex != request.SlideIndex)
            request.Editor.SelectSlide(request.SlideIndex);

        // Avalonia has no portable native clipboard sequence. Hashing every payload returned at
        // this boundary, with PNG normalization, proves the observed content still matches the
        // last successful write; an exact replay of all payloads remains indistinguishable.
        var ownCopy = !string.IsNullOrEmpty(_lastOwnerToken)
            && string.Equals(content.OwnerToken, _lastOwnerToken, StringComparison.Ordinal)
            && string.Equals(
                ComputeContentIdentity(content),
                _lastContentIdentity,
                StringComparison.Ordinal)
            && request.Editor.CanPaste;
        var source = PresentationClipboardPastePlanner.Decide(
            content.HasSelection,
            content.HasImage,
            content.HasText,
            request.Editor.CanPaste,
            ownCopy,
            content.HasRichText,
            content.HasXamlPackage);

        if (source == PresentationClipboardPasteSource.NativeSelection)
        {
            try
            {
                var shapes = PresentationClipboardSelectionCodec.Deserialize(content.SelectionBytes!);
                if (shapes.Count > 0)
                {
                    request.Editor.PasteExternalShapes(shapes);
                    return source;
                }
            }
            catch
            {
                // Fall through to the interoperable image/text formats.
            }

            source = PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                hasImage: content.HasImage,
                hasText: content.HasText,
                internalHasData: request.Editor.CanPaste,
                ownCopyIsCurrent: false,
                hasRichText: content.HasRichText,
                hasXamlPackage: content.HasXamlPackage);
        }

        if (source == PresentationClipboardPasteSource.RichText)
        {
            var payload = InCanvasRichClipboardPlanner.Deserialize(content.RichTextBytes)
                ?? ExternalRichTextClipboardPlanner.TryParseRtf(content.RtfBytes);
            if (payload is not null)
            {
                foreach (var image in payload.GetImagePayloads())
                    request.Editor.InsertPicture(image.Bytes, image.ContentType, image.WidthEmu, image.HeightEmu);
                foreach (var obj in payload.GetObjectPayloads())
                    request.Editor.InsertEmbeddedObject(obj.Bytes, obj.FileName, obj.ClassName);
                var slideBody = payload.GetImagePayloads().Count > 0
                    || payload.GetObjectPayloads().Count > 0
                    ? InCanvasRichClipboardPlanner.CloneBodyForSlideFallback(payload.Body)
                    : payload.Body;
                var table = payload.ContainsTable
                    ? request.Editor.InsertTableFromClipboard(
                        slideBody,
                        payload.TableColumnWidthsEmu,
                        payload.TableCellStyles)
                    : null;
                if (table is null
                    && !string.IsNullOrWhiteSpace(InCanvasTextEditPlanner.ExtractPlainText(slideBody)))
                    request.Editor.InsertTextBox(slideBody);
                return source;
            }

            source = PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                hasImage: content.HasImage,
                hasText: content.HasText,
                internalHasData: request.Editor.CanPaste,
                ownCopyIsCurrent: false,
                hasRichText: false,
                hasXamlPackage: content.HasXamlPackage);
        }

        if (source == PresentationClipboardPasteSource.XamlPackage)
        {
            var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(content.XamlPackageBytes);
            if (payload is not null)
            {
                foreach (var image in payload.GetImagePayloads())
                    request.Editor.InsertPicture(image.Bytes, image.ContentType, image.WidthEmu, image.HeightEmu);
                foreach (var obj in payload.GetObjectPayloads())
                    request.Editor.InsertEmbeddedObject(obj.Bytes, obj.FileName, obj.ClassName);
                var slideBody = payload.GetImagePayloads().Count > 0
                    || payload.GetObjectPayloads().Count > 0
                    ? InCanvasRichClipboardPlanner.CloneBodyForSlideFallback(payload.Body)
                    : payload.Body;
                var table = payload.ContainsTable
                    ? request.Editor.InsertTableFromClipboard(
                        slideBody,
                        payload.TableColumnWidthsEmu,
                        payload.TableCellStyles)
                    : null;
                if (table is null
                    && !string.IsNullOrWhiteSpace(InCanvasTextEditPlanner.ExtractPlainText(slideBody)))
                    request.Editor.InsertTextBox(slideBody);
                return source;
            }

            source = PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                hasImage: content.HasImage,
                hasText: content.HasText,
                internalHasData: request.Editor.CanPaste,
                ownCopyIsCurrent: false);
        }

        switch (source)
        {
            case PresentationClipboardPasteSource.Image:
                request.Editor.InsertPicture(content.PngBytes!, "image/png");
                break;
            case PresentationClipboardPasteSource.Text:
                request.Editor.InsertTextBox(content.Text!);
                break;
            case PresentationClipboardPasteSource.Internal:
                request.Editor.Paste();
                break;
        }

        return source;
    }

    private static void RestoreSelection(
        EditingSession editor,
        int slideIndex,
        IReadOnlyList<uint> selectedShapeIds)
    {
        if (editor.CurrentSlideIndex != slideIndex)
            editor.SelectSlide(slideIndex);
        else
            editor.ClearSelection();

        foreach (var shapeId in selectedShapeIds)
        {
            if (editor.CurrentSlide is { } slide && SlideShapeTraversal.FindById(slide, shapeId) is not null)
                editor.Select(shapeId, addToSelection: true);
        }
    }

    private static ClipboardSelectionSnapshot CaptureSelection(EditingSession editor) =>
        new(editor.CurrentSlideIndex, editor.SelectedShapeIds.ToArray());

    private PresentationClipboardContent? Capture(EditingSession editor)
    {
        var ownerToken = Guid.NewGuid().ToString("N");
        return PresentationClipboardContentFactory.CreateSelection(
            editor,
            (presentation, slide, shapes) =>
                renderer.RenderSelection(presentation, slide, shapes),
            ownerToken);
    }

    private async Task<bool> TryWriteAsync(PresentationClipboardContent content)
    {
        try
        {
            await systemClipboard.WriteAsync(content);
            _lastOwnerToken = content.OwnerToken;
            _lastContentIdentity = ComputeContentIdentity(content);
            return true;
        }
        catch
        {
            _lastOwnerToken = null;
            _lastContentIdentity = null;
            return false;
        }
    }

    internal static string ComputeContentIdentity(PresentationClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendBytes(hash, content.SelectionBytes);
        AppendBytes(hash, NormalizePng(content.PngBytes));
        AppendBytes(hash, content.Text is null ? null : Encoding.UTF8.GetBytes(content.Text));
        AppendBytes(hash, content.OwnerToken is null ? null : Encoding.UTF8.GetBytes(content.OwnerToken));
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

    private static byte[]? NormalizePng(byte[]? pngBytes)
    {
        if (pngBytes is not { Length: > 0 })
            return pngBytes;

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(pngBytes, writable: false));
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            // BuildDataTransfer omits an image that Avalonia cannot decode, so it is not part
            // of the native content identity in that case.
            return null;
        }
    }
}
