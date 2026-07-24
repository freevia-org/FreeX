using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private bool _allowCloseWithoutDirtyPromptForPhysicalValidation;

    internal LinuxNativeOutputCapabilities NativeOutputCapabilitiesForPhysicalValidation =>
        _nativeOutputCapabilities;

    internal Presentation PresentationForPhysicalValidation => _presentation;

    internal void AllowCloseWithoutDirtyPromptForPhysicalValidation() =>
        _allowCloseWithoutDirtyPromptForPhysicalValidation = true;
}
