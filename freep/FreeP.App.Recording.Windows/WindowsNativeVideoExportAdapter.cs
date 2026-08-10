using System.IO.Compression;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Encodes the shared PNG frame package with the Windows media stack.
///
/// This supports delayed multi-track narration and captured camera PIP through MediaComposition.
/// </summary>
public sealed class WindowsNativeVideoExportAdapter : ILinuxVideoExportAdapter
{
    public const string ExecutablePath = "windows-media-composition";
    public static bool CanUseCaptionFallback => FindExecutable("ffmpeg") is not null;

    private readonly LinuxVideoEncoderCapability _capability;
    private readonly ILinuxVideoExportAdapter? _captionFallback;
    private readonly Func<ILinuxVideoExportAdapter?> _captionFallbackFactory;

    public WindowsNativeVideoExportAdapter(
        LinuxVideoEncoderCapability capability,
        ILinuxVideoExportAdapter? captionFallback = null,
        Func<ILinuxVideoExportAdapter?>? captionFallbackFactory = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _captionFallback = captionFallback;
        _captionFallbackFactory = captionFallbackFactory ?? TryCreateCaptionFallback;
    }

    public LinuxVideoEncoderCapability Capability => _capability;

    public async Task<LinuxVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (cancellationToken.IsCancellationRequested)
            return LinuxVideoExportResult.CanceledResult(outputPath);
        if (!_capability.CanEncodeMp4 ||
            !string.Equals(_capability.ExecutablePath, ExecutablePath, StringComparison.Ordinal))
        {
            return LinuxVideoExportResult.Failed(_capability.Reason, outputPath);
        }

        var validation = PresentationVideoFramePackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
        {
            return LinuxVideoExportResult.Failed(
                validation.FailureReason ?? "Video frame package validation failed.",
                outputPath);
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var stage = "initializing MediaComposition";
        try
        {
            using var temporaryDirectoryLease = TemporaryDirectoryLease.Create("freep-windows-video-");
            var temporaryDirectory = temporaryDirectoryLease.Path;
            var composition = new MediaComposition();
            var mediaPlan = PresentationVideoMediaMuxPlanner.Prepare(
                package,
                mediaArtifacts,
                temporaryDirectory);
            if (mediaPlan.CaptionTracks.Count > 0)
            {
                var captionFallback = _captionFallback ?? _captionFallbackFactory();
                if (captionFallback is null)
                {
                    return LinuxVideoExportResult.Failed(
                        "Windows MediaComposition cannot mux timed caption tracks. Install ffmpeg to export this captioned video.",
                        outputPath);
                }

                return await captionFallback.ExportAsync(
                        package,
                        outputPath,
                        cancellationToken,
                        mediaArtifacts)
                    .ConfigureAwait(false);
            }

            using var archive = new ZipArchive(
                new MemoryStream(package.Bytes),
                ZipArchiveMode.Read,
                leaveOpen: false);

            foreach (var frame in package.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stage = $"creating slide clip {frame.FileName}";
                var framePath = ExtractFrame(archive, frame.FileName, frame.SegmentIndex, temporaryDirectory);
                var frameFile = await StorageFile.GetFileFromPathAsync(framePath)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var clip = await MediaClip.CreateFromImageFileAsync(frameFile, frame.Duration)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                composition.Clips.Add(clip);
            }

            foreach (var narration in mediaPlan.NarrationTracks)
            {
                stage = $"creating narration track {narration.Path}";
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

            if (mediaPlan.CameraTracks.Count > 0)
            {
                var overlayLayer = new MediaOverlayLayer();
                var frame = package.Frames[0];
                foreach (var camera in mediaPlan.CameraTracks)
                {
                    stage = $"creating camera overlay {camera.Path}";
                    var cameraFile = await StorageFile.GetFileFromPathAsync(camera.Path)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    var cameraClip = await MediaClip.CreateFromFileAsync(cameraFile)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    var cameraProperties = cameraClip.GetVideoEncodingProperties();
                    if (cameraProperties.Width == 0 || cameraProperties.Height == 0)
                        throw new InvalidDataException("Windows MediaComposition could not determine the camera video dimensions.");

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

            var outputDirectory = Path.GetDirectoryName(fullOutputPath)!;
            Directory.CreateDirectory(outputDirectory);
            var outputFolder = await StorageFolder.GetFolderFromPathAsync(outputDirectory)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            var outputFile = await outputFolder.CreateFileAsync(
                    Path.GetFileName(fullOutputPath),
                    CreationCollisionOption.ReplaceExisting)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
            stage = "rendering MediaComposition to MP4";
            var encodingQuality = package.Frames[0].HeightPx >= 1080
                ? VideoEncodingQuality.HD1080p
                : VideoEncodingQuality.HD720p;
            await composition.RenderToFileAsync(
                    outputFile,
                    MediaTrimmingPreference.Precise,
                    MediaEncodingProfile.CreateMp4(encodingQuality))
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            var bytes = await File.ReadAllBytesAsync(fullOutputPath, cancellationToken)
                .ConfigureAwait(false);
            if (!HasNonEmptyMp4Payload(bytes))
            {
                TryDelete(fullOutputPath);
                return LinuxVideoExportResult.Failed(
                    "Windows MediaComposition completed but did not produce a valid non-empty MP4 file.",
                    outputPath);
            }

            return LinuxVideoExportResult.Success(
                outputPath,
                _capability.EncoderName ?? ExecutablePath,
                bytes.LongLength);
        }
        catch (OperationCanceledException)
        {
            TryDelete(fullOutputPath);
            return LinuxVideoExportResult.CanceledResult(outputPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDelete(fullOutputPath);
            return LinuxVideoExportResult.Failed(
                $"Windows MediaComposition failed while {stage} with {ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}",
                outputPath);
        }
    }

    private static string ExtractFrame(
        ZipArchive archive,
        string entryName,
        int segmentIndex,
        string directory)
    {
        var entry = archive.GetEntry(entryName) ??
            throw new InvalidDataException($"Video package is missing frame '{entryName}'.");
        var framePath = Path.Combine(directory, $"frame-{segmentIndex:D6}.png");
        using var input = entry.Open();
        using var output = File.Create(framePath);
        input.CopyTo(output);
        return framePath;
    }

    private static bool HasNonEmptyMp4Payload(byte[] bytes) =>
        bytes.Length >= 16 &&
        bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
        bytes.AsSpan().IndexOf("moov"u8) >= 0 &&
        bytes.AsSpan().IndexOf("mdat"u8) >= 0;

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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

}
