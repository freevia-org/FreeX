using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FreeP.App.Compositor;

public enum SlideShowRecordingExecutionActionKind
{
    StartSession,
    StopSession,
    EnterSlide,
    LeaveSlide,
    StartNarrationCapture,
    StopNarrationCapture,
    StartCameraCapture,
    StopCameraCapture,
    CaptureUnavailable
}

public sealed record SlideShowRecordingHostCapabilities(
    string HostName,
    bool CanCaptureNarration,
    bool CanCaptureCamera,
    string UnavailableReason,
    SlideShowRecordingCaptureAdapterReadiness? CaptureAdapterReadiness = null)
{
    public static SlideShowRecordingHostCapabilities Deferred(string hostName) =>
        SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(
            SlideShowRecordingCaptureAdapterReadiness.Deferred(
                string.IsNullOrWhiteSpace(hostName) ? "Slideshow host" : hostName.Trim(),
                "Recording capture adapter"));

    public SlideShowRecordingCaptureAdapterReadiness EffectiveCaptureAdapterReadiness =>
        CaptureAdapterReadiness ??
        (CanCaptureNarration || CanCaptureCamera
            ? SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                HostName,
                "Legacy recording capture adapter",
                LegacyDevices(),
                requiresUserPermission: false,
                UnavailableReason)
            : SlideShowRecordingCaptureAdapterReadiness.Deferred(
                HostName,
                "Recording capture adapter",
                UnavailableReason));

    private IEnumerable<SlideShowRecordingCaptureDeviceDescriptor> LegacyDevices()
    {
        if (CanCaptureNarration)
        {
            yield return new SlideShowRecordingCaptureDeviceDescriptor(
                SlideShowRecordingCaptureDeviceKind.Microphone,
                "legacy-microphone",
                "Host microphone",
                IsDefault: true,
                IsAvailable: true,
                "audio/mp4");
        }

        if (CanCaptureCamera)
        {
            yield return new SlideShowRecordingCaptureDeviceDescriptor(
                SlideShowRecordingCaptureDeviceKind.Camera,
                "legacy-camera",
                "Host camera",
                IsDefault: true,
                IsAvailable: true,
                "video/mp4");
        }
    }
}

public sealed record SlideShowRecordingExecutionAction(
    SlideShowRecordingExecutionActionKind Kind,
    int? SlideIndex,
    bool IsDeferred,
    string StatusText);

public enum SlideShowRecordingMediaArtifactKind
{
    NarrationAudio,
    CameraVideo
}

public sealed record SlideShowRecordingMediaArtifact(
    SlideShowRecordingMediaArtifactKind Kind,
    int SlideIndex,
    bool IsCaptured,
    bool IsDeferred,
    string SuggestedFileName,
    string ContentType,
    string StatusText,
    string PackagePath = "",
    long ContentLengthBytes = 0,
    string ContentSha256 = "",
    byte[]? PayloadBytes = null)
{
    public bool IsPersistable =>
        IsCaptured &&
        !string.IsNullOrWhiteSpace(PackagePath) &&
        ContentLengthBytes > 0 &&
        !string.IsNullOrWhiteSpace(ContentSha256);
}

public sealed record SlideShowRecordingCaptureRequest(
    SlideShowRecordingMediaArtifactKind Kind,
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int DurationMs,
    string SuggestedFileName,
    string ContentType);

public sealed record SlideShowRecordingCaptureStartRequest(
    SlideShowRecordingMediaArtifactKind Kind,
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    string SuggestedFileName,
    string ContentType);

