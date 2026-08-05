using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared slideshow session reducer. It owns the UI-neutral presenter state while
/// each host remains responsible for input, dispatchers, native visuals, and timers.
/// </summary>
public sealed class SlideShowSessionController
{
    private readonly Presentation _presentation;
    private readonly SlideShowPlaybackRoute _playbackRoute;
    private int _currentRouteSlideIndex;

    public SlideShowSessionController(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        DateTimeOffset startedAtUtc,
        ISlideShowRecordingCaptureBackend captureBackend,
        SlideShowPresenterToolPlan? initialToolPlan = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _playbackRoute = playbackRoute ?? throw new ArgumentNullException(nameof(playbackRoute));
        ArgumentNullException.ThrowIfNull(captureBackend);

        StartedAtUtc = startedAtUtc;
        ToolPlan = initialToolPlan ?? SlideShowPresenterToolPlanner.BuildPlan(
            inkColorHex: _presentation.PresenterPenColor?.Resolved.ToString(),
            captureReadiness: captureBackend.AdapterReadiness);

        _currentRouteSlideIndex = _playbackRoute.StartIndex;
        var sourceSlideIndex = CurrentPresentationSlideIndex;
        TimingRecorderState = SlideShowTimingRecorderPlanner.CreateState(sourceSlideIndex, startedAtUtc);
        RecordingExecutionState = SlideShowRecordingExecutionPlanner.CreateState(
            ToolPlan,
            sourceSlideIndex,
            startedAtUtc,
            captureBackend);
        InkExecutionState = SlideShowInkExecutionPlanner.CreateState(
            _playbackRoute.StartIndex,
            ToolPlan.PointerInk);
    }

    public DateTimeOffset StartedAtUtc { get; }

    public SlideShowPresenterToolPlan ToolPlan { get; private set; }

    public SlideShowTimingRecorderState TimingRecorderState { get; private set; }

    public SlideShowRecordingExecutionState RecordingExecutionState { get; private set; }

    public SlideShowInkExecutionState InkExecutionState { get; private set; }

    public bool IsClosed { get; private set; }

    public int CurrentPresentationSlideIndex =>
        _playbackRoute.GetSourceSlideIndex(_currentRouteSlideIndex);

    public SlideShowRecordingReviewPlan RecordingReviewPlan =>
        SlideShowRecordingReviewPlanner.BuildPlan(_presentation, RecordingExecutionState);

    /// <summary>
    /// Applies the current recording review to the presentation without ending the
    /// slideshow. Rebuilding the plan on each call keeps the operation idempotent
    /// when the presenter clicks Apply more than once.
    /// </summary>
    public SlideShowRecordingReviewApplyResult ApplyRecordingReview()
    {
        EnsureOpen();

        var plan = RecordingReviewPlan;
        SlideShowRecordingReviewPlanner.ApplyRecordedTimings(_presentation, plan);
        return SlideShowRecordingReviewPlanner.ApplyPersistableArtifacts(_presentation, plan);
    }

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent,
        SlideShowRecordingMediaIntent mediaIntent,
        SlideShowPresenterPointerMode pointerMode,
        string? inkColorHex,
        double inkThicknessDip,
        SlideShowInkRetentionDecision inkRetentionDecision,
        int currentRouteSlideIndex,
        DateTimeOffset nowUtc)
    {
        EnsureOpen();

        _currentRouteSlideIndex = currentRouteSlideIndex;
        var sourceSlideIndex = _playbackRoute.GetSourceSlideIndex(currentRouteSlideIndex);
        var timingIntentChanged = ToolPlan.Recording.TimingIntent != timingIntent;
        if (timingIntentChanged)
        {
            FinalizePresenterTiming(nowUtc);
        }

        ToolPlan = SlideShowPresenterToolPlanner.BuildPlan(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex ?? _presentation.PresenterPenColor?.Resolved.ToString(),
            inkThicknessDip,
            inkRetentionDecision,
            RecordingExecutionState.HostCapabilities.EffectiveCaptureAdapterReadiness);
        RecordingExecutionState = SlideShowRecordingExecutionPlanner.ApplyToolPlan(
            RecordingExecutionState,
            ToolPlan,
            sourceSlideIndex,
            nowUtc);
        InkExecutionState = SlideShowInkExecutionPlanner.SelectPointerInk(
            InkExecutionState,
            ToolPlan.PointerInk);

        if (timingIntentChanged)
        {
            TimingRecorderState = SlideShowTimingRecorderPlanner.EnterSlide(
                TimingRecorderState,
                sourceSlideIndex,
                nowUtc).State;
        }

        return ToolPlan;
    }

    public void MoveToSlide(int routeSlideIndex, DateTimeOffset nowUtc)
    {
        EnsureOpen();

        FinalizePresenterTiming(nowUtc);
        _currentRouteSlideIndex = routeSlideIndex;
        var sourceSlideIndex = _playbackRoute.GetSourceSlideIndex(routeSlideIndex);
        TimingRecorderState = SlideShowTimingRecorderPlanner.EnterSlide(
            TimingRecorderState,
            sourceSlideIndex,
            nowUtc).State;
        RecordingExecutionState = SlideShowRecordingExecutionPlanner.MoveToSlide(
            RecordingExecutionState,
            sourceSlideIndex,
            nowUtc);
        InkExecutionState = SlideShowInkExecutionPlanner.MoveToSlide(
            InkExecutionState,
            routeSlideIndex);
    }

    public SlideShowInkExecutionResult BeginInkStroke(SlideShowInkPoint point) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.Begin(InkExecutionState, point));

    public SlideShowInkExecutionResult AppendInkStroke(SlideShowInkPoint point) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.Append(InkExecutionState, point));

    public SlideShowInkExecutionResult EndInkStroke(SlideShowInkPoint point) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.End(InkExecutionState, point));

    public SlideShowInkExecutionResult ClearInkStrokes() =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.ClearCurrentSlide(InkExecutionState));

    public SlideShowInkExecutionResult UndoLastInkStroke() =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.UndoLastStroke(InkExecutionState));

    public void Close(DateTimeOffset nowUtc)
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        FinalizePresenterTiming(nowUtc);
        RecordingExecutionState = SlideShowRecordingExecutionPlanner.EndSession(
            RecordingExecutionState,
            nowUtc);
        SlideShowRecordingReviewPlanner.ApplyPersistableArtifacts(
            _presentation,
            RecordingReviewPlan);
        InkExecutionState = SlideShowInkPersistencePlanner.ApplyRetentionOnExit(
            _presentation,
            InkExecutionState,
            _playbackRoute).State;
    }

    private SlideShowInkExecutionResult ApplyInkExecution(SlideShowInkExecutionResult result)
    {
        EnsureOpen();
        InkExecutionState = result.State;
        return result;
    }

    private void FinalizePresenterTiming(DateTimeOffset nowUtc)
    {
        var result = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            TimingRecorderState,
            ToolPlan,
            nowUtc);
        TimingRecorderState = result.State;
        SlideShowTimingRecorderPlanner.ApplyTimings(_presentation, result.Mutations);
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("The slideshow session is closed.");
        }
    }
}
