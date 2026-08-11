using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private sealed class AvaloniaPresentationFilePickerPort : IPresentationFilePickerPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationFilePickerPort(MainWindow owner) => _owner = owner;

        public async Task<PresentationFilePickerResult> PickOpenFileAsync(
            PresentationFileOpenPickerRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_owner._openPickerOverrideForTests is { } pickerOverride)
            {
                var overriddenPath = await pickerOverride(request.PickerPlan);
                return overriddenPath is null
                    ? PresentationFilePickerResult.Cancelled
                    : PresentationFilePickerResult.Selected(overriddenPath);
            }

            if (!AvaloniaFilePickerService.CanOpen(_owner.StorageProvider))
            {
                return PresentationFilePickerResult.Unavailable(
                    SisterAppFileTextPlanner.FormatCommandUnavailable(FileText, FileText.OpenCommand));
            }

            using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
                _owner.StorageProvider,
                AvaloniaFilePickerOpenRequest.FromDescriptors(request.Title, request.PickerPlan.FileTypes));
            if (file is null)
                return PresentationFilePickerResult.Cancelled;

            return file.LocalPath is { } path
                ? PresentationFilePickerResult.Selected(path)
                : PresentationFilePickerResult.NonLocal(
                    SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(FileText, FileText.OpenCommand));
        }

        public async Task<PresentationFilePickerResult> PickSaveFileAsync(
            PresentationFileSavePickerRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Command == PresentationFileCommand.SaveAs &&
                _owner._savePickerOverrideForTests is { } saveOverride)
            {
                var overriddenPath = await saveOverride(request.PickerPlan);
                return overriddenPath is null
                    ? PresentationFilePickerResult.Cancelled
                    : PresentationFilePickerResult.Selected(overriddenPath);
            }

            if (request.Command == PresentationFileCommand.ExportVideo &&
                _owner.VideoPickerOverrideForTests is { } videoOverride)
            {
                var selection = await videoOverride(request.PickerPlan);
                if (selection is null)
                    return PresentationFilePickerResult.Cancelled;
                return selection.LocalPath is { } selectedPath
                    ? PresentationFilePickerResult.Selected(selectedPath)
                    : PresentationFilePickerResult.NonLocal(
                        SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                            FileText,
                            PresentationExportPlanner.VideoExportCommandText));
            }

            if (!AvaloniaFilePickerService.CanSave(_owner.StorageProvider))
            {
                return PresentationFilePickerResult.Unavailable(
                    SisterAppFileTextPlanner.FormatCommandUnavailable(
                        FileText,
                        PresentationNativeCommandOutcomePlanner.CommandText(request.Command)));
            }

            using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
                _owner.StorageProvider,
                AvaloniaFilePickerSaveRequest.FromSavePlan(
                    request.Title,
                    request.PickerPlan,
                    request.ShowOverwritePrompt));
            if (file is null)
                return PresentationFilePickerResult.Cancelled;

            return file.LocalPath is { } path
                ? PresentationFilePickerResult.Selected(path)
                : PresentationFilePickerResult.NonLocal(
                    SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                        FileText,
                        PresentationNativeCommandOutcomePlanner.CommandText(request.Command)));
        }

        public async Task<PresentationFilePickerResult> PickFolderAsync(
            PresentationFolderPickerRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_owner.StorageProvider.CanPickFolder)
            {
                return PresentationFilePickerResult.Unavailable(
                    SisterAppFileTextPlanner.FormatCommandUnavailable(
                        FileText,
                        PresentationNativeCommandOutcomePlanner.CommandText(request.Command)));
            }

            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = false,
            });
            var folder = folders.Count == 0 ? null : folders[0];
            if (folder is null)
                return PresentationFilePickerResult.Cancelled;

            return folder.TryGetLocalPath() is { } path
                ? PresentationFilePickerResult.Selected(path)
                : PresentationFilePickerResult.NonLocal(
                    SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                        FileText,
                        PresentationNativeCommandOutcomePlanner.CommandText(request.Command)));
        }
    }

    private sealed class AvaloniaPresentationFileRenderPort : IPresentationFileRenderPort
    {
        public PresentationSlideImageRenderer RenderSlideToPng => SlideRenderer.RenderToBytes;
        public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
            SlideRenderer.RenderToBytesWithPrintMarkup;
        public PresentationRasterPdfWriter WriteRasterPdf => SkiaRasterPdfWriter.WriteToBytes;
        public PresentationPdfContentWriter WriteVectorPdf => SkiaPdfWriter.WriteToBytesWithPortableFallback;

        public byte[] WriteRasterPdfWithDiagnostics(
            PdfRasterDocument document,
            ICollection<string> imageDiagnostics) =>
            SkiaRasterPdfWriter.WriteToBytes(document, imageDiagnostics);

        public byte[] WriteVectorPdfWithDiagnostics(
            PdfContentDocument document,
            ICollection<string> imageDiagnostics) =>
            SkiaPdfWriter.WriteToBytesWithPortableFallback(document, imageDiagnostics);
    }

    private sealed class AvaloniaPresentationPrintPort : IPresentationPrintPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationPrintPort(MainWindow owner) => _owner = owner;

        public PresentationNativePrintHandoffHostCapabilities Capabilities =>
            _owner._nativePrintHostCapabilities;

        public async Task<PresentationNativePrintPortResult> PrintAsync(
            Presentation presentation,
            PresentationPrintRequest request,
            Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
            CancellationToken cancellationToken)
        {
            var result = await _owner.ExecutePrintWorkflowCoreAsync(
                request,
                buildPackage,
                cancellationToken).ConfigureAwait(true);
            return PresentationNativeCommandOutcomePlanner.BuildSystemPrintResult(result);
        }
    }

    private sealed class AvaloniaPresentationVideoPort : IPresentationVideoPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationVideoPort(MainWindow owner) => _owner = owner;

        public PresentationVideoExportHandoffHostCapabilities Capabilities =>
            _owner._videoExportHostCapabilities;

        public async Task<PresentationNativeCommandResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
            CancellationToken cancellationToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _owner._nativeOutputCancellation = linkedCancellation;
            try
            {
                _owner.LastVideoExportResult = await _owner._videoExportAdapter.ExportAsync(
                    package,
                    outputPath,
                    linkedCancellation.Token,
                    recordingMediaArtifacts).ConfigureAwait(true);
            }
            finally
            {
                if (ReferenceEquals(_owner._nativeOutputCancellation, linkedCancellation))
                    _owner._nativeOutputCancellation = null;
            }

            var result = _owner.LastVideoExportResult;
            return PresentationNativeCommandOutcomePlanner.BuildVideoExportCommandResult(
                result.Succeeded,
                result.Canceled,
                result.FailureReason,
                result.MuxedNarrationTrackCount,
                result.MuxedCameraTrackCount,
                result.MuxedCaptionTrackCount);
        }
    }

    private sealed class AvaloniaPresentationFileFeedbackPort : IPresentationFileCommandFeedbackPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationFileFeedbackPort(MainWindow owner) => _owner = owner;

        public async Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(result);
            if (plan.StatusText is not null)
                _owner._statusText.Text = plan.StatusText;

            if (plan.ShowAvaloniaFileErrorDialog && plan.Error is { } error)
            {
                await _owner._fileWorkflow.ShowFileCommandErrorAsync(error.Summary, error.Exception);
            }
            else if (result.Succeeded &&
                      UserMessageServiceFileCommandExtensions.BuildExportImageWarningMessage(
                          result.Message ?? PresentationNativeCommandOutcomePlanner.ExportCompletedStatus,
                          result.ImageDiagnostics) is { } warningMessage)
            {
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    _owner,
                    warningMessage,
                    FreePApplicationFrameDescriptor.Title.ApplicationName);
            }
        }
    }
}
