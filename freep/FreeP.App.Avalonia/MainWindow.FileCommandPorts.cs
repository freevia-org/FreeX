using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.IO;
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
                    SisterAppFileTextPlanner.FormatCommandUnavailable(FileText, CommandText(request.Command)));
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
                        CommandText(request.Command)));
        }

        public async Task<PresentationFilePickerResult> PickFolderAsync(
            PresentationFolderPickerRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_owner.StorageProvider.CanPickFolder)
            {
                return PresentationFilePickerResult.Unavailable(
                    SisterAppFileTextPlanner.FormatCommandUnavailable(FileText, CommandText(request.Command)));
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
                        CommandText(request.Command)));
        }

        public static string CommandText(PresentationFileCommand command) => command switch
        {
            PresentationFileCommand.Open => FileText.OpenCommand,
            PresentationFileCommand.Save or PresentationFileCommand.SaveAs => FileText.SaveCommand,
            PresentationFileCommand.ExportPdf => PresentationExportPlanner.PdfExportCommandText,
            PresentationFileCommand.ExportNotesPagePdf => PresentationExportPlanner.NotesPagePdfExportCommandText,
            PresentationFileCommand.ExportImages => PresentationExportPlanner.ImageExportCommandText,
            PresentationFileCommand.Print => "Print",
            PresentationFileCommand.ExportVideo => PresentationExportPlanner.VideoExportCommandText,
            _ => command.ToString(),
        };
    }

    private sealed class AvaloniaPresentationFileRenderPort : IPresentationFileRenderPort
    {
        public PresentationSlideImageRenderer RenderSlideToPng => SlideRenderer.RenderToBytes;
        public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
            SlideRenderer.RenderToBytesWithPrintMarkup;
        public PresentationRasterPdfWriter WriteRasterPdf => SkiaRasterPdfWriter.WriteToBytes;
        public PresentationPdfContentWriter WriteVectorPdf => SkiaPdfWriter.WriteToBytesWithPortableFallback;
    }

    private sealed class AvaloniaPresentationPrintPort : IPresentationPrintPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationPrintPort(MainWindow owner) => _owner = owner;

        public PresentationNativePrintHandoffHostCapabilities Capabilities =>
            _owner._nativePrintHostCapabilities;

        public async Task<PresentationNativeCommandResult> PrintAsync(
            Presentation presentation,
            PresentationPrintRequest request,
            Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
            CancellationToken cancellationToken)
        {
            var result = await _owner.ExecutePrintWorkflowCoreAsync(
                request,
                buildPackage,
                cancellationToken).ConfigureAwait(true);
            return result.Succeeded
                ? PresentationNativeCommandResult.Success(result.StatusText)
                : result.Canceled
                    ? PresentationNativeCommandResult.Cancel(result.StatusText)
                    : PresentationNativeCommandResult.Failure(
                        result.StatusText,
                        result.FailureReason ?? "Printing failed.");
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
            return result.Succeeded
                ? PresentationNativeCommandResult.Success(result.StatusText)
                : result.Canceled
                    ? PresentationNativeCommandResult.Cancel(result.StatusText)
                    : PresentationNativeCommandResult.Failure(
                        result.StatusText,
                        result.FailureReason ?? PresentationFileTextResources.VideoExportFailed);
        }
    }

    private sealed class AvaloniaPresentationFileFeedbackPort : IPresentationFileCommandFeedbackPort
    {
        private readonly MainWindow _owner;

        public AvaloniaPresentationFileFeedbackPort(MainWindow owner) => _owner = owner;

        public async Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Cancelled)
                return;

            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(result.Message))
                    _owner._statusText.Text = result.Message;
                return;
            }

            if (result.Status == PresentationFileCommandStatus.Unavailable ||
                result.Status == PresentationFileCommandStatus.Invalid && result.Path is null)
            {
                _owner._statusText.Text = result.Message ?? "The presentation command is unavailable.";
                return;
            }

            var commandText = AvaloniaPresentationFilePickerPort.CommandText(result.Command);
            var message = result.Error?.Exception.Message ?? result.Message ?? "The command failed.";
            _owner._statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, commandText, message);

            if (result.Error is { } error &&
                result.Command is PresentationFileCommand.Open or PresentationFileCommand.Save or PresentationFileCommand.SaveAs)
            {
                await _owner._fileWorkflow.ShowFileCommandErrorAsync(error.Summary, error.Exception);
            }
        }
    }
}
