using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Recording;

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

public sealed record LinuxNativeOutputCapabilities(LinuxVideoEncoderCapability Video)
{
    public static LinuxNativeOutputCapabilities Unavailable(string reason) =>
        new(LinuxVideoEncoderCapability.Unavailable(reason));
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
        new(
            false,
            false,
            PresentationNativeCommandOutcomePlanner.BuildVideoExportStatusText(false, false),
            reason,
            outputPath,
            null,
            0);

    public static LinuxVideoExportResult CanceledResult(string outputPath) =>
        new(
            false,
            true,
            PresentationNativeCommandOutcomePlanner.BuildVideoExportStatusText(false, true),
            null,
            outputPath,
            null,
            0);

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
            PresentationNativeCommandOutcomePlanner.BuildVideoExportStatusText(
                true,
                false,
                muxedNarrationTrackCount,
                muxedCameraTrackCount,
                muxedCaptionTrackCount),
            null,
            outputPath,
            encoderName,
            byteCount,
            muxedNarrationTrackCount,
            muxedCameraTrackCount,
            muxedCaptionTrackCount);
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
            return new LinuxNativeOutputCapabilities(DetectVideo(canCaptureNarration));

        using var narration = new LinuxNarrationCaptureBackend(
            new LinuxRecordingHostMetadata(
                "FreeP",
                "FreeP Linux narration capture adapter",
                "ppt/media/freep-recordings/avalonia"));
        return new LinuxNativeOutputCapabilities(
            DetectVideo(narration.AdapterReadiness.CanCaptureNarration));
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

    public static string? SelectSoftwareEncoder(string output)
    {
        string[] preferred = ["libx264", "libopenh264", "mpeg4", "libxvid"];
        return preferred.FirstOrDefault(encoder =>
            output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Contains(encoder, StringComparison.OrdinalIgnoreCase)));
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
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
