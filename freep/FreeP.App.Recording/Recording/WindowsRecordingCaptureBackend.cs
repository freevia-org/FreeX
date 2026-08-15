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

        if (!SlideShowRecordingMediaArtifactPolicy.TryDescribe(request.Kind, out var artifact))
            return;
        if (!SlideShowRecordingMediaArtifactPolicy.CanCapture(artifact, AdapterReadiness))
        {
            return;
        }

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == artifact.DeviceKind &&
            device.IsAvailable);
        _captureEngine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device,
            request.SlideIndex,
            request.StartedAtUtc,
            SlideShowRecordingMediaArtifactPolicy.NormalizePackagePath(
                request.Kind,
                _metadata.PackageRoot,
                request.SuggestedFileName,
                "ppt/media/freep-recordings/windows")));
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SlideShowRecordingMediaArtifactPolicy.TryDescribe(request.Kind, out var artifact))
            return SlideShowRecordingCaptureResult.Deferred($"{_metadata.AdapterName}: {AdapterReadiness.UnavailableReason}");
        if (!SlideShowRecordingMediaArtifactPolicy.CanCapture(artifact, AdapterReadiness))
            return SlideShowRecordingCaptureResult.Deferred($"{_metadata.AdapterName}: {AdapterReadiness.UnavailableReason}");

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == artifact.DeviceKind &&
            device.IsAvailable);
        var capture = _captureEngine.CompleteCapture(new WindowsRecordingCaptureRequest(
            device,
            request.SlideIndex,
            request.DurationMs,
            SlideShowRecordingMediaArtifactPolicy.NormalizePackagePath(
                request.Kind,
                _metadata.PackageRoot,
                request.SuggestedFileName,
                "ppt/media/freep-recordings/windows")));

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
            artifact.ContentType);
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        WindowsRecordingHostMetadata metadata,
        IWindowsRecordingDeviceCatalog deviceCatalog)
    {
        var availability = WindowsRecordingDeviceAvailabilityPlanner.Detect(deviceCatalog);
        if (availability.DetectionFailure is { } failure)
        {
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                metadata.HostName,
                metadata.AdapterName,
                Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
                requiresUserPermission: true,
                $"Windows recording device enumeration failed: {failure}");
        }

        return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            metadata.HostName,
            metadata.AdapterName,
            availability.Devices,
            requiresUserPermission: true,
            availability.HasAvailableDevice ? MissingDeviceReason(availability) : NoDevicesReason);
    }

    private static string MissingDeviceReason(WindowsRecordingDeviceAvailability availability) =>
        (availability.HasMicrophone, availability.HasCamera) switch
        {
            (true, true) => string.Empty,
            (true, false) => "No Windows camera devices were reported by the host OS.",
            (false, true) => "No Windows microphone devices were reported by the host OS.",
            _ => NoDevicesReason
        };

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
