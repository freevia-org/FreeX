using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

// Compatibility name retained because MainWindow.cs is owned by another worker. It adds no
// product methods; all clipboard consumers use the shared contract.
internal interface IPresentationSystemClipboard : IPlatformClipboard
{
}

internal interface IPresentationClipboardShapeRenderer
{
    byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes);
}

internal sealed class AvaloniaPresentationSystemClipboard(Func<global::Avalonia.Input.Platform.IClipboard?> getClipboard)
    : IPresentationSystemClipboard
{
    private readonly AvaloniaPlatformClipboard _inner = new(getClipboard);

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

    public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(request, cancellationToken);

    public ValueTask<PlatformClipboardWriteResult> WriteAsync(
        PlatformClipboardContent content,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(content, cancellationToken);

    public ValueTask<PlatformClipboardWriteResult> ClearAsync(
        CancellationToken cancellationToken = default) =>
        _inner.ClearAsync(cancellationToken);

    internal static DataTransfer BuildDataTransfer(
        PresentationClipboardContent content,
        out Bitmap? bitmap)
    {
        var scope = OperatingSystem.IsWindows()
            ? PlatformClipboardFormatScope.Platform
            : PlatformClipboardFormatScope.Application;
        var xamlFormat = OperatingSystem.IsWindows()
            ? PresentationClipboardFormats.WindowsXamlPackage
            : PresentationClipboardFormats.LinuxXamlPackage;
        return AvaloniaPlatformClipboard.BuildDataTransfer(
            PresentationClipboardPlatformMapper.ToPlatformContent(content, scope, xamlFormat),
            out bitmap);
    }

    internal static async Task<PresentationClipboardContent> ReadDataTransferAsync(
        IAsyncDataTransfer transfer)
    {
        var read = await AvaloniaPlatformClipboard.ReadDataTransferAsync(
            transfer,
            PresentationClipboardPlatformMapper.ReadRequest);
        return read.Status == PlatformClipboardReadStatus.Success
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();
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
    IPlatformClipboard systemClipboard,
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
        var read = await systemClipboard.ReadAsync(PresentationClipboardPlatformMapper.ReadRequest);
        var content = read.Status == PlatformClipboardReadStatus.Success
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();

        var contentIdentity = PresentationClipboardContentIdentity.Compute(content, NormalizePng);
        var ownCopy = _ownership.IsCurrent(content, contentIdentity, request.Editor.CanPaste);
        return PresentationClipboardWorkflow.ApplyPaste(request, content, ownCopy);
    }

    private async Task<bool> TryWriteAsync(PresentationClipboardContent content)
    {
        var scope = OperatingSystem.IsWindows()
            ? PlatformClipboardFormatScope.Platform
            : PlatformClipboardFormatScope.Application;
        var xamlFormat = OperatingSystem.IsWindows()
            ? PresentationClipboardFormats.WindowsXamlPackage
            : PresentationClipboardFormats.LinuxXamlPackage;
        var result = await systemClipboard.WriteAsync(
            PresentationClipboardPlatformMapper.ToPlatformContent(content, scope, xamlFormat));
        if (result.IsSuccess)
        {
            _ownership.RecordSuccessfulWrite(
                content,
                PresentationClipboardContentIdentity.Compute(content, NormalizePng));
            return true;
        }

        _ownership.Invalidate();
        return false;
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
            return null;
        }
    }
}
