using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed class LinuxNarrationCaptureBackend : ISlideShowRecordingCaptureBackend, IDisposable
{
    private readonly LinuxMediaCaptureLifecycle _lifecycle;

    public LinuxNarrationCaptureBackend(LinuxRecordingHostMetadata metadata)
        : this(
            metadata,
            new LinuxRecordingDeviceCatalog(),
            new LinuxRecordingProcessAdapter())
    {
    }

    public LinuxNarrationCaptureBackend(
        LinuxRecordingHostMetadata metadata,
        ILinuxRecordingDeviceCatalog deviceCatalog,
        ILinuxRecordingProcessAdapter processAdapter)
    {
        _ = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        ArgumentNullException.ThrowIfNull(processAdapter);

        var discovery = Discover(deviceCatalog);
        AdapterReadiness = BuildReadiness(metadata, discovery);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
        _lifecycle = new LinuxMediaCaptureLifecycle(
            metadata,
            AdapterReadiness,
            new LinuxNarrationMediaCapturePolicy(discovery.Tool),
            processAdapter);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request) =>
        _lifecycle.BeginCapture(request);

    public SlideShowRecordingCaptureResult CompleteCapture(
        SlideShowRecordingCaptureRequest request) =>
        _lifecycle.CompleteCapture(request);

    public void CancelCapture(int slideIndex) =>
        _lifecycle.CancelCapture(slideIndex);

    public void Dispose() => _lifecycle.Dispose();

    private static LinuxNarrationCaptureDiscovery Discover(
        ILinuxRecordingDeviceCatalog deviceCatalog)
    {
        try
        {
            return deviceCatalog.Discover();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return LinuxNarrationCaptureDiscovery.Unavailable(
                $"Linux recording device discovery failed: {ex.Message}");
        }
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        LinuxRecordingHostMetadata metadata,
        LinuxNarrationCaptureDiscovery discovery) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            metadata.HostName,
            metadata.AdapterName,
            discovery.Devices,
            requiresUserPermission: true,
            discovery.IsAvailable
                ? "Linux camera recording is not available in the narration adapter."
                : discovery.UnavailableReason);
}
