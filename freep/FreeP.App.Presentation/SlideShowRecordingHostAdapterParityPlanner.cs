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
}
