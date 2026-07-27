using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed class LinuxCameraCaptureBackend : ISlideShowRecordingCaptureBackend, IDisposable
{
    private readonly LinuxMediaCaptureLifecycle _lifecycle;

    public LinuxCameraCaptureBackend(LinuxRecordingHostMetadata metadata)
        : this(
            metadata,
            new LinuxCameraDeviceCatalog(),
            new LinuxRecordingProcessAdapter())
    {
    }

    public LinuxCameraCaptureBackend(
        LinuxRecordingHostMetadata metadata,
        ILinuxCameraDeviceCatalog deviceCatalog,
        ILinuxRecordingProcessAdapter processAdapter)
    {
        _ = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        ArgumentNullException.ThrowIfNull(processAdapter);

        var discovery = deviceCatalog.Discover();
        AdapterReadiness = BuildReadiness(metadata, discovery);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
        _lifecycle = new LinuxMediaCaptureLifecycle(
            metadata,
            AdapterReadiness,
            new LinuxCameraMediaCapturePolicy(discovery.Tool),
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

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        LinuxRecordingHostMetadata metadata,
        LinuxCameraCaptureDiscovery discovery) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            metadata.HostName,
            metadata.AdapterName,
            discovery.Devices,
            requiresUserPermission: true,
            discovery.IsAvailable
                ? string.Empty
                : discovery.UnavailableReason);
}
