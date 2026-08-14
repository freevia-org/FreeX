using Avalonia.Platform.Storage;
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
                    ApplyZoomCoverImage: applyZoomCoverImage,
                    EmbeddedObjectInserted: () =>
                    {
                        RefreshCanvas();
                        UpdateStatus();
                    })));
        return await workflow.ImportAsync(kind);
    }

    private bool ApplyImportedPictureBullet(PresentationPictureBulletPayload payload) =>
        _textEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true ||
        _textEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload) == true ||
        Editor.TryApplyActiveTableCellParagraphPictureBullet(payload);

    private async ValueTask MaterializePresentationAssetImportResultAsync(
        PresentationAssetImportResult result,
        PresentationAssetImportOutcomePolicy policy,
        Action<string>? statusTarget = null)
    {
        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            result,
            FileText,
            policy);
        if (presentation.StatusText is { } statusText)
        {
            if (statusTarget is null)
                _statusText.Text = statusText;
            else
                statusTarget(statusText);
        }

        if (presentation.Message is { } message)
        {
            var messageService = _messageService ?? new AvaloniaUserMessageService(this);
            await messageService.ShowMessageAsync(message);
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
