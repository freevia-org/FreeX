using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowRuntimeCaptionPreference(
    int? SlideIndex = null,
    uint? ShapeId = null,
    int? TrackIndex = null);

public sealed record SlideShowBrowseWindowSizePlan(
    double WidthDip,
    double HeightDip);

public sealed record SlideShowRuntimeWindowPlan(
    bool IsBrowseWindow,
    bool IsBorderless,
    bool IsTopmost,
    bool AllowsResize,
    bool ShowBrowseScrollbars)
{
    public const double PreferredBrowseWidthDip = 1024;
    public const double PreferredBrowseHeightDip = 768;
    public const double WorkAreaFraction = 0.85;

    public SlideShowBrowseWindowSizePlan PlanBrowseWindowSize(
        double workAreaWidthDip,
        double workAreaHeightDip) =>
        new(
            ResolveBrowseDimension(workAreaWidthDip, PreferredBrowseWidthDip),
            ResolveBrowseDimension(workAreaHeightDip, PreferredBrowseHeightDip));

    private static double ResolveBrowseDimension(double workAreaDimensionDip, double preferredDip) =>
        double.IsFinite(workAreaDimensionDip) && workAreaDimensionDip > 0
            ? Math.Min(preferredDip, workAreaDimensionDip * WorkAreaFraction)
            : preferredDip;
}

public sealed record SlideShowRuntimeScreenModePlan(
    SlideShowScreenMode Mode,
    bool IsBlank,
    bool UseWhiteSurface);

public sealed record SlideShowRuntimeDisplayPlan(
    SlideShowHostDisplayPlan Display,
    int CaptionSlideIndex,
    IReadOnlyList<PresentationMediaTranscriptTrackDescriptor> CaptionTracks,
    uint? PreferredCaptionShapeId,
    int? PreferredCaptionTrackIndex,
    int? PreferredCaptionSlideIndex,
    bool ShowMediaControls,
    bool ShowNarration)
{
    public Slide? Slide => Display.Slide;

    public SlideShowSlideMetrics Metrics => Display.Metrics;

    public bool UseDestinationBackground => Display.UseDestinationBackground;

    public SlideTransition? Transition => Display.Transition;

    public int? AutoAdvanceAfterMs => Display.AutoAdvanceAfterMs;
}

public sealed record SlideShowRuntimeRendererCallbacks(
    Action StopAutoAdvance,
    Action<DateTimeOffset> Close,
    Action<AnimationStep> PlayAnimationStep,
    Action<SlideShowNavigationRequest> NavigateToSlide,
    Action TogglePresenterView,
    Action DisplayCurrentSlideWithoutAnimation,
    Action<SlideShowRuntimeScreenModePlan> RenderScreenMode,
    Action<Hyperlink> OpenExternalHyperlink,
    Action RefreshInkOverlay,
    Action<Hyperlink>? InternalHyperlinkNavigated = null,
    Action? StopTransitionAudio = null,
    Action? TeardownMedia = null);

public sealed record SlideShowPresenterViewOperations(
    Func<SlideShowPresenterState> StateProvider,
    Action GoBack,
    Action GoNext,
    Action<SlideShowScreenMode> SetScreenMode,
    Action<SlideShowPresenterPointerMode> SelectPointerMode,
    Action ClearInk,
    Action<SlideShowTimingIntent> SetTimingIntent,
    Action<SlideShowRecordingMediaIntent> SetMediaIntent,
    Func<SlideShowRecordingReviewPlan> RecordingReviewProvider,
    Func<SlideShowRecordingReviewApplyResult> ApplyRecordingReview,
    Action<int> GoToSlide,
    Action<int, string?> SetNotesText);

/// <summary>
/// Portable application boundary for a running slideshow. Native hosts bind their
/// renderer callbacks once and adapt framework events, timers, windows, and pixels.
/// </summary>
public sealed class SlideShowRuntimeApplication
{
    private readonly Presentation _presentation;
    private readonly SlideShowSessionController _session;
    private readonly SlideShowRuntimeCaptionPreference _captionPreference;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SlideShowDisplayCoordinator _displayCoordinator = new();
    private SlideShowRuntimeRendererCallbacks? _renderer;
    private ISlideShowDisplayRenderer? _displayRenderer;
    private SlideShowSessionInputExecutionCallbacks? _inputCallbacks;
    private SlideShowHostExecutionCallbacks? _hostCallbacks;
    private bool _rendererSessionClosed;

