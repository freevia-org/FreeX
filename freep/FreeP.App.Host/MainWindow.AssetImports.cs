using System.IO;
using System.Windows;
using Free.Shared.Shell;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

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
            new WpfPresentationAssetPickerPort(this),
            new WpfPresentationAssetReaderPort(),
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
                        UpdateSlideCount();
                    })));
        return await workflow.ImportAsync(kind);
    }

    private bool ApplyImportedPictureBullet(PresentationPictureBulletPayload payload)
    {
        if (SlideCanvas.TextEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true)
            return true;

        return Editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
    }

    private sealed class WpfPresentationAssetPickerPort(Window owner) : IPresentationAssetPickerPort
    {
        public Task<PresentationAssetPickerResult> PickAsync(
            PresentationAssetImportRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = WpfFileDialogService.ShowOpenDialog(
                UsesUnownedDialog(request.Kind) ? null : owner,
                BuildFilter(request.Kind),
                title: request.PickerTitle);
            var fileName = result.FileName;
            if (!result.Chosen || string.IsNullOrWhiteSpace(fileName))
                return Task.FromResult(PresentationAssetPickerResult.Cancelled);

            return Task.FromResult(PresentationAssetPickerResult.Selected(
                Path.GetFileName(fileName),
                fileName));
        }

        private static bool UsesUnownedDialog(PresentationAssetImportKind kind) =>
            kind is PresentationAssetImportKind.Picture
                or PresentationAssetImportKind.Video
                or PresentationAssetImportKind.Audio
                or PresentationAssetImportKind.PictureBullet;

        private static string BuildFilter(PresentationAssetImportKind kind) =>
            kind switch
            {
                PresentationAssetImportKind.Picture =>
                    "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg;*.wmf;*.emf|All files|*.*",
                PresentationAssetImportKind.Video =>
                    $"{PresentationFileTextResources.VideoFileTypeName}|*.mp4;*.mov;*.avi;*.wmv;*.m4v|All files|*.*",
                PresentationAssetImportKind.Audio =>
                    $"{PresentationFileTextResources.AudioFileTypeName}|*.mp3;*.m4a;*.wav;*.wma|All files|*.*",
                PresentationAssetImportKind.TransitionSound =>
                    PresentationMediaFileTypeCatalog.BuildWpfAudioFilter(),
                PresentationAssetImportKind.EmbeddedObject =>
                    "Office files|*.xlsx;*.xlsm;*.xls;*.docx;*.doc;*.pptx;*.ppt|All files|*.*",
                PresentationAssetImportKind.PictureBullet =>
                    "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg|All files|*.*",
                PresentationAssetImportKind.SmartArtPicture =>
                    "Picture files|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp|All files|*.*",
                PresentationAssetImportKind.ZoomCoverImage =>
                    "Picture files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg;*.webp|All files|*.*",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
    }

    private sealed class WpfPresentationAssetReaderPort : IPresentationAssetReaderPort
    {
        public Task<byte[]> ReadAsync(
            PresentationAssetSelection selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(File.ReadAllBytes((string)selection.Source));
        }
    }
}
