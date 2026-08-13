using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowSessionInputActionKind
{
    None,
    TogglePresenterView,
    RevealHiddenSlide,
    SetScreenMode,
    ExecuteHostCommand,
    OpenExternalHyperlink,
}

public sealed record SlideShowSessionKeyPlan(
    bool IsHandled,
    SlideShowSessionInputActionKind ActionKind,
    SlideShowScreenMode? ScreenMode,
    SlideShowHostCommand HostCommand)
{
    public bool ShouldExecuteHostCommand =>
        ActionKind == SlideShowSessionInputActionKind.ExecuteHostCommand;

    public static SlideShowSessionKeyPlan HandledNoOp { get; } =
        new(
            true,
            SlideShowSessionInputActionKind.None,
            null,
            SlideShowHostCommand.Ignored);
}

public sealed record SlideShowSessionPointerPlan(
    bool IsHandled,
    SlideShowSessionInputActionKind ActionKind,
    SlideShowHostCommand HostCommand,
    Hyperlink? Hyperlink);

public sealed record SlideShowSessionInputExecutionCallbacks(
    Action TogglePresenterView,
    Action<string?> RevealHiddenSlide,
    Action<SlideShowScreenMode> SetScreenMode,
    Action<SlideShowHostCommand> ExecuteHostCommand,
    Action<Hyperlink> OpenExternalHyperlink,
    Action<Hyperlink>? InternalHyperlinkNavigated = null);

public sealed record SlideShowNavigationRequest(
    Slide Slide,
    int SlideIndex,
    bool AnimateSlide,
    int? TransitionDurationMs,
    bool UseDestinationBackground);

public sealed record SlideShowHostExecutionCallbacks(
    Action StopAutoAdvance,
    Action<DateTimeOffset> Close,
    Action<AnimationStep> PlayAnimationStep,
    Action<SlideShowNavigationRequest> NavigateToSlide);

/// <summary>
/// Shared slideshow session reducer. It owns the UI-neutral presenter state while
/// each host remains responsible for input, dispatchers, native visuals, and timers.
/// </summary>
public sealed class SlideShowSessionController
{
    private readonly Presentation _presentation;
    private readonly SlideShowPlaybackRoute _playbackRoute;
    private int _currentRouteSlideIndex;
    private string _slideNumberBuffer = string.Empty;
    private Slide? _revealedHiddenSlide;
    private int _revealedHiddenSlideSourceIndex = -1;

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

        Controller = new SlideShowController(
            _playbackRoute.Slides,
            _playbackRoute.StartIndex,
            _playbackRoute.AnimationStartIndex,
            showWithAnimation: _presentation.ShowWithAnimation,
            loopUntilStopped: _presentation.LoopUntilStopped);
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

    public SlideShowController Controller { get; }

    public SlideShowPlaybackRoute PlaybackRoute => _playbackRoute;

    public SlideShowScreenMode ScreenMode { get; private set; }

    public bool IsScreenBlank => SlideShowScreenModePlanner.IsBlank(ScreenMode);

    public string SlideNumberBuffer => _slideNumberBuffer;

    public Slide? DisplaySlide => _revealedHiddenSlide ?? Controller.CurrentSlide;

    public Slide? RevealedHiddenSlide => _revealedHiddenSlide;

    public int DisplaySourceSlideIndex => _revealedHiddenSlideSourceIndex >= 0
        ? _revealedHiddenSlideSourceIndex
        : CurrentPresentationSlideIndex;

    public SlideShowPresenterToolPlan ToolPlan { get; private set; }

    public SlideShowTimingRecorderState TimingRecorderState { get; private set; }

    public SlideShowRecordingExecutionState RecordingExecutionState { get; private set; }

    public SlideShowInkExecutionState InkExecutionState { get; private set; }

    public bool IsClosed { get; private set; }

    public int CurrentPresentationSlideIndex =>
        _playbackRoute.GetSourceSlideIndex(_currentRouteSlideIndex);

