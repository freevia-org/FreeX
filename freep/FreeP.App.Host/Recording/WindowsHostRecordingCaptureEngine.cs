using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Recording;

/// <summary>WPF naming boundary for the shared Windows Runtime recording engine.</summary>
internal sealed class WindowsHostRecordingCaptureEngine : WindowsNativeRecordingCaptureEngine
{
    public WindowsHostRecordingCaptureEngine(string adapterName)
        : base(adapterName)
    {
    }
}