public sealed record SlideShowRecordingCaptureResult(
    bool IsCaptured,
    bool IsDeferred,
    string StatusText,
    string PackagePath,
    long ContentLengthBytes,
    string ContentSha256,
    byte[]? PayloadBytes = null,
    string? SuggestedFileNameOverride = null,
    string? ContentTypeOverride = null)
{
    public static SlideShowRecordingCaptureResult Captured(
        string statusText,
        string packagePath = "",
        long contentLengthBytes = 0,
        string contentSha256 = "",
        byte[]? payloadBytes = null,
        string? suggestedFileNameOverride = null,
        string? contentTypeOverride = null) =>
        new(
            IsCaptured: true,
            IsDeferred: false,
            statusText,
            packagePath,
            contentLengthBytes,
            contentSha256,
            payloadBytes,
            suggestedFileNameOverride,
            contentTypeOverride);

    public static SlideShowRecordingCaptureResult Deferred(string statusText) =>
        new(
            IsCaptured: false,
            IsDeferred: true,
            statusText,
            PackagePath: string.Empty,
            ContentLengthBytes: 0,
            ContentSha256: string.Empty,
            PayloadBytes: null,
            SuggestedFileNameOverride: null,
            ContentTypeOverride: null);
}

public interface ISlideShowRecordingCaptureBackend
{
    SlideShowRecordingHostCapabilities Capabilities { get; }

    SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
    }

    SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request);
}

public sealed class SlideShowHostCapabilityRecordingCaptureBackend : ISlideShowRecordingCaptureBackend
{
    public SlideShowHostCapabilityRecordingCaptureBackend(SlideShowRecordingHostCapabilities capabilities)
    {
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness =>
        Capabilities.EffectiveCaptureAdapterReadiness;

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isAvailable = request.Kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => Capabilities.CanCaptureNarration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => Capabilities.CanCaptureCamera,
            _ => false
        };

        if (!isAvailable)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{Capabilities.HostName}: {Capabilities.UnavailableReason}");
        }

        return SlideShowRecordingCaptureResult.Captured(
            $"{KindLabel(request.Kind)} captured for slide {request.SlideIndex + 1}");
    }

    public static SlideShowHostCapabilityRecordingCaptureBackend Deferred(string hostName) =>
        new(SlideShowRecordingHostCapabilities.Deferred(hostName));

    public static SlideShowHostCapabilityRecordingCaptureBackend FromCapabilities(
        SlideShowRecordingHostCapabilities capabilities) =>
        new(capabilities);

    private static string KindLabel(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? "Narration audio"
            : "Camera video";
}

public sealed class SlideShowDeterministicRecordingCaptureBackend : ISlideShowRecordingCaptureBackend
{
    private readonly string _packageRoot;

    public SlideShowDeterministicRecordingCaptureBackend(
        string hostName,
        string packageRoot = "ppt/media/recordings")
    {
        var normalizedHostName = string.IsNullOrWhiteSpace(hostName)
            ? "Deterministic recording backend"
            : hostName.Trim();

        Capabilities = new SlideShowRecordingHostCapabilities(
            normalizedHostName,
            CanCaptureNarration: true,
            CanCaptureCamera: true,
            UnavailableReason: string.Empty,
            SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                normalizedHostName,
                "Deterministic recording capture adapter",
                new[]
                {
                    new SlideShowRecordingCaptureDeviceDescriptor(
                        SlideShowRecordingCaptureDeviceKind.Microphone,
                        "deterministic-microphone",
                        "Deterministic microphone",
                        IsDefault: true,
                        IsAvailable: true,
                        "audio/mp4"),
                    new SlideShowRecordingCaptureDeviceDescriptor(
                        SlideShowRecordingCaptureDeviceKind.Camera,
                        "deterministic-camera",
                        "Deterministic camera",
                        IsDefault: true,
                        IsAvailable: true,
                        "video/mp4")
                },
                requiresUserPermission: false));
        _packageRoot = NormalizePackageRoot(packageRoot);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness =>
        Capabilities.EffectiveCaptureAdapterReadiness;

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var packagePath = $"{_packageRoot}/{request.SuggestedFileName}";
        var payload = Encoding.UTF8.GetBytes(string.Join(
            "|",
            Capabilities.HostName,
            request.Kind,
            request.SlideIndex.ToString(CultureInfo.InvariantCulture),
            request.DurationMs.ToString(CultureInfo.InvariantCulture),
            request.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            request.EndedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            packagePath));
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        return SlideShowRecordingCaptureResult.Captured(
            $"{Capabilities.HostName}: {KindLabel(request.Kind)} captured to {packagePath}",
            packagePath,
            payload.Length,
            hash,
            payload);
    }

    private static string NormalizePackageRoot(string packageRoot)
    {
        var normalized = string.IsNullOrWhiteSpace(packageRoot)
            ? "ppt/media/recordings"
            : packageRoot.Trim().Replace('\\', '/').Trim('/');

        return normalized.Length == 0
            ? "ppt/media/recordings"
            : normalized;
    }

    private static string KindLabel(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? "Narration audio"
            : "Camera video";
}

