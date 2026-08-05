using System.IO;
using System.Security.Cryptography;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed class WindowsRecordingCaptureBackend : ISlideShowRecordingCaptureBackend
{
    private const string NoDevicesReason = "No Windows microphone or camera devices were reported by the host OS.";

    private readonly WindowsRecordingHostMetadata _metadata;
    private readonly IWindowsRecordingCaptureEngine _captureEngine;

    public WindowsRecordingCaptureBackend(
        WindowsRecordingHostMetadata metadata,
        IWindowsRecordingDeviceCatalog deviceCatalog,
        IWindowsRecordingCaptureEngine captureEngine)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        ArgumentNullException.ThrowIfNull(captureEngine);

        _metadata = metadata;
        _captureEngine = captureEngine;
        AdapterReadiness = BuildReadiness(metadata, deviceCatalog);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public static WindowsRecordingCaptureBackend CreateUnavailable(WindowsRecordingHostMetadata metadata) =>
        new(
            metadata,
            UnavailableWindowsRecordingDeviceCatalog.Instance,
            UnavailableWindowsRecordingCaptureEngine.Instance);

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canCapture = request.Kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => AdapterReadiness.CanCaptureNarration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => AdapterReadiness.CanCaptureCamera,
            _ => false
        };
        if (!canCapture)
        {
            return;
        }

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == DeviceKind(request.Kind) &&
            device.IsAvailable);
        _captureEngine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device,
            request.SlideIndex,
            request.StartedAtUtc,
            NormalizePackagePath(request.SuggestedFileName, Extension(request.Kind))));
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canCapture = request.Kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => AdapterReadiness.CanCaptureNarration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => AdapterReadiness.CanCaptureCamera,
            _ => false
        };
        if (!canCapture)
            return SlideShowRecordingCaptureResult.Deferred($"{_metadata.AdapterName}: {AdapterReadiness.UnavailableReason}");

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == DeviceKind(request.Kind) &&
            device.IsAvailable);
        var capture = _captureEngine.CompleteCapture(new WindowsRecordingCaptureRequest(
            device,
            request.SlideIndex,
            request.DurationMs,
            NormalizePackagePath(request.SuggestedFileName, Extension(request.Kind))));

        if (!capture.IsCaptured)
            return SlideShowRecordingCaptureResult.Deferred(capture.StatusText);

        var fileName = capture.PackagePath.Split('/').Last();
        return SlideShowRecordingCaptureResult.Captured(
            capture.StatusText,
            capture.PackagePath,
            capture.PayloadBytes.Length,
            Convert.ToHexString(SHA256.HashData(capture.PayloadBytes)).ToLowerInvariant(),
            capture.PayloadBytes,
            fileName,
            ContentType(request.Kind));
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        WindowsRecordingHostMetadata metadata,
        IWindowsRecordingDeviceCatalog deviceCatalog)
    {
        try
        {
            var devices = deviceCatalog.EnumerateDevices()
                .Where(device => device.Kind is SlideShowRecordingCaptureDeviceKind.Microphone
                    or SlideShowRecordingCaptureDeviceKind.Camera)
                .ToArray();
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                metadata.HostName,
                metadata.AdapterName,
                devices,
                requiresUserPermission: true,
                devices.Any(device => device.IsAvailable) ? MissingDeviceReason(devices) : NoDevicesReason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                metadata.HostName,
                metadata.AdapterName,
                Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
                requiresUserPermission: true,
                $"Windows recording device enumeration failed: {ex.Message}");
        }
    }

    private static string MissingDeviceReason(IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices)
    {
        var hasMicrophone = devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);
        var hasCamera = devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);

        return (hasMicrophone, hasCamera) switch
        {
            (true, true) => string.Empty,
            (true, false) => "No Windows camera devices were reported by the host OS.",
            (false, true) => "No Windows microphone devices were reported by the host OS.",
            _ => NoDevicesReason
        };
    }

    private static SlideShowRecordingCaptureDeviceKind DeviceKind(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? SlideShowRecordingCaptureDeviceKind.Microphone
            : SlideShowRecordingCaptureDeviceKind.Camera;

    private static string Extension(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio ? ".wav" : ".mp4";

    private static string ContentType(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio ? "audio/wav" : "video/mp4";

    private string NormalizePackagePath(string suggestedFileName, string extension)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "slide-narration" + extension
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, extension);

        return $"{_metadata.PackageRoot}/{fileName}";
    }

    private sealed class UnavailableWindowsRecordingDeviceCatalog : IWindowsRecordingDeviceCatalog
    {
        public static UnavailableWindowsRecordingDeviceCatalog Instance { get; } = new();

        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() =>
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();
    }

    private sealed class UnavailableWindowsRecordingCaptureEngine : IWindowsRecordingCaptureEngine
    {
        public static UnavailableWindowsRecordingCaptureEngine Instance { get; } = new();

        public void BeginCapture(WindowsRecordingCaptureStartRequest request)
        {
        }

        public WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request) =>
            WindowsRecordingCaptureResult.Deferred("Windows recording capture is unavailable on this platform.");
    }
}

public sealed record WindowsRecordingHostMetadata(
    string HostName,
    string AdapterName,
    string PackageRoot);

public sealed record WindowsRecordingCaptureStartRequest(
    SlideShowRecordingCaptureDeviceDescriptor Device,
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    string PackagePath);

public sealed record WindowsRecordingCaptureRequest(
    SlideShowRecordingCaptureDeviceDescriptor Device,
    int SlideIndex,
    int DurationMs,
    string PackagePath);

public sealed record WindowsRecordingCaptureResult(
    bool IsCaptured,
    string StatusText,
    string PackagePath,
    byte[] PayloadBytes)
{
    public static WindowsRecordingCaptureResult Deferred(string statusText) =>
        new(false, statusText, string.Empty, Array.Empty<byte>());

    public static WindowsRecordingCaptureResult Captured(
        string statusText,
        string packagePath,
        byte[] payloadBytes) =>
        new(true, statusText, packagePath, payloadBytes);
}

public interface IWindowsRecordingCaptureEngine
{
    void BeginCapture(WindowsRecordingCaptureStartRequest request);

    WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request);
}
