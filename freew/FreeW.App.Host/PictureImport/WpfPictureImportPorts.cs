using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.Core.Model;

namespace FreeW.App.Host;

internal sealed class WpfPictureImportPickerPort(Window? owner) : IFreeWPictureImportPickerPort
{
    public Task<FreeWPictureImportPickerResult> PickAsync(
        FreeWPictureImportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = WpfFileDialogService.ShowOpenDialog(
            owner,
            request.PickerPlan.BuildWpfFilter(),
            title: request.PickerPlan.Title);
        return Task.FromResult(
            result.Chosen && !string.IsNullOrWhiteSpace(result.FileName)
                ? FreeWPictureImportPickerResult.Selected(
                    Path.GetFileName(result.FileName),
                    result.FileName)
                : FreeWPictureImportPickerResult.Cancelled);
    }
}

internal sealed class WpfPictureImportSourceReaderPort : IFreeWPictureImportSourceReaderPort
{
    public Task<byte[]> ReadAsync(
        FreeWPictureImportSelection selection,
        CancellationToken cancellationToken) =>
        FileByteReadWorkflow.ReadLocalPathBytesAsync(
            (string)selection.Source,
            cancellationToken);
}

internal sealed class WpfPictureDecoderPort : IFreeWPictureDecoderPort
{
    public ValueTask<FreeWPictureDecoderFacts> DecodeAsync(
        FreeWPictureImportSelection selection,
        byte[] bytes,
        CancellationToken cancellationToken) =>
        FreeWPictureDecoderPolicy.DecodeOrUnavailable(cancellationToken, () =>
        {
            using var source = new MemoryStream(bytes, writable: false);
            var frame = BitmapFrame.Create(
                source,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return new FreeWPictureDecoderFacts(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.DpiX,
                frame.DpiY);
        });
}

internal sealed class WpfPictureRasterizerPort : IFreeWPictureRasterizerPort
{
    public ValueTask<FreeWPictureRasterizationOutcome> RasterizeAsync(
        FreeWPictureRasterizationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.SourceKind == FreeWPictureImportSourceKind.Svg)
        {
            using var source = new MemoryStream(request.SourceBytes, writable: false);
            var image = SvgRasterizerHelper.RasterizeToInlineImage(
                source,
                request.MaximumPixelEdge);
            return ValueTask.FromResult(new FreeWPictureRasterizationOutcome(
                image.Bytes,
                new FreeWPictureDecoderFacts(
                    image.OriginalPixelWidth,
                    image.OriginalPixelHeight,
                    96,
                    96)));
        }

        using var input = new MemoryStream(request.SourceBytes, writable: false);
        var frame = BitmapFrame.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        using var output = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(frame);
        encoder.Save(output);
        return ValueTask.FromResult(new FreeWPictureRasterizationOutcome(
            output.ToArray(),
            new FreeWPictureDecoderFacts(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.DpiX,
                frame.DpiY)));
    }
}

internal sealed class WpfPictureInsertionPort(DocumentView editor) : IFreeWPictureInsertionPort
{
    public FreeWPictureInsertionResult Insert(FreeWPictureInsertionRequest request)
    {
        editor.Focus();
        editor.InsertImage(new InlineImage(
            request.Bytes,
            request.WidthPt,
            request.HeightPt,
            request.Format)
        {
            OriginalPixelWidth = request.OriginalPixelWidth,
            OriginalPixelHeight = request.OriginalPixelHeight,
        });
        return FreeWPictureInsertionResult.Success;
    }
}
