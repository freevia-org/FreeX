using System.IO.Compression;
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
/// This supports the common single-narration-track case through MediaComposition. Camera/PIP
/// overlays and offset or multi-track narration remain explicitly deferred to the ffmpeg path.
/// </summary>
public sealed class WindowsNativeVideoExportAdapter : ILinuxVideoExportAdapter
{
    public const string ExecutablePath = "windows-media-composition";

    private readonly LinuxVideoEncoderCapability _capability;

    public WindowsNativeVideoExportAdapter(LinuxVideoEncoderCapability capability)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
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

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"freep-windows-video-{Guid.NewGuid():N}");
        var fullOutputPath = Path.GetFullPath(outputPath);
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var composition = new MediaComposition();
            var mediaPlan = PresentationVideoMediaMuxPlanner.Prepare(
                package,
                mediaArtifacts,
                temporaryDirectory);
            if (mediaPlan.CameraTracks.Count > 0)
            {
                return LinuxVideoExportResult.Failed(
                    "Windows MediaComposition does not yet support camera/PIP track composition; use the ffmpeg video export path.",
                    outputPath);
            }

            if (mediaPlan.NarrationTracks.Count > 1 ||
                mediaPlan.NarrationTracks.Any(track => track.StartTime != TimeSpan.Zero))
            {
                return LinuxVideoExportResult.Failed(
                    "Windows MediaComposition supports one narration track starting at presentation time zero; offset or multiple narration tracks require the ffmpeg video export path.",
                    outputPath);
            }

            using var archive = new ZipArchive(
                new MemoryStream(package.Bytes),
                ZipArchiveMode.Read,
                leaveOpen: false);

            foreach (var frame in package.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var framePath = ExtractFrame(archive, frame.FileName, frame.SegmentIndex, temporaryDirectory);
                var frameFile = await StorageFile.GetFileFromPathAsync(framePath)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var clip = await MediaClip.CreateFromImageFileAsync(frameFile, frame.Duration)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                composition.Clips.Add(clip);
            }

            if (mediaPlan.NarrationTracks is [{ } narration])
            {
                var narrationFile = await StorageFile.GetFileFromPathAsync(narration.Path)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var audioTrack = await BackgroundAudioTrack.CreateFromFileAsync(narrationFile)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                composition.BackgroundAudioTracks.Add(audioTrack);
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
            await composition.RenderToFileAsync(
                    outputFile,
                    MediaTrimmingPreference.Precise,
                    MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto))
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
            return LinuxVideoExportResult.Failed(ex.Message, outputPath);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
