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
    string UnavailableReason)
{
    public static SlideShowRecordingHostCapabilities Deferred(string hostName) =>
        new(
            string.IsNullOrWhiteSpace(hostName) ? "Slideshow host" : hostName.Trim(),
            CanCaptureNarration: false,
            CanCaptureCamera: false,
            "Recording capture adapter is not registered for this host.");
}

public sealed record SlideShowRecordingExecutionAction(
    SlideShowRecordingExecutionActionKind Kind,
    int? SlideIndex,
    bool IsDeferred,
    string StatusText);

public sealed record SlideShowRecordingSlideSegment(
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int DurationMs,
    SlideShowRecordingMediaIntent MediaIntent,
    bool NarrationRequested,
    bool CameraRequested,
    bool NarrationCaptured,
    bool CameraCaptured);

public sealed record SlideShowRecordingExecutionState(
    bool IsSessionActive,
    int? CurrentSlideIndex,
    DateTimeOffset? CurrentSlideStartedAtUtc,
    SlideShowRecordingTimingPlan RecordingPlan,
    SlideShowRecordingHostCapabilities HostCapabilities,
    IReadOnlyList<SlideShowRecordingSlideSegment> Segments,
    IReadOnlyList<SlideShowRecordingExecutionAction> LastActions)
{
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

        var state = EmptyState(
            toolPlan.Recording,
            hostCapabilities ?? SlideShowRecordingHostCapabilities.Deferred("Slideshow host"));

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

        var stopped = state.IsSessionActive
            ? EndSession(state, nowUtc)
            : state with { LastActions = Array.Empty<SlideShowRecordingExecutionAction>() };
        var reset = EmptyState(toolPlan.Recording, state.HostCapabilities) with
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

        actions.AddRange(EnterSlideActions(state, slideIndex));
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
        SlideShowRecordingHostCapabilities hostCapabilities) =>
        new(
            IsSessionActive: false,
            CurrentSlideIndex: null,
            CurrentSlideStartedAtUtc: null,
            recording,
            hostCapabilities,
            Array.Empty<SlideShowRecordingSlideSegment>(),
            Array.Empty<SlideShowRecordingExecutionAction>());

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
            actions.AddRange(EnterSlideActions(active, currentSlideIndex));
        }

        return active with { LastActions = actions };
    }

    private static SlideShowRecordingSlideSegment BuildSegment(
        SlideShowRecordingExecutionState state,
        int slideIndex,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc) =>
        new(
            slideIndex,
            startedAtUtc,
            endedAtUtc,
            SlideShowTimingRecorderPlanner.ClampElapsedMilliseconds(endedAtUtc - startedAtUtc),
            state.RecordingPlan.MediaIntent,
            state.RecordingPlan.IsNarrationRequested,
            state.RecordingPlan.IsMediaCaptureRequested,
            state.RecordingPlan.IsNarrationRequested && state.HostCapabilities.CanCaptureNarration,
            state.RecordingPlan.IsMediaCaptureRequested && state.HostCapabilities.CanCaptureCamera);

    private static IReadOnlyList<SlideShowRecordingExecutionAction> EnterSlideActions(
        SlideShowRecordingExecutionState state,
        int slideIndex)
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
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureNarration,
                SlideShowRecordingExecutionActionKind.StartNarrationCapture,
                "Start narration capture"));
        }

        if (state.RecordingPlan.IsMediaCaptureRequested)
        {
            actions.Add(CaptureAction(
                state,
                slideIndex,
                state.HostCapabilities.CanCaptureCamera,
                SlideShowRecordingExecutionActionKind.StartCameraCapture,
                "Start camera capture"));
        }

        return actions;
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
}
