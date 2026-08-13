using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Selects the Windows MediaComposition backend for the shared recording export lifecycle.
/// </summary>
public sealed class WindowsNativeVideoExportAdapter : ILinuxVideoExportAdapter
{
    public const string ExecutablePath = "windows-media-composition";
    public static bool CanUseCaptionFallback => FindExecutable("ffmpeg") is not null;

    private readonly LinuxVideoEncoderCapability _capability;
    private readonly PresentationVideoExportOrchestrator _orchestrator;

    public WindowsNativeVideoExportAdapter(
        LinuxVideoEncoderCapability capability,
        ILinuxVideoExportAdapter? captionFallback = null,
        Func<ILinuxVideoExportAdapter?>? captionFallbackFactory = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _orchestrator = new PresentationVideoExportOrchestrator(
            capability,
            new WindowsMediaCompositionVideoExportBackend(
                capability,
                captionFallback,
                captionFallbackFactory ?? TryCreateCaptionFallback),
            new PresentationVideoExportOrchestrationOptions(
                TemporaryDirectoryPrefix: "freep-windows-video-",
                InitialStage: "initializing MediaComposition",
                InvalidOutputReason: "Windows MediaComposition completed but did not produce a valid non-empty MP4 file.",
                CanExport: static value =>
                    value.CanEncodeMp4 &&
                    string.Equals(value.ExecutablePath, ExecutablePath, StringComparison.Ordinal),
                FormatFailureReason: static (stage, ex) =>
                    $"Windows MediaComposition failed while {stage} with {ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}",
                FramePreparationStage: static frame => $"creating slide clip {frame.FileName}"));
    }

    public LinuxVideoEncoderCapability Capability => _capability;

    public Task<LinuxVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null) =>
        _orchestrator.ExportAsync(package, outputPath, cancellationToken, mediaArtifacts);

    private static ILinuxVideoExportAdapter? TryCreateCaptionFallback()
    {
        var executable = FindExecutable("ffmpeg");
        return executable is null
            ? null
            : new LinuxVideoExportAdapter(
                new LinuxVideoEncoderCapability(
                    CanEncodeMp4: true,
                    ExecutablePath: executable,
                    EncoderName: "mpeg4",
                    CanCaptureNarration: false,
                    Reason: "ffmpeg caption fallback for Windows video export.",
                    CanMuxTimedCaptions: true));
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name + ".exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private sealed class WindowsMediaCompositionVideoExportBackend(
        LinuxVideoEncoderCapability capability,
        ILinuxVideoExportAdapter? captionFallback,
        Func<ILinuxVideoExportAdapter?> captionFallbackFactory) : IPresentationVideoExportBackend
    {
        public async Task<PresentationVideoExportBackendResult> EncodeAsync(
            PresentationVideoExportWorkspace workspace,
            PresentationVideoExportStage stage,
            CancellationToken cancellationToken)
        {
            var composition = new MediaComposition();
            if (workspace.MediaPlan.CaptionTracks.Count > 0)
            {
                var fallback = captionFallback ?? captionFallbackFactory();
                if (fallback is null)
                {
                    return PresentationVideoExportBackendResult.Failed(
                        "Windows MediaComposition cannot mux timed caption tracks. Install ffmpeg to export this captioned video.");
                }

                return PresentationVideoExportBackendResult.Completed(
                    await fallback.ExportAsync(
                            workspace.Package,
                            workspace.OutputPath,
                            cancellationToken,
                            workspace.MediaArtifacts)
                        .ConfigureAwait(false));
            }

            foreach (var workspaceFrame in workspace.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stage.Set($"creating slide clip {workspaceFrame.Frame.FileName}");
                var frameFile = await StorageFile.GetFileFromPathAsync(workspaceFrame.Path)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var clip = await MediaClip.CreateFromImageFileAsync(
                        frameFile,
                        workspaceFrame.Frame.Duration)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                composition.Clips.Add(clip);
            }

            foreach (var narration in workspace.MediaPlan.NarrationTracks)
            {
                stage.Set($"creating narration track {narration.Path}");
                var narrationFile = await StorageFile.GetFileFromPathAsync(narration.Path)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var audioTrack = await BackgroundAudioTrack.CreateFromFileAsync(narrationFile)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                audioTrack.Delay = narration.StartTime;
                if (narration.Duration > TimeSpan.Zero && audioTrack.OriginalDuration > narration.Duration)
                    audioTrack.TrimTimeFromEnd = audioTrack.OriginalDuration - narration.Duration;
                composition.BackgroundAudioTracks.Add(audioTrack);
            }

            if (workspace.MediaPlan.CameraTracks.Count > 0)
            {
                var overlayLayer = new MediaOverlayLayer();
                var frame = workspace.Package.Frames[0];
                foreach (var camera in workspace.MediaPlan.CameraTracks)
                {
                    stage.Set($"creating camera overlay {camera.Path}");
                    var cameraFile = await StorageFile.GetFileFromPathAsync(camera.Path)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    var cameraClip = await MediaClip.CreateFromFileAsync(cameraFile)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    var cameraProperties = cameraClip.GetVideoEncodingProperties();
                    if (cameraProperties.Width == 0 || cameraProperties.Height == 0)
                    {
                        throw new InvalidDataException(
                            "Windows MediaComposition could not determine the camera video dimensions.");
                    }

                    var overlayWidth = Math.Max(2, frame.WidthPx * 0.25);
                    var overlayHeight = overlayWidth * cameraProperties.Height / cameraProperties.Width;
                    var overlay = new MediaOverlay(
                        cameraClip,
                        new global::Windows.Foundation.Rect(
                            Math.Max(0, frame.WidthPx - overlayWidth - 32),
                            Math.Max(0, frame.HeightPx - overlayHeight - 32),
                            overlayWidth,
                            overlayHeight),
                        opacity: 1.0)
                    {
                        AudioEnabled = false,
                        Delay = camera.StartTime,
                    };
                    if (camera.Duration > TimeSpan.Zero && cameraClip.OriginalDuration > camera.Duration)
                        cameraClip.TrimTimeFromEnd = cameraClip.OriginalDuration - camera.Duration;
                    overlayLayer.Overlays.Add(overlay);
                }

                composition.OverlayLayers.Add(overlayLayer);
            }

            var outputFolder = await StorageFolder.GetFolderFromPathAsync(
                    Path.GetDirectoryName(workspace.FullOutputPath)!)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            var outputFile = await outputFolder.CreateFileAsync(
                    Path.GetFileName(workspace.FullOutputPath),
                    CreationCollisionOption.ReplaceExisting)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            stage.Set("rendering MediaComposition to MP4");
            var encodingQuality = workspace.Package.Frames[0].HeightPx >= 1080
                ? VideoEncodingQuality.HD1080p
                : VideoEncodingQuality.HD720p;
            await composition.RenderToFileAsync(
                    outputFile,
                    MediaTrimmingPreference.Precise,
                    MediaEncodingProfile.CreateMp4(encodingQuality))
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return PresentationVideoExportBackendResult.Encoded(
                capability.EncoderName ?? ExecutablePath);
        }
    }
}
