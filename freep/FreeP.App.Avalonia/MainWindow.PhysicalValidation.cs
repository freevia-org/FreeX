using Free.Shared.AppServices.Printing;
using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private bool _allowCloseWithoutDirtyPromptForPhysicalValidation;

    internal LinuxNativeOutputCapabilities NativeOutputCapabilitiesForPhysicalValidation =>
        _nativeOutputCapabilities;

    internal bool NativeOutputCapabilityDetectionCompletedForPhysicalValidation =>
        _nativeOutputDetectionCompleted;

    internal Task<PrinterDiscoveryResult> DiscoverPrintersForPhysicalValidationAsync(
        CancellationToken cancellationToken = default) =>
        _printService.DiscoverAsync(cancellationToken);

    internal Presentation PresentationForPhysicalValidation => _presentation;

    internal void AllowCloseWithoutDirtyPromptForPhysicalValidation() =>
        _allowCloseWithoutDirtyPromptForPhysicalValidation = true;
}