public sealed record SlideShowRecordingSlideSegment(
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int DurationMs,
    SlideShowRecordingMediaIntent MediaIntent,
    bool NarrationRequested,
    bool CameraRequested,
    bool NarrationCaptured,
    bool CameraCaptured,
    IReadOnlyList<SlideShowRecordingMediaArtifact> MediaArtifacts);

public sealed record SlideShowRecordingExecutionState(
    bool IsSessionActive,
    int? CurrentSlideIndex,
    DateTimeOffset? CurrentSlideStartedAtUtc,
    SlideShowRecordingTimingPlan RecordingPlan,
    SlideShowRecordingHostCapabilities HostCapabilities,
    IReadOnlyList<SlideShowRecordingSlideSegment> Segments,
    IReadOnlyList<SlideShowRecordingExecutionAction> LastActions,
    ISlideShowRecordingCaptureBackend? CaptureBackend = null)
{
    public ISlideShowRecordingCaptureBackend ActiveCaptureBackend =>
        CaptureBackend ?? SlideShowHostCapabilityRecordingCaptureBackend.FromCapabilities(HostCapabilities);

    public bool IsNarrationCaptureActive =>
        IsSessionActive &&
        RecordingPlan.IsNarrationRequested &&
        HostCapabilities.CanCaptureNarration;

    public bool IsCameraCaptureActive =>
        IsSessionActive &&
        RecordingPlan.IsMediaCaptureRequested &&
        HostCapabilities.CanCaptureCamera;
}

public static class SlideShowRecordingExecutionPlanner
{
    public static SlideShowRecordingExecutionState CreateState(
        SlideShowPresenterToolPlan toolPlan,
        int currentSlideIndex,
        DateTimeOffset nowUtc,
        SlideShowRecordingHostCapabilities? hostCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(toolPlan);

        var backend = hostCapabilities is null
            ? SlideShowHostCapabilityRecordingCaptureBackend.Deferred("Slideshow host")
            : SlideShowHostCapabilityRecordingCaptureBackend.FromCapabilities(hostCapabilities);

        return CreateState(toolPlan, currentSlideIndex, nowUtc, backend);
    }

    public static SlideShowRecordingExecutionState CreateState(
        SlideShowPresenterToolPlan toolPlan,
        int currentSlideIndex,
        DateTimeOffset nowUtc,
        SlideShowRecordingCaptureAdapterReadiness captureAdapterReadiness)
    {
        ArgumentNullException.ThrowIfNull(captureAdapterReadiness);

        return CreateState(
            toolPlan,
            currentSlideIndex,
            nowUtc,
            SlideShowHostCapabilityRecordingCaptureBackend.FromCapabilities(
                SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(captureAdapterReadiness)));
    }

    public static SlideShowRecordingExecutionState CreateState(
        SlideShowPresenterToolPlan toolPlan,
        int currentSlideIndex,
        DateTimeOffset nowUtc,
        ISlideShowRecordingCaptureBackend captureBackend)
    {
        ArgumentNullException.ThrowIfNull(toolPlan);
        ArgumentNullException.ThrowIfNull(captureBackend);

        var state = EmptyState(
            toolPlan.Recording,
            captureBackend.Capabilities,
            captureBackend);

        return StartSessionIfRequested(state, toolPlan.Recording, currentSlideIndex, nowUtc);
    }

