using System.Globalization;
using System.IO.Compression;
using System.Text;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Recording;

public sealed record PresentationVideoExportWorkspaceFrame(
    PresentationVideoFramePackageFrame Frame,
    string Path);

public sealed record PresentationVideoExportWorkspace(
    PresentationVideoFramePackage Package,
    string OutputPath,
    string FullOutputPath,
    string TemporaryDirectory,
    IReadOnlyList<PresentationVideoExportWorkspaceFrame> Frames,
    string? ConcatPath,
    PresentationVideoMediaMuxPlan MediaPlan,
    IReadOnlyList<PresentationRecordingMediaArtifact> MediaArtifacts);

public sealed class PresentationVideoExportStage(string initialStage)
{
    public string Current { get; private set; } =
        string.IsNullOrWhiteSpace(initialStage) ? "exporting video" : initialStage.Trim();

    public void Set(string stage)
    {
        if (!string.IsNullOrWhiteSpace(stage))
            Current = stage.Trim();
    }
}

public sealed record PresentationVideoExportBackendResult(
    string? EncoderName,
    int MuxedNarrationTrackCount = 0,
    int MuxedCameraTrackCount = 0,
    int MuxedCaptionTrackCount = 0,
    string? FailureReason = null,
    LinuxVideoExportResult? CompletedResult = null)
{
    public static PresentationVideoExportBackendResult Encoded(
        string encoderName,
        int muxedNarrationTrackCount = 0,
        int muxedCameraTrackCount = 0,
        int muxedCaptionTrackCount = 0) =>
        new(
            encoderName,
            muxedNarrationTrackCount,
            muxedCameraTrackCount,
            muxedCaptionTrackCount);

    public static PresentationVideoExportBackendResult Failed(string reason) =>
        new(
            EncoderName: null,
            FailureReason: string.IsNullOrWhiteSpace(reason)
                ? "Video export backend failed."
                : reason.Trim());

    public static PresentationVideoExportBackendResult Completed(LinuxVideoExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.EncoderName,
            result.MuxedNarrationTrackCount,
            result.MuxedCameraTrackCount,
            result.MuxedCaptionTrackCount,
            CompletedResult: result);
    }
}

public interface IPresentationVideoExportBackend
{
    Task<PresentationVideoExportBackendResult> EncodeAsync(
        PresentationVideoExportWorkspace workspace,
        PresentationVideoExportStage stage,
        CancellationToken cancellationToken);
}

public sealed record PresentationVideoExportOrchestrationOptions(
    string TemporaryDirectoryPrefix,
    string InitialStage,
    string InvalidOutputReason,
    Func<LinuxVideoEncoderCapability, bool> CanExport,
    Func<string, Exception, string> FormatFailureReason,
    bool BuildFfmpegConcatFile = false,
    bool RequireNonEmptyFrames = false,
    Func<PresentationVideoFramePackageFrame, string>? FramePreparationStage = null);

/// <summary>
/// Owns the portable video-export lifecycle around a platform encoder backend.
/// </summary>
public sealed class PresentationVideoExportOrchestrator
{
    private readonly LinuxVideoEncoderCapability _capability;
    private readonly IPresentationVideoExportBackend _backend;
    private readonly PresentationVideoExportOrchestrationOptions _options;

