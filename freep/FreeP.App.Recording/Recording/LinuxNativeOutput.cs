using System.Diagnostics;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
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

    public static string? SelectSoftwareEncoder(string output)
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

        try
        {
            using var temporaryFile = TemporaryFileLease.Create("freep-print-", ".pdf");
            var temporaryPath = temporaryFile.Path;
            await temporaryFile.WriteAllBytesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
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
    }

    private IReadOnlyList<string> BuildLpArguments(string path, string documentName) =>
        ["-d", _capability.PrinterName ?? string.Empty, "-t", NormalizeJobName(documentName), path];

    private IReadOnlyList<string> BuildLprArguments(string path, string documentName) =>
        ["-P", _capability.PrinterName ?? string.Empty, "-J", NormalizeJobName(documentName), path];

    private static string NormalizeJobName(string value) =>
        PresentationFileTextResources.NormalizePrintJobName(value);

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
    private readonly PresentationVideoExportOrchestrator _orchestrator;

    public LinuxVideoExportAdapter(
        LinuxVideoEncoderCapability capability,
        IProcessRunner? processRunner = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _orchestrator = new PresentationVideoExportOrchestrator(
            capability,
            new FfmpegVideoExportBackend(
                capability,
                processRunner ?? new SystemProcessRunner()),
            new PresentationVideoExportOrchestrationOptions(
                TemporaryDirectoryPrefix: "freep-video-",
                InitialStage: "running ffmpeg",
                InvalidOutputReason: "ffmpeg completed but did not produce a valid non-empty MP4 file.",
                CanExport: static value =>
                    value.CanEncodeMp4 &&
                    !string.IsNullOrWhiteSpace(value.ExecutablePath) &&
                    !string.IsNullOrWhiteSpace(value.EncoderName),
                FormatFailureReason: static (_, ex) => ex.Message,
                BuildFfmpegConcatFile: true,
                RequireNonEmptyFrames: true));
    }

    public LinuxVideoEncoderCapability Capability => _capability;

    public Task<LinuxVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null) =>
        _orchestrator.ExportAsync(package, outputPath, cancellationToken, mediaArtifacts);

    internal static bool HasNonEmptyMp4Payload(byte[] bytes)
        => PresentationVideoExportOrchestrator.HasNonEmptyMp4Payload(bytes);

    private sealed class FfmpegVideoExportBackend(
        LinuxVideoEncoderCapability capability,
        IProcessRunner processRunner) : IPresentationVideoExportBackend
    {
        public async Task<PresentationVideoExportBackendResult> EncodeAsync(
            PresentationVideoExportWorkspace workspace,
            PresentationVideoExportStage stage,
            CancellationToken cancellationToken)
        {
            var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
                workspace.ConcatPath!,
                workspace.OutputPath,
                capability.EncoderName!,
                workspace.MediaPlan);
            var result = await processRunner.RunAsync(
                new ProcessInvocation(capability.ExecutablePath!, arguments),
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return PresentationVideoExportBackendResult.Failed(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"ffmpeg exited with code {result.ExitCode}."
                        : result.StandardError.Trim());
            }

            return PresentationVideoExportBackendResult.Encoded(
                capability.EncoderName!,
                workspace.MediaPlan.MuxedNarrationTrackCount,
                workspace.MediaPlan.MuxedCameraTrackCount,
                workspace.MediaPlan.MuxedCaptionTrackCount);
        }
    }
}
