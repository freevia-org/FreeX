using Avalonia.Platform.Storage;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private PresentationAssetImportHostSession? _assetImportSession;

    private PresentationAssetImportHostSession AssetImportSession =>
        _assetImportSession ??= new PresentationAssetImportHostSession(
            new AvaloniaPresentationAssetPickerPort(this),
            new AvaloniaPresentationAssetReaderPort(),
            Editor,
            new PresentationAssetImportExecutionCallbacks(
                ApplyPictureBullet: ApplyImportedPictureBullet,
                ApplySmartArtPicture: (bytes, contentType) =>
                    ApplySmartArtTextPanePicture(bytes, contentType)?.Applied == true,
                EmbeddedObjectInserted: () =>
                {
                    RefreshCanvas();
                    UpdateStatus();
                }));

    private Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind) =>
        ImportPresentationAssetAsync(kind, applyZoomCoverImage: null);

    private Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind,
        Func<byte[], string, bool>? applyZoomCoverImage) =>
        AssetImportSession.ImportAsync(kind, applyZoomCoverImage);

    private bool ApplyImportedPictureBullet(PresentationPictureBulletPayload payload) =>
        _textEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true ||
        _textEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload) == true ||
        Editor.TryApplyActiveTableCellParagraphPictureBullet(payload);

    private async ValueTask MaterializePresentationAssetImportResultAsync(
        PresentationAssetImportResult result,
        PresentationAssetImportOutcomePolicy policy,
        Action<string>? statusTarget = null)
        => await AssetImportSession.MaterializeOutcomeAsync(
            result,
            FileText,
            policy,
            status => _statusText.Text = status,
            _messageService ?? new AvaloniaUserMessageService(this),
            statusTarget);

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
                    UiText.Format(
                        "File_Error_PlatformPickerUnavailableFormat",
                        request.CommandName));
            }

            var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
                owner.StorageProvider,
                AvaloniaFilePickerOpenRequest.FromFileTypes(
                    request.PickerTitle,
                    [ResolveFileType(request.PickerProfile.Avalonia)]));
            return file is null
                ? PresentationAssetPickerResult.Cancelled
                : PresentationAssetPickerResult.Selected(file.Name, file);
        }

        private static FilePickerFileType ResolveFileType(
            PresentationAssetPickerFileTypeProfile profile) =>
            AvaloniaFilePickerTypeAdapter.CreateFileType(
                profile.DisplayName,
                profile.Patterns,
                profile.MimeTypes);
    }

    private sealed class AvaloniaPresentationAssetReaderPort : IPresentationAssetReaderPort
    {
        public async Task<byte[]> ReadAsync(
            PresentationAssetSelection selection,
            CancellationToken cancellationToken)
        {
            if (selection.Source is not IStorageFile file)
                throw new InvalidOperationException(
                    UiText.Get("File_Error_InvalidAvaloniaAssetSelection"));

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
}
