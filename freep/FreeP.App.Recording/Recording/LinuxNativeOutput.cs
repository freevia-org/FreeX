using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Recording;

public sealed record LinuxNativePrintCapability(
    bool CanPrint,
    string? ExecutablePath,
    string? PrinterName,
    string Reason)
{
    public static LinuxNativePrintCapability Unavailable(string reason) =>
        new(false, null, null, Normalize(reason, "No Linux print queue is available."));

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed record LinuxVideoEncoderCapability(
    bool CanEncodeMp4,
    string? ExecutablePath,
    string? EncoderName,
    bool CanCaptureNarration,
    string Reason,
    bool CanCaptureCameraAndMedia = false,
    bool CanMuxTimedCaptions = false)
{
    public static LinuxVideoEncoderCapability Unavailable(
        string reason,
        bool canCaptureNarration = false) =>
        new(false, null, null, canCaptureNarration, Normalize(reason, "No usable Linux MP4 encoder is available."));

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed record LinuxNativeOutputCapabilities(
    LinuxNativePrintCapability Print,
    LinuxVideoEncoderCapability Video)
{
    public static LinuxNativeOutputCapabilities Unavailable(string reason) =>
        new(
            LinuxNativePrintCapability.Unavailable(reason),
            LinuxVideoEncoderCapability.Unavailable(reason));
}

public sealed record LinuxNativePrintResult(
    bool Succeeded,
    bool Canceled,
    string StatusText,
    string? FailureReason,
    int? ExitCode)
{
    public static LinuxNativePrintResult Failed(string reason, int? exitCode = null) =>
        new(false, false, "Linux print handoff failed", reason, exitCode);

    public static LinuxNativePrintResult CanceledResult() =>
        new(false, true, "Linux print handoff canceled", null, null);

    public static LinuxNativePrintResult Success(int? exitCode) =>
        new(true, false, "Linux print handoff completed", null, exitCode);
}

public sealed record LinuxVideoExportResult(
    bool Succeeded,
    bool Canceled,
    string StatusText,
    string? FailureReason,
    string OutputPath,
    string? EncoderName,
    long ByteCount,
    int MuxedNarrationTrackCount = 0,
    int MuxedCameraTrackCount = 0,
    int MuxedCaptionTrackCount = 0)
{
    public static LinuxVideoExportResult Failed(string reason, string outputPath = "") =>
        new(false, false, "Linux video export failed", reason, outputPath, null, 0);

    public static LinuxVideoExportResult CanceledResult(string outputPath) =>
        new(false, true, "Linux video export canceled", null, outputPath, null, 0);

    public static LinuxVideoExportResult Success(
        string outputPath,
        string encoderName,
        long byteCount,
        int muxedNarrationTrackCount = 0,
        int muxedCameraTrackCount = 0,
        int muxedCaptionTrackCount = 0) =>
        new(
            true,
            false,
            BuildVideoSuccessText(muxedNarrationTrackCount, muxedCameraTrackCount, muxedCaptionTrackCount),
            null,
            outputPath,
            encoderName,
            byteCount,
            muxedNarrationTrackCount,
            muxedCameraTrackCount,
            muxedCaptionTrackCount);

    private static string BuildVideoSuccessText(int narrationCount, int cameraCount, int captionCount) =>
        narrationCount == 0 && cameraCount == 0 && captionCount == 0
            ? "Linux video export completed (video-only)"
            : $"Linux video export completed with {narrationCount} narration track(s), {cameraCount} camera track(s), and {captionCount} caption track(s)";
}

public sealed class LinuxNativeOutputCapabilityDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILinuxRecordingExecutableLocator _executableLocator;
    private readonly ILinuxRecordingProbeRunner _probeRunner;
    private readonly bool _isLinux;

    public LinuxNativeOutputCapabilityDetector()
        : this(
            new PathLinuxRecordingExecutableLocator(),
            new SystemLinuxRecordingProbeRunner(),
            OperatingSystem.IsLinux())
    {
    }

    public LinuxNativeOutputCapabilityDetector(
        ILinuxRecordingExecutableLocator executableLocator,
        ILinuxRecordingProbeRunner probeRunner,
        bool isLinux = true)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _probeRunner = probeRunner ?? throw new ArgumentNullException(nameof(probeRunner));
        _isLinux = isLinux;
    }

    public LinuxNativeOutputCapabilities Detect(bool? canCaptureNarrationOverride = null)
    {
        if (!_isLinux)
            return LinuxNativeOutputCapabilities.Unavailable("Linux native output is only available on Linux.");

        if (canCaptureNarrationOverride is { } canCaptureNarration)
        {
            return new LinuxNativeOutputCapabilities(
                DetectPrint(),
                DetectVideo(canCaptureNarration));
        }

        using var narration = new LinuxNarrationCaptureBackend(
            new LinuxRecordingHostMetadata(
                "FreeP",
                "FreeP Linux narration capture adapter",
                "ppt/media/freep-recordings/avalonia"));
        return new LinuxNativeOutputCapabilities(
            DetectPrint(),
            DetectVideo(narration.AdapterReadiness.CanCaptureNarration));
    }

    private LinuxNativePrintCapability DetectPrint()
    {
        var executable = _executableLocator.FindExecutable("lp") ??
            _executableLocator.FindExecutable("lpr");
        if (executable is null)
            return LinuxNativePrintCapability.Unavailable("Install CUPS lp or lpr to enable native Linux printing.");

        var lpstat = _executableLocator.FindExecutable("lpstat");
        if (lpstat is null)
            return LinuxNativePrintCapability.Unavailable("CUPS lpstat is required to verify an available Linux print queue.");

        var defaultResult = _probeRunner.Run(lpstat, ["-d"], ProbeTimeout);
        if (defaultResult.Succeeded)
        {
            var printer = ParseDefaultPrinter(defaultResult.StandardOutput);
            if (!string.IsNullOrWhiteSpace(printer))
                return new LinuxNativePrintCapability(
                    true,
                    executable,
                    printer,
                    $"Linux print queue '{printer}' is available through {Path.GetFileName(executable)}.");
        }

        var queueResult = _probeRunner.Run(lpstat, ["-a"], ProbeTimeout);
        var fallbackPrinter = queueResult.Succeeded ? ParseFirstPrinter(queueResult.StandardOutput) : null;
        if (!string.IsNullOrWhiteSpace(fallbackPrinter))
        {
            return new LinuxNativePrintCapability(
                true,
                executable,
                fallbackPrinter,
                $"Linux print queue '{fallbackPrinter}' is available through {Path.GetFileName(executable)}.");
        }

        var detail = FirstNonEmpty(
            defaultResult.StandardError,
            queueResult.StandardError,
            "CUPS reported no available printer queue.");
        return LinuxNativePrintCapability.Unavailable($"No available Linux print queue was detected: {detail}");
    }

    private LinuxVideoEncoderCapability DetectVideo(bool canCaptureNarration)
    {
        var executable = _executableLocator.FindExecutable("ffmpeg");
        if (executable is null)
        {
            return LinuxVideoEncoderCapability.Unavailable(
                "Install ffmpeg with a software MP4 encoder to enable Linux video export.",
                canCaptureNarration);
        }

        var result = _probeRunner.Run(
            executable,
            ["-hide_banner", "-encoders"],
            ProbeTimeout);
        if (!result.Succeeded)
        {
            return LinuxVideoEncoderCapability.Unavailable(
                $"ffmpeg encoder discovery failed: {FirstNonEmpty(result.StandardError, result.StandardOutput, $"exit code {result.ExitCode}")}",
                canCaptureNarration);
        }

        var encoder = SelectSoftwareEncoder(result.StandardOutput + Environment.NewLine + result.StandardError);
        return encoder is null
            ? LinuxVideoEncoderCapability.Unavailable(
                "ffmpeg is installed, but no supported software MP4 encoder was reported.",
                canCaptureNarration)
            : new LinuxVideoEncoderCapability(
                true,
                executable,
                encoder,
                canCaptureNarration,
                $"Linux video export can use ffmpeg encoder '{encoder}'.",
                CanMuxTimedCaptions: true);
    }

    internal static string? ParseDefaultPrinter(string output)
    {
        foreach (var line in SplitLines(output))
        {
            const string marker = "system default destination:";
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var value = line[(index + marker.Length)..].Trim();
                return value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : value;
            }
        }

        return null;
    }

    internal static string? ParseFirstPrinter(string output)
    {
        foreach (var line in SplitLines(output))
        {
            var value = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("printer", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    internal static string? SelectSoftwareEncoder(string output)
    {
        string[] preferred = ["libx264", "libopenh264", "mpeg4", "libxvid"];
        return preferred.FirstOrDefault(encoder =>
            output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Contains(encoder, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public interface ILinuxNativePrintHandoffAdapter
{
    LinuxNativePrintCapability Capability { get; }

    Task<LinuxNativePrintResult> PrintAsync(
        byte[] pdfBytes,
        string documentName,
        CancellationToken cancellationToken = default);
}

public sealed class LinuxNativePrintHandoffAdapter : ILinuxNativePrintHandoffAdapter
{
    private readonly LinuxNativePrintCapability _capability;

    public LinuxNativePrintHandoffAdapter(LinuxNativePrintCapability capability)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }

    public LinuxNativePrintCapability Capability => _capability;

    public async Task<LinuxNativePrintResult> PrintAsync(
        byte[] pdfBytes,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (!_capability.CanPrint || string.IsNullOrWhiteSpace(_capability.ExecutablePath))
            return LinuxNativePrintResult.Failed(_capability.Reason);
        if (!HasPdfPayload(pdfBytes))
            return LinuxNativePrintResult.Failed("The printable package is not a valid non-empty PDF.");

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"freep-print-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, pdfBytes, cancellationToken).ConfigureAwait(false);
            var executableName = Path.GetFileNameWithoutExtension(_capability.ExecutablePath);
            var arguments = executableName.Equals("lpr", StringComparison.OrdinalIgnoreCase)
                ? BuildLprArguments(temporaryPath, documentName)
                : BuildLpArguments(temporaryPath, documentName);
            return await RunAsync(
                _capability.ExecutablePath,
                arguments,
                cancellationToken,
                successText: "Linux print handoff completed.").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return LinuxNativePrintResult.CanceledResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return LinuxNativePrintResult.Failed(ex.Message);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private IReadOnlyList<string> BuildLpArguments(string path, string documentName) =>
        ["-d", _capability.PrinterName ?? string.Empty, "-t", NormalizeJobName(documentName), path];

    private IReadOnlyList<string> BuildLprArguments(string path, string documentName) =>
        ["-P", _capability.PrinterName ?? string.Empty, "-J", NormalizeJobName(documentName), path];

    private static string NormalizeJobName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "FreeP presentation" : value.Trim();

    private static async Task<LinuxNativePrintResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string successText)
    {
        using var process = CreateProcess(executable, arguments);
        try
        {
            if (!process.Start())
                return LinuxNativePrintResult.Failed($"Could not start '{executable}'.");

            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            return process.ExitCode == 0
                ? new LinuxNativePrintResult(true, false, successText, null, process.ExitCode)
                : LinuxNativePrintResult.Failed(
                    FirstNonEmpty(error, output, $"'{executable}' exited with code {process.ExitCode}"),
                    process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return LinuxNativePrintResult.CanceledResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryKill(process);
            return LinuxNativePrintResult.Failed(ex.Message);
        }
    }

    private static Process CreateProcess(string executable, IReadOnlyList<string> arguments)
    {
        var process = new Process
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
        return process;
    }

    internal static bool HasPdfPayload(byte[] bytes) =>
        bytes.Length > 8 &&
        bytes.AsSpan().StartsWith("%PDF-"u8) &&
        bytes.AsSpan().IndexOf("%%EOF"u8) >= 0;

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

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

public interface ILinuxVideoExportAdapter
{
    LinuxVideoEncoderCapability Capability { get; }

    Task<LinuxVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null);
}

public sealed class LinuxVideoExportAdapter : ILinuxVideoExportAdapter
{
    private readonly LinuxVideoEncoderCapability _capability;
    private readonly ILinuxNativeProcessRunner _processRunner;

    public LinuxVideoExportAdapter(
        LinuxVideoEncoderCapability capability,
        ILinuxNativeProcessRunner? processRunner = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _processRunner = processRunner ?? new ProcessLinuxNativeProcessRunner();
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
        if (!_capability.CanEncodeMp4 || string.IsNullOrWhiteSpace(_capability.ExecutablePath))
            return LinuxVideoExportResult.Failed(_capability.Reason, outputPath);

        var validation = PresentationVideoFramePackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
            return LinuxVideoExportResult.Failed(
                validation.FailureReason ?? "Video frame package validation failed.",
                outputPath);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"freep-video-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var concatPath = ExtractFramesAndBuildConcatFile(package, temporaryDirectory);
            var mediaPlan = PresentationVideoMediaMuxPlanner.Prepare(
                package,
                mediaArtifacts,
                temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
                concatPath,
                outputPath,
                _capability.EncoderName!,
                mediaPlan);
            var processResult = await RunFfmpegAsync(
                _capability.ExecutablePath,
                arguments,
                outputPath,
                cancellationToken).ConfigureAwait(false);
            if (processResult is not null)
            {
                if (!processResult.Succeeded)
                    TryDelete(outputPath);
                return processResult;
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
            if (!HasNonEmptyMp4Payload(bytes))
            {
                TryDelete(outputPath);
                return LinuxVideoExportResult.Failed(
                    "ffmpeg completed but did not produce a valid non-empty MP4 file.",
                    outputPath);
            }

            return LinuxVideoExportResult.Success(
                outputPath,
                _capability.EncoderName!,
                bytes.LongLength,
                mediaPlan.MuxedNarrationTrackCount,
                mediaPlan.MuxedCameraTrackCount,
                mediaPlan.MuxedCaptionTrackCount);
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            return LinuxVideoExportResult.CanceledResult(outputPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDelete(outputPath);
            return LinuxVideoExportResult.Failed(ex.Message, outputPath);
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
            var payload = File.ReadAllBytes(framePath);
            if (payload.Length == 0)
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

    private async Task<LinuxVideoExportResult?> RunFfmpegAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(executable, arguments, cancellationToken).ConfigureAwait(false);
        if (result.Canceled)
            return LinuxVideoExportResult.CanceledResult(outputPath);
        if (result.ExitCode == 0)
            return null;
        return LinuxVideoExportResult.Failed(
            string.IsNullOrWhiteSpace(result.StandardError)
                ? $"ffmpeg exited with code {result.ExitCode}."
                : result.StandardError.Trim(),
            outputPath);
    }

    internal static bool HasNonEmptyMp4Payload(byte[] bytes)
    {
        if (bytes.Length < 16 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            return false;

        return bytes.AsSpan().IndexOf("moov"u8) >= 0 && bytes.AsSpan().IndexOf("mdat"u8) >= 0;
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

public sealed record LinuxNativeProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool Canceled);

public interface ILinuxNativeProcessRunner
{
    Task<LinuxNativeProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class ProcessLinuxNativeProcessRunner : ILinuxNativeProcessRunner
{
    public async Task<LinuxNativeProcessResult> RunAsync(
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
            return new LinuxNativeProcessResult(-1, string.Empty, $"Could not start '{executable}'.", false);

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new LinuxNativeProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false),
                false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return new LinuxNativeProcessResult(
                -1,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false),
                true);
        }
        catch
        {
            TryKill(process);
            throw;
        }
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
