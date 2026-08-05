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

        var crop = PresentationClipboardShapeCropPlanner.Plan(
            presentation,
            shapes,
            WidthPx,
            HeightPx);
        if (crop.IsFullFrame(WidthPx, HeightPx))
            return Save(full);

        var cropped = new CroppedBitmap(
            full,
            new PixelRect(crop.X, crop.Y, crop.Width, crop.Height));
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
    private readonly PresentationClipboardOwnershipTracker _ownership = new();

    public Task<bool> CopyAsync(EditingSession editor) =>
        ExecuteCopyAsync(PrepareWrite(editor));

    public Task<bool> CutAsync(EditingSession editor) =>
        ExecuteCutAsync(PrepareWrite(editor));

    public Task<PresentationClipboardPasteSource> PasteAsync(EditingSession editor) =>
        ExecutePasteAsync(PreparePaste(editor));

    internal PresentationClipboardWriteRequest PrepareWrite(EditingSession editor) =>
        PresentationClipboardWorkflow.PrepareWrite(
            editor,
            (presentation, slide, shapes) => renderer.RenderSelection(
                presentation,
                slide,
                shapes),
            Guid.NewGuid().ToString("N"));

    internal PresentationClipboardPasteRequest PreparePaste(EditingSession editor) =>
        PresentationClipboardWorkflow.PreparePaste(editor);

    internal async Task<bool> ExecuteCopyAsync(PresentationClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PresentationClipboardWorkflow.CommitCopy(request);
        return request.Content is not null && await TryWriteAsync(request.Content);
    }

    internal async Task<bool> ExecuteCutAsync(PresentationClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Start exporting while the source selection still exists. The cut mutation is then
        // committed before awaiting the OS clipboard so a later user selection is not restored
        // over the top of the UI after an asynchronous write.
        var writeTask = request.Content is not null
            ? TryWriteAsync(request.Content)
            : Task.FromResult(false);

        PresentationClipboardWorkflow.CommitCut(request);
        return await writeTask;
    }

    internal async Task<PresentationClipboardPasteSource> ExecutePasteAsync(
        PresentationClipboardPasteRequest request)
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

        // Avalonia has no portable native clipboard sequence. Hashing every payload returned at
        // this boundary, with PNG normalization, proves the observed content still matches the
        // last successful write; an exact replay of all payloads remains indistinguishable.
        var contentIdentity = PresentationClipboardContentIdentity.Compute(content, NormalizePng);
        var ownCopy = _ownership.IsCurrent(content, contentIdentity, request.Editor.CanPaste);
        return PresentationClipboardWorkflow.ApplyPaste(request, content, ownCopy);
    }

    private async Task<bool> TryWriteAsync(PresentationClipboardContent content)
    {
        try
        {
            await systemClipboard.WriteAsync(content);
            _ownership.RecordSuccessfulWrite(
                content,
                PresentationClipboardContentIdentity.Compute(content, NormalizePng));
            return true;
        }
        catch
        {
            _ownership.Invalidate();
            return false;
        }
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
