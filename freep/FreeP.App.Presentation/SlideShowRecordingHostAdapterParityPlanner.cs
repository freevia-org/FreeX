namespace FreeP.App.Compositor;

public sealed record SlideShowRecordingHostAdapterParityRow(
    string HostName,
    string AdapterName,
    bool CanCaptureNarration,
    bool CanCaptureCamera,
    bool RequiresUserPermission,
    IReadOnlyList<SlideShowRecordingCaptureStreamKind> ReadyStreams,
    IReadOnlyList<SlideShowRecordingCaptureStreamKind> MissingStreams,
    string StatusText);

public sealed record SlideShowRecordingHostAdapterParityEvidence(
    IReadOnlyList<SlideShowRecordingHostAdapterParityRow> HostRows)
{
    public bool HasWpfNarrationHandoff =>
        HostRows.Any(row => IsWpf(row.HostName) && row.CanCaptureNarration);

    public bool HasAvaloniaNarrationHandoff =>
        HostRows.Any(row => IsAvalonia(row.HostName) && row.CanCaptureNarration);

    public bool HasPairedNarrationHandoff =>
        HasWpfNarrationHandoff && HasAvaloniaNarrationHandoff;

    public bool HasAnyCameraHandoff =>
        HostRows.Any(row => row.CanCaptureCamera);

    public bool HasWpfCameraHandoff =>
        HostRows.Any(row => IsWpf(row.HostName) && row.CanCaptureCamera);

    public bool HasAvaloniaCameraHandoff =>
        HostRows.Any(row => IsAvalonia(row.HostName) && row.CanCaptureCamera);

    public bool HasPairedCameraHandoff =>
        HasWpfCameraHandoff && HasAvaloniaCameraHandoff;

    public bool RequiresUserPermission =>
        HostRows.Any(row => row.RequiresUserPermission);

    public IReadOnlyList<SlideShowRecordingCaptureStreamKind> SharedReadyStreams =>
        BuildSharedStreams(ready: true);

    public IReadOnlyList<SlideShowRecordingCaptureStreamKind> SharedMissingStreams =>
        BuildSharedStreams(ready: false);

    public string SummaryText =>
        (HasPairedNarrationHandoff, HasPairedCameraHandoff) switch
        {
            (true, true) => "WPF and Avalonia both expose real Windows microphone narration and camera video handoff readiness through host recording adapters.",
            (true, false) => "WPF and Avalonia both expose real Windows microphone narration handoff through host recording adapters.",
            _ => "WPF/Avalonia microphone narration handoff is not paired across the supplied host adapters."
        };

    public string RemainingWork =>
        HasPairedCameraHandoff
            ? "Encoded real camera media payload capture, PowerPoint COM recording baselines, unavailable-hardware live capture, and broader real-deck media/caption baselines remain deferred."
            : "Real camera capture, PowerPoint COM recording baselines, and broader real-deck media/caption baselines remain deferred.";

    private IReadOnlyList<SlideShowRecordingCaptureStreamKind> BuildSharedStreams(bool ready)
    {
        if (HostRows.Count == 0)
            return Array.Empty<SlideShowRecordingCaptureStreamKind>();

        var streams = Enum.GetValues<SlideShowRecordingCaptureStreamKind>();
        return streams
            .Where(stream => HostRows.All(row =>
                (ready ? row.ReadyStreams : row.MissingStreams).Contains(stream)))
            .ToArray();
    }

    private static bool IsWpf(string hostName) =>
        hostName.Contains("WPF", StringComparison.OrdinalIgnoreCase);

    private static bool IsAvalonia(string hostName) =>
        hostName.Contains("Avalonia", StringComparison.OrdinalIgnoreCase);
}

public sealed record SlideShowRecordingCameraEncodingReadinessRow(
    string HostName,
    string AdapterName,
    string PackagePath,
    string ContentType,
    bool DeviceHandoffReached,
    bool IsCaptured,
    long PayloadLengthBytes,
    bool RequiresPowerPointCom,
    string StatusText)
{
    public bool HasPackageTarget =>
        !string.IsNullOrWhiteSpace(PackagePath) &&
        PackagePath.StartsWith("ppt/media/", StringComparison.Ordinal) &&
        PackagePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    public bool IsNoComHandoffOnly =>
        DeviceHandoffReached &&
        !IsCaptured &&
        PayloadLengthBytes == 0 &&
        !RequiresPowerPointCom &&
        string.Equals(ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase);
}

public sealed record SlideShowRecordingCameraEncodingReadinessEvidence(
    IReadOnlyList<SlideShowRecordingCameraEncodingReadinessRow> HostRows)
{
    public bool HasWpfNoComHandoff =>
        HostRows.Any(row => IsWpf(row.HostName) && row.IsNoComHandoffOnly);

    public bool HasAvaloniaNoComHandoff =>
        HostRows.Any(row => IsAvalonia(row.HostName) && row.IsNoComHandoffOnly);

    public bool HasPairedNoComHandoff =>
        HasWpfNoComHandoff && HasAvaloniaNoComHandoff;

    public bool HasLocalEncodedPayload =>
        HostRows.Any(row => row.IsCaptured && row.PayloadLengthBytes > 0);

    public bool ClaimsPowerPointComBaseline =>
        HostRows.Any(row => row.RequiresPowerPointCom);

    public bool HasPackageTargets =>
        HostRows.Count > 0 && HostRows.All(row => row.HasPackageTarget);

    public string SummaryText =>
        HasPairedNoComHandoff
            ? "WPF and Avalonia both reach local default no-COM camera handoff with stable mp4 package targets while deferring video encoding honestly."
            : "Local default no-COM camera handoff is not paired across WPF and Avalonia.";

    public string RemainingWork =>
        HasLocalEncodedPayload
            ? "PowerPoint COM recording baselines and broader real-deck media/caption baselines remain deferred."
            : "Local default no-COM real camera video encoding, PowerPoint COM recording baselines, and broader real-deck media/caption baselines remain deferred.";

    private static bool IsWpf(string hostName) =>
        hostName.Contains("WPF", StringComparison.OrdinalIgnoreCase);

    private static bool IsAvalonia(string hostName) =>
        hostName.Contains("Avalonia", StringComparison.OrdinalIgnoreCase);
}

