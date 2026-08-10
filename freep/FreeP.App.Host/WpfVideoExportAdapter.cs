using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed record WpfVideoEncoderCapability(
    bool CanEncodeMp4,
    string? ExecutablePath,
    string? EncoderName,
    string Reason,
    bool CanCaptureNarration = false,
    bool CanCaptureCameraAndMedia = false,
    bool CanMuxTimedCaptions = false)
{
    public static WpfVideoEncoderCapability Unavailable(string reason) =>
        new(false, null, null, string.IsNullOrWhiteSpace(reason)
            ? "Install ffmpeg with a software MP4 encoder to enable WPF video export."
            : reason.Trim());
}

internal sealed record WpfVideoExportResult(
    bool Succeeded,
    bool Canceled,
    string StatusText,
    string? FailureReason,
    string OutputPath,
    string? EncoderName,
    long ByteCount,
    int MuxedNarrationTrackCount,
    int MuxedCameraTrackCount = 0,
    int MuxedCaptionTrackCount = 0)
{
    public static WpfVideoExportResult Failed(string reason, string outputPath = "") =>
        new(false, false, "Video export failed", reason, outputPath, null, 0, 0);

    public static WpfVideoExportResult CanceledResult(string outputPath) =>
        new(false, true, "Video export canceled", null, outputPath, null, 0, 0);

    public static WpfVideoExportResult Success(
        string outputPath,
        string encoderName,
        long byteCount,
        int muxedNarrationTrackCount,
        int muxedCameraTrackCount = 0,
        int muxedCaptionTrackCount = 0) =>
        new(
            true,
            false,
            BuildSuccessText(muxedNarrationTrackCount, muxedCameraTrackCount, muxedCaptionTrackCount),
            null,
            outputPath,
            encoderName,
            byteCount,
            muxedNarrationTrackCount,
            muxedCameraTrackCount,
            muxedCaptionTrackCount);

    private static string BuildSuccessText(int narrationCount, int cameraCount, int captionCount) =>
        narrationCount == 0 && cameraCount == 0 && captionCount == 0
            ? "Video export completed (video-only)"
            : $"Video export completed with {narrationCount} narration track(s), {cameraCount} camera track(s), and {captionCount} caption track(s)";
}

internal sealed record WpfVideoProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool Canceled);

