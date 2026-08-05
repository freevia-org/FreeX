using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public interface IWindowsRecordingDeviceCatalog
{
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices();
}