public sealed record SlideShowRecordingUnavailableHardwareRow(
    string HostName,
    string AdapterName,
    bool RequiresUserPermission,
    IReadOnlyList<SlideShowRecordingCaptureStreamKind> ReadyStreams,
    IReadOnlyList<SlideShowRecordingCaptureStreamKind> MissingStreams,
    bool HasDeviceDescriptors,
    string StatusText)
{
    public bool IsUnavailableHardwareEvidence =>
        RequiresUserPermission &&
        !HasDeviceDescriptors &&
        ReadyStreams.Count == 0 &&
        MissingStreams.Contains(SlideShowRecordingCaptureStreamKind.NarrationAudio) &&
        MissingStreams.Contains(SlideShowRecordingCaptureStreamKind.CameraVideo);
}

public sealed record SlideShowRecordingUnavailableHardwareEvidence(
    IReadOnlyList<SlideShowRecordingUnavailableHardwareRow> HostRows)
{
    public bool HasWpfUnavailableHardware =>
        HostRows.Any(row => IsWpf(row.HostName) && row.IsUnavailableHardwareEvidence);

    public bool HasAvaloniaUnavailableHardware =>
        HostRows.Any(row => IsAvalonia(row.HostName) && row.IsUnavailableHardwareEvidence);

    public bool HasPairedUnavailableHardware =>
        HasWpfUnavailableHardware && HasAvaloniaUnavailableHardware;

    public bool ClaimsCapture =>
        HostRows.Any(row => row.ReadyStreams.Count > 0 || row.HasDeviceDescriptors);

    public bool ClaimsPowerPointComBaseline => false;

    public string SummaryText =>
        HasPairedUnavailableHardware
            ? "WPF and Avalonia both report OS-backed recording adapters with no available microphone or camera hardware; no capture, encoded payload, or PowerPoint COM baseline is claimed."
            : "Unavailable-hardware recording evidence is not paired across WPF and Avalonia.";

    public string RemainingWork =>
        "Live capture on real microphone/camera hardware, local default camera mp4 encoding, PowerPoint COM recording baselines, and broader real-deck media/caption baselines remain deferred.";

    private static bool IsWpf(string hostName) =>
        hostName.Contains("WPF", StringComparison.OrdinalIgnoreCase);

    private static bool IsAvalonia(string hostName) =>
        hostName.Contains("Avalonia", StringComparison.OrdinalIgnoreCase);
}

public static class SlideShowRecordingHostAdapterParityPlanner
{
    public static SlideShowRecordingHostAdapterParityEvidence BuildEvidence(
        IEnumerable<SlideShowRecordingCaptureAdapterReadiness> hostReadiness)
    {
        ArgumentNullException.ThrowIfNull(hostReadiness);

        return new SlideShowRecordingHostAdapterParityEvidence(
            hostReadiness
                .Select(BuildRow)
                .OrderBy(row => row.HostName, StringComparer.Ordinal)
                .ToArray());
    }

    public static SlideShowRecordingCameraEncodingReadinessEvidence BuildCameraEncodingReadinessEvidence(
        IEnumerable<SlideShowRecordingCameraEncodingReadinessRow> hostRows)
    {
        ArgumentNullException.ThrowIfNull(hostRows);

        return new SlideShowRecordingCameraEncodingReadinessEvidence(
            hostRows
                .OrderBy(row => row.HostName, StringComparer.Ordinal)
                .ToArray());
    }

    public static SlideShowRecordingUnavailableHardwareEvidence BuildUnavailableHardwareEvidence(
        IEnumerable<SlideShowRecordingCaptureAdapterReadiness> hostReadiness)
    {
        ArgumentNullException.ThrowIfNull(hostReadiness);

        return new SlideShowRecordingUnavailableHardwareEvidence(
            hostReadiness
                .Select(BuildUnavailableHardwareRow)
                .OrderBy(row => row.HostName, StringComparer.Ordinal)
                .ToArray());
    }

    private static SlideShowRecordingHostAdapterParityRow BuildRow(
        SlideShowRecordingCaptureAdapterReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        return new SlideShowRecordingHostAdapterParityRow(
            readiness.HostName,
            readiness.AdapterName,
            readiness.CanCaptureNarration,
            readiness.CanCaptureCamera,
            readiness.RequiresUserPermission,
            readiness.ReadyStreams,
            readiness.MissingStreams,
            readiness.StatusText);
    }

    private static SlideShowRecordingUnavailableHardwareRow BuildUnavailableHardwareRow(
        SlideShowRecordingCaptureAdapterReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        return new SlideShowRecordingUnavailableHardwareRow(
            readiness.HostName,
            readiness.AdapterName,
            readiness.RequiresUserPermission,
            readiness.ReadyStreams,
            readiness.MissingStreams,
            readiness.Devices.Count > 0,
            readiness.StatusText);
    }
}
