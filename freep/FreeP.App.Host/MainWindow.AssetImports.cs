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
            var pickerProfile = request.PickerProfile;
            var result = WpfFileDialogService.ShowOpenDialog(
                pickerProfile.UseUnownedWpfDialog ? null : owner,
                pickerProfile.Wpf.BuildWpfFilter(),
                title: request.PickerTitle);
            var fileName = result.FileName;
            if (!result.Chosen || string.IsNullOrWhiteSpace(fileName))
                return Task.FromResult(PresentationAssetPickerResult.Cancelled);

            return Task.FromResult(PresentationAssetPickerResult.Selected(
                Path.GetFileName(fileName),
                fileName));
        }
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
