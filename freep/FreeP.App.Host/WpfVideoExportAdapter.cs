using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Selects the Windows-native encoder when available and otherwise delegates ffmpeg probing to
/// the portable recording capability detector.
/// </summary>
internal static class WpfVideoEncoderCapabilityDetector
{
    public static LinuxVideoEncoderCapability Detect() =>
        OperatingSystem.IsWindows()
            ? DetectWindowsCaptureCapability(new WindowsNativeRecordingDeviceCatalog())
            : new LinuxNativeOutputCapabilityDetector(
                    new PathLinuxRecordingExecutableLocator(),
                    new SystemLinuxRecordingProbeRunner())
                .Detect(canCaptureNarrationOverride: false)
                .Video;

    internal static LinuxVideoEncoderCapability DetectWindowsCaptureCapability(
        IWindowsRecordingDeviceCatalog deviceCatalog) =>
        WindowsNativePrintOutput.DetectWindowsVideoCapability(deviceCatalog);

    internal static string? SelectSoftwareEncoder(string output) =>
        LinuxNativeOutputCapabilityDetector.SelectSoftwareEncoder(output);
}

/// <summary>
/// WPF backend selector over the portable recording export contract. Package validation,
/// temporary ownership, ffmpeg execution, media muxing, cancellation, and output validation
/// remain in FreeP.App.Recording.
/// </summary>
internal sealed class WpfVideoExportAdapter : ILinuxVideoExportAdapter
{
    private readonly ILinuxVideoExportAdapter _inner;

    public WpfVideoExportAdapter(
        LinuxVideoEncoderCapability capability,
        IProcessRunner? processRunner = null)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _inner = string.Equals(
                capability.ExecutablePath,
                WindowsNativeVideoExportAdapter.ExecutablePath,
                StringComparison.Ordinal)
            ? new WindowsNativeVideoExportAdapter(capability)
            : new LinuxVideoExportAdapter(capability, processRunner);
    }

    public LinuxVideoEncoderCapability Capability { get; }

    public Task<LinuxVideoExportResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null) =>
        _inner.ExportAsync(package, outputPath, cancellationToken, mediaArtifacts);
}