    public static SlideShowRecordingExecutionState ApplyToolPlan(
        SlideShowRecordingExecutionState state,
        SlideShowPresenterToolPlan toolPlan,
        int currentSlideIndex,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(toolPlan);

        // Presenter tool state (pointer mode, ink colour/thickness, ink retention)
        // is independent of the recording lifecycle. When the recording-relevant
        // portion of the plan (timing/media intent and capture readiness) hasn't
        // actually changed, an in-progress capture must keep running uninterrupted
        // for the current slide -- restarting it here would truncate or duplicate
        // the saved narration every time the presenter switches pointer/ink tools
        // mid-recording.
        if (state.IsSessionActive && state.RecordingPlan == toolPlan.Recording)
        {
            return state with { LastActions = Array.Empty<SlideShowRecordingExecutionAction>() };
        }

        var stopped = state.IsSessionActive
            ? EndSession(state, nowUtc)
            : state with { LastActions = Array.Empty<SlideShowRecordingExecutionAction>() };
        var reset = EmptyState(toolPlan.Recording, state.HostCapabilities, state.ActiveCaptureBackend) with
        {
            Segments = stopped.Segments
        };

        return StartSessionIfRequested(reset, toolPlan.Recording, currentSlideIndex, nowUtc);
    }

    public static SlideShowRecordingExecutionState MoveToSlide(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSessionActive)
        {
            return state with
            {
                CurrentSlideIndex = slideIndex >= 0 ? slideIndex : null,
                CurrentSlideStartedAtUtc = slideIndex >= 0 ? nowUtc : null,
                LastActions = Array.Empty<SlideShowRecordingExecutionAction>()
            };
        }

        var actions = new List<SlideShowRecordingExecutionAction>();
        var segments = state.Segments;
        if (state.CurrentSlideIndex is int previousSlideIndex &&
            state.CurrentSlideStartedAtUtc is DateTimeOffset startedAtUtc)
        {
            segments = segments.Concat(new[]
            {
                BuildSegment(state, previousSlideIndex, startedAtUtc, nowUtc)
            }).ToArray();
            actions.AddRange(LeaveSlideActions(state, previousSlideIndex));
        }

        if (slideIndex < 0)
        {
            return state with
            {
                CurrentSlideIndex = null,
                CurrentSlideStartedAtUtc = null,
                Segments = segments,
                LastActions = actions
            };
        }