    public SlideShowRecordingReviewPlan RecordingReviewPlan =>
        SlideShowRecordingReviewPlanner.BuildPlan(_presentation, RecordingExecutionState);

    public SlideShowPresenterSessionSummary PresenterSummary =>
        SlideShowPresenterSessionSummaryPlanner.BuildSummary(
            RecordingExecutionState,
            InkExecutionState,
            _presentation,
            _playbackRoute.GetSourceSlideIndex);

    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        SlideShowHostPlanner.BuildPresenterState(
            _presentation,
            Controller,
            _playbackRoute.Slides,
            StartedAtUtc,
            nowUtc,
            displayIntent,
            ToolPlan,
            _playbackRoute.SourceSlideIndices);

    public SlideShowHostDisplayPlan BuildDisplayPlan(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
    {
        var plan = SlideShowHostPlanner.BuildDisplayPlan(
            _presentation,
            Controller,
            animated,
            zoomTransitionDurationMs,
            zoomShowBackground);
        return _revealedHiddenSlide is null
            ? plan
            : plan with
            {
                Slide = _revealedHiddenSlide,
                Transition = null,
                AutoAdvanceAfterMs = null,
            };
    }

    public SlideShowHostCommand PlanAdvance(bool stopAutoAdvance = false) =>
        SlideShowHostPlanner.PlanAdvance(Controller, stopAutoAdvance);

    public SlideShowHostCommand PlanBack(bool stopAutoAdvance = false) =>
        SlideShowHostPlanner.PlanBack(Controller, stopAutoAdvance);

    public SlideShowHostCommand PlanKey(string? keyName) =>
        SlideShowHostPlanner.PlanKey(keyName, Controller, _playbackRoute.Slides);

    public SlideShowHostCommand PlanTrigger(uint triggerShapeId) =>
        SlideShowHostPlanner.PlanTrigger(Controller, triggerShapeId);

    public SlideShowHostCommand PlanZoomNavigation(
        int targetSlideIndex,
        bool returnToParent = false,
        int? transitionDurationMs = null,
        bool showBackground = true) =>
        SlideShowHostPlanner.PlanZoomNavigation(
            Controller,
            _presentation.Slides,
            targetSlideIndex,
            returnToParent,
            transitionDurationMs,
            showBackground);

    public SlideShowHostCommand PlanInternalSlideJump(string? targetSlideId) =>
        SlideShowHostPlanner.PlanInternalSlideJump(
            Controller,
            _playbackRoute.Slides,
            targetSlideId);

    public SlideShowHostCommand PlanSlideNumberJump(int oneBasedSlideNumber)
    {
        _slideNumberBuffer = string.Empty;
        return SlideShowHostPlanner.PlanSlideNumberJump(
            Controller,
            _playbackRoute.Slides,
            oneBasedSlideNumber,
            _playbackRoute.SourceSlideIndices);
    }

    public SlideShowHostCommand PlanFirstSlide() =>
        SlideShowHostPlanner.PlanIntent(
            SlideShowHostIntent.FirstSlide,
            Controller,
            _playbackRoute.Slides,
            stopAutoAdvance: true);

    public SlideShowSessionKeyPlan PlanKeyboardInput(
        string? keyName,
        bool controlPressed = false)
    {
        var normalizedKey = keyName?.Trim() ?? string.Empty;
        if (normalizedKey == "P" && controlPressed)
        {
            return new SlideShowSessionKeyPlan(
                true,
                SlideShowSessionInputActionKind.TogglePresenterView,
                null,
                SlideShowHostCommand.Ignored);
        }

        if (IsScreenBlank)
        {
            _slideNumberBuffer = string.Empty;
            if (SlideShowScreenModePlanner.TryPlanKey(normalizedKey, ScreenMode, out var blankScreenMode))
            {
                ScreenMode = blankScreenMode;
                return new SlideShowSessionKeyPlan(
                    true,
                    SlideShowSessionInputActionKind.SetScreenMode,
                    blankScreenMode,
                    SlideShowHostCommand.Ignored);
            }

            return SlideShowHostPlanner.IntentFromKeyName(normalizedKey) == SlideShowHostIntent.Close
                ? new SlideShowSessionKeyPlan(
                    true,
                    SlideShowSessionInputActionKind.ExecuteHostCommand,
                    null,
                    SlideShowHostCommand.Close(stopAutoAdvance: true))
                : SlideShowSessionKeyPlan.HandledNoOp;
        }

        if (normalizedKey == "H")
        {
            return new SlideShowSessionKeyPlan(
                true,
                SlideShowSessionInputActionKind.RevealHiddenSlide,
                null,
                SlideShowHostCommand.Ignored);
        }

        if (SlideShowSlideNumberPlanner.TryGetDigit(normalizedKey, out var digit))
        {
            _slideNumberBuffer = SlideShowSlideNumberPlanner.AppendDigit(_slideNumberBuffer, digit);
            return SlideShowSessionKeyPlan.HandledNoOp;
        }

        if (normalizedKey == "Escape" && _slideNumberBuffer.Length > 0)
        {
            _slideNumberBuffer = string.Empty;
            return SlideShowSessionKeyPlan.HandledNoOp;
        }

        if (normalizedKey is "Enter" or "Return" && _slideNumberBuffer.Length > 0)
        {
            var buffer = _slideNumberBuffer;
            _slideNumberBuffer = string.Empty;
            var command = SlideShowSlideNumberPlanner.TryParseSlideNumber(buffer, out var slideNumber)
                ? PlanSlideNumberJump(slideNumber)
                : SlideShowHostCommand.Ignored;
            return new SlideShowSessionKeyPlan(
                true,
                command.IsHandled
                    ? SlideShowSessionInputActionKind.ExecuteHostCommand
                    : SlideShowSessionInputActionKind.None,
                null,
                command);
        }

        _slideNumberBuffer = string.Empty;
        if (SlideShowScreenModePlanner.TryPlanKey(normalizedKey, ScreenMode, out var screenMode))
        {
            ScreenMode = screenMode;
            return new SlideShowSessionKeyPlan(
                true,
                SlideShowSessionInputActionKind.SetScreenMode,
                screenMode,
                SlideShowHostCommand.Ignored);
        }

        var hostCommand = PlanKey(normalizedKey);
        return new SlideShowSessionKeyPlan(
            hostCommand.IsHandled,
            SlideShowSessionInputActionKind.ExecuteHostCommand,
            null,
            hostCommand);
    }

    public void ExecuteInputPlan(
        SlideShowSessionKeyPlan plan,
        SlideShowSessionInputExecutionCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(callbacks);

        switch (plan.ActionKind)
        {
            case SlideShowSessionInputActionKind.TogglePresenterView:
                callbacks.TogglePresenterView();
                break;
            case SlideShowSessionInputActionKind.RevealHiddenSlide:
                callbacks.RevealHiddenSlide(null);
                break;
            case SlideShowSessionInputActionKind.SetScreenMode when plan.ScreenMode is { } screenMode:
                callbacks.SetScreenMode(screenMode);
                break;
            case SlideShowSessionInputActionKind.ExecuteHostCommand:
                callbacks.ExecuteHostCommand(plan.HostCommand);
                break;
        }
    }

    public void SetScreenMode(SlideShowScreenMode mode) => ScreenMode = mode;

    public Slide? RevealNextHiddenSlide() => RevealHiddenSlide(targetSlideId: null);

    public Slide? RevealHiddenSlide(string? targetSlideId)
    {
        SlideShowHiddenSlideTarget? target;
        if (!string.IsNullOrWhiteSpace(targetSlideId))
        {
            target = SlideShowHostPlanner.FindHiddenSlideById(_presentation, targetSlideId);
        }
        else
        {
            if (Controller.CurrentSlideIndex < 0 ||
                Controller.CurrentSlideIndex >= _playbackRoute.SourceSlideIndices.Count)
            {
                return null;
            }

            var currentSourceIndex = _revealedHiddenSlideSourceIndex >= 0
                ? _revealedHiddenSlideSourceIndex
                : _playbackRoute.SourceSlideIndices[Controller.CurrentSlideIndex];
            target = SlideShowHostPlanner.FindNextHiddenSlide(
                _presentation,
                _playbackRoute,
                currentSourceIndex);
        }

        if (target is null)
        {
            return null;
        }

        _revealedHiddenSlide = target.Slide;
        _revealedHiddenSlideSourceIndex = target.SourceSlideIndex;
        return _revealedHiddenSlide;
    }

    public SlideShowPointerClickIntent PlanPointerClick(SlideShowCanvasPointer pointer) =>
        SlideShowPointerInteractionPlanner.PlanClick(DisplaySlide, _presentation, pointer);

    public SlideShowSessionPointerPlan PlanPointerInput(SlideShowCanvasPointer pointer)
    {
        if (IsScreenBlank)
        {
            return new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.None,
                SlideShowHostCommand.Ignored,
                null);
        }

        var intent = PlanPointerClick(pointer);
        return intent.Kind switch
        {
            SlideShowPointerClickIntentKind.Trigger when intent.TriggerShapeId is uint triggerShapeId =>
                new SlideShowSessionPointerPlan(
                    intent.IsHandled,
                    SlideShowSessionInputActionKind.ExecuteHostCommand,
                    PlanTrigger(triggerShapeId),
                    null),
            SlideShowPointerClickIntentKind.Zoom when intent.TargetSlideIndex is int targetSlideIndex =>
                new SlideShowSessionPointerPlan(
                    intent.IsHandled,
                    SlideShowSessionInputActionKind.ExecuteHostCommand,
                    PlanZoomNavigation(
                        targetSlideIndex,
                        intent.ReturnToParent,
                        intent.TransitionDurationMs,
                        intent.ShowBackground),
                    null),
            SlideShowPointerClickIntentKind.Hyperlink when intent.Hyperlink is not null =>
                PlanHyperlinkActivation(intent.Hyperlink) with { IsHandled = intent.IsHandled },
            SlideShowPointerClickIntentKind.Advance =>
                new SlideShowSessionPointerPlan(
                    intent.IsHandled,
                    SlideShowSessionInputActionKind.ExecuteHostCommand,
                    PlanAdvance(stopAutoAdvance: true),
                    null),
            _ => new SlideShowSessionPointerPlan(
                intent.IsHandled,
                SlideShowSessionInputActionKind.None,
                SlideShowHostCommand.Ignored,
                null),
        };
    }

