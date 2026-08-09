using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind) =>
        ImportPresentationAssetAsync(kind, applyZoomCoverImage: null);

    private async Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind,
        Func<byte[], string, bool>? applyZoomCoverImage)
    {
        var workflow = new PresentationAssetImportWorkflow(
            new AvaloniaPresentationAssetPickerPort(this),
            new AvaloniaPresentationAssetReaderPort(),
            new PresentationAssetImportExecutionPort(
                Editor,
                new PresentationAssetImportExecutionCallbacks(
                    ApplyPictureBullet: ApplyImportedPictureBullet,
                    ApplySmartArtPicture: (bytes, contentType) =>
                        ApplySmartArtTextPanePicture(bytes, contentType)?.Applied == true,
                    ApplyZoomCoverImage: applyZoomCoverImage)));
        return await workflow.ImportAsync(kind);
    }

    private bool ApplyImportedPictureBullet(PresentationPictureBulletPayload payload) =>
        _textEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true ||
        _textEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload) == true ||
        Editor.TryApplyActiveTableCellParagraphPictureBullet(payload);

    private void MaterializePresentationAssetImportResult(
        PresentationAssetImportResult result,
        bool showInsertedStatus = false,
        string? successStatus = null)
    {
        switch (result.Status)
        {
            case PresentationAssetImportStatus.Succeeded when successStatus is not null:
                _statusText.Text = successStatus;
                break;
            case PresentationAssetImportStatus.Succeeded when showInsertedStatus && result.SourceName is not null:
                _statusText.Text = SisterAppFileTextPlanner.FormatInserted(FileText, result.SourceName);
                break;
            case PresentationAssetImportStatus.Unavailable:
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                    FileText,
                    result.Request.CommandName);
                break;
            case PresentationAssetImportStatus.Failed:
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                    FileText,
                    result.Request.CommandName,
                    result.Message ?? string.Empty);
                break;
        }
    }

    private sealed class AvaloniaPresentationAssetPickerPort(MainWindow owner) : IPresentationAssetPickerPort
    {
        public async Task<PresentationAssetPickerResult> PickAsync(
            PresentationAssetImportRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AvaloniaFilePickerService.CanOpen(owner.StorageProvider))
            {
                return PresentationAssetPickerResult.Unavailable(
                    $"{request.CommandName} is unavailable because this platform cannot open files.");
            }

            var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
                owner.StorageProvider,
                AvaloniaFilePickerOpenRequest.FromFileTypes(
                    request.PickerTitle,
                    [ResolveFileType(request.Kind)]));
            return file is null
                ? PresentationAssetPickerResult.Cancelled
                : PresentationAssetPickerResult.Selected(file.Name, file);
        }

        private static FilePickerFileType ResolveFileType(PresentationAssetImportKind kind) =>
            kind switch
            {
                PresentationAssetImportKind.Video => VideoFileType,
                PresentationAssetImportKind.Audio or PresentationAssetImportKind.TransitionSound => AudioFileType,
                PresentationAssetImportKind.EmbeddedObject => EmbeddedObjectFileType,
                PresentationAssetImportKind.Picture
                    or PresentationAssetImportKind.PictureBullet
                    or PresentationAssetImportKind.SmartArtPicture
                    or PresentationAssetImportKind.ZoomCoverImage => PictureFileType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
    }

    private sealed class AvaloniaPresentationAssetReaderPort : IPresentationAssetReaderPort
    {
        public async Task<byte[]> ReadAsync(
            PresentationAssetSelection selection,
            CancellationToken cancellationToken)
        {
            if (selection.Source is not IStorageFile file)
                throw new InvalidOperationException("The selected presentation asset is not an Avalonia storage file.");

            try
            {
                await using var source = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await source.CopyToAsync(memory, cancellationToken);
                return memory.ToArray();
            }
            finally
            {
                file.Dispose();
            }
        }
    }
}
