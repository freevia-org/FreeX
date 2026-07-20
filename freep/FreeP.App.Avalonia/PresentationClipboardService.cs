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
        item.Set(SelectionFormat, content.SelectionBytes);
        item.Set(OwnerTokenFormat, content.OwnerToken);
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
        string? text = null;
        try { selection = await transfer.TryGetValueAsync(SelectionFormat); }
        catch { }
        try { ownerToken = await transfer.TryGetValueAsync(OwnerTokenFormat); }
        catch { }
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

        return new PresentationClipboardContent(selection, png, text, ownerToken);
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

internal sealed class AvaloniaPresentationClipboardService(
    IPresentationSystemClipboard systemClipboard,
    IPresentationClipboardShapeRenderer renderer)
{
    private string? _lastOwnerToken;

    public async Task<bool> CopyAsync(EditingSession editor)
    {
        var content = Capture(editor);
        editor.CopySelectedShapes();
        return content is not null && await TryWriteAsync(content);
    }

    public async Task<bool> CutAsync(EditingSession editor)
    {
        // Capture before DeleteSelected clears the selection. The internal clipboard is
        // populated even when the OS clipboard is unavailable, matching WPF semantics.
        var content = Capture(editor);
        editor.CopySelectedShapes();
        editor.DeleteSelected();
        return content is not null && await TryWriteAsync(content);
    }

    public async Task<PresentationClipboardPasteSource> PasteAsync(EditingSession editor)
    {
        PresentationClipboardContent content;
        try
        {
            content = await systemClipboard.ReadAsync();
        }
        catch
        {
            content = new PresentationClipboardContent();
        }

        var ownCopy = !string.IsNullOrEmpty(_lastOwnerToken)
            && string.Equals(content.OwnerToken, _lastOwnerToken, StringComparison.Ordinal)
            && editor.CanPaste;
        var source = PresentationClipboardPastePlanner.Decide(
            content.HasSelection,
            content.HasImage,
            content.HasText,
            editor.CanPaste,
            ownCopy);

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
                // Fall through to the interoperable image/text formats.
            }

            source = PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                content.HasImage,
                content.HasText,
                editor.CanPaste,
                ownCopyIsCurrent: false);
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
            return true;
        }
        catch
        {
            _lastOwnerToken = null;
            return false;
        }
    }
}
