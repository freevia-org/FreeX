using System.IO;
using Free.Shared.Shell;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    private PresentationAssetImportHostSession? _assetImportSession;

    private PresentationAssetImportHostSession AssetImportSession =>
        _assetImportSession ??= new PresentationAssetImportHostSession(
            new PresentationAssetPickerAdapter(PickPresentationAssetAsync),
            new PresentationAssetReaderAdapter<string>(
                FileByteReadWorkflow.ReadLocalPathBytesAsync),
            Editor,
            new PresentationAssetImportExecutionCallbacks(
                ApplyPictureBullet: ApplyImportedPictureBullet,
                ApplySmartArtPicture: (bytes, contentType) =>
                    ApplySmartArtTextPanePicture(bytes, contentType)?.Applied == true,
                EmbeddedObjectInserted: () =>
                {
                    RefreshCanvas();
                    UpdateSlideCount();
                }));

    private Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind) =>
        ImportPresentationAssetAsync(kind, applyZoomCoverImage: null);

    private Task<PresentationAssetImportResult> ImportPresentationAssetAsync(
        PresentationAssetImportKind kind,
        Func<byte[], string, bool>? applyZoomCoverImage) =>
        AssetImportSession.ImportAsync(kind, applyZoomCoverImage);

    private bool ApplyImportedPictureBullet(PresentationPictureBulletPayload payload)
    {
        if (SlideCanvas.TextEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true)
            return true;

        if (SlideCanvas.TableCellEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload) == true)
            return true;

        return Editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
    }

    private async ValueTask MaterializePresentationAssetImportResultAsync(
        PresentationAssetImportResult result,
        PresentationAssetImportOutcomePolicy policy,
        Action<string>? statusTarget = null)
        => await AssetImportSession.MaterializeOutcomeAsync(
            result,
            PresentationFileTextResources.Presentation,
            policy,
            status => _slideCountText.Text = status,
            _messageService ?? new WpfUserMessageService(this),
            statusTarget);

    private Task<PresentationAssetPickerResult> PickPresentationAssetAsync(
        PresentationAssetImportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pickerProfile = request.PickerProfile;
        var result = WpfFileDialogService.ShowOpenDialog(
            pickerProfile.UseUnownedWpfDialog ? null : this,
            pickerProfile.Wpf.BuildWpfFilter(),
            title: request.PickerTitle);
        var fileName = result.FileName;
        return !result.Chosen || string.IsNullOrWhiteSpace(fileName)
            ? Task.FromResult(PresentationAssetPickerResult.Cancelled)
            : Task.FromResult(PresentationAssetPickerResult.Selected(
                Path.GetFileName(fileName),
                fileName));
    }
}