    public SlideShowSessionPointerPlan PlanHyperlinkActivation(Hyperlink hyperlink)
    {
        ArgumentNullException.ThrowIfNull(hyperlink);

        if (IsScreenBlank)
        {
            return new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.None,
                SlideShowHostCommand.Ignored,
                hyperlink);
        }

        if (hyperlink.IsExternal)
        {
            return new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.OpenExternalHyperlink,
                SlideShowHostCommand.Ignored,
                hyperlink);
        }

        if (hyperlink.TargetSlideId is null)
        {
            return new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.None,
                SlideShowHostCommand.Ignored,
                hyperlink);
        }

        var command = PlanInternalSlideJump(hyperlink.TargetSlideId);
        if (command.Kind == SlideShowHostCommandKind.NavigateToSlide)
        {
            return new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.ExecuteHostCommand,
                command,
                hyperlink);
        }

        return SlideShowHostPlanner.FindHiddenSlideById(_presentation, hyperlink.TargetSlideId) is not null
            ? new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.RevealHiddenSlide,
                SlideShowHostCommand.Ignored,
                hyperlink)
            : new SlideShowSessionPointerPlan(
                true,
                SlideShowSessionInputActionKind.None,
                SlideShowHostCommand.Ignored,
                hyperlink);
    }

    public void ExecuteInputPlan(
        SlideShowSessionPointerPlan plan,
        SlideShowSessionInputExecutionCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(callbacks);

        switch (plan.ActionKind)
        {
            case SlideShowSessionInputActionKind.ExecuteHostCommand:
                callbacks.ExecuteHostCommand(plan.HostCommand);
                if (plan.Hyperlink is { IsExternal: false } internalHyperlink)
                {
                    callbacks.InternalHyperlinkNavigated?.Invoke(internalHyperlink);
                }
                break;
            case SlideShowSessionInputActionKind.OpenExternalHyperlink when plan.Hyperlink is not null:
                callbacks.OpenExternalHyperlink(plan.Hyperlink);
                break;
            case SlideShowSessionInputActionKind.RevealHiddenSlide when plan.Hyperlink is not null:
                callbacks.RevealHiddenSlide(plan.Hyperlink.TargetSlideId);
                callbacks.InternalHyperlinkNavigated?.Invoke(plan.Hyperlink);
                break;
        }
    }

    public Hyperlink? HitTestHyperlink(
        Slide slide,
        SlideShowCanvasPointer pointer) =>
        SlideShowPointerInteractionPlanner.HitTestHyperlink(slide, pointer);

    public SlideShowInkExecutionResult BeginPointerInk(SlideShowCanvasPointer pointer) =>
        IsScreenBlank
            ? new SlideShowInkExecutionResult(InkExecutionState, [], IsHandled: true)
            : BeginInkStroke(SlideShowPointerInteractionPlanner.MapInkPoint(pointer));

    public SlideShowInkExecutionResult AppendPointerInk(SlideShowCanvasPointer pointer) =>
        IsScreenBlank
            ? new SlideShowInkExecutionResult(InkExecutionState, [], IsHandled: true)
            : AppendInkStroke(SlideShowPointerInteractionPlanner.MapInkPoint(pointer));

    public SlideShowInkExecutionResult EndPointerInk(SlideShowCanvasPointer pointer) =>
        IsScreenBlank
            ? new SlideShowInkExecutionResult(InkExecutionState, [], IsHandled: true)
            : EndInkStroke(SlideShowPointerInteractionPlanner.MapInkPoint(pointer));

    public void ExecuteHostCommand(
        SlideShowHostCommand command,
        DateTimeOffset nowUtc,
        SlideShowHostExecutionCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(callbacks);

        _revealedHiddenSlide = null;
        _revealedHiddenSlideSourceIndex = -1;

        if (command.StopAutoAdvance)
        {
            callbacks.StopAutoAdvance();
        }

        switch (command.Kind)
        {
            case SlideShowHostCommandKind.Close:
                callbacks.Close(nowUtc);
                break;
            case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:
                callbacks.PlayAnimationStep(command.Step);
                break;
            case SlideShowHostCommandKind.NavigateToSlide when command.Slide is not null:
                MoveToSlide(command.SlideIndex, nowUtc);
                callbacks.NavigateToSlide(new SlideShowNavigationRequest(
                    command.Slide,
                    command.SlideIndex,
                    command.AnimateSlide,
                    command.TransitionDurationMs,
                    command.UseDestinationBackground));
                break;
        }
    }

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

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent,
        SlideShowRecordingMediaIntent mediaIntent,
        SlideShowPresenterPointerMode pointerMode,
        string? inkColorHex,
        double inkThicknessDip,
        SlideShowInkRetentionDecision inkRetentionDecision,
        DateTimeOffset nowUtc) =>
        ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            Controller.CurrentSlideIndex,
            nowUtc);

    public SlideShowPresenterToolPlan SetPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset nowUtc)
    {
        var current = ToolPlan;
        return ApplyPresenterToolIntent(
            current.Recording.TimingIntent,
            current.Recording.MediaIntent,
            pointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
    }

    public SlideShowPresenterToolPlan SetTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset nowUtc)
    {
        var current = ToolPlan;
        return ApplyPresenterToolIntent(
            timingIntent,
            current.Recording.MediaIntent,
            current.PointerInk.PointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
    }

    public SlideShowPresenterToolPlan SetMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset nowUtc)
    {
        var current = ToolPlan;
        return ApplyPresenterToolIntent(
            current.Recording.TimingIntent,
            mediaIntent,
            current.PointerInk.PointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
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