internal interface IWpfVideoProcessRunner
{
    Task<WpfVideoProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal static class WpfVideoEncoderCapabilityDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public static WpfVideoEncoderCapability Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            return DetectWindowsCaptureCapability(new WindowsNativeRecordingDeviceCatalog());
        }

        var executable = FindExecutable("ffmpeg");
        if (executable is null)
            return WpfVideoEncoderCapability.Unavailable(
                "Install ffmpeg and add it to PATH to enable WPF video export.");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-encoders");
            if (!process.Start())
                return WpfVideoEncoderCapability.Unavailable($"Could not start '{executable}'.");

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                TryKill(process);
                return WpfVideoEncoderCapability.Unavailable("ffmpeg encoder discovery timed out.");
            }

            var encoder = SelectSoftwareEncoder(output.GetAwaiter().GetResult() + Environment.NewLine +
                error.GetAwaiter().GetResult());
            return encoder is null
                ? WpfVideoEncoderCapability.Unavailable(
                    "ffmpeg is installed, but no supported software MP4 encoder was reported.")
                : new WpfVideoEncoderCapability(
                    true,
                    executable,
                    encoder,
                    $"WPF video export can use ffmpeg encoder '{encoder}'.",
                    CanMuxTimedCaptions: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WpfVideoEncoderCapability.Unavailable(
                $"ffmpeg encoder discovery failed: {ex.Message}");
        }
    }

    internal static WpfVideoEncoderCapability DetectWindowsCaptureCapability(
        IWindowsRecordingDeviceCatalog deviceCatalog)
    {
        ArgumentNullException.ThrowIfNull(deviceCatalog);

        try
        {
            var devices = deviceCatalog.EnumerateDevices();
            var hasMicrophone = devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
                device.IsAvailable);
            var hasCamera = devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
                device.IsAvailable);

            return new WpfVideoEncoderCapability(
                true,
                WindowsNativeVideoExportAdapter.ExecutablePath,
                "Windows MediaComposition",
                BuildWindowsCapabilityReason(hasMicrophone, hasCamera, WindowsNativeVideoExportAdapter.CanUseCaptionFallback),
                CanCaptureNarration: hasMicrophone,
                CanCaptureCameraAndMedia: hasCamera,
                CanMuxTimedCaptions: WindowsNativeVideoExportAdapter.CanUseCaptionFallback);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new WpfVideoEncoderCapability(
                true,
                WindowsNativeVideoExportAdapter.ExecutablePath,
                "Windows MediaComposition",
                $"Windows MediaComposition video export is available, but recording device detection failed: {ex.Message}");
        }
    }

    private static string BuildWindowsCapabilityReason(bool hasMicrophone, bool hasCamera, bool canMuxTimedCaptions) =>
        (hasMicrophone, hasCamera) switch
        {
            (true, true) => AppendTimedCaptionReason("Windows MediaComposition video export, delayed multi-track narration, and captured camera PIP are available.", canMuxTimedCaptions),
            (true, false) => AppendTimedCaptionReason("Windows MediaComposition video export and narration capture are available; no camera device is currently available for camera PIP.", canMuxTimedCaptions),
            (false, true) => AppendTimedCaptionReason("Windows MediaComposition video export and camera PIP are available; no microphone device is currently available for narration.", canMuxTimedCaptions),
            _ => AppendTimedCaptionReason("Windows MediaComposition video export is available; no microphone device is currently available for narration, and no camera device is currently available for camera PIP.", canMuxTimedCaptions)
        };

    private static string AppendTimedCaptionReason(string reason, bool canMuxTimedCaptions) =>
        canMuxTimedCaptions
            ? $"{reason} Timed captions use the available ffmpeg mov_text fallback."
            : $"{reason} Timed captions require ffmpeg because MediaComposition has no timed-text stream API.";

    internal static string? SelectSoftwareEncoder(string output)
    {
        string[] preferred = ["libx264", "libopenh264", "mpeg4", "libxvid"];
        return preferred.FirstOrDefault(encoder => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(encoder, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name + extension);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(1000);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed class WpfVideoExportAdapter : IWpfVideoProcessRunner
{
    private readonly WpfVideoEncoderCapability _capability;
    private readonly IWpfVideoProcessRunner _processRunner;

    public WpfVideoExportAdapter(
        WpfVideoEncoderCapability capability,
        IWpfVideoProcessRunner? processRunner = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _processRunner = processRunner ?? this;
    }

    public WpfVideoEncoderCapability Capability => _capability;

    public async Task<WpfVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (cancellationToken.IsCancellationRequested)
            return WpfVideoExportResult.CanceledResult(outputPath);
        if (!_capability.CanEncodeMp4 || string.IsNullOrWhiteSpace(_capability.ExecutablePath) ||
            string.IsNullOrWhiteSpace(_capability.EncoderName))
            return WpfVideoExportResult.Failed(_capability.Reason, outputPath);

        if (string.Equals(
                _capability.ExecutablePath,
                WindowsNativeVideoExportAdapter.ExecutablePath,
                StringComparison.Ordinal))
        {
            var nativeResult = await new WindowsNativeVideoExportAdapter(
                    new LinuxVideoEncoderCapability(
                        CanEncodeMp4: true,
                        ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                        EncoderName: _capability.EncoderName,
                        CanCaptureNarration: _capability.CanCaptureNarration,
                        Reason: _capability.Reason,
                        CanCaptureCameraAndMedia: _capability.CanCaptureCameraAndMedia,
                        CanMuxTimedCaptions: _capability.CanMuxTimedCaptions))
                .ExportAsync(package, outputPath, cancellationToken, mediaArtifacts)
                .ConfigureAwait(false);
            return nativeResult.Canceled
                ? WpfVideoExportResult.CanceledResult(outputPath)
                : nativeResult.Succeeded
                    ? WpfVideoExportResult.Success(
                        outputPath,
                        nativeResult.EncoderName ?? _capability.EncoderName,
                        nativeResult.ByteCount,
                        nativeResult.MuxedNarrationTrackCount,
                        nativeResult.MuxedCameraTrackCount,
                        nativeResult.MuxedCaptionTrackCount)
                    : WpfVideoExportResult.Failed(
                        nativeResult.FailureReason ?? nativeResult.StatusText,
                        outputPath);
        }

        var validation = PresentationVideoFramePackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
            return WpfVideoExportResult.Failed(
                validation.FailureReason ?? "Video frame package validation failed.", outputPath);

        try
        {
            using var temporaryDirectoryLease = TemporaryDirectoryLease.Create("freep-video-");
            var temporaryDirectory = temporaryDirectoryLease.Path;
            var concatPath = ExtractFramesAndBuildConcatFile(package, temporaryDirectory);
            var mediaPlan = PresentationVideoMediaMuxPlanner.Prepare(
                package,
                mediaArtifacts,
                temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var processResult = await _processRunner.RunAsync(
                _capability.ExecutablePath,
                PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
                    concatPath,
                    outputPath,
                    _capability.EncoderName,
                    mediaPlan),
                cancellationToken).ConfigureAwait(false);
            if (processResult.Canceled)
                return WpfVideoExportResult.CanceledResult(outputPath);
            if (processResult.ExitCode != 0)
            {
                TryDelete(outputPath);
                return WpfVideoExportResult.Failed(
                    string.IsNullOrWhiteSpace(processResult.StandardError)
                        ? $"ffmpeg exited with code {processResult.ExitCode}."
                        : processResult.StandardError.Trim(),
                    outputPath);
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
            if (!HasNonEmptyMp4Payload(bytes))
            {
                TryDelete(outputPath);
                return WpfVideoExportResult.Failed(
                    "ffmpeg completed but did not produce a valid non-empty MP4 file.", outputPath);
            }

            return WpfVideoExportResult.Success(
                outputPath,
                _capability.EncoderName,
                bytes.LongLength,
                mediaPlan.MuxedNarrationTrackCount,
                mediaPlan.MuxedCameraTrackCount,
                mediaPlan.MuxedCaptionTrackCount);
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            return WpfVideoExportResult.CanceledResult(outputPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDelete(outputPath);
            return WpfVideoExportResult.Failed(ex.Message, outputPath);
        }
    }

    private static string ExtractFramesAndBuildConcatFile(
        PresentationVideoFramePackage package,
        string directory)
    {
        using var archive = new ZipArchive(new MemoryStream(package.Bytes), ZipArchiveMode.Read);
        var concatPath = Path.Combine(directory, "frames.txt");
        var lines = new List<string>(package.Frames.Count * 2 + 1);
        foreach (var frame in package.Frames)
        {
            var entry = archive.GetEntry(frame.FileName) ??
                throw new InvalidDataException($"Video package is missing frame '{frame.FileName}'.");
            var framePath = Path.Combine(directory, $"frame-{frame.SegmentIndex:D6}.png");
            using (var input = entry.Open())
            using (var output = File.Create(framePath))
                input.CopyTo(output);
            if (new FileInfo(framePath).Length == 0)
                throw new InvalidDataException($"Video package frame '{frame.FileName}' is empty.");

            lines.Add($"file '{EscapeConcatPath(framePath)}'");
            lines.Add($"duration {frame.Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        if (package.Frames.Count > 0)
        {
            var lastPath = Path.Combine(directory, $"frame-{package.Frames[^1].SegmentIndex:D6}.png");
            lines.Add($"file '{EscapeConcatPath(lastPath)}'");
        }

        File.WriteAllLines(concatPath, lines, new UTF8Encoding(false));
        return concatPath;
    }

    private static bool HasNonEmptyMp4Payload(byte[] bytes) =>
        bytes.Length >= 16 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
        bytes.AsSpan().IndexOf("moov"u8) >= 0 && bytes.AsSpan().IndexOf("mdat"u8) >= 0;

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

    async Task<WpfVideoProcessResult> IWpfVideoProcessRunner.RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            return new WpfVideoProcessResult(-1, string.Empty, $"Could not start '{executable}'.", false);

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new WpfVideoProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false),
                false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (InvalidOperationException)
            {
            }

            return new WpfVideoProcessResult(-1, string.Empty, string.Empty, true);
        }
    }
}