        actions.AddRange(EnterSlideActions(state, slideIndex, nowUtc));
        return state with
        {
            CurrentSlideIndex = slideIndex,
            CurrentSlideStartedAtUtc = nowUtc,
            Segments = segments,
            LastActions = actions
        };
    }

    public static SlideShowRecordingExecutionState EndSession(
        SlideShowRecordingExecutionState state,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSessionActive)
        {
            return state with { LastActions = Array.Empty<SlideShowRecordingExecutionAction>() };
        }

        var moved = MoveToSlide(state, slideIndex: -1, nowUtc);
        var actions = moved.LastActions.Concat(new[]
        {
            new SlideShowRecordingExecutionAction(
                SlideShowRecordingExecutionActionKind.StopSession,
                SlideIndex: null,
                IsDeferred: false,
                "Stop recording session")
        }).ToArray();

        return moved with
        {
            IsSessionActive = false,
            LastActions = actions
        };
    }

    public static bool IsSessionRequested(SlideShowRecordingTimingPlan recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return recording.ShouldTrackPerSlideTimings ||
            recording.IsNarrationRequested ||
            recording.IsMediaCaptureRequested;
    }

    private static SlideShowRecordingExecutionState EmptyState(
        SlideShowRecordingTimingPlan recording,
        SlideShowRecordingHostCapabilities hostCapabilities,
        ISlideShowRecordingCaptureBackend? captureBackend) =>
        new(
            IsSessionActive: false,
            CurrentSlideIndex: null,
            CurrentSlideStartedAtUtc: null,
            recording,
            hostCapabilities,
            Array.Empty<SlideShowRecordingSlideSegment>(),
            Array.Empty<SlideShowRecordingExecutionAction>(),
            captureBackend);

    private static SlideShowRecordingExecutionState StartSessionIfRequested(
        SlideShowRecordingExecutionState state,
        SlideShowRecordingTimingPlan recording,
        int currentSlideIndex,
        DateTimeOffset nowUtc)
    {
        if (!IsSessionRequested(recording))
        {
            return state with { RecordingPlan = recording };
        }

        var active = state with
        {
            IsSessionActive = true,
            RecordingPlan = recording,
            CurrentSlideIndex = currentSlideIndex >= 0 ? currentSlideIndex : null,
            CurrentSlideStartedAtUtc = currentSlideIndex >= 0 ? nowUtc : null
        };

        var actions = new List<SlideShowRecordingExecutionAction>
        {
            new(
                SlideShowRecordingExecutionActionKind.StartSession,
                SlideIndex: null,
                IsDeferred: false,
                "Start recording session")
        };

        if (currentSlideIndex >= 0)
        {
            actions.AddRange(EnterSlideActions(active, currentSlideIndex, nowUtc));
        }

        return active with { LastActions = actions };
    }

    private static SlideShowRecordingSlideSegment BuildSegment(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc)
    {
        var durationMs = SlideShowTimingRecorderPlanner.ClampElapsedMilliseconds(endedAtUtc - startedAtUtc);
        var mediaArtifacts = BuildMediaArtifacts(state, slideIndex, startedAtUtc, endedAtUtc, durationMs);

        return new(
            slideIndex,
            startedAtUtc,
            endedAtUtc,
            durationMs,
            state.RecordingPlan.MediaIntent,
            state.RecordingPlan.IsNarrationRequested,
            state.RecordingPlan.IsMediaCaptureRequested,
            mediaArtifacts.Any(artifact =>
                artifact.Kind == SlideShowRecordingMediaArtifactKind.NarrationAudio &&
                artifact.IsCaptured),
            mediaArtifacts.Any(artifact =>
                artifact.Kind == SlideShowRecordingMediaArtifactKind.CameraVideo &&
                artifact.IsCaptured),
            mediaArtifacts);
    }

    private static IReadOnlyList<SlideShowRecordingMediaArtifact> BuildMediaArtifacts(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        int durationMs)
    {
        var artifacts = new List<SlideShowRecordingMediaArtifact>();
        if (state.RecordingPlan.IsNarrationRequested)
        {
            artifacts.Add(BuildMediaArtifact(
                state,
                slideIndex,
                startedAtUtc,
                endedAtUtc,
                durationMs,
                SlideShowRecordingMediaArtifactKind.NarrationAudio,
                "narration",
                "m4a",
                "audio/mp4"));
        }

        if (state.RecordingPlan.IsMediaCaptureRequested)
        {
            artifacts.Add(BuildMediaArtifact(
                state,
                slideIndex,
                startedAtUtc,
                endedAtUtc,
                durationMs,
                SlideShowRecordingMediaArtifactKind.CameraVideo,
                "camera",
                "mp4",
                "video/mp4"));
        }

        return artifacts;
    }

    private static SlideShowRecordingMediaArtifact BuildMediaArtifact(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        int durationMs,
        SlideShowRecordingMediaArtifactKind kind,
        string fileStem,
        string extension,
        string contentType)
    {
        var suggestedFileName = BuildSuggestedFileName(slideIndex, fileStem, extension);
        var result = state.ActiveCaptureBackend.CompleteCapture(
            new SlideShowRecordingCaptureRequest(
                kind,
                slideIndex,
                startedAtUtc,
                endedAtUtc,
                durationMs,
                suggestedFileName,
                contentType));

        return new(
            kind,
            slideIndex,
            result.IsCaptured,
            result.IsDeferred,
            result.SuggestedFileNameOverride ?? suggestedFileName,
            result.ContentTypeOverride ?? contentType,
            result.StatusText,
            result.PackagePath,
            result.ContentLengthBytes,
            result.ContentSha256,
            result.PayloadBytes);
    }

    private static IReadOnlyList<SlideShowRecordingExecutionAction> EnterSlideActions(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset nowUtc)
    {
        var actions = new List<SlideShowRecordingExecutionAction>
        {
            new(
                SlideShowRecordingExecutionActionKind.EnterSlide,
                slideIndex,
                IsDeferred: false,
                $"Enter recording slide {slideIndex + 1}")
        };

        if (state.RecordingPlan.IsNarrationRequested)
        {
            BeginCaptureIfAvailable(
                state,
                slideIndex,
                nowUtc,
                SlideShowRecordingMediaArtifactKind.NarrationAudio,
                "narration",
                "m4a",
                "audio/mp4");
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureNarration,
                SlideShowRecordingExecutionActionKind.StartNarrationCapture,
                "Start narration capture"));
        }

        if (state.RecordingPlan.IsMediaCaptureRequested)
        {
            BeginCaptureIfAvailable(
                state,
                slideIndex,
                nowUtc,
                SlideShowRecordingMediaArtifactKind.CameraVideo,
                "camera",
                "mp4",
                "video/mp4");
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureCamera,
                SlideShowRecordingExecutionActionKind.StartCameraCapture,
                "Start camera capture"));
        }

        return actions;
    }

    private static void BeginCaptureIfAvailable(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset nowUtc,
        SlideShowRecordingMediaArtifactKind kind,
        string fileStem,
        string extension,
        string contentType)
    {
        var available = kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? state.HostCapabilities.CanCaptureNarration
            : state.HostCapabilities.CanCaptureCamera;
        if (!available)
            return;

        state.ActiveCaptureBackend.BeginCapture(new SlideShowRecordingCaptureStartRequest(
            kind,
            slideIndex,
            nowUtc,
            BuildSuggestedFileName(slideIndex, fileStem, extension),
            contentType));
    }

    private static IReadOnlyList<SlideShowRecordingExecutionAction> LeaveSlideActions(
        SlideShowRecordingExecutionState state,
        int slideIndex)
    {
        var actions = new List<SlideShowRecordingExecutionAction>();
        if (state.RecordingPlan.IsMediaCaptureRequested)
        {
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureCamera,
                SlideShowRecordingExecutionActionKind.StopCameraCapture,
                "Stop camera capture"));
        }

        if (state.RecordingPlan.IsNarrationRequested)
        {
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureNarration,
                SlideShowRecordingExecutionActionKind.StopNarrationCapture,
                "Stop narration capture"));
        }

        actions.Add(new(
            SlideShowRecordingExecutionActionKind.LeaveSlide,
            slideIndex,
            IsDeferred: false,
            $"Leave recording slide {slideIndex + 1}"));

        return actions;
    }

    private static SlideShowRecordingExecutionAction CaptureAction(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        bool isAvailable,
        SlideShowRecordingExecutionActionKind availableKind,
        string availableText) =>
        isAvailable
            ? new(availableKind, slideIndex, IsDeferred: false, availableText)
            : new(
                SlideShowRecordingExecutionActionKind.CaptureUnavailable,
                slideIndex,
                IsDeferred: true,
                $"{state.HostCapabilities.HostName}: {state.HostCapabilities.UnavailableReason}");

    private static string BuildSuggestedFileName(int slideIndex, string fileStem, string extension) =>
        $"slide-{slideIndex + 1:000}-{fileStem}.{extension}";
}
