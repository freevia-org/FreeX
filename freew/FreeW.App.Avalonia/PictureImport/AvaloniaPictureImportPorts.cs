using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Free.Shared.IO;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentFragments;

namespace FreeW.App.Avalonia;

internal sealed class AvaloniaPictureImportPickerPort(IStorageProvider storageProvider)
    : IFreeWPictureImportPickerPort
{
    public async Task<FreeWPictureImportPickerResult> PickAsync(
        FreeWPictureImportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AvaloniaFilePickerService.CanOpen(storageProvider))
        {
            return FreeWPictureImportPickerResult.Unavailable(
                $"{request.CommandName} is unavailable because this platform cannot open files.");
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            storageProvider,
            AvaloniaFilePickerOpenRequest.FromDescriptors(
                request.PickerPlan.Title,
                request.PickerPlan.FileTypes));
        return file is null
            ? FreeWPictureImportPickerResult.Cancelled
            : FreeWPictureImportPickerResult.Selected(file.Name, file);
    }
}

internal sealed class AvaloniaPictureImportSourceReaderPort : IFreeWPictureImportSourceReaderPort
{
    public async Task<byte[]> ReadAsync(
        FreeWPictureImportSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Source is not IStorageFile file)
            throw new InvalidOperationException("The selected picture is not an Avalonia storage file.");

        try
        {
            return await FileByteReadWorkflow.ReadStreamBytesAsync(
                file.OpenReadAsync,
                cancellationToken);
        }
        finally
        {
            file.Dispose();
        }
    }
}

internal sealed class AvaloniaPictureDecoderPort : IFreeWPictureDecoderPort
{
    public ValueTask<FreeWPictureDecoderFacts> DecodeAsync(
        FreeWPictureImportSelection selection,
        byte[] bytes,
        CancellationToken cancellationToken) =>
        FreeWPictureDecoderPolicy.DecodeOrUnavailable(cancellationToken, () =>
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var bitmap = new Bitmap(source);
            return new FreeWPictureDecoderFacts(
                bitmap.PixelSize.Width,
                bitmap.PixelSize.Height,
                bitmap.Dpi.X,
                bitmap.Dpi.Y);
        });
}

internal sealed class AvaloniaPictureRasterizerPort : IFreeWPictureRasterizerPort
{
    public ValueTask<FreeWPictureRasterizationOutcome> RasterizeAsync(
        FreeWPictureRasterizationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            request.SourceKind == FreeWPictureImportSourceKind.Svg
                ? RasterizeSvg(request)
                : RasterizeNativeBitmap(request.SourceBytes));
    }

    private static FreeWPictureRasterizationOutcome RasterizeSvg(
        FreeWPictureRasterizationRequest request)
    {
        using var temporaryFile = TemporaryFileLease.Create("freew_picture_", ".svg");
        using (var output = temporaryFile.OpenWrite())
            output.Write(request.SourceBytes);

        var drawing = SvgIconRasterizer.LoadFileToPaintedBounds(temporaryFile.Path);
        var drawingSize = drawing.Size;
        var sourceWidth = drawingSize.Width > 0 ? drawingSize.Width : request.MaximumPixelEdge;
        var sourceHeight = drawingSize.Height > 0 ? drawingSize.Height : request.MaximumPixelEdge;
        var scale = request.MaximumPixelEdge / Math.Max(sourceWidth, sourceHeight);
        var pixelWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        var image = new Image
        {
            Source = drawing,
            Width = pixelWidth,
            Height = pixelHeight,
            Stretch = Stretch.Uniform,
        };
        var size = new Size(pixelWidth, pixelHeight);
        image.Measure(size);
        image.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96));
        bitmap.Render(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return new FreeWPictureRasterizationOutcome(
            stream.ToArray(),
            new FreeWPictureDecoderFacts(pixelWidth, pixelHeight, 96, 96));
    }

    private static FreeWPictureRasterizationOutcome RasterizeNativeBitmap(byte[] sourceBytes)
    {
        using var source = new MemoryStream(sourceBytes, writable: false);
        using var bitmap = new Bitmap(source);
        using var output = new MemoryStream();
        bitmap.Save(output);
        return new FreeWPictureRasterizationOutcome(
            output.ToArray(),
            new FreeWPictureDecoderFacts(
                bitmap.PixelSize.Width,
                bitmap.PixelSize.Height,
                bitmap.Dpi.X,
                bitmap.Dpi.Y));
    }
}

internal sealed class AvaloniaPictureInsertionPort(DocumentView editor) : IFreeWPictureInsertionPort
{
    public FreeWPictureInsertionResult Insert(FreeWPictureInsertionRequest request)
    {
        editor.InsertInlineImage(
            request.Bytes,
            request.WidthPt,
            request.HeightPt,
            request.Format,
            request.OriginalPixelWidth,
            request.OriginalPixelHeight);
        editor.Focus();
        return FreeWPictureInsertionResult.Success;
    }
}