    public PresentationVideoExportOrchestrator(
        LinuxVideoEncoderCapability capability,
        IPresentationVideoExportBackend backend,
        PresentationVideoExportOrchestrationOptions options)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TemporaryDirectoryPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.InvalidOutputReason);
        ArgumentNullException.ThrowIfNull(options.CanExport);
        ArgumentNullException.ThrowIfNull(options.FormatFailureReason);
    }

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
        if (!_options.CanExport(_capability))
            return LinuxVideoExportResult.Failed(_capability.Reason, outputPath);

        var validation = PresentationVideoFramePackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
        {
            return LinuxVideoExportResult.Failed(
                validation.FailureReason ?? "Video frame package validation failed.",
                outputPath);
        }

        var stage = new PresentationVideoExportStage(_options.InitialStage);
        string? fullOutputPath = null;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
            using var temporaryDirectoryLease = TemporaryDirectoryLease.Create(
                _options.TemporaryDirectoryPrefix);
            var workspace = PrepareWorkspace(
                package,
                outputPath,
                fullOutputPath,
                temporaryDirectoryLease.Path,
                mediaArtifacts,
                stage,
                cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            stage.Set(_options.InitialStage);

            var backendResult = await _backend.EncodeAsync(
                workspace,
                stage,
                cancellationToken).ConfigureAwait(false);
            if (backendResult.CompletedResult is { } completed && !completed.Succeeded)
            {
                TryDelete(fullOutputPath);
                return completed;
            }
            if (backendResult.FailureReason is { } backendFailure)
            {
                TryDelete(fullOutputPath);
                return LinuxVideoExportResult.Failed(backendFailure, outputPath);
            }

            var bytes = await File.ReadAllBytesAsync(fullOutputPath, cancellationToken)
                .ConfigureAwait(false);
            if (!HasNonEmptyMp4Payload(bytes))
            {
                TryDelete(fullOutputPath);
                return LinuxVideoExportResult.Failed(_options.InvalidOutputReason, outputPath);
            }

            if (backendResult.CompletedResult is { } completedSuccess)
                return completedSuccess;

            return LinuxVideoExportResult.Success(
                outputPath,
                backendResult.EncoderName ?? _capability.EncoderName ?? string.Empty,
                bytes.LongLength,
                backendResult.MuxedNarrationTrackCount,
                backendResult.MuxedCameraTrackCount,
                backendResult.MuxedCaptionTrackCount);
        }
        catch (OperationCanceledException)
        {
            TryDelete(fullOutputPath ?? outputPath);
            return LinuxVideoExportResult.CanceledResult(outputPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDelete(fullOutputPath ?? outputPath);
            return LinuxVideoExportResult.Failed(
                _options.FormatFailureReason(stage.Current, ex),
                outputPath);
        }
    }

    public static bool HasNonEmptyMp4Payload(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.Length >= 16 &&
            bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
            bytes.AsSpan().IndexOf("moov"u8) >= 0 &&
            bytes.AsSpan().IndexOf("mdat"u8) >= 0;
    }

    private PresentationVideoExportWorkspace PrepareWorkspace(
        PresentationVideoFramePackage package,
        string outputPath,
        string fullOutputPath,
        string temporaryDirectory,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts,
        PresentationVideoExportStage stage,
        CancellationToken cancellationToken)
    {
        var artifacts = mediaArtifacts ?? [];
        var mediaPlan = PresentationVideoMediaMuxPlanner.Prepare(
            package,
            artifacts,
            temporaryDirectory);
        using var archive = new ZipArchive(
            new MemoryStream(package.Bytes),
            ZipArchiveMode.Read,
            leaveOpen: false);
        var frames = new List<PresentationVideoExportWorkspaceFrame>(package.Frames.Count);
        var concatLines = _options.BuildFfmpegConcatFile
            ? new List<string>(package.Frames.Count * 2 + 1)
            : null;

        foreach (var frame in package.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.FramePreparationStage is not null)
                stage.Set(_options.FramePreparationStage(frame));

            var entry = archive.GetEntry(frame.FileName) ??
                throw new InvalidDataException($"Video package is missing frame '{frame.FileName}'.");
            var framePath = Path.Combine(temporaryDirectory, $"frame-{frame.SegmentIndex:D6}.png");
            using (var input = entry.Open())
            using (var output = File.Create(framePath))
                input.CopyTo(output);

            if (_options.RequireNonEmptyFrames && new FileInfo(framePath).Length == 0)
                throw new InvalidDataException($"Video package frame '{frame.FileName}' is empty.");

            frames.Add(new PresentationVideoExportWorkspaceFrame(frame, framePath));
            if (concatLines is not null)
            {
                concatLines.Add($"file '{EscapeConcatPath(framePath)}'");
                concatLines.Add(
                    $"duration {frame.Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture)}");
            }
        }

        string? concatPath = null;
        if (concatLines is not null)
        {
            if (frames.Count > 0)
                concatLines.Add($"file '{EscapeConcatPath(frames[^1].Path)}'");
            concatPath = Path.Combine(temporaryDirectory, "frames.txt");
            File.WriteAllLines(concatPath, concatLines, new UTF8Encoding(false));
        }

        return new PresentationVideoExportWorkspace(
            package,
            outputPath,
            fullOutputPath,
            temporaryDirectory,
            frames,
            concatPath,
            mediaPlan,
            artifacts);
    }

    private static string EscapeConcatPath(string path) =>
        path.Replace("'", "'\\''", StringComparison.Ordinal);

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
