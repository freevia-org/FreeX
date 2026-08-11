using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Media;
using FreeP.App.Recording;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif

namespace FreeP.App.Avalonia;

/// <summary>
/// Borderless fullscreen window that plays a FreeP presentation as a cross-platform slide show.
///
/// Rendering model
/// ───────────────
/// The window contains a black <see cref="Panel"/> that letter-boxes the slide content.
/// We layer two <see cref="SlideCanvas"/> instances ("back" + "front") for cross-fade
/// and directional transitions, and a <see cref="Canvas"/> animation overlay where per-shape
/// entrance/emphasis/exit effects run.
///
/// Navigation state machine
/// ────────────────────────
/// Delegated to <see cref="SlideShowController"/>. When the user presses an advance key:
///   1. If there are pending animation steps → play the next step group.
///   2. If all steps are exhausted → play the incoming slide's transition, then show the slide.
///
/// Shape animation approach
/// ────────────────────────
/// When entering a slide that has entrance animations, all targeted shapes start hidden
/// (Opacity=0 or translated off-screen). Each click-step reveals the step's shapes via
/// Avalonia Animation. The per-shape visuals come from dedicated Image overlays rendered
/// via RenderTargetBitmap (one per shape) so we can animate them individually without
/// decomposing SlideCanvas's rendering internals.
///
/// Transition approach
/// ────────────────────
/// A snapshot of the outgoing slide is captured into a <see cref="RenderTargetBitmap"/>,
/// displayed in the back layer. The front layer (new slide) is animated in according to
/// the transition kind. Supported: Fade (cross-fade), Cut/None (instant), Push/Cover
/// (directional translate), Wipe/Reveal (incoming edge clip), Uncover (outgoing clip),
/// Push (bidirectional displacement), Pan (scaled directional exchange), Gallery
/// (two-surface gallery exchange), Conveyor (belt-like panel exchange), and Flash
/// (white-flash). Window uses a centered aperture. Morph uses matched object
/// overlays and falls back to Fade only when no object correspondence exists.
///
/// Media
/// ─────
/// Media shapes display the poster bitmap + a play badge (same as the slide renderer).
/// Actual audio/video playback uses the LibVLCSharp adapter with poster/click fallback
/// when a platform cannot load its native LibVLC runtime.
/// </summary>
public sealed class SlideShowWindow : Window, ISlideShowTransitionPlaybackRenderer
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly Presentation    _presentation;
    private readonly SlideShowPlaybackRoute _playbackRoute;
    private readonly SlideShowController _controller;
    private readonly SlideShowSessionController _session;
    private readonly Action<int, string?>? _setSlideNotesText;
    private readonly AvaloniaSlideShowMediaController _mediaController;
    private readonly DispatcherTimer  _autoAdvanceTimer;
    private readonly DispatcherTimer  _kioskRestartTimer;
    private PresenterViewWindow? _presenterViewWindow;
    private bool _zoomShowBackgroundForTransition = true;
    private SlideShowShapeAnimationVisualFramePlan? _lastAnimationFramePlan;
    private IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> _lastAnimationStepFrameEvidence = Array.Empty<SlideShowAnimationStepVisualCheckpointPlan>();
    private SlideShowAnimationStepPlaybackReadinessPlan? _lastAnimationStepPlaybackReadinessPlan;

    // DA2 + DA3: all per-frame DispatcherTimers created by animation/transition helpers
    // (AnimateOpacity, AnimateTranslate, AnimateRectClip, AnimateScale, AnimateRotate,
    //  DelayedAction) register themselves here.  CancelActiveTimers() stops all of them
    // immediately — called before starting a new transition (DA2) and in Teardown (DA3).
    private readonly List<DispatcherTimer> _activeTimers = new();

    // ── Visual tree ───────────────────────────────────────────────────────────────

    // Root: black panel filling the whole window.
    private readonly Panel _root;

    // Back layer: snapshot image for transition outgoing state.
    private readonly Image       _transitionBackImage;
    // Front layer: the live SlideCanvas.
    private readonly SlideCanvas _slideCanvas;
    // Shape animation overlay: a Canvas placed on top of _slideCanvas.
    private readonly Canvas _animOverlay;
    // LibVLC video surfaces and audio-only playback slots for the current slide.
    private readonly Canvas _mediaOverlay;
    // Presenter ink overlay: shared-plan-backed strokes and laser pointer above slide content.
    private readonly Canvas _inkOverlay;
    private readonly Rectangle _transitionFlashOverlay;
    private readonly Rectangle _screenModeOverlay;
    private SlideShowScreenMode _screenMode;
    private string _slideNumberBuffer = string.Empty;
    private Slide? _revealedHiddenSlide;
    private int _revealedHiddenSlideSourceIndex = -1;

    // Per-shape animation state for the current slide.
    // Maps shapeId → the Image element in _animOverlay that represents that shape.
    private readonly Dictionary<uint, Control> _animElements = new();
    private readonly Dictionary<uint, Control> _animFillElements = new();
    private readonly Dictionary<uint, Control> _animLineElements = new();
    private readonly Dictionary<uint, Control> _animFontStyleElements = new();
    private readonly Dictionary<uint, Control> _animFontSizeElements = new();
    private readonly Dictionary<uint, IReadOnlyList<Control>> _paragraphAnimElements = new();

    // Per-animation overlay for entrance/emphasis/exit builds that target an explicit authored
    // paragraph range (p:tgtEl/p:spTgt/p:txEl/p:pRg — e.g. PowerPoint's "By 1st Level Paragraphs"
    // entrance, which authors one p:par per paragraph). Keyed by the ShapeAnimation instance
    // (not ShapeId) because several such entries can target the same shape, each its own range.
    private readonly Dictionary<ShapeAnimation, Control> _paragraphRangeAnimElements = new();

    // Track which shapes have been revealed.
    private readonly HashSet<uint> _revealedShapes = new();
    private List<uint> _entranceShapeIds = new();

    // Current slide dimensions in DIP.
    private double _slideDipW;
    private double _slideDipH;
    private readonly int? _preferredCaptionSlideIndex;
    private readonly uint? _preferredCaptionShapeId;
    private readonly int? _preferredCaptionTrackIndex;

    // ── Construction ─────────────────────────────────────────────────────────────

    /// <param name="presentation">The presentation to play.</param>
    /// <param name="startIndex">Zero-based slide index to start from.</param>
    public SlideShowWindow(Presentation presentation, int startIndex = 0)
        : this(
            presentation,
            SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex),
            captureBackend: null)
    {
    }

    internal SlideShowWindow(
        Presentation presentation,
        int startIndex,
        ISlideShowRecordingCaptureBackend? captureBackend)
        : this(
            presentation,
            SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex),
            captureBackend)
    {
    }

    /// <param name="presentation">The presentation that owns slide size, theme, and timing state.</param>
    /// <param name="playbackRoute">The ordered slide route to play.</param>
    public SlideShowWindow(Presentation presentation, SlideShowPlaybackRoute playbackRoute)
        : this(presentation, playbackRoute, captureBackend: null, setSlideNotesText: null)
    {
    }

    public SlideShowWindow(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        Action<int, string?>? setSlideNotesText,
        int? preferredCaptionSlideIndex = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null)
        : this(
            presentation,
            playbackRoute,
            captureBackend: null,
            setSlideNotesText,
            preferredCaptionSlideIndex,
            preferredCaptionShapeId,
            preferredCaptionTrackIndex)
    {
    }

    internal SlideShowWindow(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        ISlideShowRecordingCaptureBackend? captureBackend,
        Action<int, string?>? setSlideNotesText = null,
        int? preferredCaptionSlideIndex = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _playbackRoute = playbackRoute ?? throw new ArgumentNullException(nameof(playbackRoute));
        _setSlideNotesText = setSlideNotesText;
        _preferredCaptionSlideIndex = preferredCaptionSlideIndex;
        _preferredCaptionShapeId = preferredCaptionShapeId;
        _preferredCaptionTrackIndex = preferredCaptionTrackIndex;
        _controller = new SlideShowController(
            _playbackRoute.Slides,
            _playbackRoute.StartIndex,
            _playbackRoute.AnimationStartIndex,
            showWithAnimation: _presentation.ShowWithAnimation,
            loopUntilStopped: _presentation.LoopUntilStopped);
        _session = new SlideShowSessionController(
            _presentation,
            _playbackRoute,
            DateTimeOffset.UtcNow,
            captureBackend ?? CreateDefaultRecordingCaptureBackend());

        // Pre-compute slide DIP dimensions.
        var metrics = SlideShowHostPlanner.BuildSlideMetrics(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
        _slideDipW = metrics.WidthDip;
        _slideDipH = metrics.HeightDip;

        // Speaker and kiosk modes are fullscreen; individual browsing is a normal window.
        var isBrowseWindow = _presentation.ShowType == PresentationShowType.BrowsedByIndividual;
        WindowState        = isBrowseWindow ? WindowState.Normal : WindowState.FullScreen;
        ExtendClientAreaToDecorationsHint = !isBrowseWindow;
        Topmost            = !isBrowseWindow;
        if (isBrowseWindow)
        {
            Width = 1024;
            Height = 768;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }
        Background         = Brushes.Black;
        Focusable          = true;
        CanResize          = isBrowseWindow;

        // ── Visual tree ────────────────────────────────────────────────────────

        // Transition back image (snapshot of outgoing slide).
        _transitionBackImage = new Image
        {
            Stretch             = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsVisible           = false,
        };

        // Front layer: live SlideCanvas.
        _slideCanvas = new SlideCanvas
        {
            Presentation        = _presentation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        // Animation overlay: sits on top of the slide canvas.
        _animOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        _mediaOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        _inkOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        // Stack everything in a Grid (single cell).
        var stage = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        stage.Children.Add(_transitionBackImage);
        stage.Children.Add(_slideCanvas);
        stage.Children.Add(_animOverlay);
        stage.Children.Add(_mediaOverlay);
        stage.Children.Add(_inkOverlay);

        _mediaController = new AvaloniaSlideShowMediaController(_mediaOverlay);
        SizeChanged += (_, _) => SyncMediaOverlayBounds();

        _transitionFlashOverlay = new Rectangle
        {
            Fill = Brushes.White,
            Opacity = 0,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 3,
        };
        stage.Children.Add(_transitionFlashOverlay);

        _screenModeOverlay = new Rectangle
        {
            Fill = Brushes.Black,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 4,
        };
        stage.Children.Add(_screenModeOverlay);

        _root = new Panel { Background = Brushes.Black };
        if (isBrowseWindow)
        {
            stage.Width = _slideDipW;
            stage.Height = _slideDipH;
            var browser = new ScrollViewer
            {
                Background = Brushes.Black,
                HorizontalScrollBarVisibility = _presentation.ShowBrowseScrollbar
                    ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = _presentation.ShowBrowseScrollbar
                    ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = stage,
            };
            _root.Children.Add(browser);
        }
        else
        {
            _root.Children.Add(stage);
        }

        Content = _root;

        // ── Auto-advance timer ─────────────────────────────────────────────────
        _autoAdvanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            IsEnabled = false,
        };
        _autoAdvanceTimer.Tick += (_, _) => DoAdvance();

        _kioskRestartTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            IsEnabled = false,
        };
        _kioskRestartTimer.Tick += (_, _) => RestartKioskShow();

        // ── Event wiring ───────────────────────────────────────────────────────
        KeyDown             += OnKeyDown;
        PointerPressed      += OnPointerPressed;
        PointerMoved        += OnPointerMoved;
        PointerReleased     += OnPointerReleased;
        Opened              += (_, _) =>
        {
            Focus();
            DisplayCurrentSlide(animated: false);
            StartKioskRestartTimer();
        };
        Closed              += (_, _) => Teardown();
    }

    // ── Public API (callable by test code without showing the window) ─────────────

    /// <summary>
    /// Execute a single logical advance step and return what happened.
    /// </summary>
    public AdvanceResult ExecuteAdvance(DateTimeOffset? nowUtc = null)
    {
        var command = SlideShowHostPlanner.PlanAdvance(_controller);
        ApplyHostCommand(command, nowUtc);
        return command.AdvanceResult!;
    }

    /// <summary>Execute a logical back step and return what happened.</summary>
    public BackResult ExecuteBack(DateTimeOffset? nowUtc = null)
    {
        var command = SlideShowHostPlanner.PlanBack(_controller);
        ApplyHostCommand(command, nowUtc);
        return command.BackResult!;
    }

    /// <summary>Jump to a one-based slide number without playing its entrance transition.</summary>
    public void ExecuteSlideNumberJump(int oneBasedSlideNumber)
    {
        _slideNumberBuffer = string.Empty;
        ApplyHostCommand(SlideShowHostPlanner.PlanSlideNumberJump(
            _controller,
            _playbackRoute.Slides,
            oneBasedSlideNumber,
            _playbackRoute.SourceSlideIndices));
    }

    public Slide? ExecuteHiddenSlideReveal()
    {
        if (_controller.CurrentSlideIndex < 0 ||
            _controller.CurrentSlideIndex >= _playbackRoute.SourceSlideIndices.Count)
        {
            return null;
        }

        var currentSourceIndex = _revealedHiddenSlideSourceIndex >= 0
            ? _revealedHiddenSlideSourceIndex
            : _playbackRoute.SourceSlideIndices[_controller.CurrentSlideIndex];
        var target = SlideShowHostPlanner.FindNextHiddenSlide(
            _presentation,
            _playbackRoute,
            currentSourceIndex);
        if (target is null)
            return null;

        _revealedHiddenSlide = target.Slide;
        _revealedHiddenSlideSourceIndex = target.SourceSlideIndex;
        DisplayCurrentSlide(animated: false);
        return _revealedHiddenSlide;
    }

    /// <summary>The underlying state machine (for test assertions).</summary>
    public SlideShowController Controller => _controller;

    /// <summary>The presenter blank-screen mode currently covering the slide.</summary>
    public SlideShowScreenMode ScreenMode => _screenMode;

    /// <summary>Show the slide, a black screen, or a white screen during presentation.</summary>
    public void SetScreenMode(SlideShowScreenMode mode)
    {
        _screenMode = mode;
        _screenModeOverlay.Fill = mode == SlideShowScreenMode.White ? Brushes.White : Brushes.Black;
        _screenModeOverlay.IsVisible = SlideShowScreenModePlanner.IsBlank(mode);
    }

    public DateTimeOffset PresenterStartedAtUtc => _session.StartedAtUtc;

    public SlideShowPresenterToolPlan PresenterToolPlan => _session.ToolPlan;

    public IReadOnlyList<SlideShowPresenterWorkflowAction> PresenterWorkflowActions =>
        _session.ToolPlan.WorkflowActions;

    public IReadOnlyList<SlideShowPresenterCommandState> PresenterCommandStates =>
        _session.ToolPlan.CommandStates;

    public SlideShowTimingRecorderState TimingRecorderState => _session.TimingRecorderState;

    public SlideShowRecordingExecutionState RecordingExecutionState => _session.RecordingExecutionState;

    public SlideShowRecordingCaptureAdapterReadiness RecordingCaptureAdapterReadiness =>
        _session.RecordingExecutionState.HostCapabilities.EffectiveCaptureAdapterReadiness;

    public IReadOnlyList<SlideShowRecordingExecutionAction> RecordingExecutionActions =>
        _session.RecordingExecutionState.LastActions;

    public bool IsPresenterSessionClosed => _session.IsClosed;

    public SlideShowInkExecutionState InkExecutionState => _session.InkExecutionState;
    public SlideShowPresenterSessionSummary PresenterSessionSummary =>
        SlideShowPresenterSessionSummaryPlanner.BuildSummary(
            _session.RecordingExecutionState,
            _session.InkExecutionState,
            _presentation,
            _playbackRoute.GetSourceSlideIndex);

    public SlideShowRecordingReviewPlan RecordingReviewPlan =>
        _session.RecordingReviewPlan;

    public SlideShowRecordingReviewApplyResult ApplyRecordingReview() =>
        _session.ApplyRecordingReview();

    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal IReadOnlyList<SlideShowMediaShapePlan> ActiveMediaPlansForTest => _mediaController.Active;
    internal string? ActiveMediaCaptionForTest(uint shapeId) => _mediaController.CaptionTextForTest(shapeId);
    internal void RefreshMediaCaptionsForTest() => _mediaController.RefreshCaptionsForTest();
    internal SlideShowMediaClickPlan LastMediaClickForTest => _mediaController.LastClick;
    internal MediaPlaybackBackendAvailability? MediaPlaybackAvailabilityForTest => _mediaController.Availability;
    internal MediaPlaybackFailure? LastMediaPlaybackFailureForTest => _mediaController.LastFailure;
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest => _lastAnimationFramePlan;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest => _lastAnimationStepFrameEvidence;
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest => _lastAnimationStepPlaybackReadinessPlan;
    internal SlideShowPlaybackRoute PlaybackRoute => _playbackRoute;
    internal int CurrentPresentationSlideIndex => _session.CurrentPresentationSlideIndex;
    internal Slide? RevealedHiddenSlideForTest => _revealedHiddenSlide;

    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        SlideShowHostPlanner.BuildPresenterState(
            _presentation,
            _controller,
            _playbackRoute.Slides,
            _session.StartedAtUtc,
            nowUtc,
            displayIntent,
            _session.ToolPlan);

    /// <summary>Whether the synchronized presenter dashboard is currently open.</summary>
    public bool IsPresenterViewOpen => _presenterViewWindow?.IsVisible == true;

    /// <summary>Opens or closes the presenter dashboard without changing audience playback.</summary>
    public void TogglePresenterView()
    {
        if (_presenterViewWindow is { IsVisible: true })
        {
            _presenterViewWindow.Close();
            return;
        }

        var window = new PresenterViewWindow(
            _presentation,
            () => CreatePresenterState(DateTimeOffset.UtcNow),
            () => ExecuteBack(),
            () => ExecuteAdvance(),
            SetScreenMode,
            mode => SetPresenterPointerMode(mode),
            () => ClearPresenterInkStrokes(),
            timing => SetPresenterTimingIntent(timing),
            media => SetPresenterMediaIntent(media),
            () => RecordingReviewPlan,
            () => ApplyRecordingReview(),
            slideNumber => ExecuteSlideNumberJump(slideNumber),
            (slideIndex, text) => _setSlideNotesText?.Invoke(slideIndex, text));
        _presenterViewWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_presenterViewWindow, window))
                _presenterViewWindow = null;
        };
        window.Show(this);
    }

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var plan = _session.ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            _controller.CurrentSlideIndex,
            now);

        RefreshInkOverlay();
        return plan;
    }

    public SlideShowPresenterToolPlan SetPresenterPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset? nowUtc = null)
    {
        var current = _session.ToolPlan;
        return ApplyPresenterToolIntent(
            current.Recording.TimingIntent,
            current.Recording.MediaIntent,
            pointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
    }

    public SlideShowPresenterToolPlan SetPresenterTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset? nowUtc = null)
    {
        var current = _session.ToolPlan;
        return ApplyPresenterToolIntent(
            timingIntent,
            current.Recording.MediaIntent,
            current.PointerInk.PointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
    }

    public SlideShowPresenterToolPlan SetPresenterMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset? nowUtc = null)
    {
        var current = _session.ToolPlan;
        return ApplyPresenterToolIntent(
            current.Recording.TimingIntent,
            mediaIntent,
            current.PointerInk.PointerMode,
            current.PointerInk.InkState.ColorHex,
            current.PointerInk.InkState.ThicknessDip,
            current.PointerInk.InkRetentionDecision,
            nowUtc);
    }

    public SlideShowInkExecutionResult BeginPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(_session.BeginInkStroke(MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult AppendPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(_session.AppendInkStroke(MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult EndPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(_session.EndInkStroke(MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult ClearPresenterInkStrokes() =>
        ApplyInkExecution(_session.ClearInkStrokes());

    public SlideShowInkExecutionResult UndoLastPresenterInkStroke() =>
        ApplyInkExecution(_session.UndoLastInkStroke());

    private static ISlideShowRecordingCaptureBackend CreateDefaultRecordingCaptureBackend()
    {
        if (OperatingSystem.IsLinux())
        {
            var metadata = new LinuxRecordingHostMetadata(
                "Avalonia slideshow",
                "Avalonia Linux recording capture adapter",
                "ppt/media/freep-recordings/avalonia");
            return new LinuxRecordingCaptureBackend(
                new LinuxNarrationCaptureBackend(metadata),
                new LinuxCameraCaptureBackend(metadata));
        }

        var windowsMetadata = new WindowsRecordingHostMetadata(
            "Avalonia slideshow",
            "Avalonia Windows recording capture adapter",
            "ppt/media/freep-recordings/avalonia");
#if FREEP_WINDOWS_CAPTURE
        return new WindowsRecordingCaptureBackend(
            windowsMetadata,
            new WindowsNativeRecordingDeviceCatalog(),
            new WindowsNativeRecordingCaptureEngine(windowsMetadata.AdapterName));
#else
        return new WindowsRecordingCaptureBackend(windowsMetadata);
#endif
    }

    /// <summary>Exposes the slide canvas for test assertions (DA1 suppression).</summary>
    internal SlideCanvas CanvasForTest => _slideCanvas;

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            TogglePresenterView();
            e.Handled = true;
            return;
        }

        // While the audience screen is blanked (B/W), only the keys that legitimately
        // affect the blank state itself may act: toggling B/W again, and Escape to end
        // the show. Every other key — advance, back, slide-number jump, H reveal — must
        // NOT silently move the deck or fire an animation underneath the blank screen.
        if (SlideShowScreenModePlanner.IsBlank(_screenMode))
        {
            if (SlideShowScreenModePlanner.TryPlanKey(e.Key.ToString(), _screenMode, out var blankScreenMode))
            {
                SetScreenMode(blankScreenMode);
                e.Handled = true;
                return;
            }

            if (SlideShowHostPlanner.IntentFromKeyName(e.Key.ToString()) == SlideShowHostIntent.Close)
            {
                ApplyHostCommand(SlideShowHostCommand.Close(stopAutoAdvance: true));
                e.Handled = true;
                return;
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.H)
        {
            ExecuteHiddenSlideReveal();
            e.Handled = true;
            return;
        }

        if (TryHandleSlideNumberKey(e.Key.ToString()))
        {
            e.Handled = true;
            return;
        }

        _slideNumberBuffer = string.Empty;

        if (SlideShowScreenModePlanner.TryPlanKey(e.Key.ToString(), _screenMode, out var screenMode))
        {
            SetScreenMode(screenMode);
            e.Handled = true;
            return;
        }

        var command = SlideShowHostPlanner.PlanKey(e.Key.ToString(), _controller, _playbackRoute.Slides);
        ApplyHostCommand(command);
        e.Handled = command.IsHandled;
    }

    private bool TryHandleSlideNumberKey(string keyName)
    {
        if (SlideShowSlideNumberPlanner.TryGetDigit(keyName, out var digit))
        {
            _slideNumberBuffer = SlideShowSlideNumberPlanner.AppendDigit(_slideNumberBuffer, digit);
            return true;
        }

        if (keyName is "Escape" && _slideNumberBuffer.Length > 0)
        {
            _slideNumberBuffer = string.Empty;
            return true;
        }

        if (keyName is not ("Enter" or "Return") || _slideNumberBuffer.Length == 0)
            return false;

        var buffer = _slideNumberBuffer;
        _slideNumberBuffer = string.Empty;
        if (SlideShowSlideNumberPlanner.TryParseSlideNumber(buffer, out var slideNumber))
        {
            ApplyHostCommand(SlideShowHostPlanner.PlanSlideNumberJump(
                _controller,
                _playbackRoute.Slides,
                slideNumber,
                _playbackRoute.SourceSlideIndices));
        }

        return true;
    }

    // ── Pointer navigation ────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // The audience sees a blank (black/white) screen; a stray click underneath it
        // must not ink, trigger an animation, follow a hyperlink, or advance the deck.
        // Only the B/W/Escape keyboard shortcuts may change the blank state.
        if (SlideShowScreenModePlanner.IsBlank(_screenMode))
        {
            e.Handled = true;
            return;
        }

        var slide = _revealedHiddenSlide ?? _controller.CurrentSlide;
        var pt = e.GetPosition(_slideCanvas);
        var inkResult = BeginPresenterInkStroke(pt.X, pt.Y);
        if (inkResult.IsHandled)
        {
            e.Handled = true;
            return;
        }

        if (slide is not null && _mediaController.TryHandleClick(
            slide,
            _slideDipW,
            _slideDipH,
            _slideCanvas.Bounds.Width,
            _slideCanvas.Bounds.Height,
            pt.X,
            pt.Y))
        {
            e.Handled = true;
            return;
        }

        var pointerIntent = SlideShowHostPlanner.PlanPointerClick(
            slide,
            SlideShowHostPlanner.MapCanvasPointToSlide(
                pt.X,
                pt.Y,
                _slideCanvas.Bounds.Width,
                _slideCanvas.Bounds.Height,
                CurrentSlideMetrics()),
            _presentation);
        switch (pointerIntent.Kind)
        {
            case SlideShowPointerClickIntentKind.Trigger when pointerIntent.TriggerShapeId is uint triggerShapeId:
                PlayTriggerGroup(triggerShapeId);
                break;
            case SlideShowPointerClickIntentKind.Zoom when pointerIntent.TargetSlideIndex is int targetSlideIndex:
                ApplyHostCommand(SlideShowHostPlanner.PlanZoomNavigation(
                    _controller,
                    _presentation.Slides,
                    targetSlideIndex,
                    pointerIntent.ReturnToParent,
                    pointerIntent.TransitionDurationMs,
                    pointerIntent.ShowBackground));
                break;
            case SlideShowPointerClickIntentKind.Hyperlink when pointerIntent.Hyperlink is not null:
                ActivateHyperlink(pointerIntent.Hyperlink);
                break;
            case SlideShowPointerClickIntentKind.Advance:
                DoAdvance();
                break;
        }

        e.Handled = pointerIntent.IsHandled;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var slide = _revealedHiddenSlide ?? _controller.CurrentSlide;
        if (slide is null) { Cursor = Cursor.Default; return; }
        var pt    = e.GetPosition(_slideCanvas);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var inkResult = AppendPresenterInkStroke(pt.X, pt.Y);
            if (inkResult.IsHandled)
            {
                Cursor = CursorForPresenterInk();
                e.Handled = true;
                return;
            }
        }

        var hlink = HitTestHyperlink(slide, pt.X, pt.Y);
        Cursor = hlink is not null ? new Cursor(StandardCursorType.Hand) : CursorForPresenterInk();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pt = e.GetPosition(_slideCanvas);
        var inkResult = EndPresenterInkStroke(pt.X, pt.Y);
        e.Handled = inkResult.IsHandled;
    }

    // ── Hyperlink hit-testing & activation ─────────────────────────────────────────

    /// <summary>
    /// Hit-tests the click point against shapes that carry a hyperlink.
    /// Returns the first matching hyperlink, or null.
    /// </summary>
    internal Hyperlink? HitTestHyperlink(Slide slide, double canvasX, double canvasY)
    {
        var slidePoint = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.Bounds.Width,
            _slideCanvas.Bounds.Height,
            CurrentSlideMetrics());
        return SlideShowHostPlanner.HitTestHyperlink(slide, slidePoint);
    }

    /// <summary>
    /// Activates a hyperlink: external → open URL or local file;
    /// internal → navigate to the target slide.
    /// </summary>
    internal void ActivateHyperlink(Hyperlink hlink)
    {
        if (hlink.IsExternal)
        {
            OpenExternalUrl(hlink.Url!);
        }
        else if (hlink.TargetSlideId is not null)
        {
            var command = SlideShowHostPlanner.PlanInternalSlideJump(
                _controller,
                _playbackRoute.Slides,
                hlink.TargetSlideId);
            if (command.Kind == SlideShowHostCommandKind.NavigateToSlide)
            {
                ApplyHostCommand(command);
            }
            else
            {
                // The target isn't in the playback route — normal advance skips hidden
                // slides, but PowerPoint still honours an explicit hyperlink to one.
                // Reveal it the same way the H key does, without moving the controller's
                // own slide index, so a later Advance resumes where the presenter left off.
                var hiddenTarget = SlideShowHostPlanner.FindHiddenSlideById(_presentation, hlink.TargetSlideId);
                if (hiddenTarget is not null)
                {
                    _autoAdvanceTimer.Stop();
                    _revealedHiddenSlide = hiddenTarget.Slide;
                    _revealedHiddenSlideSourceIndex = hiddenTarget.SourceSlideIndex;
                    DisplayCurrentSlide(animated: false);
                }
            }

            var postconditionPath = Environment.GetEnvironmentVariable("FREEP_PHYSICAL_HYPERLINK_POSTCONDITION");
            if (!string.IsNullOrWhiteSpace(postconditionPath))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(postconditionPath)!);
                File.WriteAllText(
                    postconditionPath,
                    $"activation=internal-slide-hyperlink\ntargetSlideId={hlink.TargetSlideId}\ncurrentSlideIndex={_controller.CurrentSlideIndex}\n");
            }
        }
    }

    /// <summary>
    /// Opens an external URL in the default browser through the shared URI allowlist.
    /// Blocked schemes and launch failures are silently ignored so a bad slideshow link never crashes playback.
    /// </summary>
    internal static void OpenExternalUrl(string url)
    {
        ExternalUriLauncher.Open(
            url,
            uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
    }

    // ── Trigger shape hit-testing ─────────────────────────────────────────────────

    private uint? HitTestTriggerShape(Slide slide, double canvasX, double canvasY)
    {
        var slidePoint = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.Bounds.Width,
            _slideCanvas.Bounds.Height,
            CurrentSlideMetrics());
        return SlideShowHostPlanner.HitTestTriggerShape(slide, slidePoint);
    }

    private SlideShowInkPoint MapPresenterInkPoint(double canvasX, double canvasY)
    {
        var point = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.Bounds.Width,
            _slideCanvas.Bounds.Height,
            CurrentSlideMetrics());
        return new SlideShowInkPoint(point.X, point.Y);
    }

    private SlideShowInkExecutionResult ApplyInkExecution(SlideShowInkExecutionResult result)
    {
        RefreshInkOverlay();
        return result;
    }

    private void RefreshInkOverlay()
    {
        _inkOverlay.Children.Clear();

        var canvasWidth = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW;
        var canvasHeight = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH;
        var plan = SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(
            _session.InkExecutionState,
            canvasWidth,
            canvasHeight,
            CurrentSlideMetrics());
        _inkOverlay.Width = canvasWidth;
        _inkOverlay.Height = canvasHeight;

        foreach (var primitive in plan.Primitives)
        {
            if (primitive.Kind == SlideShowInkOverlayPrimitiveKind.StrokePath)
            {
                AddInkStroke(primitive);
            }
            else if (primitive.Kind == SlideShowInkOverlayPrimitiveKind.LaserDot)
            {
                AddLaserOverlay(primitive);
            }
        }
    }

    private void AddInkStroke(SlideShowInkOverlayPrimitive primitive)
    {
        if (primitive.Points.Count == 0)
        {
            return;
        }

        var polyline = new global::Avalonia.Controls.Shapes.Polyline
        {
            Stroke = InkBrush(primitive.InkState),
            StrokeThickness = primitive.StrokeThicknessDip,
            StrokeLineCap = primitive.UseRoundLineCaps ? PenLineCap.Round : PenLineCap.Flat,
            StrokeJoin = primitive.UseRoundLineJoin ? PenLineJoin.Round : PenLineJoin.Miter,
            Opacity = primitive.InkState.Opacity,
            IsHitTestVisible = false,
        };
        foreach (var point in primitive.Points)
        {
            polyline.Points.Add(new Point(point.X, point.Y));
        }

        _inkOverlay.Children.Add(polyline);
    }

    private void AddLaserOverlay(SlideShowInkOverlayPrimitive primitive)
    {
        if (primitive.CenterPoint is null)
        {
            return;
        }

        var dot = new global::Avalonia.Controls.Shapes.Ellipse
        {
            Width = primitive.RadiusDip * 2,
            Height = primitive.RadiusDip * 2,
            Fill = InkBrush(primitive.InkState),
            Stroke = InkOutlineBrush(primitive),
            StrokeThickness = primitive.OutlineThicknessDip,
            Opacity = primitive.InkState.Opacity,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, primitive.CenterPoint.X - primitive.RadiusDip);
        Canvas.SetTop(dot, primitive.CenterPoint.Y - primitive.RadiusDip);
        _inkOverlay.Children.Add(dot);
    }

    private static IBrush InkBrush(SlideShowInkState inkState)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(inkState.ColorHex));
        }
        catch (FormatException)
        {
            return Brushes.Red;
        }
    }

    private static IBrush InkOutlineBrush(SlideShowInkOverlayPrimitive primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive.OutlineColorHex))
        {
            return Brushes.Transparent;
        }

        try
        {
            return new SolidColorBrush(Color.Parse(primitive.OutlineColorHex));
        }
        catch (FormatException)
        {
            return Brushes.White;
        }
    }

    private Cursor CursorForPresenterInk() =>
        _session.InkExecutionState.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter =>
                new Cursor(StandardCursorType.Cross),
            SlideShowPresenterPointerMode.Eraser => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default
        };

    private void PlayTriggerGroup(uint triggerShapeId)
    {
        ApplyHostCommand(SlideShowHostPlanner.PlanTrigger(_controller, triggerShapeId));
    }

    // ── Navigation helpers ────────────────────────────────────────────────────────

    private void DoAdvance()
    {
        ApplyHostCommand(SlideShowHostPlanner.PlanAdvance(_controller, stopAutoAdvance: true));
    }

    private void DoBack()
    {
        ApplyHostCommand(SlideShowHostPlanner.PlanBack(_controller, stopAutoAdvance: true));
    }

    private void CloseSlideShow(DateTimeOffset nowUtc)
    {
        Teardown(nowUtc);
        Close();
    }

    private void NavigateToSlide(
        Slide slide,
        int index,
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
    {
        _ = slide;
        _ = index;
        DisplayCurrentSlide(animated, zoomTransitionDurationMs, zoomShowBackground);
    }

    private void ApplyHostCommand(SlideShowHostCommand command, DateTimeOffset? nowUtc = null)
    {
        _revealedHiddenSlide = null;
        _revealedHiddenSlideSourceIndex = -1;
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (command.StopAutoAdvance)
            _autoAdvanceTimer.Stop();

        switch (command.Kind)
        {
            case SlideShowHostCommandKind.Close:
                CloseSlideShow(now);
                break;
            case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:
                PlayAnimationStep(command.Step);
                break;
            case SlideShowHostCommandKind.NavigateToSlide when command.Slide is not null:
                MovePresenterTimingToSlide(command.SlideIndex, now);
                NavigateToSlide(
                    command.Slide,
                    command.SlideIndex,
                    command.AnimateSlide,
                    command.TransitionDurationMs,
                    command.UseDestinationBackground);
                break;
        }
    }

    private void MovePresenterTimingToSlide(int slideIndex, DateTimeOffset nowUtc)
    {
        _session.MoveToSlide(slideIndex, nowUtc);
    }

    // ── Slide display + transitions ───────────────────────────────────────────────

    private void DisplayCurrentSlide(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
    {
        var plan = SlideShowHostPlanner.BuildDisplayPlan(
            _presentation,
            _controller,
            animated,
            zoomTransitionDurationMs,
            zoomShowBackground);
        if (_revealedHiddenSlide is not null)
            plan = plan with { Transition = null, AutoAdvanceAfterMs = null };
        _slideDipW = plan.Metrics.WidthDip;
        _slideDipH = plan.Metrics.HeightDip;
        _zoomShowBackgroundForTransition = plan.UseDestinationBackground;
        _slideCanvas.RenderSlideBackground = true;
        RefreshInkOverlay();

        var slide = _revealedHiddenSlide ?? plan.Slide;
        if (slide is null) return;

        var captionSlideIndex = _revealedHiddenSlideSourceIndex >= 0
            ? _revealedHiddenSlideSourceIndex
            : CurrentPresentationSlideIndex;

        // DA2: cancel any in-flight transition/animation timers from the PREVIOUS slide so
        // their stale onComplete callbacks don't clobber the new slide's visual state.
        CancelActiveTimers();

        PrepareAnimationOverlay(slide);

        var captionTracks = PresentationMediaTranscriptPlanner
            .BuildTranscriptPlan(_presentation)
            .Tracks
            .Where(track => track.SlideIndex == captionSlideIndex)
            .ToArray();

        _mediaController.EnterSlide(
            slide,
            _slideDipW,
            _slideDipH,
            _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW,
            _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH,
            captionTracks,
            preferredCaptionShapeId: _preferredCaptionSlideIndex == captionSlideIndex ? _preferredCaptionShapeId : null,
            preferredCaptionTrackIndex: _preferredCaptionTrackIndex,
            captionSlideIndex: captionSlideIndex,
            preferredCaptionSlideIndex: _preferredCaptionSlideIndex,
            showMediaControls: _presentation.ShowMediaControls,
            showNarration: _presentation.ShowWithNarration,
            presentationSlideIndex: captionSlideIndex);

        if (plan.Transition is { } t)
            PlayTransition(slide, t);
        else
            ShowSlideInstant(slide);

        // Wire auto-advance timer.
        _autoAdvanceTimer.Stop();
        if (plan.AutoAdvanceAfterMs is int advMs)
        {
            _autoAdvanceTimer.Interval = TimeSpan.FromMilliseconds(advMs);
            _autoAdvanceTimer.Start();
        }
    }

    private SlideShowSlideMetrics CurrentSlideMetrics() => new(_slideDipW, _slideDipH);

    private void StartKioskRestartTimer()
    {
        _kioskRestartTimer.Stop();
        if (!SlideShowKioskRestartPlanner.TryGetInterval(
                _presentation,
                out var interval))
            return;

        _kioskRestartTimer.Interval = interval;
        _kioskRestartTimer.Start();
    }

    private void RestartKioskShow()
    {
        if (_session.IsClosed)
            return;

        ApplyHostCommand(SlideShowHostPlanner.PlanIntent(
            SlideShowHostIntent.FirstSlide,
            _controller,
            _playbackRoute.Slides,
            stopAutoAdvance: true));
    }

    private void SyncMediaOverlayBounds()
    {
        var width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW;
        var height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH;
        var slide = _revealedHiddenSlide ?? _controller.CurrentSlide;
        if (slide is null)
        {
            _mediaController.SetCanvasBounds(width, height);
            return;
        }

        _mediaController.UpdateLayout(slide, _slideDipW, _slideDipH, width, height);
    }

    private void ShowSlideInstant(Slide slide)
    {
        _transitionBackImage.IsVisible = false;
        _transitionBackImage.Clip = null;
        _transitionBackImage.RenderTransform = null;
        _transitionBackImage.Opacity = 1;
        _transitionBackImage.ZIndex = 0;
        _transitionFlashOverlay.IsVisible = false;
        _transitionFlashOverlay.Opacity = 0;
        _slideCanvas.ZIndex = 0;
        _slideCanvas.RenderSlideBackground = true;
        _slideCanvas.Slide   = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Clip = null;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Refresh();
    }

    /// <summary>
    /// Captures the currently displayed slide as a bitmap for transition use.
    /// Returns null if the canvas has no valid size.
    /// </summary>
    private RenderTargetBitmap? CaptureCurrentSlide()
    {
        double w = _slideCanvas.Bounds.Width;
        double h = _slideCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return null;

        try
        {
            var rtb = new RenderTargetBitmap(new PixelSize((int)w, (int)h));
            rtb.Render(_slideCanvas);
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    // ── Transition effects ────────────────────────────────────────────────────────

    private void PlayTransition(Slide slide, SlideTransition t)
    {
        SlideShowTransitionPlaybackCoordinator.Play(_presentation, slide, t, this);
    }

    void ISlideShowTransitionPlaybackRenderer.PlayTransitionSound(SlideTransition transition)
    {
        if (transition.Sound?.AudioBytes is { Length: > 0 })
            _mediaController.PlayTransitionSound(transition.Sound);
    }

    void ISlideShowTransitionPlaybackRenderer.ResetTransitionVisuals()
    {
        _transitionBackImage.IsVisible = false;
        _transitionBackImage.Clip = null;
        _transitionBackImage.RenderTransform = null;
        _transitionBackImage.Opacity = 1;
        _transitionBackImage.ZIndex = 0;
        _transitionFlashOverlay.IsVisible = false;
        _transitionFlashOverlay.Opacity = 0;
        _slideCanvas.ZIndex = 0;
    }

    void ISlideShowTransitionPlaybackRenderer.ShowInstant(Slide slide, SlideShowTransitionPlaybackPlan plan) => ShowSlideInstant(slide);
    void ISlideShowTransitionPlaybackRenderer.PlayFade(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFadeTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayFlash(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFlashTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayDissolve(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayDissolveTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayBox(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayBoxTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayReveal(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRevealTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayUncover(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayUncoverTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayCover(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayCoverTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlaySplit(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlaySplitTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayBlinds(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayBlindsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayRandomBars(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRandomBarsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayStrips(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayStripsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayWheel(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayWheelTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayZoom(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayZoomTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPan(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPanTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayGallery(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayGalleryTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayConveyor(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayConveyorTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayWindow(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayWindowTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayMorph(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayMorphTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayFlip(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFlipTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayCube(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayCubeTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayRotate(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRotateTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayHoneycomb(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayHoneycombTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlaySwitch(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlaySwitchTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayOrbit(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayOrbitTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayFerris(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFerrisTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayFlythrough(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFlythroughTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayGlitter(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayGlitterTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayRipple(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRippleTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayWind(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayWindTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayCurtains(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayCurtainsTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayShred(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayShredTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayDrape(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayDrapeTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayFracture(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFractureTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayCrush(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayCrushTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPrism(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPrismTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPrestige(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPrestigeTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayWarp(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayWarpTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayVortex(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayVortexTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPageCurl(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPageCurlTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPush(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPushTransition(slide, plan);

    private void PlayFadeTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();

        _slideCanvas.Slide   = slide;
        _slideCanvas.Opacity = 0;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source    = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        // Animate opacity 0→1 on the front canvas.
        AnimateOpacity(_slideCanvas, from: 0, to: 1, durationMs, onComplete: () =>
        {
            _transitionBackImage.IsVisible = false;
            _slideCanvas.Opacity = 1;
        });
    }

    private void PlayFlashTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();

        // Keep the incoming slide below the outgoing snapshot and peak a white
        // surface once between them. This makes Flash distinct from Fade while
        // remaining deterministic for both hosts.
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Refresh();
        _slideCanvas.ZIndex = 1;

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.ZIndex = 2;
        }

        _transitionFlashOverlay.IsVisible = true;
        _transitionFlashOverlay.Opacity = 0;
        _transitionFlashOverlay.ZIndex = 3;

        int halfDuration = Math.Max(1, durationMs / 2);
        if (snapshot is not null)
            AnimateOpacity(_transitionBackImage, 1, 0, halfDuration);

        AnimateOpacity(_transitionFlashOverlay, 0, 1, halfDuration, onComplete: () =>
            AnimateOpacity(_transitionFlashOverlay, 1, 0, halfDuration, onComplete: () =>
            {
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.Opacity = 1;
                _transitionBackImage.ZIndex = 0;
                _transitionFlashOverlay.IsVisible = false;
                _transitionFlashOverlay.Opacity = 0;
                _transitionFlashOverlay.ZIndex = 3;
                _slideCanvas.ZIndex = 0;
            }));
    }

    private void PlayCoverTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var dx = plan.IncomingOffsetX;
        var dy = plan.IncomingOffsetY;

        double w = _slideCanvas.Bounds.Width  > 0 ? _slideCanvas.Bounds.Width  : 960;
        double h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        // Start the incoming slide off-screen.
        _slideCanvas.RenderTransform = new TranslateTransform(dx * w, dy * h);
        _slideCanvas.Slide   = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source    = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateTranslate(_slideCanvas, fromX: dx * w, fromY: dy * h, toX: 0, toY: 0, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _slideCanvas.RenderSlideBackground = true;
                _transitionBackImage.IsVisible = false;
            });
    }

    private void PlayPushTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var dx = plan.IncomingOffsetX;
        var dy = plan.IncomingOffsetY;
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.RenderTransform = new TranslateTransform(dx * w, dy * h);
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransform = new TranslateTransform(0, 0);
            _transitionBackImage.IsVisible = true;
        }

        AnimateTranslate(_slideCanvas, dx * w, dy * h, 0, 0, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _transitionBackImage.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
            });

        if (snapshot is not null)
        {
            AnimateTranslate(_transitionBackImage, 0, 0, dx * w, dy * h, plan.DurationMs);
        }
    }

    // ── Animation helpers (Avalonia dispatcher-based) ─────────────────────────────

    /// <summary>
    /// Animates a control's Opacity from <paramref name="from"/> to <paramref name="to"/>
    /// over <paramref name="durationMs"/> milliseconds, then calls <paramref name="onComplete"/>.
    /// Uses a DispatcherTimer stepping approach for cross-platform compatibility.
    /// </summary>
    private void PlaySplitTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildSplitGeometry(w, h, 0, plan.SplitHorizontal, plan.SplitOut);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateSplitClip(_slideCanvas, w, h, plan.SplitHorizontal, plan.SplitOut,
            plan.DurationMs, onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private void PlayDissolveTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildDissolveTransitionGeometry(w, h, 0);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateDissolveTransitionClip(
            _slideCanvas,
            w,
            h,
            durationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static Geometry BuildDissolveTransitionGeometry(
        double width,
        double height,
        double progress)
    {
        var geometry = new GeometryGroup();
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildDissolveTransitionRects(
                     width,
                     height,
                     SlideShowPlaybackPlanner.DissolveRowCount,
                     SlideShowPlaybackPlanner.DissolveColumnCount,
                     progress))
        {
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        }

        return geometry;
    }

    private void PlayBoxTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        var clipRect = BuildBoxTransitionGeometry(w, h, 0, plan.BoxExpandsFromCenter);
        _slideCanvas.Clip = clipRect;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateRectClip(
            _slideCanvas,
            clipRect,
            ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(w, h, 0, plan.BoxExpandsFromCenter)),
            ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(w, h, 1, plan.BoxExpandsFromCenter)),
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static RectangleGeometry BuildBoxTransitionGeometry(
        double width,
        double height,
        double progress,
        bool expandsFromCenter) =>
        new(ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(
            width, height, progress, expandsFromCenter)));

    private static RectangleGeometry BuildWindowTransitionGeometry(
        double width,
        double height,
        double progress)
    {
        var opening = SlideShowPlaybackPlanner.WindowInitialOpenFactor
            + (1 - SlideShowPlaybackPlanner.WindowInitialOpenFactor) * Math.Clamp(progress, 0, 1);
        return BuildBoxTransitionGeometry(width, height, opening, expandsFromCenter: true);
    }

    private void PlayRevealTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        var clipRect = BuildRevealTransitionGeometry(w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY);
        _slideCanvas.Clip = clipRect;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateRectClip(
            _slideCanvas,
            clipRect,
            ToRect(SlideShowMaskGeometryPlanner.BuildRevealTransitionRect(
                w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY)),
            ToRect(SlideShowMaskGeometryPlanner.BuildRevealTransitionRect(
                w, h, 1, plan.IncomingOffsetX, plan.IncomingOffsetY)),
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static RectangleGeometry BuildRevealTransitionGeometry(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY) =>
        new(ToRect(SlideShowMaskGeometryPlanner.BuildRevealTransitionRect(
            width, height, progress, incomingOffsetX, incomingOffsetY)));

    private void PlayUncoverTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = null;
        _slideCanvas.Refresh();

        if (snapshot is null)
            return;

        _transitionBackImage.Source = snapshot;
        _transitionBackImage.IsVisible = true;
        _transitionBackImage.ZIndex = 2;
        var clipRect = BuildUncoverTransitionGeometry(w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY);
        _transitionBackImage.Clip = clipRect;

        AnimateRectClip(
            _transitionBackImage,
            clipRect,
            ToRect(SlideShowMaskGeometryPlanner.BuildUncoverTransitionRect(
                w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY)),
            ToRect(SlideShowMaskGeometryPlanner.BuildUncoverTransitionRect(
                w, h, 1, plan.IncomingOffsetX, plan.IncomingOffsetY)),
            plan.DurationMs,
            onComplete: () =>
            {
                _transitionBackImage.Clip = null;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            });
    }

    private static RectangleGeometry BuildUncoverTransitionGeometry(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY) =>
        new(ToRect(SlideShowMaskGeometryPlanner.BuildUncoverTransitionRect(
            width, height, progress, incomingOffsetX, incomingOffsetY)));

    private void AnimateDissolveTransitionClip(
        Control target,
        double width,
        double height,
        int durationMs,
        Action? onComplete = null,
        bool reverse = false,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildDissolveTransitionGeometry(width, height, reverse ? 0 : 1);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var progress = ApplyAnimationEasing(t, acceleration, deceleration);
            target.Clip = BuildDissolveTransitionGeometry(
                width,
                height,
                reverse ? 1 - progress : progress);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildDissolveTransitionGeometry(width, height, reverse ? 0 : 1);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static Geometry BuildSplitGeometry(
        double width, double height, double progress, bool horizontal, bool fromCenter)
    {
        var geometry = new GeometryGroup();
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildSplitRects(
                     width, height, progress, horizontal, fromCenter))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayBlindsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildBlindsTransitionGeometry(w, h, 0, plan.BlindsHorizontal);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateBlindsTransitionClip(
            _slideCanvas, w, h, plan.BlindsHorizontal, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static Geometry BuildBlindsTransitionGeometry(
        double width, double height, double progress, bool horizontal)
    {
        var geometry = new GeometryGroup();
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildBlindsTransitionRects(
                     width, height, SlideShowPlaybackPlanner.BlindsBandCount, progress, horizontal))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayRandomBarsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildRandomBarsTransitionGeometry(w, h, 0, plan.RandomBarsHorizontal);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateRandomBarsTransitionClip(
            _slideCanvas, w, h, plan.RandomBarsHorizontal, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static Geometry BuildRandomBarsTransitionGeometry(
        double width, double height, double progress, bool horizontal)
    {
        var geometry = new GeometryGroup();
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildRandomBarsTransitionRects(
                     width, height, SlideShowPlaybackPlanner.RandomBarsBandCount, progress, horizontal))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayStripsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildStripsTransitionGeometry(w, h, 0, plan.StripsSlopeDown);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateStripsTransitionClip(
            _slideCanvas, w, h, plan.StripsSlopeDown, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static Geometry BuildStripsTransitionGeometry(
        double width, double height, double progress, bool slopeDown) =>
        BuildStripsGeometry(
            width, height, progress, SlideShowPlaybackPlanner.StripsBandCount, slopeDown);

    private void PlayWheelTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildWheelTransitionGeometry(
            w, h, 0, plan.WheelSpokeCount, plan.WheelReverse);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateWheelTransitionClip(
            _slideCanvas, w, h, plan.WheelSpokeCount, plan.WheelReverse, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private static Geometry BuildWheelTransitionGeometry(
        double width,
        double height,
        double progress,
        int spokeCount,
        bool reverse) =>
        BuildWheelGeometry(width, height, progress, spokeCount, reverse);

    private void AnimateWheelTransitionClip(
        Control target,
        double width,
        double height,
        int spokeCount,
        bool reverse,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildWheelTransitionGeometry(width, height, 1, spokeCount, reverse);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            target.Clip = BuildWheelTransitionGeometry(
                width, height, EaseInOut(t), spokeCount, reverse);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildWheelTransitionGeometry(width, height, 1, spokeCount, reverse);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void PlayZoomTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var startScale = plan.ZoomIn
            ? SlideShowPlaybackPlanner.ZoomInStartScale
            : SlideShowPlaybackPlanner.ZoomOutStartScale;

        // Capture the outgoing slide with its own background, then apply showBg to the
        // incoming destination surface only.
        _slideCanvas.Slide = slide;
        _slideCanvas.RenderSlideBackground = _zoomShowBackgroundForTransition;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        var transform = new ScaleTransform(startScale, startScale);
        _slideCanvas.RenderTransform = transform;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimateZoomTransition(
            transform, startScale, plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private void AnimateZoomTransition(
        ScaleTransform transform,
        double startScale,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            transform.ScaleX = transform.ScaleY = 1;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            var value = startScale + (1 - startScale) * eased;
            transform.ScaleX = transform.ScaleY = value;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                transform.ScaleX = transform.ScaleY = 1;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void PlayPanTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var dx = plan.IncomingOffsetX * w;
        var dy = plan.IncomingOffsetY * h;
        var transform = new MatrixTransform(Matrix.CreateScale(
            SlideShowPlaybackPlanner.PanStartScale,
            SlideShowPlaybackPlanner.PanStartScale));

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        _slideCanvas.RenderTransform = transform;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        AnimatePanTransition(
            transform,
            dx,
            dy,
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private void PlayGalleryTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var travelX = plan.IncomingOffsetX * w * SlideShowPlaybackPlanner.GalleryTravelFactor;
        var travelY = plan.IncomingOffsetY * h * SlideShowPlaybackPlanner.GalleryTravelFactor;
        var incomingTransform = new MatrixTransform(Matrix.CreateScale(
            SlideShowPlaybackPlanner.GalleryStartScale,
            SlideShowPlaybackPlanner.GalleryStartScale));

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        _slideCanvas.RenderTransform = incomingTransform;
        _slideCanvas.ZIndex = 1;
        _slideCanvas.Refresh();

        MatrixTransform? outgoingTransform = null;
        if (snapshot is not null)
        {
            outgoingTransform = new MatrixTransform(Matrix.Identity);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransformOrigin = RelativePoint.Center;
            _transitionBackImage.RenderTransform = outgoingTransform;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.ZIndex = 0;
        }

        AnimateGalleryTransition(
            incomingTransform,
            outgoingTransform,
            travelX,
            travelY,
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _slideCanvas.ZIndex = 0;
                _transitionBackImage.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            });
    }

    private void PlayConveyorTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var horizontal = Math.Abs(plan.IncomingOffsetX) > 0;
        var travelX = plan.IncomingOffsetX * w * SlideShowPlaybackPlanner.ConveyorTravelFactor;
        var travelY = plan.IncomingOffsetY * h * SlideShowPlaybackPlanner.ConveyorTravelFactor;
        var crossX = horizontal
            ? 0
            : Math.Sign(plan.IncomingOffsetY) * w * SlideShowPlaybackPlanner.ConveyorCrossAxisFactor;
        var crossY = horizontal
            ? -Math.Sign(plan.IncomingOffsetX) * h * SlideShowPlaybackPlanner.ConveyorCrossAxisFactor
            : 0;
        var endX = travelX + crossX;
        var endY = travelY + crossY;
        var tilt = (horizontal ? -Math.Sign(plan.IncomingOffsetX) : Math.Sign(plan.IncomingOffsetY))
            * SlideShowPlaybackPlanner.ConveyorTiltDegrees;

        var incomingTransform = new MatrixTransform(Matrix.CreateScale(
            SlideShowPlaybackPlanner.ConveyorStartScale,
            SlideShowPlaybackPlanner.ConveyorStartScale)
            * Matrix.CreateRotation(tilt * Math.PI / 180)
            * Matrix.CreateTranslation(endX, endY));

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        _slideCanvas.RenderTransform = incomingTransform;
        _slideCanvas.ZIndex = 1;
        _slideCanvas.Refresh();

        MatrixTransform? outgoingTransform = null;
        if (snapshot is not null)
        {
            outgoingTransform = new MatrixTransform(Matrix.Identity);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransformOrigin = RelativePoint.Center;
            _transitionBackImage.RenderTransform = outgoingTransform;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.ZIndex = 0;
        }

        AnimateConveyorTransition(
            incomingTransform,
            outgoingTransform,
            endX,
            endY,
            tilt,
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _slideCanvas.ZIndex = 0;
                _transitionBackImage.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            });
    }

    private void AnimateConveyorTransition(
        MatrixTransform incoming,
        MatrixTransform? outgoing,
        double endX,
        double endY,
        double tilt,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            incoming.Matrix = Matrix.Identity;
            if (outgoing is not null) outgoing.Matrix = Matrix.Identity;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            var incomingScale = SlideShowPlaybackPlanner.ConveyorStartScale
                + (1 - SlideShowPlaybackPlanner.ConveyorStartScale) * eased;
            var incomingAngle = tilt * (1 - eased) * Math.PI / 180;
            incoming.Matrix = Matrix.CreateScale(incomingScale, incomingScale)
                * Matrix.CreateRotation(incomingAngle)
                * Matrix.CreateTranslation(endX * (1 - eased), endY * (1 - eased));

            if (outgoing is not null)
            {
                var outgoingScale = 1
                    + (SlideShowPlaybackPlanner.ConveyorOutgoingEndScale - 1) * eased;
                outgoing.Matrix = Matrix.CreateScale(outgoingScale, outgoingScale)
                    * Matrix.CreateRotation(-tilt * eased * Math.PI / 180)
                    * Matrix.CreateTranslation(endX * eased, endY * eased);
            }

            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                incoming.Matrix = Matrix.Identity;
                if (outgoing is not null) outgoing.Matrix = Matrix.Identity;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void PlayWindowTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var scale = new ScaleTransform(
            SlideShowPlaybackPlanner.WindowStartScale,
            SlideShowPlaybackPlanner.WindowStartScale);
        var clipRect = BuildWindowTransitionGeometry(w, h, 0);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        _slideCanvas.RenderTransform = scale;
        _slideCanvas.Clip = clipRect;
        _slideCanvas.ZIndex = 1;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.ZIndex = 0;
        }

        AnimateWindowTransition(
            scale,
            clipRect,
            w,
            h,
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.Clip = null;
                _slideCanvas.RenderTransform = null;
                _slideCanvas.ZIndex = 0;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            });
    }

    private void AnimateWindowTransition(
        ScaleTransform scale,
        RectangleGeometry clipRect,
        double width,
        double height,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            scale.ScaleX = scale.ScaleY = 1;
            clipRect.Rect = ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(width, height, 1, true));
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            scale.ScaleX = scale.ScaleY = SlideShowPlaybackPlanner.WindowStartScale
                + (1 - SlideShowPlaybackPlanner.WindowStartScale) * eased;
            var opening = SlideShowPlaybackPlanner.WindowInitialOpenFactor
                + (1 - SlideShowPlaybackPlanner.WindowInitialOpenFactor) * eased;
            clipRect.Rect = ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(
                width, height, opening, true));
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                scale.ScaleX = scale.ScaleY = 1;
                clipRect.Rect = ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(width, height, 1, true));
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Plays a shape-aware Morph exchange. Matched incoming objects are rendered
    /// as transparent full-stage overlays and begin at the outgoing object's
    /// bounds before interpolating to their authored target bounds.
    /// </summary>
    private void PlayMorphTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var source = _slideCanvas.Slide;
        if (source is null)
        {
            PlayFadeTransition(slide, plan.DurationMs);
            return;
        }

        var morphPlan = SlideShowMorphPlanner.Plan(transition, source, slide);
        if (!morphPlan.HasObjectMatches)
        {
            PlayFadeTransition(slide, plan.DurationMs);
            return;
        }

        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var transform = SlideTransformCore.Compute(w, h, _slideDipW, _slideDipH);
        var prepared = new List<(Image Image, MatrixTransform Transform, double ScaleX, double ScaleY, double TranslateX, double TranslateY, uint ShapeId)>();

        void AddMorphOverlay(RenderTargetBitmap? bitmap, Rect sourceRect, Rect targetRect, uint shapeId)
        {
            if (bitmap is null || sourceRect.Width < 0.5 || sourceRect.Height < 0.5
                || targetRect.Width < 0.5 || targetRect.Height < 0.5)
                return;

            var scaleX = sourceRect.Width / targetRect.Width;
            var scaleY = sourceRect.Height / targetRect.Height;
            var translateX = sourceRect.Left + sourceRect.Width / 2 - (targetRect.Left + targetRect.Width / 2);
            var translateY = sourceRect.Top + sourceRect.Height / 2 - (targetRect.Top + targetRect.Height / 2);
            var matrix = new MatrixTransform(Matrix.CreateScale(scaleX, scaleY)
                * Matrix.CreateTranslation(translateX, translateY));
            var image = new Image
            {
                Source = bitmap,
                Width = w,
                Height = h,
                Stretch = Stretch.None,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(
                    (targetRect.Left + targetRect.Width / 2) / w,
                    (targetRect.Top + targetRect.Height / 2) / h,
                    RelativeUnit.Relative),
                RenderTransform = matrix
            };
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, 0);
            _animOverlay.Children.Add(image);
            _slideCanvas.SuppressedShapeIds.Add(shapeId);
            prepared.Add((image, matrix, scaleX, scaleY, translateX, translateY, shapeId));
        }

        foreach (var match in morphPlan.Matches)
        {
            if (match.Source.ExtentCxEmu <= 0 || match.Source.ExtentCyEmu <= 0
                || match.Target.ExtentCxEmu <= 0 || match.Target.ExtentCyEmu <= 0)
                continue;

            var sourceRect = MorphShapeScreenRect(match.Source, transform);
            var targetRect = MorphShapeScreenRect(match.Target, transform);
            bool tokenMorph = morphPlan.Option is "byWord" or "byChar" &&
                match.Tokens.Count > 0 &&
                !string.IsNullOrWhiteSpace(match.Source.PlainText) &&
                !string.IsNullOrWhiteSpace(match.Target.PlainText);
            if (!tokenMorph)
            {
                AddMorphOverlay(RenderShapeToOverlayBitmap(slide, match.Target, w, h), sourceRect, targetRect, match.Target.Id);
                continue;
            }

            var background = SlideCloner.CloneShape(match.Target);
            background.TextBody = null;
            AddMorphOverlay(RenderShapeToOverlayBitmap(slide, background, w, h), sourceRect, targetRect, match.Target.Id);
            foreach (var token in match.Tokens)
            {
                var tokenShape = SlideShowMorphPlanner.CreateTokenShape(
                    match.Target,
                    token.TargetStart,
                    token.TargetLength);
                AddMorphOverlay(
                    RenderShapeToOverlayBitmap(slide, tokenShape, w, h),
                    MorphTokenScreenRect(match.Source, token, source: true, transform),
                    MorphTokenScreenRect(match.Target, token, source: false, transform),
                    match.Target.Id);
            }
        }

        if (prepared.Count == 0)
        {
            foreach (var item in prepared)
            {
                _animOverlay.Children.Remove(item.Image);
                _slideCanvas.SuppressedShapeIds.Remove(item.ShapeId);
            }
            PlayFadeTransition(slide, plan.DurationMs);
            return;
        }

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.ZIndex = 1;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.ZIndex = 0;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            if (snapshot is not null)
                _transitionBackImage.Opacity = 1 - eased;

            foreach (var item in prepared)
            {
                item.Transform.Matrix = Matrix.CreateScale(
                        item.ScaleX + (1 - item.ScaleX) * eased,
                        item.ScaleY + (1 - item.ScaleY) * eased)
                    * Matrix.CreateTranslation(
                        item.TranslateX * (1 - eased),
                        item.TranslateY * (1 - eased));
                item.Image.Opacity = eased;
            }

            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                foreach (var item in prepared)
                {
                    _animOverlay.Children.Remove(item.Image);
                    _slideCanvas.SuppressedShapeIds.Remove(item.ShapeId);
                }

                _transitionBackImage.Opacity = 1;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
                _slideCanvas.ZIndex = 0;
                _slideCanvas.Refresh();
            }
        };
        timer.Start();
    }

    private static Rect MorphShapeScreenRect(SlideShape shape, SlideTransformCore transform)
    {
        var topLeft = transform.SlideToScreen(
            SlideTransformCore.EmuToDip(shape.OffsetXEmu),
            SlideTransformCore.EmuToDip(shape.OffsetYEmu));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            transform.ScaleDipToScreen(SlideTransformCore.EmuToDip(shape.ExtentCxEmu)),
            transform.ScaleDipToScreen(SlideTransformCore.EmuToDip(shape.ExtentCyEmu)));
    }

    private static Rect MorphTokenScreenRect(
        SlideShape shape,
        SlideShowMorphTokenMatch token,
        bool source,
        SlideTransformCore transform)
    {
        string text = shape.PlainText;
        int start = source ? token.SourceStart : token.TargetStart;
        int length = source ? token.SourceLength : token.TargetLength;
        int lineStart = text.LastIndexOf('\n', Math.Clamp(start - 1, 0, text.Length - 1)) + 1;
        int lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = text.Length;
        int lineLength = Math.Max(1, lineEnd - lineStart);
        int lineIndex = text[..Math.Clamp(start, 0, text.Length)].Count(ch => ch == '\n');
        int lineCount = Math.Max(1, text.Count(ch => ch == '\n') + 1);
        var shapeRect = MorphShapeScreenRect(shape, transform);
        const double horizontalInset = 0.06;
        double textWidth = shapeRect.Width * (1 - horizontalInset * 2);
        double x = shapeRect.Left + shapeRect.Width * horizontalInset +
            textWidth * (start - lineStart) / lineLength;
        double y = shapeRect.Top + shapeRect.Height * lineIndex / lineCount;
        double width = Math.Max(1, textWidth * Math.Max(1, length) / lineLength);
        double height = Math.Max(1, shapeRect.Height / lineCount);
        return new Rect(x, y, width, height);
    }

    private void PlayFlipTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlayCubeTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlayRotateTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlaySwitchTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlayOrbitTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlayFerrisTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    private void PlayFlythroughTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan) =>
        PlayPerspectiveTransition(slide, transition, plan);

    /// <summary>Shared two-surface projection for Flip, Cube, and Rotate.</summary>
    private void PlayPerspectiveTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var perspective = SlideShowPerspectiveTransitionPlanner.Plan(transition);
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var travelX = plan.IncomingOffsetX * w * perspective.TravelFactor;
        var travelY = plan.IncomingOffsetY * h * perspective.TravelFactor;

        var incomingScaleX = perspective.HorizontalAxis ? perspective.StartScale : 1;
        var incomingScaleY = perspective.HorizontalAxis ? 1 : perspective.StartScale;
        if (!perspective.IsAxisCollapsed)
        {
            incomingScaleX = perspective.StartScale;
            incomingScaleY = perspective.StartScale;
        }

        var incoming = new MatrixTransform(BuildPerspectiveMatrix(
            incomingScaleX,
            incomingScaleY,
            perspective.StartRotationDegrees,
            travelX,
            travelY));
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransformOrigin = RelativePoint.Center;
        _slideCanvas.RenderTransform = incoming;
        _slideCanvas.ZIndex = 1;
        _slideCanvas.Refresh();

        MatrixTransform? outgoing = null;
        if (snapshot is not null)
        {
            outgoing = new MatrixTransform(Matrix.Identity);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransformOrigin = RelativePoint.Center;
            _transitionBackImage.RenderTransform = outgoing;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.IsVisible = true;
            _transitionBackImage.ZIndex = 0;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            var incomingScaleX = perspective.HorizontalAxis ?
                perspective.StartScale + (1 - perspective.StartScale) * eased : 1;
            var incomingScaleY = perspective.HorizontalAxis ? 1 :
                perspective.StartScale + (1 - perspective.StartScale) * eased;
            if (!perspective.IsAxisCollapsed)
            {
                incomingScaleX = perspective.StartScale + (1 - perspective.StartScale) * eased;
                incomingScaleY = incomingScaleX;
            }

            incoming.Matrix = BuildPerspectiveMatrix(
                incomingScaleX,
                incomingScaleY,
                perspective.StartRotationDegrees * (1 - eased),
                travelX * (1 - eased),
                travelY * (1 - eased));

            if (outgoing is not null)
            {
                var outgoingScaleX = perspective.HorizontalAxis && perspective.IsAxisCollapsed
                    ? 1 + (perspective.StartScale - 1) * eased
                    : 1 + (incomingScaleX - 1) * eased;
                var outgoingScaleY = !perspective.HorizontalAxis && perspective.IsAxisCollapsed
                    ? 1 + (perspective.StartScale - 1) * eased
                    : 1 + (incomingScaleY - 1) * eased;
                if (!perspective.IsAxisCollapsed)
                {
                    outgoingScaleX = outgoingScaleY =
                        1 + (perspective.StartScale - 1) * eased;
                }

                outgoing.Matrix = BuildPerspectiveMatrix(
                    outgoingScaleX,
                    outgoingScaleY,
                    -perspective.StartRotationDegrees * eased,
                    -travelX * eased,
                    -travelY * eased);
                _transitionBackImage.Opacity = 1 - eased;
            }

            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.RenderTransform = null;
                _slideCanvas.ZIndex = 0;
                _transitionBackImage.RenderTransform = null;
                _transitionBackImage.Opacity = 1;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            }
        };
        timer.Start();
    }

    private void PlayHoneycombTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var honeycomb = SlideShowHoneycombTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildHoneycombTransitionGeometry(w, h, 0, honeycomb);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildHoneycombTransitionGeometry(w, h, progress, honeycomb);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayGlitterTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var glitter = SlideShowGlitterTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildGlitterTransitionGeometry(w, h, 0, glitter);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildGlitterTransitionGeometry(w, h, progress, glitter);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayRippleTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var ripple = SlideShowRippleTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildRippleTransitionGeometry(w, h, 0, ripple);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildRippleTransitionGeometry(w, h, progress, ripple);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayWindTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var wind = SlideShowWindTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildWindTransitionGeometry(w, h, 0, wind);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildWindTransitionGeometry(w, h, progress, wind);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayCurtainsTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var curtains = SlideShowCurtainsTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildCurtainsTransitionGeometry(w, h, 0, curtains);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildCurtainsTransitionGeometry(w, h, progress, curtains);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayShredTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var shred = SlideShowShredTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildShredTransitionGeometry(w, h, 0, shred);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildShredTransitionGeometry(w, h, progress, shred);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayDrapeTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var drape = SlideShowDrapeTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildDrapeTransitionGeometry(w, h, 0, drape);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildDrapeTransitionGeometry(w, h, progress, drape);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayVortexTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var vortex = SlideShowVortexTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildVortexTransitionGeometry(w, h, 0, vortex);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildVortexTransitionGeometry(w, h, progress, vortex);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayWarpTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var warp = SlideShowWarpTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildWarpTransitionGeometry(w, h, 0, warp);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildWarpTransitionGeometry(w, h, progress, warp);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayFractureTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var fracture = SlideShowFractureTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildFractureTransitionGeometry(w, h, 0, fracture);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildFractureTransitionGeometry(w, h, progress, fracture);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayCrushTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var crush = SlideShowCrushTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildCrushTransitionGeometry(w, h, 0, crush);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildCrushTransitionGeometry(w, h, progress, crush);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayPrismTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var prism = SlideShowPrismTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildPrismTransitionGeometry(w, h, 0, prism);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildPrismTransitionGeometry(w, h, progress, prism);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private void PlayPrestigeTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var prestige = SlideShowPrestigeTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildPrestigeTransitionGeometry(w, h, 0, prestige);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _slideCanvas.Clip = BuildPrestigeTransitionGeometry(w, h, progress, prestige);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _slideCanvas.Clip = null;
                _transitionBackImage.IsVisible = false;
            }
        };
        timer.Start();
    }

    private static Matrix BuildPerspectiveMatrix(
        double scaleX,
        double scaleY,
        double rotationDegrees,
        double translateX,
        double translateY) =>
        Matrix.CreateScale(scaleX, scaleY)
            * Matrix.CreateRotation(rotationDegrees * Math.PI / 180)
            * Matrix.CreateTranslation(translateX, translateY);

    private void PlayPageCurlTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var curl = SlideShowPageCurlTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = null;
        _slideCanvas.Refresh();

        if (snapshot is null)
            return;

        _transitionBackImage.Source = snapshot;
        _transitionBackImage.IsVisible = true;
        _transitionBackImage.Clip = BuildPageCurlGeometry(w, h, 0, curl);
        _transitionBackImage.ZIndex = 1;

        const int frameMs = 16;
        var steps = Math.Max(1, plan.DurationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = EaseInOut(Math.Min(1.0, (double)frame / steps));
            _transitionBackImage.Clip = BuildPageCurlGeometry(w, h, progress, curl);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                _transitionBackImage.Clip = null;
                _transitionBackImage.IsVisible = false;
                _transitionBackImage.ZIndex = 0;
            }
        };
        timer.Start();
    }

    private void AnimateGalleryTransition(
        MatrixTransform incoming,
        MatrixTransform? outgoing,
        double travelX,
        double travelY,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            incoming.Matrix = Matrix.Identity;
            if (outgoing is not null) outgoing.Matrix = Matrix.Identity;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            var incomingScale = SlideShowPlaybackPlanner.GalleryStartScale
                + (1 - SlideShowPlaybackPlanner.GalleryStartScale) * eased;
            incoming.Matrix = Matrix.CreateScale(incomingScale, incomingScale)
                * Matrix.CreateTranslation(travelX * (1 - eased), travelY * (1 - eased));

            if (outgoing is not null)
            {
                var outgoingScale = 1
                    + (SlideShowPlaybackPlanner.GalleryOutgoingEndScale - 1) * eased;
                outgoing.Matrix = Matrix.CreateScale(outgoingScale, outgoingScale)
                    * Matrix.CreateTranslation(travelX * eased, travelY * eased);
            }

            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                incoming.Matrix = Matrix.Identity;
                if (outgoing is not null) outgoing.Matrix = Matrix.Identity;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimatePanTransition(
        MatrixTransform transform,
        double startX,
        double startY,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            transform.Matrix = Matrix.CreateScale(1, 1);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var eased = EaseInOut(t);
            var scale = SlideShowPlaybackPlanner.PanStartScale
                + (1 - SlideShowPlaybackPlanner.PanStartScale) * eased;
            var x = startX * (1 - eased);
            var y = startY * (1 - eased);
            transform.Matrix = Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation(x, y);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                transform.Matrix = Matrix.CreateScale(1, 1);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateStripsTransitionClip(
        Control target,
        double width,
        double height,
        bool slopeDown,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildStripsTransitionGeometry(width, height, 1, slopeDown);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            target.Clip = BuildStripsTransitionGeometry(width, height, EaseInOut(t), slopeDown);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildStripsTransitionGeometry(width, height, 1, slopeDown);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateRandomBarsTransitionClip(
        Control target,
        double width,
        double height,
        bool horizontal,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildRandomBarsTransitionGeometry(width, height, 1, horizontal);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            target.Clip = BuildRandomBarsTransitionGeometry(width, height, EaseInOut(t), horizontal);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildRandomBarsTransitionGeometry(width, height, 1, horizontal);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateBlindsTransitionClip(
        Control target,
        double width,
        double height,
        bool horizontal,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildBlindsTransitionGeometry(width, height, 1, horizontal);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            target.Clip = BuildBlindsTransitionGeometry(width, height, EaseInOut(t), horizontal);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildBlindsTransitionGeometry(width, height, 1, horizontal);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateSplitClip(
        Control target,
        double width,
        double height,
        bool horizontal,
        bool fromCenter,
        int durationMs,
        Action? onComplete = null,
        double startProgress = 0,
        double endProgress = 1,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildSplitGeometry(width, height, endProgress, horizontal, fromCenter);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var progress = startProgress + (endProgress - startProgress)
                * ApplyAnimationEasing(t, acceleration, deceleration);
            target.Clip = BuildSplitGeometry(width, height, progress, horizontal, fromCenter);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildSplitGeometry(width, height, endProgress, horizontal, fromCenter);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateOpacity(Control target, double from, double to, int durationMs,
        Action? onComplete = null, int? acceleration = null, int? deceleration = null)
    {
        target.Opacity = from;
        if (durationMs <= 0) { target.Opacity = to; onComplete?.Invoke(); return; }

        const int frameMs = 16; // ~60 fps
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(frameMs) });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double eased = ApplyAnimationEasing(t, acceleration, deceleration);
            target.Opacity = from + (to - from) * eased;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Opacity = to;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Animates a TranslateTransform on a control from (fromX, fromY) to (toX, toY).
    /// </summary>
    private void AnimateTranslate(Control target,
        double fromX, double fromY, double toX, double toY,
        int durationMs, Action? onComplete = null,
        int? acceleration = null, int? deceleration = null)
    {
        var translate = new TranslateTransform(fromX, fromY);
        target.RenderTransform = translate;
        if (durationMs <= 0) { translate.X = toX; translate.Y = toY; onComplete?.Invoke(); return; }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(frameMs) });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double eased = ApplyAnimationEasing(t, acceleration, deceleration);
            translate.X = fromX + (toX - fromX) * eased;
            translate.Y = fromY + (toY - fromY) * eased;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = toX;
                translate.Y = toY;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    /// <summary>Cubic ease-in-out: smooth start and end.</summary>
    private static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static double ApplyAnimationEasing(
        double progress, int? acceleration, int? deceleration) =>
        SlideShowPlaybackPlanner.ApplyHostTimingEasing(progress, acceleration, deceleration);

    // ── Shape animation overlay ───────────────────────────────────────────────────

    /// <summary>
    /// Sets up per-shape animated elements for a new slide:
    ///   1. Identifies shapes with Entrance animations → renders each to a bitmap
    ///      and places it as an Image in _animOverlay, hidden.
    ///   2. DA1: Each entrance shape is added to _slideCanvas.SuppressedShapeIds so the
    ///      base canvas does NOT paint it — the overlay Image is the only visible copy,
    ///      eliminating the "ghost duplicate" where the real shape sat fully visible under
    ///      the animated overlay.
    ///   3. When a build step reveals a shape, RevealShape() removes it from the suppressed
    ///      set and calls Refresh() so the base canvas takes over painting it.
    /// </summary>
    private void PrepareAnimationOverlay(Slide slide)
    {
        _animOverlay.Children.Clear();
        _animElements.Clear();
        _animFillElements.Clear();
        _animLineElements.Clear();
        _animFontStyleElements.Clear();
        _animFontSizeElements.Clear();
        _paragraphAnimElements.Clear();
        _paragraphRangeAnimElements.Clear();
        _revealedShapes.Clear();

        // DA1: clear any suppression from the previous slide.
        _slideCanvas.SuppressedShapeIds.Clear();

        _entranceShapeIds = slide.Animations
            .Where(a => (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion)
                        && a.TriggerShapeId == null)
            .Select(a => a.ShapeId)
            .Distinct()
            .ToList();

        var animatedShapeIds = slide.Animations
            .Where(a => a.Kind == AnimationKind.Emphasis
                        || a.Kind == AnimationKind.Exit
                        || (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion))
            .Select(a => a.ShapeId)
            .Distinct()
            .ToList();

        if (animatedShapeIds.Count == 0) return;

        double w = _slideCanvas.Bounds.Width  > 0 ? _slideCanvas.Bounds.Width  : 960;
        double h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _animOverlay.Width  = w;
        _animOverlay.Height = h;

        foreach (var shapeId in animatedShapeIds)
        {
            var shape = ShapeTreeLookup.Find(slide, shapeId);
            if (shape is null) continue;

            // PowerPoint's "By 1st Level Paragraphs" build (and similar) authors one animation
            // per paragraph, each targeting p:tgtEl/p:spTgt/p:txEl/p:pRg instead of the whole
            // shape. This explicit per-paragraph timing is richer than the bldLst marker
            // checked below (it carries the real reveal order/effect per paragraph), so it
            // takes precedence whenever it is present and covers every paragraph. Only take
            // this path when every paragraph is covered by some ranged animation — a partial
            // authoring falls back to the bldLst-driven split (or whole-shape overlay) below
            // so no text is silently hidden forever.
            var rangedAnims = slide.Animations
                .Where(a => a.ShapeId == shapeId && a.ParagraphRangeStart.HasValue)
                .ToList();
            if (rangedAnims.Count > 0
                && SlideShowAnimationBuildPlanner.ParagraphRangesCoverWholeShape(shape, rangedAnims))
            {
                var rangeBackground = SlideCloner.CloneShape(shape);
                rangeBackground.TextBody = null;
                var rangeBackgroundBitmap = RenderShapeToOverlayBitmap(slide, rangeBackground, w, h);
                if (rangeBackgroundBitmap is not null)
                {
                    _animOverlay.Children.Add(new Image
                    {
                        Source = rangeBackgroundBitmap,
                        Width = w,
                        Height = h,
                        Stretch = Stretch.None,
                        Opacity = 1,
                        IsHitTestVisible = false,
                    });
                }

                var anyRangeRendered = false;
                foreach (var rangedAnim in rangedAnims)
                {
                    var rangeShape = SlideShowAnimationBuildPlanner.CreateParagraphRangeShape(
                        shape,
                        rangedAnim.ParagraphRangeStart!.Value,
                        rangedAnim.ParagraphRangeEnd ?? rangedAnim.ParagraphRangeStart!.Value);
                    if (rangeShape is null) continue;

                    var rangeBitmap = RenderShapeToOverlayBitmap(slide, rangeShape, w, h);
                    if (rangeBitmap is null) continue;

                    var rangeImage = new Image
                    {
                        Source = rangeBitmap,
                        Width = w,
                        Height = h,
                        Stretch = Stretch.None,
                        Opacity = rangedAnim.Kind is AnimationKind.Entrance or AnimationKind.Motion
                            && _entranceShapeIds.Contains(shapeId) ? 0 : 1,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(rangeImage, 0);
                    Canvas.SetTop(rangeImage, 0);
                    _animOverlay.Children.Add(rangeImage);
                    _paragraphRangeAnimElements[rangedAnim] = rangeImage;
                    anyRangeRendered = true;
                }

                if (anyRangeRendered)
                {
                    _slideCanvas.SuppressedShapeIds.Add(shapeId);
                    continue;
                }
            }

            // Fallback: some "By 1st Level Paragraphs" builds emit only the
            // bldLst/bldP[@build='p'] marker without explicit per-paragraph timing
            // (p:txEl/p:pRg) nodes. When there was no usable ranged timing above, split the
            // shape into one overlay per paragraph using that marker alone.
            if (SlideShowAnimationBuildPlanner.IsParagraphBuild(slide, shapeId))
            {
                var paragraphShapes = SlideShowAnimationBuildPlanner.CreateParagraphShapes(shape);
                if (paragraphShapes.Count > 0)
                {
                    var background = SlideCloner.CloneShape(shape);
                    background.TextBody = null;
                    var backgroundBitmap = RenderShapeToOverlayBitmap(slide, background, w, h);
                    if (backgroundBitmap is not null)
                    {
                        _animOverlay.Children.Add(new Image
                        {
                            Source = backgroundBitmap,
                            Width = w,
                            Height = h,
                            Stretch = Stretch.None,
                            Opacity = 1,
                            IsHitTestVisible = false,
                        });
                    }

                    var paragraphElements = new List<Control>(paragraphShapes.Count);
                    foreach (var paragraphShape in paragraphShapes)
                    {
                        var paragraphBitmap = RenderShapeToOverlayBitmap(slide, paragraphShape, w, h);
                        if (paragraphBitmap is null) continue;

                        var paragraphImage = new Image
                        {
                            Source = paragraphBitmap,
                            Width = w,
                            Height = h,
                            Stretch = Stretch.None,
                            Opacity = _entranceShapeIds.Contains(shapeId) ? 0 : 1,
                            IsHitTestVisible = false,
                        };
                        Canvas.SetLeft(paragraphImage, 0);
                        Canvas.SetTop(paragraphImage, 0);
                        _animOverlay.Children.Add(paragraphImage);
                        paragraphElements.Add(paragraphImage);
                    }

                    if (paragraphElements.Count > 0)
                    {
                        _paragraphAnimElements[shapeId] = paragraphElements;
                        _slideCanvas.SuppressedShapeIds.Add(shapeId);
                        continue;
                    }
                }
            }

            var shapeBitmap = RenderShapeToOverlayBitmap(slide, shape, w, h);
            if (shapeBitmap is null) continue;

            var img = new Image
            {
                Source           = shapeBitmap,
                Width            = w,
                Height           = h,
                Stretch          = Stretch.None,
                Opacity          = _entranceShapeIds.Contains(shapeId) ? 0 : 1,
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);

            _animOverlay.Children.Add(img);
            _animElements[shapeId] = img;

            if (slide.Animations.Any(a => a.ShapeId == shapeId
                                          && a.Preset == AnimationPreset.ChangeFillColor)
                && shape.Fill is not ShapeFill.None)
            {
                var fillMaskShape = SlideCloner.CloneShape(shape);
                fillMaskShape.TextBody = null;
                fillMaskShape.Outline = null;
                var fillBitmap = RenderShapeToOverlayBitmap(slide, fillMaskShape, w, h);
                if (fillBitmap is not null)
                {
                    var fillTint = new Rectangle
                    {
                        Width = w,
                        Height = h,
                        Fill = new SolidColorBrush(Colors.Transparent),
                        Opacity = 0,
                        OpacityMask = new ImageBrush(fillBitmap) { Stretch = Stretch.None },
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(fillTint, 0);
                    Canvas.SetTop(fillTint, 0);
                    _animOverlay.Children.Add(fillTint);
                    _animFillElements[shapeId] = fillTint;
                }
            }

            var lineAnimation = slide.Animations.FirstOrDefault(a =>
                a.ShapeId == shapeId && a.Preset == AnimationPreset.ChangeLineColor);
            if (lineAnimation is not null
                && shape.TextBody is null
                && shape.Outline is ShapeOutline.Visible outline
                && SlideShowPlaybackPlanner.PlanShapeAnimation(
                    lineAnimation,
                    startDelayMs: 0,
                    presentation: _presentation,
                    effectiveClrMap: slide.ColorMapOverride).ColorToHex is { } lineColor
                && TryParseAnimationColorHex(lineColor, out var lineRgb))
            {
                var lineShape = SlideCloner.CloneShape(shape);
                lineShape.Outline = new ShapeOutline.Visible(
                    lineRgb,
                    outline.WidthPt,
                    outline.Dash,
                    outline.BeginLineEnd,
                    outline.EndLineEnd);
                var lineBitmap = RenderShapeToOverlayBitmap(slide, lineShape, w, h);
                if (lineBitmap is not null)
                {
                    var lineElement = new Image
                    {
                        Source = lineBitmap,
                        Width = w,
                        Height = h,
                        Stretch = Stretch.None,
                        Opacity = 0,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(lineElement, 0);
                    Canvas.SetTop(lineElement, 0);
                    _animOverlay.Children.Add(lineElement);
                    _animLineElements[shapeId] = lineElement;
                }
            }

            var fontStyleAnimation = slide.Animations.FirstOrDefault(a =>
                a.ShapeId == shapeId
                && a.Preset is (AnimationPreset.ChangeFontStyle
                    or AnimationPreset.Bold
                    or AnimationPreset.Underline));
            var fontStylePlan = fontStyleAnimation is null
                ? null
                : SlideShowPlaybackPlanner.ResolveFontStyleBehavior(fontStyleAnimation);
            if (fontStyleAnimation is not null
                && shape.TextBody is not null
                && shape.TextBody.Paragraphs.SelectMany(paragraph => paragraph.Runs).Any()
                && fontStylePlan is { } targetStyle
                && (targetStyle.Italic is not null
                    || targetStyle.Bold is not null
                    || targetStyle.Underline is not null))
            {
                var fontStyleShape = SlideCloner.CloneShape(shape);
                foreach (var run in fontStyleShape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs))
                {
                    if (targetStyle.Italic is bool italic)
                        run.Italic = italic;
                    if (targetStyle.Bold is bool bold)
                        run.Bold = bold;
                    if (targetStyle.Underline is bool underline)
                        run.Underline = underline;
                }

                var fontStyleBitmap = RenderShapeToOverlayBitmap(slide, fontStyleShape, w, h);
                if (fontStyleBitmap is not null)
                {
                    var fontStyleElement = new Image
                    {
                        Source = fontStyleBitmap,
                        Width = w,
                        Height = h,
                        Stretch = Stretch.None,
                        Opacity = 0,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(fontStyleElement, 0);
                    Canvas.SetTop(fontStyleElement, 0);
                    _animOverlay.Children.Add(fontStyleElement);
                    _animFontStyleElements[shapeId] = fontStyleElement;
                }
            }

            var fontSizeAnimation = slide.Animations.FirstOrDefault(a =>
                a.ShapeId == shapeId
                && a.Preset is (AnimationPreset.Grow or AnimationPreset.Shrink)
                && SlideShowPlaybackPlanner.ResolveFontSizeBehavior(a) is not null);
            var fontSizePlan = fontSizeAnimation is null
                ? null
                : SlideShowPlaybackPlanner.ResolveFontSizeBehavior(fontSizeAnimation);
            var explicitRuns = shape.TextBody?.Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .ToList();
            if (fontSizeAnimation is not null
                && explicitRuns is { Count: > 0 }
                && explicitRuns.All(run => run.FontSizePt is > 0)
                && fontSizePlan is { } targetSize)
            {
                var fontSizeShape = SlideCloner.CloneShape(shape);
                foreach (var run in fontSizeShape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs))
                    run.FontSizePt = run.FontSizePt!.Value * targetSize.Multiplier;

                var fontSizeBitmap = RenderShapeToOverlayBitmap(slide, fontSizeShape, w, h);
                if (fontSizeBitmap is not null)
                {
                    var fontSizeElement = new Image
                    {
                        Source = fontSizeBitmap,
                        Width = w,
                        Height = h,
                        Stretch = Stretch.None,
                        Opacity = 0,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(fontSizeElement, 0);
                    Canvas.SetTop(fontSizeElement, 0);
                    _animOverlay.Children.Add(fontSizeElement);
                    _animFontSizeElements[shapeId] = fontSizeElement;
                }
            }

            // DA1: hide this shape in the base canvas — the overlay image is the sole copy.
            _slideCanvas.SuppressedShapeIds.Add(shapeId);
        }

        // DA1: trigger a repaint so the suppressed shapes are hidden from the base canvas.
        _slideCanvas.Refresh();
    }

    /// <summary>
    /// DA1: Called when a build step has finished animating a shape in.
    /// Removes the shape from the suppressed set so the base canvas renders it permanently,
    /// matching PowerPoint's behaviour where the shape is visible after its build completes.
    /// </summary>
    private void RevealShape(uint shapeId)
    {
        if (_paragraphAnimElements.ContainsKey(shapeId))
            return;
        if (_paragraphRangeAnimElements.Keys.Any(a => a.ShapeId == shapeId))
            return;

        if (_slideCanvas.SuppressedShapeIds.Remove(shapeId))
            _slideCanvas.Refresh();
    }

    /// <summary>
    /// Renders a single shape into a bitmap at the full slide canvas size.
    /// </summary>
    private RenderTargetBitmap? RenderShapeToOverlayBitmap(Slide slide, SlideShape shape, double w, double h)
    {
        try
        {
            var tempSlide = new Slide { Background = null };
            tempSlide.Shapes.Add(shape);

            var tempCanvas = new SlideCanvas
            {
                Presentation = _presentation,
                Slide        = tempSlide,
                Width        = w,
                Height       = h,
            };
            tempCanvas.Measure(new Size(w, h));
            tempCanvas.Arrange(new Rect(0, 0, w, h));

            var rtb = new RenderTargetBitmap(new PixelSize((int)w, (int)h));
            rtb.Render(tempCanvas);
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    // ── Animation step playback ───────────────────────────────────────────────────

    private void PlayAnimationStep(AnimationStep step)
    {
        _lastAnimationStepFrameEvidence = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(step, _slideDipW, _slideDipH);
        _lastAnimationStepPlaybackReadinessPlan =
            SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(
                step,
                CurrentPresentationSlideIndex,
                stepIndex: 0,
                slideWidthDip: _slideDipW,
                slideHeightDip: _slideDipH);

        var effectiveColorMap = (_revealedHiddenSlide ?? _controller.CurrentSlide)?.ColorMapOverride;
        foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step, _presentation, effectiveColorMap))
        {
            var anim = plan.Animation;
            if (anim.ParagraphRangeStart.HasValue
                && _paragraphRangeAnimElements.TryGetValue(anim, out var rangedElement))
            {
                PlayShapeAnimationWithTiming(rangedElement, plan, onReveal: null);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (_paragraphAnimElements.TryGetValue(anim.ShapeId, out var paragraphElements))
            {
                for (var index = 0; index < paragraphElements.Count; index++)
                {
                    var paragraphPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
                        anim,
                        plan.DelayMs + index * plan.DurationMs,
                        _presentation,
                        effectiveColorMap);
                    PlayShapeAnimationWithTiming(paragraphElements[index], paragraphPlan, onReveal: null);
                }

                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                PlayFallbackAnimation(anim, plan.DelayMs, plan.DurationMs);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFillColor
                && _animFillElements.TryGetValue(anim.ShapeId, out var fillElement))
            {
                PlayShapeAnimationWithTiming(fillElement, plan, onReveal: null);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeLineColor
                && _animLineElements.TryGetValue(anim.ShapeId, out var lineElement))
            {
                PlayShapeAnimationWithTiming(lineElement, plan, onReveal: null);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind is (SlideShowShapeAnimationEffectKind.ChangeFontStyle
                    or SlideShowShapeAnimationEffectKind.Bold
                    or SlideShowShapeAnimationEffectKind.Underline)
                && _animFontStyleElements.TryGetValue(anim.ShapeId, out var fontStyleElement))
            {
                PlayShapeAnimationWithTiming(fontStyleElement, plan, onReveal: null);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFontSize)
            {
                if (_animFontSizeElements.TryGetValue(anim.ShapeId, out var fontSizeElement))
                    PlayShapeAnimationWithTiming(fontSizeElement, plan, onReveal: null);
                else
                    PlayShapeAnimationWithTiming(
                        element,
                        plan with { EffectKind = SlideShowShapeAnimationEffectKind.GrowShrink },
                        onReveal: null);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            var shapeId = anim.ShapeId;
            PlayShapeAnimationWithTiming(element, plan, onReveal: anim.Kind == AnimationKind.Exit ? null : () =>
            {
                // DA1: once the entrance animation finishes (or fires), hand off painting to
                // the base canvas.  The overlay element stays in the tree at full opacity but
                // the base canvas version is now also visible — they are identical, so there
                // is no visible seam.  An alternative is to collapse the overlay element here;
                // either approach is correct.  We keep it simple: just reveal in base canvas.
                RevealShape(shapeId);
            });
            _revealedShapes.Add(shapeId);
        }
    }

    /// <param name="plan">
    /// DA4: the planner already supplies the computed start delay for this entry, so
    /// AfterPrevious animations begin after their predecessor completes rather than all
    /// firing simultaneously.
    /// </param>
    /// <param name="onReveal">
    /// DA1: called once the animation finishes so the base canvas takes over painting the shape.
    /// For non-entrance (Emphasis/Exit) effects this callback is invoked at animation start
    /// because the shape is already visible in the base canvas.
    /// </param>
    private void PlayShapeAnimationWithTiming(
        Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        var passCount = plan.RepeatIndefinitely
            ? (int?)null
            : Math.Max(1, plan.RepeatCount ?? 1);
        var basePlan = plan with
        {
            RepeatCount = null,
            RepeatIndefinitely = false,
            AutoReverse = plan.AutoReverse,
        };

        PlayShapeAnimationPass(element, basePlan, onReveal, passCount, 0);
    }

    private void PlayShapeAnimationPass(
        Control element,
        SlideShowShapeAnimationPlaybackPlan basePlan,
        Action? onReveal,
        int? passCount,
        int passIndex)
    {
        var isFinalPass = passCount is int count && passIndex >= count - 1;
        var currentPlan = passIndex == 0 ? basePlan : basePlan with { DelayMs = 0 };
        var passPlan = passIndex % 2 == 1 && basePlan.AutoReverse
            ? BuildReverseAnimationPlan(currentPlan)
            : currentPlan;

        PlayShapeAnimation(element, passPlan, isFinalPass ? onReveal : null);

        if (!isFinalPass)
        {
            var nextPassDelay = passPlan.DelayMs + passPlan.DurationMs;
            DelayedAction(nextPassDelay, () =>
                PlayShapeAnimationPass(element, basePlan, onReveal, passCount, passIndex + 1));
        }
    }

    private SlideShowShapeAnimationPlaybackPlan BuildReverseAnimationPlan(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var reversedAnimation = PresentationAnimationCommandPlanner.CloneAnimation(plan.Animation);
        reversedAnimation.Kind = reversedAnimation.Kind switch
        {
            AnimationKind.Entrance => AnimationKind.Exit,
            AnimationKind.Exit => AnimationKind.Entrance,
            _ => AnimationKind.Emphasis,
        };

        var effectiveColorMap = (_revealedHiddenSlide ?? _controller.CurrentSlide)?.ColorMapOverride;
        var reversePlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            reversedAnimation,
            0,
            _presentation,
            effectiveColorMap);
        return reversePlan with
        {
            RepeatCount = null,
            RepeatIndefinitely = false,
            AutoReverse = false,
            FromOpacity = plan.ToOpacity,
            ToOpacity = plan.FromOpacity,
            FromScale = plan.ToScale,
            ToScale = plan.FromScale,
            OffsetXFactor = -plan.OffsetXFactor,
            OffsetYFactor = -plan.OffsetYFactor,
            MotionKeyFrames = SlideShowPlaybackPlanner.ReverseMotionPathKeyFrames(plan.MotionKeyFrames),
        };
    }

    private void PlayShapeAnimation(Control element, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        _lastAnimationFramePlan = SlideShowPlaybackFramePlanner.PlanFrame(plan, 0, _slideDipW, _slideDipH);

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.MotionPath)
        {
            MotionPathEffect(element, plan, onReveal);
            return;
        }

        switch (plan.EffectKind)
        {
            case SlideShowShapeAnimationEffectKind.Appear:
                if (plan.Animation.Kind == AnimationKind.Exit)
                    DisappearEffect(element, plan.DelayMs);
                else
                    AppearEffect(element, plan.DelayMs, CompleteReveal(plan, onReveal));
                break;

            case SlideShowShapeAnimationEffectKind.Fade:
                FadeEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.FlyIn:
                FlyInEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Wipe:
                WipeEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Split:
                SplitEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.RandomBars:
                RandomBarsEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Blinds:
                BlindsEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Box:
                BoxEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Checkerboard:
                CheckerboardEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Circle:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Diamond:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Plus:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Strips:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Wedge:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Wheel:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Dissolve:
                DissolveEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Flash:
                FlashEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Spiral:
                InvokeRevealAtStart(plan, onReveal);
                SpiralEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Swivel:
                InvokeRevealAtStart(plan, onReveal);
                SwivelEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bounce:
                BounceEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Float:
                FloatEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Swoop:
                SwoopEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Boomerang:
                BoomerangEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Peek:
                PeekEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Crawl:
                CrawlEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Zoom:
                ZoomEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Pulse:
                InvokeRevealAtStart(plan, onReveal);
                PulseEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.GrowShrink:
                InvokeRevealAtStart(plan, onReveal);
                GrowShrinkEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Spin:
                InvokeRevealAtStart(plan, onReveal);
                SpinEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Teeter:
                InvokeRevealAtStart(plan, onReveal);
                TeeterEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Blink:
                InvokeRevealAtStart(plan, onReveal);
                BlinkEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.FlashBulb:
                InvokeRevealAtStart(plan, onReveal);
                FlashBulbEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Flicker:
                InvokeRevealAtStart(plan, onReveal);
                FlickerEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Wave:
                InvokeRevealAtStart(plan, onReveal);
                WaveEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorPulse:
            case SlideShowShapeAnimationEffectKind.ChangeColor:
                EmphasisPulseEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFontStyle:
                FontStyleEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFontSize:
                FontSizeEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorWave:
                ColorWaveEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeLineColor:
                LineColorEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFillColor:
                FillColorEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.GrowWithColor:
            case SlideShowShapeAnimationEffectKind.Shimmer:
                InvokeRevealAtStart(plan, onReveal);
                EmphasisPulseEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bold:
            case SlideShowShapeAnimationEffectKind.Underline:
                FontStyleEffect(element, plan);
                break;

            default:
                AppearEffect(element, plan.DelayMs, CompleteReveal(plan, onReveal));
                break;
        }
    }

    private static void InvokeRevealAtStart(SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal)
    {
        if (plan.RevealTiming == SlideShowAnimationRevealTiming.AtStart)
            onReveal?.Invoke();
    }

    private static Action? CompleteReveal(SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal) =>
        plan.RevealTiming == SlideShowAnimationRevealTiming.OnComplete
            ? onReveal
            : null;

    private void AppearEffect(Control el, int delayMs, Action? onComplete = null)
    {
        if (delayMs <= 0)
        {
            el.Opacity = 1;
            onComplete?.Invoke();
            return;
        }
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        });
        timer.Tick += (_, _) => { timer.Stop(); _activeTimers.Remove(timer); el.Opacity = 1; onComplete?.Invoke(); };
        timer.Start();
    }

    private void FadeEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(plan.DelayMs, () =>
            AnimateOpacity(
                el,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void FlashEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        InvokeRevealAtStart(plan, onReveal);

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var firstOpacity = isExit ? plan.FromOpacity : 0;
        var firstDuration = Math.Max(1, (int)(plan.DurationMs * 0.2));
        var secondDuration = Math.Max(1, (int)(plan.DurationMs * 0.35));
        var finalDuration = Math.Max(1, plan.DurationMs - firstDuration - secondDuration);

        DelayedAction(plan.DelayMs, () =>
            AnimateOpacity(el, firstOpacity, 0.7, firstDuration, onComplete: () =>
                AnimateOpacity(el, 0.7, 0.35, secondDuration, onComplete: () =>
                    AnimateOpacity(
                        el,
                        0.35,
                        plan.ToOpacity,
                        finalDuration,
                        onComplete: CompleteReveal(plan, onReveal),
                        acceleration: plan.Acceleration,
                        deceleration: plan.Deceleration),
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void DissolveEffect(Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        InvokeRevealAtStart(plan, onReveal);

        double width = element.Bounds.Width > 0 ? element.Bounds.Width : 960;
        double height = element.Bounds.Height > 0 ? element.Bounds.Height : 540;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        element.Opacity = isExit ? plan.FromOpacity : 1;
        element.Clip = BuildDissolveTransitionGeometry(width, height, isExit ? 1 : 0);

        DelayedAction(plan.DelayMs, () =>
            AnimateDissolveTransitionClip(
                element,
                width,
                height,
                plan.DurationMs,
                onComplete: () =>
                {
                    element.Clip = null;
                    element.Opacity = plan.ToOpacity;
                    CompleteReveal(plan, onReveal)?.Invoke();
                },
                reverse: isExit,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void FlyInEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = _slideCanvas.Bounds.Width  > 0 ? _slideCanvas.Bounds.Width  : 960;
        double h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        double dx = plan.OffsetXFactor * w;
        double dy = plan.OffsetYFactor * h;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        el.Opacity = plan.FromOpacity;
        el.RenderTransform = new TranslateTransform(isExit ? 0 : dx, isExit ? 0 : dy);

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateTranslate(el, isExit ? 0 : dx, isExit ? 0 : dy,
                isExit ? dx : 0, isExit ? dy : 0, plan.DurationMs,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            // Reveal in base canvas when the fade-in completes.
            AnimateOpacity(
                el,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
        });
    }

    private void FloatEffect(Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        double width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        double height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        double directionX = plan.OffsetXFactor * width;
        double directionY = plan.OffsetYFactor * height;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        double startX = isExit ? 0 : directionX;
        double startY = isExit ? 0 : directionY;
        double endX = isExit ? directionX : 0;
        double endY = isExit ? directionY : 0;
        double arcX = Math.Abs(plan.OffsetYFactor) > 0.01
            ? -Math.Sign(plan.OffsetYFactor) * width * 0.06
            : 0;
        double arcY = Math.Abs(plan.OffsetXFactor) > 0.01
            ? Math.Sign(plan.OffsetXFactor) * height * 0.06
            : 0;
        double midX = (startX + endX) / 2 + arcX;
        double midY = (startY + endY) / 2 + arcY;

        var translate = new TranslateTransform(startX, startY);
        element.RenderTransform = translate;
        DelayedAction(plan.DelayMs, () =>
        {
            AnimateOpacity(
                element,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateFloatTranslate(
                translate,
                startX,
                startY,
                midX,
                midY,
                endX,
                endY,
                plan.DurationMs,
                CompleteReveal(plan, onReveal),
                plan.Acceleration,
                plan.Deceleration);
        });
    }

    private void AnimateFloatTranslate(
        TranslateTransform translate,
        double startX,
        double startY,
        double middleX,
        double middleY,
        double endX,
        double endY,
        int durationMs,
        Action? onComplete,
        int? acceleration,
        int? deceleration)
    {
        if (durationMs <= 0)
        {
            translate.X = endX;
            translate.Y = endY;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var (x, y) = InterpolateFloatPoint(
                t,
                startX,
                startY,
                middleX,
                middleY,
                endX,
                endY,
                acceleration,
                deceleration);
            translate.X = x;
            translate.Y = y;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = endX;
                translate.Y = endY;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static (double X, double Y) InterpolateFloatPoint(
        double t,
        double startX,
        double startY,
        double middleX,
        double middleY,
        double endX,
        double endY,
        int? acceleration,
        int? deceleration)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0.72)
        {
            var eased = ApplyAnimationEasing(t / 0.72, acceleration, deceleration);
            return (
                startX + (middleX - startX) * eased,
                startY + (middleY - startY) * eased);
        }

        var finalEased = ApplyAnimationEasing((t - 0.72) / 0.28, acceleration, deceleration);
        return (
            middleX + (endX - middleX) * finalEased,
            middleY + (endY - middleY) * finalEased);
    }

    private void SwoopEffect(Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        double width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        double height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        double directionX = plan.OffsetXFactor * width;
        double directionY = plan.OffsetYFactor * height;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        double startX = isExit ? 0 : directionX;
        double startY = isExit ? 0 : directionY;
        double endX = isExit ? directionX : 0;
        double endY = isExit ? directionY : 0;
        double arcX = Math.Abs(plan.OffsetYFactor) > 0.01
            ? -Math.Sign(plan.OffsetYFactor) * width * 0.14
            : 0;
        double arcY = Math.Abs(plan.OffsetXFactor) > 0.01
            ? Math.Sign(plan.OffsetXFactor) * height * 0.14
            : 0;
        double midX = (startX + endX) / 2 + arcX;
        double midY = (startY + endY) / 2 + arcY;

        var translate = new TranslateTransform(startX, startY);
        element.RenderTransform = translate;
        DelayedAction(plan.DelayMs, () =>
        {
            AnimateOpacity(
                element,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateSwoopTranslate(
                translate,
                startX,
                startY,
                midX,
                midY,
                endX,
                endY,
                plan.DurationMs,
                CompleteReveal(plan, onReveal),
                plan.Acceleration,
                plan.Deceleration);
        });
    }

    private void AnimateSwoopTranslate(
        TranslateTransform translate,
        double startX,
        double startY,
        double middleX,
        double middleY,
        double endX,
        double endY,
        int durationMs,
        Action? onComplete,
        int? acceleration,
        int? deceleration)
    {
        if (durationMs <= 0)
        {
            translate.X = endX;
            translate.Y = endY;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var (x, y) = InterpolateSwoopPoint(
                t,
                startX,
                startY,
                middleX,
                middleY,
                endX,
                endY,
                acceleration,
                deceleration);
            translate.X = x;
            translate.Y = y;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = endX;
                translate.Y = endY;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static (double X, double Y) InterpolateSwoopPoint(
        double t,
        double startX,
        double startY,
        double middleX,
        double middleY,
        double endX,
        double endY,
        int? acceleration,
        int? deceleration)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0.55)
        {
            var eased = ApplyAnimationEasing(t / 0.55, acceleration, deceleration);
            return (
                startX + (middleX - startX) * eased,
                startY + (middleY - startY) * eased);
        }

        var finalEased = ApplyAnimationEasing((t - 0.55) / 0.45, acceleration, deceleration);
        return (
            middleX + (endX - middleX) * finalEased,
            middleY + (endY - middleY) * finalEased);
    }

    private void BoomerangEffect(Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        double width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        double height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        double directionX = plan.OffsetXFactor * width;
        double directionY = plan.OffsetYFactor * height;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        double startX = isExit ? 0 : directionX;
        double startY = isExit ? 0 : directionY;
        double endX = isExit ? directionX : 0;
        double endY = isExit ? directionY : 0;
        double overshootX = isExit
            ? endX + directionX * 0.08
            : endX - directionX * 0.08;
        double overshootY = isExit
            ? endY + directionY * 0.08
            : endY - directionY * 0.08;

        var translate = new TranslateTransform(startX, startY);
        element.RenderTransform = translate;
        DelayedAction(plan.DelayMs, () =>
        {
            AnimateOpacity(
                element,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateBoomerangTranslate(
                translate,
                startX,
                startY,
                overshootX,
                overshootY,
                endX,
                endY,
                plan.DurationMs,
                CompleteReveal(plan, onReveal),
                plan.Acceleration,
                plan.Deceleration);
        });
    }

    private void AnimateBoomerangTranslate(
        TranslateTransform translate,
        double startX,
        double startY,
        double overshootX,
        double overshootY,
        double endX,
        double endY,
        int durationMs,
        Action? onComplete,
        int? acceleration,
        int? deceleration)
    {
        if (durationMs <= 0)
        {
            translate.X = endX;
            translate.Y = endY;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var (x, y) = InterpolateBoomerangPoint(
                t,
                startX,
                startY,
                overshootX,
                overshootY,
                endX,
                endY,
                acceleration,
                deceleration);
            translate.X = x;
            translate.Y = y;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = endX;
                translate.Y = endY;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static (double X, double Y) InterpolateBoomerangPoint(
        double t,
        double startX,
        double startY,
        double overshootX,
        double overshootY,
        double endX,
        double endY,
        int? acceleration,
        int? deceleration)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0.78)
        {
            var eased = ApplyAnimationEasing(t / 0.78, acceleration, deceleration);
            return (
                startX + (overshootX - startX) * eased,
                startY + (overshootY - startY) * eased);
        }

        var finalEased = ApplyAnimationEasing((t - 0.78) / 0.22, acceleration, deceleration);
        return (
            overshootX + (endX - overshootX) * finalEased,
            overshootY + (endY - overshootY) * finalEased);
    }

    private void BounceEffect(Control element,
        SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        InvokeRevealAtStart(plan, onReveal);

        double width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        double height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        double directionX = plan.OffsetXFactor * width;
        double directionY = plan.OffsetYFactor * height;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        double startX = isExit ? 0 : directionX;
        double startY = isExit ? 0 : directionY;
        double endX = isExit ? directionX : 0;
        double endY = isExit ? directionY : 0;
        double overshootX = isExit ? endX + directionX * 0.08 : -directionX * 0.08;
        double overshootY = isExit ? endY + directionY * 0.08 : -directionY * 0.08;
        double reboundX = isExit ? endX - directionX * 0.04 : directionX * 0.04;
        double reboundY = isExit ? endY - directionY * 0.04 : directionY * 0.04;

        var translate = new TranslateTransform(startX, startY);
        element.RenderTransform = translate;
        DelayedAction(plan.DelayMs, () =>
        {
            AnimateOpacity(
                element,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateBounceTranslate(
                translate,
                startX,
                startY,
                endX,
                endY,
                overshootX,
                overshootY,
                reboundX,
                reboundY,
                plan.DurationMs,
                CompleteReveal(plan, onReveal),
                plan.Acceleration,
                plan.Deceleration);
        });
    }

    private void AnimateBounceTranslate(
        TranslateTransform translate,
        double startX,
        double startY,
        double endX,
        double endY,
        double overshootX,
        double overshootY,
        double reboundX,
        double reboundY,
        int durationMs,
        Action? onComplete,
        int? acceleration,
        int? deceleration)
    {
        if (durationMs <= 0)
        {
            translate.X = endX;
            translate.Y = endY;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1.0, (double)frame / steps);
            var (x, y) = InterpolateBouncePoint(
                t,
                startX,
                startY,
                endX,
                endY,
                overshootX,
                overshootY,
                reboundX,
                reboundY,
                acceleration,
                deceleration);
            translate.X = x;
            translate.Y = y;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = endX;
                translate.Y = endY;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static (double X, double Y) InterpolateBouncePoint(
        double t,
        double startX,
        double startY,
        double endX,
        double endY,
        double overshootX,
        double overshootY,
        double reboundX,
        double reboundY,
        int? acceleration,
        int? deceleration)
    {
        var local = t switch
        {
            <= 0.55 => t / 0.55,
            <= 0.72 => (t - 0.55) / 0.17,
            <= 0.86 => (t - 0.72) / 0.14,
            _ => (t - 0.86) / 0.14
        };
        var eased = ApplyAnimationEasing(Math.Clamp(local, 0, 1), acceleration, deceleration);
        return t <= 0.55
            ? (startX + (endX - startX) * eased, startY + (endY - startY) * eased)
            : t <= 0.72
                ? (endX + (overshootX - endX) * eased, endY + (overshootY - endY) * eased)
                : t <= 0.86
                    ? (overshootX + (reboundX - overshootX) * eased, overshootY + (reboundY - overshootY) * eased)
                    : (reboundX + (endX - reboundX) * eased, reboundY + (endY - reboundY) * eased);
    }

    private void PeekEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = _slideCanvas.Bounds.Width  > 0 ? _slideCanvas.Bounds.Width  : 960;
        double h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        double dx = plan.OffsetXFactor * w;
        double dy = plan.OffsetYFactor * h;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var fromX = isExit ? 0 : dx;
        var fromY = isExit ? 0 : dy;
        var toX = isExit ? dx : 0;
        var toY = isExit ? dy : 0;
        el.Opacity = 1;
        el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));
        el.RenderTransform = new TranslateTransform(fromX, fromY);

        DelayedAction(plan.DelayMs, () =>
            AnimateTranslate(
                el,
                fromX,
                fromY,
                toX,
                toY,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void CrawlEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null) =>
        PeekEffect(el, plan, onReveal);

    private void WipeEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        el.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        if (plan.WipeHorizontal)
        {
            // Clip from 0 width → full width.
            var from = isExit ? new Rect(0, 0, w, h) : new Rect(0, 0, 0, h);
            var to = isExit ? new Rect(0, 0, 0, h) : new Rect(0, 0, w, h);
            var clipRect = new RectangleGeometry(from);
            el.Clip = clipRect;
            DelayedAction(plan.DelayMs, () =>
                AnimateRectClip(
                    el,
                    clipRect,
                    from,
                    to,
                    plan.DurationMs,
                    onComplete: CompleteReveal(plan, onReveal),
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration));
        }
        else
        {
            var from = isExit ? new Rect(0, 0, w, h) : new Rect(0, 0, w, 0);
            var to = isExit ? new Rect(0, 0, w, 0) : new Rect(0, 0, w, h);
            var clipRect = new RectangleGeometry(from);
            el.Clip = clipRect;
            DelayedAction(plan.DelayMs, () =>
                AnimateRectClip(
                    el,
                    clipRect,
                    from,
                    to,
                    plan.DurationMs,
                    onComplete: CompleteReveal(plan, onReveal),
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration));
        }
    }

    private void SplitEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        el.Opacity = 1;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var fromProgress = isExit ? 1 : 0;
        var toProgress = isExit ? 0 : 1;
        el.Clip = BuildSplitGeometry(
            w, h, fromProgress, plan.SplitHorizontal, plan.SplitFromCenter);
        DelayedAction(plan.DelayMs, () =>
            AnimateSplitClip(
                el,
                w,
                h,
                plan.SplitHorizontal,
                plan.SplitFromCenter,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                startProgress: fromProgress,
                endProgress: toProgress,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void RandomBarsEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var bars = new GeometryGroup();
        var animatedBars = new List<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)>();
        var randomBars = SlideShowMaskGeometryPlanner.BuildRandomBars(
            w,
            h,
            SlideShowPlaybackPlanner.RandomBarsBandCount,
            plan.WipeHorizontal);
        var barStaggerMs = plan.DurationMs / Math.Max(1, randomBars.Count + 1);

        foreach (var randomBar in randomBars)
        {
            var closed = ToRect(randomBar.Geometry.Closed);
            var open = ToRect(randomBar.Geometry.Open);
            var from = isExit ? open : closed;
            var to = isExit ? closed : open;
            var bar = new RectangleGeometry(from);
            bars.Children.Add(bar);
            animatedBars.Add((
                bar,
                from,
                to,
                randomBar.Order * barStaggerMs,
                Math.Max(1, plan.DurationMs - randomBar.Order * barStaggerMs)));
        }

        el.Clip = bars;
        el.Opacity = isExit ? plan.FromOpacity : 0;

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateRandomBarsClip(
                animatedBars,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            el.Opacity = isExit ? 0.7 : 0.15;
            DelayedAction(plan.DurationMs / 5, () => el.Opacity = isExit ? 0.35 : 0.45);
            DelayedAction(plan.DurationMs / 2, () => el.Opacity = isExit ? 0.15 : 0.75);
            DelayedAction(plan.DurationMs, () => el.Opacity = plan.ToOpacity);
        });
    }

    private void AnimateRandomBarsClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)> bars,
        int durationMs,
        Action? onComplete = null,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            foreach (var (geometry, _, to, _, _) in bars)
                geometry.Rect = to;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        var elapsedMs = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            elapsedMs = Math.Min(durationMs, elapsedMs + frameMs);
            foreach (var (geometry, from, to, delayMs, barDurationMs) in bars)
            {
                var localElapsed = Math.Max(0, elapsedMs - delayMs);
                var t = Math.Min(1.0, (double)localElapsed / barDurationMs);
                var eased = ApplyAnimationEasing(t, acceleration, deceleration);
                geometry.Rect = new Rect(
                    from.X + (to.X - from.X) * eased,
                    from.Y + (to.Y - from.Y) * eased,
                    from.Width + (to.Width - from.Width) * eased,
                    from.Height + (to.Height - from.Height) * eased);
            }

            if (elapsedMs >= durationMs)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                foreach (var (geometry, _, to, _, _) in bars)
                    geometry.Rect = to;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void BlindsEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var opens = plan.ToOpacity >= plan.FromOpacity;
        var bandCount = Math.Max(1, plan.BlindsBandCount);
        var bands = new GeometryGroup();
        var animatedBands = new List<(RectangleGeometry Geometry, Rect From, Rect To)>(bandCount);

        for (var i = 0; i < bandCount; i++)
        {
            var bandPlan = SlideShowMaskGeometryPlanner.BuildBlindsBand(
                w, h, bandCount, i, plan.BlindsHorizontal);
            var closed = ToRect(bandPlan.Closed);
            var open = ToRect(bandPlan.Open);
            var from = opens ? closed : open;
            var to = opens ? open : closed;
            var band = new RectangleGeometry(from);
            bands.Children.Add(band);
            animatedBands.Add((band, from, to));
        }

        el.Clip = bands;
        el.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(plan.DelayMs, () =>
            AnimateBlindsClip(
                animatedBands,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void CheckerboardEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var opens = plan.ToOpacity >= plan.FromOpacity;
        var rowCount = Math.Max(1, plan.CheckerboardRowCount);
        var columnCount = Math.Max(1, plan.CheckerboardColumnCount);
        var phaseDelayMs = Math.Max(0, plan.DurationMs / 3);
        var cellDurationMs = Math.Max(1, plan.DurationMs - phaseDelayMs);
        var cells = new GeometryGroup();
        var animatedCells = new List<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)>(
            rowCount * columnCount);

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var cellPlan = SlideShowMaskGeometryPlanner.BuildCheckerboardCell(
                    w,
                    h,
                    rowCount,
                    columnCount,
                    row,
                    column,
                    plan.CheckerboardHorizontal);
                var closed = ToRect(cellPlan.Closed);
                var open = ToRect(cellPlan.Open);
                var from = opens ? closed : open;
                var to = opens ? open : closed;
                var cell = new RectangleGeometry(from);
                cells.Children.Add(cell);
                animatedCells.Add((
                    cell,
                    from,
                    to,
                    SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(row, column) ? phaseDelayMs : 0,
                    cellDurationMs));
            }
        }

        el.Clip = cells;
        el.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(plan.DelayMs, () =>
            AnimateCheckerboardClip(
                animatedCells,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void AnimateCheckerboardClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)> cells,
        int durationMs,
        Action? onComplete = null,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            foreach (var (geometry, _, to, _, _) in cells)
                geometry.Rect = to;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        int elapsedMs = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            elapsedMs = Math.Min(durationMs, elapsedMs + frameMs);
            foreach (var (geometry, from, to, delayMs, cellDurationMs) in cells)
            {
                var localElapsed = Math.Max(0, elapsedMs - delayMs);
                var t = Math.Min(1.0, (double)localElapsed / cellDurationMs);
                var e = ApplyAnimationEasing(t, acceleration, deceleration);
                geometry.Rect = new Rect(
                    from.X + (to.X - from.X) * e,
                    from.Y + (to.Y - from.Y) * e,
                    from.Width  + (to.Width  - from.Width)  * e,
                    from.Height + (to.Height - from.Height) * e);
            }

            if (elapsedMs >= durationMs)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                foreach (var (geometry, _, to, _, _) in cells)
                    geometry.Rect = to;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void AnimateBlindsClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To)> bands,
        int durationMs,
        Action? onComplete = null,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            foreach (var (geometry, _, to) in bands)
                geometry.Rect = to;
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double e = ApplyAnimationEasing(t, acceleration, deceleration);
            foreach (var (geometry, from, to) in bands)
            {
                geometry.Rect = new Rect(
                    from.X + (to.X - from.X) * e,
                    from.Y + (to.Y - from.Y) * e,
                    from.Width  + (to.Width  - from.Width)  * e,
                    from.Height + (to.Height - from.Height) * e);
            }

            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                foreach (var (geometry, _, to) in bands)
                    geometry.Rect = to;
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void BoxEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var center = new Rect(w / 2, h / 2, 0, 0);
        var full = new Rect(0, 0, w, h);
        var from = plan.BoxExpandsFromCenter ? center : full;
        var to = plan.BoxExpandsFromCenter ? full : center;

        var clipRect = new RectangleGeometry(from);
        el.Clip = clipRect;
        el.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(plan.DelayMs, () =>
            AnimateRectClip(
                el,
                clipRect,
                from,
                to,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void GeometricMaskEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        switch (plan.GeometricMaskKind)
        {
            case SlideShowGeometricMaskKind.Circle:
            case SlideShowGeometricMaskKind.Diamond:
            case SlideShowGeometricMaskKind.Plus:
            case SlideShowGeometricMaskKind.Strips:
            case SlideShowGeometricMaskKind.Wedge:
            case SlideShowGeometricMaskKind.Wheel:
                GeometricMaskClipEffect(el, plan, onReveal);
                break;

            default:
                AppearEffect(el, plan.DelayMs, CompleteReveal(plan, onReveal));
                break;
        }
    }

    private void GeometricMaskClipEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;

        el.Clip = BuildGeometricMaskGeometry(
            plan.GeometricMaskKind,
            w,
            h,
            fromProgress,
            plan.GeometricMaskSpokeCount,
            plan.GeometricMaskStripCount,
            plan.GeometricMaskStripsSlopeDown);
        el.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(plan.DelayMs, () =>
            AnimateGeometricMaskClip(
                el,
                plan.GeometricMaskKind,
                plan.GeometricMaskSpokeCount,
                plan.GeometricMaskStripCount,
                plan.GeometricMaskStripsSlopeDown,
                w,
                h,
                fromProgress,
                toProgress,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration));
    }

    private void AnimateGeometricMaskClip(
        Control target,
        SlideShowGeometricMaskKind maskKind,
        int spokeCount,
        int stripCount,
        bool stripsSlopeDown,
        double width,
        double height,
        double fromProgress,
        double toProgress,
        int durationMs,
        Action? onComplete = null,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildGeometricMaskGeometry(maskKind, width, height, toProgress, spokeCount, stripCount, stripsSlopeDown);
            onComplete?.Invoke();
            return;
        }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double eased = ApplyAnimationEasing(t, acceleration, deceleration);
            double progress = fromProgress + (toProgress - fromProgress) * eased;
            target.Clip = BuildGeometricMaskGeometry(maskKind, width, height, progress, spokeCount, stripCount, stripsSlopeDown);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildGeometricMaskGeometry(maskKind, width, height, toProgress, spokeCount, stripCount, stripsSlopeDown);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private static Geometry BuildGeometricMaskGeometry(
        SlideShowGeometricMaskKind maskKind,
        double width,
        double height,
        double progress,
        int spokeCount,
        int stripCount,
        bool stripsSlopeDown) =>
        maskKind switch
        {
            SlideShowGeometricMaskKind.Circle => BuildCircleGeometry(width, height, progress),
            SlideShowGeometricMaskKind.Diamond => BuildDiamondGeometry(width, height, progress),
            SlideShowGeometricMaskKind.Plus => BuildPlusGeometry(width, height, progress),
            SlideShowGeometricMaskKind.Strips => BuildStripsGeometry(width, height, progress, stripCount, stripsSlopeDown),
            SlideShowGeometricMaskKind.Wedge => BuildWedgeGeometry(width, height, progress),
            SlideShowGeometricMaskKind.Wheel => BuildWheelGeometry(width, height, progress, spokeCount),
            _ => new RectangleGeometry(new Rect(0, 0, width, height))
        };

    private static EllipseGeometry BuildCircleGeometry(double width, double height, double progress)
    {
        var circlePlan = SlideShowMaskGeometryPlanner.BuildCircle(width, height, progress);
        return new EllipseGeometry
        {
            Center = ToPoint(circlePlan.Center),
            RadiusX = circlePlan.RadiusX,
            RadiusY = circlePlan.RadiusY
        };
    }

    private static StreamGeometry BuildDiamondGeometry(double width, double height, double progress)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 0, progress: progress)), isFilled: true);
            ctx.LineTo(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 1, progress: progress)));
            ctx.LineTo(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 2, progress: progress)));
            ctx.LineTo(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 3, progress: progress)));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static GeometryGroup BuildPlusGeometry(double width, double height, double progress)
    {
        var plusPlan = SlideShowMaskGeometryPlanner.BuildPlusRects(width, height, progress);
        var vertical = ToRect(plusPlan.Closed);
        var horizontal = ToRect(plusPlan.Open);
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        geometry.Children.Add(new RectangleGeometry(vertical));
        geometry.Children.Add(new RectangleGeometry(horizontal));
        return geometry;
    }

    private static Geometry BuildStripsGeometry(
        double width,
        double height,
        double progress,
        int stripCount,
        bool slopeDown)
    {
        var stripPlan = SlideShowMaskGeometryPlanner.BuildStrips(
            width,
            height,
            progress,
            stripCount,
            slopeDown);
        if (stripPlan.IsFullyOpen)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in stripPlan.Polygons)
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static StreamGeometry BuildStripGeometry(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true);
            ctx.LineTo(points[1]);
            ctx.LineTo(points[2]);
            ctx.LineTo(points[3]);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry BuildHoneycombTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowHoneycombTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowHoneycombTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildGlitterTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowGlitterTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowGlitterTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildGlitterPolygon(polygon.Points));
        }

        return geometry;
    }

    private static StreamGeometry BuildGlitterPolygon(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();
        var geometry = new StreamGeometry();
        if (points.Length == 0)
            return geometry;

        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true);
            for (var index = 1; index < points.Length; index++)
                context.LineTo(points[index]);
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry BuildRippleTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowRippleTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowRippleTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildRipplePolygon(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildWindTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowWindTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowWindTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildCurtainsTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowCurtainsTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowCurtainsTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildShredTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowShredTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowShredTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildDrapeTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowDrapeTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowDrapeTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildVortexTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowVortexTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowVortexTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildWarpTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowWarpTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowWarpTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildFractureTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowFractureTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowFractureTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildCrushTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowCrushTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowCrushTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildPrismTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowPrismTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowPrismTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildPrestigeTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowPrestigeTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowPrestigeTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static StreamGeometry BuildRipplePolygon(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();
        var geometry = new StreamGeometry();
        if (points.Length == 0)
            return geometry;

        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true);
            for (var index = 1; index < points.Length; index++)
                context.LineTo(points[index]);
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry BuildPageCurlGeometry(
        double width,
        double height,
        double progress,
        SlideShowPageCurlTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in SlideShowPageCurlTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static Geometry BuildWedgeGeometry(double width, double height, double progress)
    {
        var wedgePlan = SlideShowMaskGeometryPlanner.BuildWedge(width, height, progress);
        if (wedgePlan.IsFullyOpen)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        if (wedgePlan.IsCollapsed)
        {
            var center = ToPoint(new SlideShowMaskPoint(width / 2, height / 2));
            var collapsed = new StreamGeometry();
            using (var ctx = collapsed.Open())
            {
                ctx.BeginFigure(center, isFilled: true);
                ctx.LineTo(center);
                ctx.EndFigure(isClosed: true);
            }

            return collapsed;
        }

        var arc = wedgePlan.Arcs[0];
        var centerPoint = ToPoint(arc.Center);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(centerPoint, isFilled: true);
            ctx.LineTo(ToPoint(arc.Start));
            ctx.ArcTo(ToPoint(arc.End), new Size(arc.Radius, arc.Radius), 0, arc.IsLargeArc, SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry BuildWheelGeometry(
        double width,
        double height,
        double progress,
        int spokeCount,
        bool reverse = false)
    {
        var wheelPlan = SlideShowMaskGeometryPlanner.BuildWheel(
            width, height, progress, spokeCount, clockwise: !reverse);
        if (wheelPlan.IsFullyOpen)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        if (wheelPlan.IsCollapsed)
        {
            var center = ToPoint(new SlideShowMaskPoint(width / 2, height / 2));
            var collapsed = new StreamGeometry();
            using (var ctx = collapsed.Open())
            {
                ctx.BeginFigure(center, isFilled: true);
                ctx.LineTo(center);
                ctx.EndFigure(isClosed: true);
            }

            return collapsed;
        }

        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };

        foreach (var arc in wheelPlan.Arcs)
        {
            geometry.Children.Add(BuildWheelSpokeGeometry(arc));
        }

        return geometry;
    }

    private static StreamGeometry BuildWheelSpokeGeometry(
        SlideShowMaskArc arc)
    {
        var center = ToPoint(arc.Center);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(center, isFilled: true);
            ctx.LineTo(ToPoint(arc.Start));
            ctx.ArcTo(
                ToPoint(arc.End),
                new Size(arc.Radius, arc.Radius),
                0,
                arc.IsLargeArc,
                arc.IsClockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Rect ToRect(SlideShowMaskRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(SlideShowMaskPoint point) =>
        new(point.X, point.Y);

    private void AnimateRectClip(Control target, RectangleGeometry clipRect,
        Rect from, Rect to, int durationMs, Action? onComplete = null,
        int? acceleration = null, int? deceleration = null)
    {
        if (durationMs <= 0) { clipRect.Rect = to; onComplete?.Invoke(); return; }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double e = ApplyAnimationEasing(t, acceleration, deceleration);
            clipRect.Rect = new Rect(
                from.X + (to.X - from.X) * e,
                from.Y + (to.Y - from.Y) * e,
                from.Width  + (to.Width  - from.Width)  * e,
                from.Height + (to.Height - from.Height) * e);
            if (frame >= steps) { timer.Stop(); _activeTimers.Remove(timer); clipRect.Rect = to; onComplete?.Invoke(); }
        };
        timer.Start();
    }

    private void ZoomEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        InvokeRevealAtStart(plan, onReveal);

        el.Opacity = plan.FromOpacity;
        var scale = new ScaleTransform(plan.FromScale, plan.FromScale);
        // Avalonia ScaleTransform doesn't take center point in the ctor like WPF.
        // We'll adjust by setting RenderTransformOrigin.
        el.RenderTransformOrigin = RelativePoint.Center;
        el.RenderTransform = scale;

        DelayedAction(plan.DelayMs, () =>
        {
            // Reveal when opacity reaches 1 (entrance) or immediately (exit already revealed).
            AnimateOpacity(
                el,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal),
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateScale(
                el,
                scale,
                plan.FromScale,
                plan.ToScale,
                plan.DurationMs,
                plan.Acceleration,
                plan.Deceleration);
        });
    }

    private void AnimateScale(Control target, ScaleTransform scale,
        double from, double to, int durationMs,
        int? acceleration = null, int? deceleration = null) =>
        AnimateScaleAxes(scale, from, from, to, to, durationMs, acceleration, deceleration);

    private void AnimateScaleAxes(ScaleTransform scale,
        double fromX, double fromY, double toX, double toY, int durationMs,
        int? acceleration = null, int? deceleration = null)
    {
        if (durationMs <= 0) { scale.ScaleX = toX; scale.ScaleY = toY; return; }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double eased = ApplyAnimationEasing(t, acceleration, deceleration);
            scale.ScaleX = fromX + (toX - fromX) * eased;
            scale.ScaleY = fromY + (toY - fromY) * eased;
            if (frame >= steps) { timer.Stop(); _activeTimers.Remove(timer); scale.ScaleX = toX; scale.ScaleY = toY; }
        };
        timer.Start();
    }

    private void PulseEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var scale = new ScaleTransform(1, 1);
        el.RenderTransform = scale;

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateScale(
                el,
                scale,
                1.0,
                plan.PeakScale,
                plan.DurationMs / 2,
                plan.Acceleration,
                plan.Deceleration);
            DelayedAction(plan.DurationMs / 2, () =>
                AnimateScale(
                    el,
                    scale,
                    plan.PeakScale,
                    1.0,
                    plan.DurationMs / 2,
                    plan.Acceleration,
                    plan.Deceleration));
        });
    }

    private void GrowShrinkEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var scale = new ScaleTransform(plan.FromScaleX, plan.FromScaleY);
        el.RenderTransform = scale;

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateScaleAxes(
                scale,
                plan.FromScaleX,
                plan.FromScaleY,
                plan.PeakScaleX,
                plan.PeakScaleY,
                plan.DurationMs / 2,
                plan.Acceleration,
                plan.Deceleration);
            DelayedAction(plan.DurationMs / 2, () =>
                AnimateScaleAxes(
                    scale,
                    plan.PeakScaleX,
                    plan.PeakScaleY,
                    plan.ToScaleX,
                    plan.ToScaleY,
                    plan.DurationMs / 2,
                    plan.Acceleration,
                    plan.Deceleration));
        });
    }

    private void SpinEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var rotate = new RotateTransform(0);
        el.RenderTransform = rotate;

        DelayedAction(plan.DelayMs, () =>
            AnimateRotate(
                rotate,
                0,
                plan.RotationDegrees,
                plan.DurationMs,
                plan.Acceleration,
                plan.Deceleration));
    }

    private void SpiralEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var rotate = new RotateTransform(0);
        el.RenderTransform = rotate;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[]
            {
                (0.0, 0.0),
                (plan.RotationDegrees * 0.82, 0.7),
                (plan.RotationDegrees, 1.0)
            },
            value => rotate.Angle = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void SwivelEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var transform = new MatrixTransform(Matrix.Identity);
        el.RenderTransform = transform;

        DelayedAction(plan.DelayMs, () => AnimateSwivel(
            transform,
            plan.RotationDegrees,
            plan.DurationMs,
            plan.Acceleration,
            plan.Deceleration));
    }

    private void AnimateSwivel(
        MatrixTransform transform,
        double rotationDegrees,
        int durationMs,
        int? acceleration,
        int? deceleration)
    {
        if (durationMs <= 0)
        {
            ApplySwivelTransform(transform, rotationDegrees, 1);
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = Math.Min(1.0, (double)frame / steps);
            var eased = ApplyAnimationEasing(progress, acceleration, deceleration);
            ApplySwivelTransform(
                transform,
                rotationDegrees * eased,
                SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(eased));
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                ApplySwivelTransform(transform, rotationDegrees, 1);
            }
        };
        timer.Start();
    }

    private static void ApplySwivelTransform(
        MatrixTransform transform,
        double rotationDegrees,
        double horizontalScale)
    {
        transform.Matrix = Matrix.CreateScale(horizontalScale, 1)
            * Matrix.CreateRotation(rotationDegrees * Math.PI / 180);
    }

    private void TeeterEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var rotate = new RotateTransform(0);
        el.RenderTransform = rotate;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (-10.0, 0.2), (10.0, 0.4), (-10.0, 0.6), (0.0, 1.0) },
            value => rotate.Angle = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void BlinkEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.15, 0.25), (1.0, 0.5), (0.15, 0.75), (1.0, 1.0) },
            value => el.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void FlashBulbEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.05, 0.08), (1.0, 0.16), (0.70, 0.30), (1.0, 1.0) },
            value => el.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void FlickerEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.20, 0.20), (0.80, 0.35), (0.15, 0.50), (0.65, 0.65), (0.25, 0.80), (1.0, 1.0) },
            value => el.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void WaveEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var translate = new TranslateTransform();
        el.RenderTransform = translate;
        var amplitude = (_slideDipW > 0 ? _slideDipW : 960) * 0.00625;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (-amplitude, 0.2), (amplitude, 0.4), (-amplitude, 0.6), (0.0, 1.0) },
            value => translate.X = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void EmphasisPulseEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.65, 0.5), (1.0, 1.0) },
            value => el.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));

        AddAuthoredColorOverlay(el, plan);

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.GrowWithColor)
        {
            el.RenderTransformOrigin = RelativePoint.Center;
            var scale = new ScaleTransform(1, 1);
            el.RenderTransform = scale;
            DelayedAction(plan.DelayMs, () =>
            {
                AnimateScale(
                    el,
                    scale,
                    1,
                    plan.PeakScale,
                    plan.DurationMs / 2,
                    plan.Acceleration,
                    plan.Deceleration);
                DelayedAction(plan.DurationMs / 2, () =>
                    AnimateScale(
                        el,
                        scale,
                        plan.PeakScale,
                        1,
                        plan.DurationMs / 2,
                        plan.Acceleration,
                        plan.Deceleration));
            });
        }
    }

    private void ColorWaveEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.65, 0.25), (1.0, 0.50), (0.65, 0.75), (1.0, 1.0) },
            value => el.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
        AddAuthoredColorOverlay(el, plan);
    }

    private void FillColorEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        if (element is not Rectangle rectangle
            || rectangle.Fill is not SolidColorBrush brush
            || plan.ColorFromHex is null
            || plan.ColorToHex is null
            || !TryParseAnimationColor(plan.ColorFromHex, out var from)
            || !TryParseAnimationColor(plan.ColorToHex, out var to))
        {
            return;
        }

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateColorKeyframes(
                plan.DurationMs,
                new[] { (from, 0.0), (to, 1.0) },
                value => brush.Color = value,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
            AnimateKeyframes(
                plan.DurationMs,
                new[] { (0.0, 0.0), (1.0, 1.0) },
                value => rectangle.Opacity = value,
                acceleration: plan.Acceleration,
                deceleration: plan.Deceleration);
        });
    }

    private void LineColorEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        element.Opacity = 0;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (0.0, 0.0), (1.0, 1.0) },
            value => element.Opacity = value,
            acceleration: plan.Acceleration,
            deceleration: plan.Deceleration));
    }

    private void FontStyleEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        element.Opacity = 0;
        DelayedAction(plan.DelayMs, () => element.Opacity = 1);
    }

    private void FontSizeEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        element.Opacity = 0;
        DelayedAction(plan.DelayMs, () => element.Opacity = 1);
    }

    private void AddAuthoredColorOverlay(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        if (plan.ColorFromHex is null
            || plan.ColorToHex is null
            || element is not Image image
            || image.Source is not Bitmap source
            || element.Parent is not Panel parent
            || !TryParseAnimationColor(plan.ColorFromHex, out var from)
            || !TryParseAnimationColor(plan.ColorToHex, out var to))
        {
            return;
        }

        var brush = new SolidColorBrush(from);
        var tint = new Rectangle
        {
            Width = element.Width,
            Height = element.Height,
            Fill = brush,
            Opacity = 0,
            OpacityMask = new ImageBrush(source) { Stretch = Stretch.None },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tint, Canvas.GetLeft(element));
        Canvas.SetTop(tint, Canvas.GetTop(element));
        parent.Children.Add(tint);

        DelayedAction(plan.DelayMs, () =>
        {
            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ColorWave)
            {
                AnimateColorKeyframes(
                    plan.DurationMs,
                    new[] { (from, 0.0), (to, 0.25), (from, 0.50), (to, 0.75), (from, 1.0) },
                    value => brush.Color = value,
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration);
                AnimateKeyframes(
                    plan.DurationMs,
                    new[] { (0.0, 0.0), (0.65, 0.25), (0.0, 0.50), (0.65, 0.75), (0.0, 1.0) },
                    value => tint.Opacity = value,
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration);
            }
            else
            {
                var endColor = plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeColor ? to : from;
                AnimateColorKeyframes(
                    plan.DurationMs,
                    new[] { (from, 0.0), (to, 0.5), (endColor, 1.0) },
                    value => brush.Color = value,
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration);
                AnimateKeyframes(
                    plan.DurationMs,
                    new[] { (0.0, 0.0), (0.65, 0.5), (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeColor ? 0.65 : 0.0, 1.0) },
                    value => tint.Opacity = value,
                    acceleration: plan.Acceleration,
                    deceleration: plan.Deceleration);
            }
        });
    }

    private static bool TryParseAnimationColor(string value, out Color color)
    {
        color = default;
        if (value.Length != 6
            || !byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(value[4..], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromRgb(r, g, b);
        return true;
    }

    private static bool TryParseAnimationColorHex(string value, out SrgbColor color)
    {
        if (value is { Length: 6 }
            && int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            color = SrgbColor.FromRgb(rgb);
            return true;
        }

        color = SrgbColor.Black;
        return false;
    }

    private void AnimateColorKeyframes(
        int durationMs,
        IReadOnlyList<(Color Value, double Progress)> keyframes,
        Action<Color> apply,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (keyframes.Count == 0)
            return;

        if (durationMs <= 0)
        {
            apply(keyframes[^1].Value);
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frameMs) });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = Math.Min(1.0, (double)frame / steps);
            var value = keyframes[0].Value;
            for (var i = 1; i < keyframes.Count; i++)
            {
                if (progress > keyframes[i].Progress)
                    continue;

                var previous = keyframes[i - 1];
                var current = keyframes[i];
                var local = (progress - previous.Progress) / Math.Max(0.0001, current.Progress - previous.Progress);
                value = InterpolateAnimationColor(
                    previous.Value,
                    current.Value,
                    ApplyAnimationEasing(local, acceleration, deceleration));
                break;
            }

            if (progress >= keyframes[^1].Progress)
                value = keyframes[^1].Value;

            apply(value);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                apply(keyframes[^1].Value);
            }
        };
        timer.Start();
    }

    private static Color InterpolateAnimationColor(Color from, Color to, double progress) =>
        Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * progress),
            (byte)(from.R + (to.R - from.R) * progress),
            (byte)(from.G + (to.G - from.G) * progress),
            (byte)(from.B + (to.B - from.B) * progress));

    private void AnimateKeyframes(
        int durationMs,
        IReadOnlyList<(double Value, double Progress)> keyframes,
        Action<double> apply,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (keyframes.Count == 0)
        {
            return;
        }

        if (durationMs <= 0)
        {
            apply(keyframes[^1].Value);
            return;
        }

        const int frameMs = 16;
        var steps = Math.Max(1, durationMs / frameMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = Math.Min(1.0, (double)frame / steps);
            var value = keyframes[0].Value;
            for (var i = 1; i < keyframes.Count; i++)
            {
                if (progress > keyframes[i].Progress)
                {
                    continue;
                }

                var previous = keyframes[i - 1];
                var current = keyframes[i];
                var local = (progress - previous.Progress) / Math.Max(0.0001, current.Progress - previous.Progress);
                value = previous.Value + (current.Value - previous.Value)
                    * ApplyAnimationEasing(local, acceleration, deceleration);
                break;
            }

            if (progress >= keyframes[^1].Progress)
            {
                value = keyframes[^1].Value;
            }

            apply(value);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                apply(keyframes[^1].Value);
            }
        };
        timer.Start();
    }

    private void DisappearEffect(Control el, int delayMs)
    {
        DelayedAction(delayMs, () => el.Opacity = 0);
    }

    private void AnimateRotate(
        RotateTransform rotate,
        double from,
        double to,
        int durationMs,
        int? acceleration = null,
        int? deceleration = null)
    {
        if (durationMs <= 0) { rotate.Angle = to; return; }

        const int frameMs = 16;
        int steps = Math.Max(1, durationMs / frameMs);
        int frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            double t = Math.Min(1.0, (double)frame / steps);
            double eased = ApplyAnimationEasing(t, acceleration, deceleration);
            rotate.Angle = from + (to - from) * eased;
            if (frame >= steps) { timer.Stop(); _activeTimers.Remove(timer); rotate.Angle = to; }
        };
        timer.Start();
    }

    /// <summary>
    /// Motion-path animation: translates the shape along the normalized path in DIP space.
    /// </summary>
    private void MotionPathEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan,
        Action? onReveal = null)
    {
        double slideW = _slideDipW > 0 ? _slideDipW : 960;
        double slideH = _slideDipH > 0 ? _slideDipH : 540;

        // Motion paths reveal the shape immediately (it's already at its starting position).
        element.Opacity = 1;
        InvokeRevealAtStart(plan, onReveal);

        var pts = plan.MotionKeyFrames
            .Select(frame => (dx: frame.OffsetXFactor * slideW, dy: frame.OffsetYFactor * slideH))
            .ToArray();
        if (pts.Length == 0) return;

        var translate = new TranslateTransform(pts[0].dx, pts[0].dy);
        element.RenderTransform = translate;

        if (plan.DurationMs <= 0)
        {
            translate.X = pts[^1].dx;
            translate.Y = pts[^1].dy;
            return;
        }

        DelayedAction(plan.DelayMs, () =>
        {
            const int frameMs = 16;
            int steps = Math.Max(1, plan.DurationMs / frameMs);
            int frame = 0;
            var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(frameMs)
            });
            timer.Tick += (_, _) =>
            {
                frame++;
                double t = Math.Min(1.0, (double)frame / steps);
                t = SlideShowPlaybackPlanner.ApplyTimingEasing(
                    t,
                    plan.Acceleration,
                    plan.Deceleration);
                // Sample the pre-sampled array.
                double scaledT = t * (pts.Length - 1);
                int lo = Math.Min((int)scaledT, pts.Length - 2);
                int hi = Math.Min(lo + 1, pts.Length - 1);
                double frac = scaledT - lo;
                translate.X = pts[lo].dx + (pts[hi].dx - pts[lo].dx) * frac;
                translate.Y = pts[lo].dy + (pts[hi].dy - pts[lo].dy) * frac;
                if (frame >= steps)
                {
                    timer.Stop();
                    _activeTimers.Remove(timer);
                    translate.X = pts[^1].dx;
                    translate.Y = pts[^1].dy;
                }
            };
            timer.Start();
        });
    }

    /// <summary>Best-effort fallback for shapes without an overlay element.</summary>
    private void PlayFallbackAnimation(ShapeAnimation animation, int delayMs, int durationMs)
    {
        var visibilityPlan = SlideShowPlaybackPlanner.PlanFallbackVisibility(animation);
        if (visibilityPlan.SuppressAtStart)
        {
            _slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);
            _slideCanvas.Refresh();
        }

        if (visibilityPlan.SuppressAtStart || visibilityPlan.SuppressAtCompletion)
        {
            DelayedAction(
                Math.Max(0, delayMs) + Math.Max(0, durationMs),
                () =>
                {
                    if (visibilityPlan.SuppressAtCompletion)
                        _slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);
                    else
                        RevealShape(animation.ShapeId);

                    _slideCanvas.Refresh();
                });
            return;
        }

        PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(animation, delayMs));
    }

    /// <summary>Best-effort emphasis fallback for shapes without an overlay element.</summary>
    private void PlayFallbackAnimation(SlideShowFallbackAnimationPlaybackPlan? plan)
    {
        if (plan is null) return;

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateOpacity(_slideCanvas, plan.FromOpacity, plan.FlashOpacity, plan.DurationMs / 2, onComplete: () =>
                AnimateOpacity(_slideCanvas, plan.FlashOpacity, plan.FromOpacity, plan.DurationMs / 2));
        });
    }

    // ── Utility ───────────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="action"/> after <paramref name="delayMs"/> milliseconds.</summary>
    private void DelayedAction(int delayMs, Action action)
    {
        if (delayMs <= 0) { action(); return; }

        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        });
        timer.Tick += (_, _) => { timer.Stop(); _activeTimers.Remove(timer); action(); };
        timer.Start();
    }

    // ── Active-timer management (DA2 + DA3) ──────────────────────────────────────

    /// <summary>
    /// Registers a timer so it can be batch-cancelled by <see cref="CancelActiveTimers"/>.
    /// Returns the timer unchanged so callers can one-line register + start.
    /// </summary>
    private DispatcherTimer TrackTimer(DispatcherTimer timer)
    {
        _activeTimers.Add(timer);
        return timer;
    }

    /// <summary>
    /// DA2: Cancels every in-flight animation/transition timer so a new advance starts
    /// from a clean state (no stale onComplete callbacks will fire against the new slide).
    /// DA3: Same mechanism used by Teardown to prevent timers leaking past window close.
    /// </summary>
    private void CancelActiveTimers()
    {
        foreach (var t in _activeTimers)
            t.Stop();
        _activeTimers.Clear();
    }

    // ── Teardown ──────────────────────────────────────────────────────────────────

    private void Teardown(DateTimeOffset? nowUtc = null)
    {
        _presenterViewWindow?.Close();
        _presenterViewWindow = null;
        _mediaController.Teardown();
        if (_session.IsClosed)
        {
            return;
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        _session.Close(now);
        _autoAdvanceTimer.Stop();
        _kioskRestartTimer.Stop();
        // DA3: stop ALL per-frame animation/transition timers so they don't keep
        // ticking against the closed window's canvas.  A running DispatcherTimer is
        // rooted by the dispatcher and will NOT be collected automatically.
        CancelActiveTimers();
    }

    /// <summary>Expose active-timer count for test assertions (DA2/DA3).</summary>
    internal int ActiveTimerCount => _activeTimers.Count;
}
