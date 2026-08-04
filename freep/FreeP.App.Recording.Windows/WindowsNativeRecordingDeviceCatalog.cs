using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Enumerates the same WinRT device identities consumed by <see cref="MediaCapture"/>.
/// The shared SetupAPI catalog remains useful for non-WinRT recording paths, but its
/// interface identifiers are not a reliable input to the WinRT camera stack.
/// </summary>
public sealed class WindowsNativeRecordingDeviceCatalog : IWindowsRecordingDeviceCatalog
{
    public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();

        var devices = new List<SlideShowRecordingCaptureDeviceDescriptor>();
        AddDevices(devices, DeviceClass.AudioCapture, SlideShowRecordingCaptureDeviceKind.Microphone, "audio/wav");
        AddDevices(devices, DeviceClass.VideoCapture, SlideShowRecordingCaptureDeviceKind.Camera, "video/mp4");
        return devices;
    }

    private static void AddDevices(
        List<SlideShowRecordingCaptureDeviceDescriptor> devices,
        DeviceClass deviceClass,
        SlideShowRecordingCaptureDeviceKind kind,
        string contentType)
    {
        var discovered = DeviceInformation.FindAllAsync(deviceClass)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        var defaultAudioId = kind == SlideShowRecordingCaptureDeviceKind.Microphone
            ? MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default)
            : string.Empty;

        for (var index = 0; index < discovered.Count; index++)
        {
            var device = discovered[index];
            if (!device.IsEnabled)
                continue;

            devices.Add(new SlideShowRecordingCaptureDeviceDescriptor(
                kind,
                device.Id,
                string.IsNullOrWhiteSpace(device.Name) ? $"{kind} {index + 1}" : device.Name,
                IsDefault: string.Equals(device.Id, defaultAudioId, StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrWhiteSpace(defaultAudioId) && index == 0),
                IsAvailable: true,
                contentType));
        }
    }
}
