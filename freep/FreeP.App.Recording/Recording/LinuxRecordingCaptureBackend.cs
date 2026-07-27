using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed class LinuxRecordingCaptureBackend : ISlideShowRecordingCaptureBackend, IDisposable
{
    private readonly LinuxNarrationCaptureBackend _narration;
    private readonly LinuxCameraCaptureBackend _camera;

    public LinuxRecordingCaptureBackend(
        LinuxNarrationCaptureBackend narration,
        LinuxCameraCaptureBackend camera)
    {
        _narration = narration ?? throw new ArgumentNullException(nameof(narration));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));

        var narrationReadiness = narration.AdapterReadiness;
        var cameraReadiness = camera.AdapterReadiness;
        var devices = narrationReadiness.Devices.Concat(cameraReadiness.Devices).ToArray();
        var unavailableReason = string.Join(
            " ",
            new[] { narrationReadiness, cameraReadiness }
                .Where(readiness => !readiness.CanCaptureNarration && !readiness.CanCaptureCamera)
                .Select(readiness => readiness.UnavailableReason)
                .Where(reason => !string.IsNullOrWhiteSpace(reason)));
        AdapterReadiness = SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            narrationReadiness.HostName,
            "Linux slideshow recording capture adapter",
            devices,
            requiresUserPermission: narrationReadiness.RequiresUserPermission || cameraReadiness.RequiresUserPermission,
            unavailableReason);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Backend(request.Kind).BeginCapture(request);
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Backend(request.Kind).CompleteCapture(request);
    }

    public void Dispose()
    {
        _narration.Dispose();
        _camera.Dispose();
    }

    private ISlideShowRecordingCaptureBackend Backend(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.CameraVideo
            ? _camera
            : _narration;
}
