namespace FreeP.App.Compositor;

public enum SlideShowRecordingCaptureDeviceKind
{
    Microphone,
    Camera
}

public enum SlideShowRecordingCaptureStreamKind
{
    NarrationAudio,
    CameraVideo
}

public sealed record SlideShowRecordingCaptureDeviceDescriptor(
    SlideShowRecordingCaptureDeviceKind Kind,
    string DeviceId,
    string DisplayName,
    bool IsDefault,
    bool IsAvailable,
    string ContentType);

public sealed record SlideShowRecordingCaptureAdapterReadiness(
    string HostName,
    string AdapterName,
    bool RequiresUserPermission,
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> Devices,
    string UnavailableReason)
{
    public bool CanCaptureNarration =>
        Devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);

    public bool CanCaptureCamera =>
        Devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);

    public IReadOnlyList<SlideShowRecordingCaptureStreamKind> ReadyStreams =>
        BuildStreamList(CanCaptureNarration, CanCaptureCamera);

    public IReadOnlyList<SlideShowRecordingCaptureStreamKind> MissingStreams =>
        BuildStreamList(!CanCaptureNarration, !CanCaptureCamera);

    public string StatusText =>
        CanCaptureNarration || CanCaptureCamera
            ? $"{AdapterName}: {ReadyStreams.Count} capture stream(s) ready"
            : $"{AdapterName}: {UnavailableReason}";

    public static SlideShowRecordingCaptureAdapterReadiness Deferred(
        string hostName,
        string adapterName,
        string unavailableReason = "Recording capture adapter is not registered for this host.") =>
        new(
            Normalize(hostName, "Slideshow host"),
            Normalize(adapterName, "Recording capture adapter"),
            RequiresUserPermission: false,
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            Normalize(unavailableReason, "Recording capture adapter is not registered for this host."));

    public static SlideShowRecordingCaptureAdapterReadiness FromDevices(
        string hostName,
        string adapterName,
        IEnumerable<SlideShowRecordingCaptureDeviceDescriptor> devices,
        bool requiresUserPermission = true,
        string unavailableReason = "No available microphone or camera devices were reported by the host adapter.") =>
        new(
            Normalize(hostName, "Slideshow host"),
            Normalize(adapterName, "Recording capture adapter"),
            requiresUserPermission,
            devices?.ToArray() ?? Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            Normalize(unavailableReason, "No available microphone or camera devices were reported by the host adapter."));

    private static IReadOnlyList<SlideShowRecordingCaptureStreamKind> BuildStreamList(
        bool narration,
        bool camera)
    {
        var streams = new List<SlideShowRecordingCaptureStreamKind>(capacity: 2);
        if (narration)
        {
            streams.Add(SlideShowRecordingCaptureStreamKind.NarrationAudio);
        }

        if (camera)
        {
            streams.Add(SlideShowRecordingCaptureStreamKind.CameraVideo);
        }

        return streams;
    }

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public static class SlideShowRecordingCaptureAdapterPlanner
{
    public static SlideShowRecordingHostCapabilities BuildCapabilities(
        SlideShowRecordingCaptureAdapterReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        return new SlideShowRecordingHostCapabilities(
            readiness.HostName,
            readiness.CanCaptureNarration,
            readiness.CanCaptureCamera,
            readiness.MissingStreams.Count == 0
                ? string.Empty
                : readiness.UnavailableReason,
            readiness);
    }

    public static SlideShowRecordingCaptureAdapterReadiness BuildDeferredReadiness(
        string hostName,
        string adapterName) =>
        SlideShowRecordingCaptureAdapterReadiness.Deferred(hostName, adapterName);
}