    public SlideShowRuntimeApplication(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        DateTimeOffset startedAtUtc,
        ISlideShowRecordingCaptureBackend captureBackend,
        SlideShowRuntimeCaptionPreference? captionPreference = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        ArgumentNullException.ThrowIfNull(playbackRoute);
        ArgumentNullException.ThrowIfNull(captureBackend);

        _captionPreference = captionPreference ?? new SlideShowRuntimeCaptionPreference();
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        _session = new SlideShowSessionController(
            presentation,
            playbackRoute,
            startedAtUtc,
            captureBackend);
        AnimationRendererSession = new SlideShowAnimationRendererSession(presentation);

        InitialSlideMetrics = SlideShowHostPlanner.BuildSlideMetrics(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
        var isBrowseWindow = presentation.ShowType == PresentationShowType.BrowsedByIndividual;
        WindowPlan = new SlideShowRuntimeWindowPlan(
            isBrowseWindow,
            IsBorderless: !isBrowseWindow,
            IsTopmost: !isBrowseWindow,
            AllowsResize: isBrowseWindow,
            ShowBrowseScrollbars: isBrowseWindow && presentation.ShowBrowseScrollbar);
        KioskRestartInterval = SlideShowKioskRestartPlanner.TryGetInterval(presentation, out var interval)
            ? interval
            : null;
    }

    public SlideShowSlideMetrics InitialSlideMetrics { get; }

    public SlideShowRuntimeWindowPlan WindowPlan { get; }

    public TimeSpan? KioskRestartInterval { get; }

    public DateTimeOffset StartedAtUtc => _session.StartedAtUtc;

    public SlideShowController Controller => _session.Controller;

    public SlideShowAnimationRendererSession AnimationRendererSession { get; }

    public SlideShowPlaybackRoute PlaybackRoute => _session.PlaybackRoute;

    public SlideShowScreenMode ScreenMode => _session.ScreenMode;

    public Slide? DisplaySlide => _session.DisplaySlide;

    public Slide? RevealedHiddenSlide => _session.RevealedHiddenSlide;

    public int CurrentPresentationSlideIndex => _session.CurrentPresentationSlideIndex;

    public SlideShowPresenterToolPlan ToolPlan => _session.ToolPlan;

    public SlideShowTimingRecorderState TimingRecorderState => _session.TimingRecorderState;

    public SlideShowRecordingExecutionState RecordingExecutionState => _session.RecordingExecutionState;

    public SlideShowInkExecutionState InkExecutionState => _session.InkExecutionState;

    public bool IsClosed => _session.IsClosed;

    public bool IsPresenterViewOpen => _displayCoordinator.IsPresenterViewOpen;

    public SlideShowPresenterSessionSummary PresenterSummary => _session.PresenterSummary;

    public SlideShowRecordingReviewPlan RecordingReviewPlan => _session.RecordingReviewPlan;

    public void BindRenderer(
        SlideShowRuntimeRendererCallbacks callbacks,
        ISlideShowDisplayRenderer? displayRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        if (_renderer is not null)
        {
            throw new InvalidOperationException("The slideshow runtime renderer is already bound.");
        }

        _renderer = callbacks;
        _displayRenderer = displayRenderer;
        _hostCallbacks = new SlideShowHostExecutionCallbacks(
            callbacks.StopAutoAdvance,
            callbacks.Close,
            callbacks.PlayAnimationStep,
            callbacks.NavigateToSlide);
        _inputCallbacks = new SlideShowSessionInputExecutionCallbacks(
            callbacks.TogglePresenterView,
            targetSlideId => ExecuteHiddenSlideReveal(targetSlideId),
            mode => SetScreenMode(mode),
            command => ExecuteHostCommand(command),
            callbacks.OpenExternalHyperlink,
            callbacks.InternalHyperlinkNavigated);
    }

    public SlideShowDisplayRendererPlan DisplayCurrentSlide(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true) =>
        _displayCoordinator.Display(
            BuildDisplayPlan(animated, zoomTransitionDurationMs, zoomShowBackground),
            RequireDisplayRenderer());

    public SlideShowDisplayRendererPlan StartRendererSession() =>
        _displayCoordinator.StartSession(KioskRestartInterval, RequireDisplayRenderer());

    public SlideShowDisplayRendererPlan HandleAutoAdvanceElapsed(long displayVersion) =>
        _displayCoordinator.HandleAutoAdvanceElapsed(displayVersion, RequireDisplayRenderer());

    public SlideShowDisplayRendererPlan HandleKioskRestartElapsed() =>
        _displayCoordinator.HandleKioskRestartElapsed(RequireDisplayRenderer());

    public SlideShowDisplayRendererPlan TogglePresenterView() =>
        _displayCoordinator.TogglePresenterView(RequireDisplayRenderer());

    public void NotifyPresenterViewClosed() =>
        _displayCoordinator.NotifyPresenterViewClosed();

    public AdvanceResult ExecuteAdvance(
        DateTimeOffset? nowUtc = null,
        bool stopAutoAdvance = false)
    {
        var command = _session.PlanAdvance(stopAutoAdvance);
        ExecuteHostCommand(command, nowUtc);
        return command.AdvanceResult!;
    }

    public BackResult ExecuteBack(
        DateTimeOffset? nowUtc = null,
        bool stopAutoAdvance = false)
    {
        var command = _session.PlanBack(stopAutoAdvance);
        ExecuteHostCommand(command, nowUtc);
        return command.BackResult!;
    }

    public void ExecuteSlideNumberJump(int oneBasedSlideNumber, DateTimeOffset? nowUtc = null) =>
        ExecuteHostCommand(_session.PlanSlideNumberJump(oneBasedSlideNumber), nowUtc);

    public Slide? ExecuteHiddenSlideReveal(string? targetSlideId = null)
    {
        var slide = _session.RevealHiddenSlide(targetSlideId);
        if (slide is not null)
        {
            RequireRenderer().StopAutoAdvance();
            RequireRenderer().DisplayCurrentSlideWithoutAnimation();
        }

        return slide;
    }

    public void SetScreenMode(SlideShowScreenMode mode)
    {
        _session.SetScreenMode(mode);
        RequireRenderer().RenderScreenMode(new SlideShowRuntimeScreenModePlan(
            mode,
            _session.IsScreenBlank,
            mode == SlideShowScreenMode.White));
    }

    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        _session.CreatePresenterState(nowUtc, displayIntent);

    public SlideShowPresenterViewOperations CreatePresenterViewOperations(
        Action<int, string?>? setNotesText = null) =>
        new(
            () => CreatePresenterState(_utcNow()),
            () => ExecuteBack(),
            () => ExecuteAdvance(),
            SetScreenMode,
            mode => SetPointerMode(mode),
            () => ClearInkStrokes(),
            timing => SetTimingIntent(timing),
            media => SetMediaIntent(media),
            () => RecordingReviewPlan,
            ApplyRecordingReview,
            slideNumber => ExecuteSlideNumberJump(slideNumber),
            setNotesText ?? (static (_, _) => { }));

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        DateTimeOffset? nowUtc = null)
    {
        var plan = _session.ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            ResolveNow(nowUtc));
        RequireRenderer().RefreshInkOverlay();
        return plan;
    }

    public SlideShowPresenterToolPlan SetPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset? nowUtc = null)
    {
        var plan = _session.SetPointerMode(pointerMode, ResolveNow(nowUtc));
        RequireRenderer().RefreshInkOverlay();
        return plan;
    }

    public SlideShowPresenterToolPlan SetTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset? nowUtc = null)
    {
        var plan = _session.SetTimingIntent(timingIntent, ResolveNow(nowUtc));
        RequireRenderer().RefreshInkOverlay();
        return plan;
    }

    public SlideShowPresenterToolPlan SetMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset? nowUtc = null)
    {
        var plan = _session.SetMediaIntent(mediaIntent, ResolveNow(nowUtc));
        RequireRenderer().RefreshInkOverlay();
        return plan;
    }

    public SlideShowInkExecutionResult BeginPointerInk(SlideShowCanvasPointer pointer) =>
        RefreshInkAfter(_session.BeginPointerInk(pointer));

    public SlideShowInkExecutionResult AppendPointerInk(SlideShowCanvasPointer pointer) =>
        RefreshInkAfter(_session.AppendPointerInk(pointer));

    public SlideShowInkExecutionResult EndPointerInk(SlideShowCanvasPointer pointer) =>
        RefreshInkAfter(_session.EndPointerInk(pointer));

    public SlideShowInkExecutionResult ClearInkStrokes() =>
        RefreshInkAfter(_session.ClearInkStrokes());

    public SlideShowInkExecutionResult UndoLastInkStroke() =>
        RefreshInkAfter(_session.UndoLastInkStroke());

    public bool HandleKeyboardInput(string? keyName, bool controlPressed = false)
    {
        var plan = _session.PlanKeyboardInput(keyName, controlPressed);
        _session.ExecuteInputPlan(plan, RequireInputCallbacks());
        return plan.IsHandled;
    }

    public bool HandlePointerInput(SlideShowCanvasPointer pointer)
    {
        var plan = _session.PlanPointerInput(pointer);
        _session.ExecuteInputPlan(plan, RequireInputCallbacks());
        return plan.IsHandled;
    }

    public void ActivateHyperlink(Hyperlink hyperlink)
    {
        var plan = _session.PlanHyperlinkActivation(hyperlink);
        _session.ExecuteInputPlan(plan, RequireInputCallbacks());
    }

    public Hyperlink? HitTestHyperlink(Slide slide, SlideShowCanvasPointer pointer) =>
        _session.HitTestHyperlink(slide, pointer);

    public SlideShowRuntimeDisplayPlan BuildDisplayPlan(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
    {
        var display = _session.BuildDisplayPlan(
            animated,
            zoomTransitionDurationMs,
            zoomShowBackground);
        var captionSlideIndex = _session.DisplaySourceSlideIndex;
        var captionTracks = PresentationMediaTranscriptPlanner
            .BuildTranscriptPlan(_presentation)
            .Tracks
            .Where(track => track.SlideIndex == captionSlideIndex)
            .ToArray();

        return new SlideShowRuntimeDisplayPlan(
            display,
            captionSlideIndex,
            captionTracks,
            _captionPreference.SlideIndex == captionSlideIndex
                ? _captionPreference.ShapeId
                : null,
            _captionPreference.TrackIndex,
            _captionPreference.SlideIndex,
            _presentation.ShowMediaControls,
            _presentation.ShowWithNarration);
    }

    public void RestartKioskShow()
    {
        if (!IsClosed)
        {
            ExecuteHostCommand(_session.PlanFirstSlide());
        }
    }

    public SlideShowRecordingReviewApplyResult ApplyRecordingReview() =>
        _session.ApplyRecordingReview();

    public void Close(DateTimeOffset? nowUtc = null) =>
        _session.Close(ResolveNow(nowUtc));

    public void CloseRendererSession(DateTimeOffset? nowUtc = null)
    {
        if (_rendererSessionClosed)
        {
            return;
        }

        _rendererSessionClosed = true;
        var renderer = RequireRenderer();
        renderer.StopTransitionAudio?.Invoke();
        if (_displayRenderer is not null)
        {
            _displayCoordinator.CloseSession(_displayRenderer);
        }

        renderer.TeardownMedia?.Invoke();
        if (!IsClosed)
        {
            Close(nowUtc);
        }
    }

    private void ExecuteHostCommand(
        SlideShowHostCommand command,
        DateTimeOffset? nowUtc = null) =>
        _session.ExecuteHostCommand(command, ResolveNow(nowUtc), RequireHostCallbacks());

    private SlideShowInkExecutionResult RefreshInkAfter(SlideShowInkExecutionResult result)
    {
        RequireRenderer().RefreshInkOverlay();
        return result;
    }

    private DateTimeOffset ResolveNow(DateTimeOffset? nowUtc) => nowUtc ?? _utcNow();

    private SlideShowRuntimeRendererCallbacks RequireRenderer() =>
        _renderer ?? throw new InvalidOperationException("Bind a slideshow runtime renderer before executing host actions.");

    private SlideShowSessionInputExecutionCallbacks RequireInputCallbacks() =>
        _inputCallbacks ?? throw new InvalidOperationException("Bind a slideshow runtime renderer before executing input.");

    private SlideShowHostExecutionCallbacks RequireHostCallbacks() =>
        _hostCallbacks ?? throw new InvalidOperationException("Bind a slideshow runtime renderer before executing host commands.");

    private ISlideShowDisplayRenderer RequireDisplayRenderer() =>
        _displayRenderer ?? throw new InvalidOperationException(
            "Bind a slideshow display renderer before executing renderer-session actions.");
}
