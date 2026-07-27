using System.Globalization;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed record LinuxCameraCaptureTool(
    string ExecutablePath,
    string EncoderName,
    string DisplayName);

public sealed record LinuxCameraCaptureDiscovery(
    LinuxCameraCaptureTool? Tool,
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> Devices,
    string UnavailableReason)
{
    public bool IsAvailable =>
        Tool is not null &&
        Devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);

    public static LinuxCameraCaptureDiscovery Unavailable(string reason) =>
        new(
            Tool: null,
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            string.IsNullOrWhiteSpace(reason)
                ? "No Linux camera or software MP4 encoder is available."
                : reason.Trim());
}

public interface ILinuxCameraDeviceCatalog
{
    LinuxCameraCaptureDiscovery Discover();
}

public sealed class LinuxCameraDeviceCatalog : ILinuxCameraDeviceCatalog
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILinuxRecordingExecutableLocator _executableLocator;
    private readonly ILinuxRecordingProbeRunner _probeRunner;
    private readonly Func<IEnumerable<string>> _deviceEnumerator;
    private readonly bool _isLinux;

    public LinuxCameraDeviceCatalog()
        : this(
            new PathLinuxRecordingExecutableLocator(),
            new SystemLinuxRecordingProbeRunner(),
            EnumerateVideoDevices,
            OperatingSystem.IsLinux())
    {
    }

    public LinuxCameraDeviceCatalog(
        ILinuxRecordingExecutableLocator executableLocator,
        ILinuxRecordingProbeRunner probeRunner,
        Func<IEnumerable<string>>? deviceEnumerator = null,
        bool isLinux = true)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _probeRunner = probeRunner ?? throw new ArgumentNullException(nameof(probeRunner));
        _deviceEnumerator = deviceEnumerator ?? EnumerateVideoDevices;
        _isLinux = isLinux;
    }

    public LinuxCameraCaptureDiscovery Discover()
    {
        if (!_isLinux)
            return LinuxCameraCaptureDiscovery.Unavailable("Linux camera capture is only available on Linux.");

        var ffmpeg = _executableLocator.FindExecutable("ffmpeg");
        if (ffmpeg is null)
            return LinuxCameraCaptureDiscovery.Unavailable(
                "Install ffmpeg with a software MP4 encoder to enable Linux camera capture.");

        var encoderProbe = _probeRunner.Run(
            ffmpeg,
            ["-hide_banner", "-encoders"],
            ProbeTimeout);
        if (!encoderProbe.Succeeded)
        {
            var detail = FirstNonEmpty(
                encoderProbe.StandardError,
                encoderProbe.StandardOutput,
                $"exit code {encoderProbe.ExitCode}");
            return LinuxCameraCaptureDiscovery.Unavailable(
                $"Linux camera encoder discovery failed: {detail}");
        }

        var encoder = LinuxNativeOutputCapabilityDetector.SelectSoftwareEncoder(
            encoderProbe.StandardOutput + Environment.NewLine + encoderProbe.StandardError);
        if (encoder is null)
            return LinuxCameraCaptureDiscovery.Unavailable(
                "ffmpeg is installed, but no supported software MP4 encoder was reported.");

        string[] paths;
        try
        {
            paths = _deviceEnumerator()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Where(File.Exists)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LinuxCameraCaptureDiscovery.Unavailable(
                $"Linux camera device discovery failed: {ex.Message}");
        }

        if (paths.Length == 0)
            return LinuxCameraCaptureDiscovery.Unavailable(
                "No Linux V4L2 camera devices were reported under /dev/video*.");

        var devices = paths
            .Select((path, index) => new SlideShowRecordingCaptureDeviceDescriptor(
                SlideShowRecordingCaptureDeviceKind.Camera,
                path,
                $"Camera {index + 1}",
                IsDefault: index == 0,
                IsAvailable: true,
                "video/mp4"))
            .ToArray();

        return new LinuxCameraCaptureDiscovery(
            new LinuxCameraCaptureTool(ffmpeg, encoder, "ffmpeg V4L2 camera recorder"),
            devices,
            string.Empty);
    }

    private static IEnumerable<string> EnumerateVideoDevices()
    {
        if (!Directory.Exists("/dev"))
            return Array.Empty<string>();

        return Directory.EnumerateFileSystemEntries("/dev", "video*");
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public static class LinuxCameraCapturePlanner
{
    public const int FrameRate = 30;
    public const int Width = 1280;
    public const int Height = 720;

    public static LinuxNarrationCaptureCommand BuildCaptureCommand(
        LinuxCameraCaptureTool tool,
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (device.Kind != SlideShowRecordingCaptureDeviceKind.Camera)
            throw new ArgumentException("Linux camera capture requires a camera device.", nameof(device));

        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-f", "v4l2",
            "-framerate", FrameRate.ToString(CultureInfo.InvariantCulture),
            "-video_size", $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}",
            "-i", device.DeviceId,
            "-an",
            "-c:v", tool.EncoderName,
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            outputPath
        };

        return new LinuxNarrationCaptureCommand(
            tool.ExecutablePath,
            arguments,
            outputPath,
            LinuxNarrationCaptureToolKind.FfmpegCamera);
    }
}
