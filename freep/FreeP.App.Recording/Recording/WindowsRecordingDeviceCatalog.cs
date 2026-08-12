using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public interface IWindowsRecordingDeviceCatalog
{
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices();
}

public sealed record WindowsRecordingDeviceAvailability(
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> Devices,
    bool HasMicrophone,
    bool HasCamera,
    string? DetectionFailure)
{
    public bool HasAvailableDevice => HasMicrophone || HasCamera;
}

public static class WindowsRecordingDeviceAvailabilityPlanner
{
    public static WindowsRecordingDeviceAvailability Detect(
        IWindowsRecordingDeviceCatalog deviceCatalog)
    {
        ArgumentNullException.ThrowIfNull(deviceCatalog);

        try
        {
            var devices = deviceCatalog.EnumerateDevices()
                .Where(device => device.Kind is SlideShowRecordingCaptureDeviceKind.Microphone
                    or SlideShowRecordingCaptureDeviceKind.Camera)
                .ToArray();
            return new WindowsRecordingDeviceAvailability(
                devices,
                HasAvailable(devices, SlideShowRecordingCaptureDeviceKind.Microphone),
                HasAvailable(devices, SlideShowRecordingCaptureDeviceKind.Camera),
                DetectionFailure: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new WindowsRecordingDeviceAvailability(
                Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
                HasMicrophone: false,
                HasCamera: false,
                ex.Message);
        }
    }

    private static bool HasAvailable(
        IEnumerable<SlideShowRecordingCaptureDeviceDescriptor> devices,
        SlideShowRecordingCaptureDeviceKind kind) =>
        devices.Any(device => device.Kind == kind && device.IsAvailable);
}
