using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed record WpfVideoEncoderCapability(
    bool CanEncodeMp4,
    string? ExecutablePath,
    string? EncoderName,
    string Reason)
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
    long ByteCount)
{
    public static WpfVideoExportResult Failed(string reason, string outputPath = "") =>
        new(false, false, "Video export failed", reason, outputPath, null, 0);

    public static WpfVideoExportResult CanceledResult(string outputPath) =>
        new(false, true, "Video export canceled", null, outputPath, null, 0);

    public static WpfVideoExportResult Success(string outputPath, string encoderName, long byteCount) =>
        new(true, false, "Video export completed (video-only; narration and camera/media were not muxed)",
            null, outputPath, encoderName, byteCount);
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
                    $"WPF video export can use ffmpeg encoder '{encoder}'.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WpfVideoEncoderCapability.Unavailable(
                $"ffmpeg encoder discovery failed: {ex.Message}");
        }
    }

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (cancellationToken.IsCancellationRequested)
            return WpfVideoExportResult.CanceledResult(outputPath);
        if (!_capability.CanEncodeMp4 || string.IsNullOrWhiteSpace(_capability.ExecutablePath) ||
            string.IsNullOrWhiteSpace(_capability.EncoderName))
            return WpfVideoExportResult.Failed(_capability.Reason, outputPath);

        var validation = PresentationVideoFramePackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
            return WpfVideoExportResult.Failed(
                validation.FailureReason ?? "Video frame package validation failed.", outputPath);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"freep-video-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var concatPath = ExtractFramesAndBuildConcatFile(package, temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var processResult = await _processRunner.RunAsync(
                _capability.ExecutablePath,
                BuildFfmpegArguments(concatPath, outputPath, _capability.EncoderName),
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

            return WpfVideoExportResult.Success(outputPath, _capability.EncoderName, bytes.LongLength);
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
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
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

    private static IReadOnlyList<string> BuildFfmpegArguments(
        string concatPath,
        string outputPath,
        string encoderName) =>
    [
        "-hide_banner",
        "-loglevel", "error",
        "-y",
        "-f", "concat",
        "-safe", "0",
        "-i", concatPath,
        "-an",
        "-c:v", encoderName,
        "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        outputPath,
    ];

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
