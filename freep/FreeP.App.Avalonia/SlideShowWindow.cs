using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
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
public sealed class SlideShowWindow : Window, ISlideShowTransitionPlaybackRenderer, ISlideShowDisplayRenderer
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly Presentation    _presentation;
    private readonly SlideShowRuntimeApplication _runtime;
    private readonly Action<int, string?>? _setSlideNotesText;
    private readonly AvaloniaSlideShowMediaController _mediaController;
    private readonly DispatcherTimer  _autoAdvanceTimer;
    private readonly DispatcherTimer  _kioskRestartTimer;
    private long _autoAdvanceDisplayVersion;
    private PresenterViewWindow? _presenterViewWindow;
    private bool _zoomShowBackgroundForTransition = true;

    // DA2 + DA3: all per-frame DispatcherTimers created by animation/transition helpers
    // (AnimateOpacity, AnimateTranslate, AnimateRectClip, AnimateScale, AnimateRotate,
    //  DelayedAction) register themselves here.  CancelActiveTimers() stops all of them
    // immediately — called before starting a new transition (DA2) and in Teardown (DA3).
    private readonly List<DispatcherTimer> _activeTimers = new();
    private Action<Hyperlink, int>? _internalHyperlinkNavigationObserver;

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

    // Native controls keyed by the renderer-neutral animation target registry.
    private readonly SlideShowAnimationTargetRegistry<Control> _animationTargets = new();

    // Current slide dimensions in DIP.
    private double _slideDipW;
    private double _slideDipH;

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
        ArgumentNullException.ThrowIfNull(playbackRoute);
        _setSlideNotesText = setSlideNotesText;
        _runtime = new SlideShowRuntimeApplication(
            _presentation,
            playbackRoute,
            DateTimeOffset.UtcNow,
            captureBackend ?? CreateDefaultRecordingCaptureBackend(),
            new SlideShowRuntimeCaptionPreference(
                preferredCaptionSlideIndex,
                preferredCaptionShapeId,
                preferredCaptionTrackIndex));

        // Pre-compute slide DIP dimensions.
        var metrics = _runtime.InitialSlideMetrics;
        _slideDipW = metrics.WidthDip;
        _slideDipH = metrics.HeightDip;

        // Speaker and kiosk modes are fullscreen; individual browsing is a normal window.
        var windowPlan = _runtime.WindowPlan;
        var isBrowseWindow = windowPlan.IsBrowseWindow;
        WindowState        = isBrowseWindow ? WindowState.Normal : WindowState.FullScreen;
        ExtendClientAreaToDecorationsHint = windowPlan.IsBorderless;
        Topmost            = windowPlan.IsTopmost;
        if (isBrowseWindow)
        {
            Width = 1024;
            Height = 768;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }
        Background         = Brushes.Black;
        Focusable          = true;
        CanResize          = windowPlan.AllowsResize;

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
                HorizontalScrollBarVisibility = windowPlan.ShowBrowseScrollbars
                    ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = windowPlan.ShowBrowseScrollbars
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
        _autoAdvanceTimer.Tick += (_, _) =>
            _runtime.HandleAutoAdvanceElapsed(_autoAdvanceDisplayVersion);

        _kioskRestartTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            IsEnabled = false,
        };
        _kioskRestartTimer.Tick += (_, _) =>
            _runtime.HandleKioskRestartElapsed();

        _runtime.BindRenderer(new SlideShowRuntimeRendererCallbacks(
            _autoAdvanceTimer.Stop,
            CloseSlideShow,
            PlayAnimationStep,
            navigation => DisplayCurrentSlide(
                navigation.AnimateSlide,
                navigation.TransitionDurationMs,
                navigation.UseDestinationBackground),
            TogglePresenterView,
            () => DisplayCurrentSlide(animated: false),
            RenderScreenMode,
            hyperlink => OpenExternalUrl(hyperlink.Url!),
            RefreshInkOverlay,
            RecordInternalHyperlinkNavigation,
            TeardownMedia: _mediaController.Teardown),
            this);

        // ── Event wiring ───────────────────────────────────────────────────────
        KeyDown             += OnKeyDown;
        PointerPressed      += OnPointerPressed;
        PointerMoved        += OnPointerMoved;
        PointerReleased     += OnPointerReleased;
        Opened              += (_, _) =>
        {
            Focus();
            DisplayCurrentSlide(animated: false);
            _runtime.StartRendererSession();
        };
        Closed              += (_, _) => Teardown();
    }

    // ── Public API (callable by test code without showing the window) ─────────────

    /// <summary>
    /// Execute a single logical advance step and return what happened.
    /// </summary>
    public AdvanceResult ExecuteAdvance(DateTimeOffset? nowUtc = null) =>
        _runtime.ExecuteAdvance(nowUtc);

    /// <summary>Execute a logical back step and return what happened.</summary>
    public BackResult ExecuteBack(DateTimeOffset? nowUtc = null) =>
        _runtime.ExecuteBack(nowUtc);

    /// <summary>Jump to a one-based slide number without playing its entrance transition.</summary>
    public void ExecuteSlideNumberJump(int oneBasedSlideNumber) =>
        _runtime.ExecuteSlideNumberJump(oneBasedSlideNumber);

    public Slide? ExecuteHiddenSlideReveal() => _runtime.ExecuteHiddenSlideReveal();

    /// <summary>The underlying state machine (for test assertions).</summary>
    public SlideShowController Controller => _runtime.Controller;

    /// <summary>The presenter blank-screen mode currently covering the slide.</summary>
    public SlideShowScreenMode ScreenMode => _runtime.ScreenMode;

    /// <summary>Show the slide, a black screen, or a white screen during presentation.</summary>
    public void SetScreenMode(SlideShowScreenMode mode) => _runtime.SetScreenMode(mode);

    private void RenderScreenMode(SlideShowRuntimeScreenModePlan plan)
    {
        _screenModeOverlay.Fill = plan.UseWhiteSurface ? Brushes.White : Brushes.Black;
        _screenModeOverlay.IsVisible = plan.IsBlank;
    }

    public DateTimeOffset PresenterStartedAtUtc => _runtime.StartedAtUtc;

    public SlideShowPresenterToolPlan PresenterToolPlan => _runtime.ToolPlan;

    public IReadOnlyList<SlideShowPresenterWorkflowAction> PresenterWorkflowActions =>
        _runtime.ToolPlan.WorkflowActions;

    public IReadOnlyList<SlideShowPresenterCommandState> PresenterCommandStates =>
        _runtime.ToolPlan.CommandStates;

    public SlideShowTimingRecorderState TimingRecorderState => _runtime.TimingRecorderState;

    public SlideShowRecordingExecutionState RecordingExecutionState => _runtime.RecordingExecutionState;

    public SlideShowRecordingCaptureAdapterReadiness RecordingCaptureAdapterReadiness =>
        _runtime.RecordingExecutionState.HostCapabilities.EffectiveCaptureAdapterReadiness;

    public IReadOnlyList<SlideShowRecordingExecutionAction> RecordingExecutionActions =>
        _runtime.RecordingExecutionState.LastActions;

    public bool IsPresenterSessionClosed => _runtime.IsClosed;

    public SlideShowInkExecutionState InkExecutionState => _runtime.InkExecutionState;
    public SlideShowPresenterSessionSummary PresenterSessionSummary =>
        _runtime.PresenterSummary;

    public SlideShowRecordingReviewPlan RecordingReviewPlan =>
        _runtime.RecordingReviewPlan;

    public SlideShowRecordingReviewApplyResult ApplyRecordingReview() =>
        _runtime.ApplyRecordingReview();

    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal string? ActiveMediaCaptionForTest(uint shapeId) => _mediaController.CaptionTextForTest(shapeId);
    internal void RefreshMediaCaptionsForTest() => _mediaController.RefreshCaptionsForTest();
    internal SlideShowMediaClickPlan LastMediaClickForTest => _mediaController.LastClick;
    internal ValidationAccessAdapter CreateValidationAccessAdapter() => new(this);
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest =>
        _runtime.AnimationRendererSession.LastFrame;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest =>
        _runtime.AnimationRendererSession.LastStep?.Checkpoints ?? [];
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest =>
        _runtime.AnimationRendererSession.LastStep?.Readiness;
    internal SlideShowPlaybackRoute PlaybackRoute => _runtime.PlaybackRoute;
    internal int CurrentPresentationSlideIndex => _runtime.CurrentPresentationSlideIndex;
    internal Slide? RevealedHiddenSlideForTest => _runtime.RevealedHiddenSlide;

    internal sealed class ValidationAccessAdapter
    {
        private readonly SlideShowWindow _owner;

        internal ValidationAccessAdapter(SlideShowWindow owner) => _owner = owner;

        internal bool IsVisible => _owner.IsVisible;
        internal int CurrentSlideIndex => _owner.Controller.CurrentSlideIndex;

        internal string Advance()
        {
            var result = _owner.ExecuteAdvance();
            return result.GetType().Name;
        }

        internal ValidationMediaPlaybackState CaptureMediaPlayback() => new(
            _owner._mediaController.Availability?.IsAvailable,
            _owner._mediaController.Availability?.FailureReason,
            _owner._mediaController.Active.Count,
            _owner._mediaController.LastFailure is not null);

        internal void Close() => _owner.Close();
    }

    internal sealed record ValidationMediaPlaybackState(
        bool? IsAvailable,
        string? FailureReason,
        int ActiveMediaCount,
        bool HasFailure);

    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        _runtime.CreatePresenterState(nowUtc, displayIntent);

    /// <summary>Whether the synchronized presenter dashboard is currently open.</summary>
    public bool IsPresenterViewOpen => _runtime.IsPresenterViewOpen;

    /// <summary>Opens or closes the presenter dashboard without changing audience playback.</summary>
    public void TogglePresenterView()
        => _runtime.TogglePresenterView();

    void ISlideShowDisplayRenderer.OpenPresenterView()
    {
        var window = new PresenterViewWindow(
            _presentation,
            _runtime.CreatePresenterViewOperations(_setSlideNotesText));
        _presenterViewWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_presenterViewWindow, window))
            {
                _presenterViewWindow = null;
                _runtime.NotifyPresenterViewClosed();
            }
        };
        window.Show(this);
    }

    void ISlideShowDisplayRenderer.ClosePresenterView() => _presenterViewWindow?.Close();

    void ISlideShowDisplayRenderer.RefreshPresenterView() =>
        _presenterViewWindow?.RefreshFromState();

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        DateTimeOffset? nowUtc = null)
        => _runtime.ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            nowUtc);

    public SlideShowPresenterToolPlan SetPresenterPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset? nowUtc = null)
        => _runtime.SetPointerMode(pointerMode, nowUtc);

    public SlideShowPresenterToolPlan SetPresenterTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset? nowUtc = null)
        => _runtime.SetTimingIntent(timingIntent, nowUtc);

    public SlideShowPresenterToolPlan SetPresenterMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset? nowUtc = null)
        => _runtime.SetMediaIntent(mediaIntent, nowUtc);

    public SlideShowInkExecutionResult BeginPresenterInkStroke(double canvasX, double canvasY) =>
        _runtime.BeginPointerInk(CreateCanvasPointer(canvasX, canvasY));

    public SlideShowInkExecutionResult AppendPresenterInkStroke(double canvasX, double canvasY) =>
        _runtime.AppendPointerInk(CreateCanvasPointer(canvasX, canvasY));

    public SlideShowInkExecutionResult EndPresenterInkStroke(double canvasX, double canvasY) =>
        _runtime.EndPointerInk(CreateCanvasPointer(canvasX, canvasY));

    public SlideShowInkExecutionResult ClearPresenterInkStrokes() =>
        _runtime.ClearInkStrokes();

    public SlideShowInkExecutionResult UndoLastPresenterInkStroke() =>
        _runtime.UndoLastInkStroke();

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
        return WindowsRecordingCaptureBackend.CreateUnavailable(windowsMetadata);
#endif
    }

    /// <summary>Exposes the slide canvas for test assertions (DA1 suppression).</summary>
    internal SlideCanvas CanvasForTest => _slideCanvas;

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = _runtime.HandleKeyboardInput(
            e.Key.ToString(),
            controlPressed: e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    // ── Pointer navigation ────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var slide = _runtime.DisplaySlide;
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

        e.Handled = _runtime.HandlePointerInput(CreateCanvasPointer(pt.X, pt.Y));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var slide = _runtime.DisplaySlide;
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
        => _runtime.HitTestHyperlink(slide, CreateCanvasPointer(canvasX, canvasY));

    /// <summary>
    /// Activates a hyperlink: external → open URL or local file;
    /// internal → navigate to the target slide.
    /// </summary>
    internal void ActivateHyperlink(Hyperlink hlink)
        => _runtime.ActivateHyperlink(hlink);

    internal void SetInternalHyperlinkNavigationObserver(Action<Hyperlink, int>? observer) =>
        _internalHyperlinkNavigationObserver = observer;

    /// <summary>
    /// Opens an external URL in the default browser through the shared URI allowlist.
    /// Blocked schemes and launch failures are silently ignored so a bad slideshow link never crashes playback.
    /// </summary>
    internal static void OpenExternalUrl(string url)
    {
        DesktopExternalUriLauncher.Open(url);
    }

    // ── Trigger shape hit-testing ─────────────────────────────────────────────────

    private void RecordInternalHyperlinkNavigation(Hyperlink hyperlink)
        => _internalHyperlinkNavigationObserver?.Invoke(
            hyperlink,
            _runtime.Controller.CurrentSlideIndex);

    private SlideShowCanvasPointer CreateCanvasPointer(double canvasX, double canvasY) =>
        new(
            canvasX,
            canvasY,
            _slideCanvas.Bounds.Width,
            _slideCanvas.Bounds.Height,
            CurrentSlideMetrics());

    private void RefreshInkOverlay()
    {
        _inkOverlay.Children.Clear();

        var canvasWidth = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW;
        var canvasHeight = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH;
        var plan = SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(
            _runtime.InkExecutionState,
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
        _runtime.InkExecutionState.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter =>
                new Cursor(StandardCursorType.Cross),
            SlideShowPresenterPointerMode.Eraser => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default
        };

    // ── Navigation helpers ────────────────────────────────────────────────────────

    private void CloseSlideShow(DateTimeOffset nowUtc)
    {
        Teardown(nowUtc);
        Close();
    }

    // ── Slide display + transitions ───────────────────────────────────────────────

    private void DisplayCurrentSlide(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
        => _runtime.DisplayCurrentSlide(
            animated,
            zoomTransitionDurationMs,
            zoomShowBackground);

    void ISlideShowDisplayRenderer.ApplyDisplayState(SlideShowRuntimeDisplayPlan plan)
    {
        _slideDipW = plan.Metrics.WidthDip;
        _slideDipH = plan.Metrics.HeightDip;
        _zoomShowBackgroundForTransition = plan.UseDestinationBackground;
        _slideCanvas.RenderSlideBackground = true;
    }

    void ISlideShowDisplayRenderer.EnterMediaSlide(SlideShowRuntimeDisplayPlan plan)
    {
        _mediaController.EnterSlide(
            plan.Slide!,
            _slideDipW,
            _slideDipH,
            _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW,
            _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH,
            plan.CaptionTracks,
            preferredCaptionShapeId: plan.PreferredCaptionShapeId,
            preferredCaptionTrackIndex: plan.PreferredCaptionTrackIndex,
            captionSlideIndex: plan.CaptionSlideIndex,
            preferredCaptionSlideIndex: plan.PreferredCaptionSlideIndex,
            showMediaControls: plan.ShowMediaControls,
            showNarration: plan.ShowNarration,
            presentationSlideIndex: plan.CaptionSlideIndex);
    }

    void ISlideShowDisplayRenderer.StopAutoAdvanceTimer() => _autoAdvanceTimer.Stop();

    void ISlideShowDisplayRenderer.StartAutoAdvanceTimer(
        TimeSpan interval,
        long displayVersion)
    {
        _autoAdvanceDisplayVersion = displayVersion;
        _autoAdvanceTimer.Interval = interval;
        _autoAdvanceTimer.Start();
    }

    void ISlideShowDisplayRenderer.StopKioskRestartTimer() => _kioskRestartTimer.Stop();

    void ISlideShowDisplayRenderer.StartKioskRestartTimer(TimeSpan interval)
    {
        _kioskRestartTimer.Interval = interval;
        _kioskRestartTimer.Start();
    }

    void ISlideShowDisplayRenderer.RequestAutoAdvance() =>
        _runtime.ExecuteAdvance(stopAutoAdvance: true);

    void ISlideShowDisplayRenderer.RequestKioskRestart() => _runtime.RestartKioskShow();

    void ISlideShowDisplayRenderer.CancelVisualOperations() => CancelActiveTimers();

    void ISlideShowDisplayRenderer.RefreshInkOverlay() => RefreshInkOverlay();

    void ISlideShowDisplayRenderer.PrepareAnimationOverlay(Slide slide) =>
        PrepareAnimationOverlay(slide);

    void ISlideShowDisplayRenderer.PlayTransition(Slide slide, SlideTransition transition) =>
        PlayTransition(slide, transition);

    void ISlideShowDisplayRenderer.ShowSlideInstant(Slide slide) => ShowSlideInstant(slide);

    private SlideShowSlideMetrics CurrentSlideMetrics() => new(_slideDipW, _slideDipH);

    private void SyncMediaOverlayBounds()
    {
        var width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : _slideDipW;
        var height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : _slideDipH;
        var slide = _runtime.DisplaySlide;
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
    void ISlideShowTransitionPlaybackRenderer.PlayZoom(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayZoomTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayPan(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayPanTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayGallery(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayGalleryTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayConveyor(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayConveyorTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayWindow(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayWindowTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayMorph(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayMorphTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPerspective(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPerspectiveTransitionPlan perspectivePlan) =>
        PlayPerspectiveTransition(slide, plan, perspectivePlan);
    void ISlideShowTransitionPlaybackRenderer.PlayPolygonClip(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPolygonClipTransitionPlan polygonPlan) =>
        PlayPolygonClipTransition(slide, plan, polygonPlan);
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
        double opening) =>
        BuildBoxTransitionGeometry(width, height, opening, expandsFromCenter: true);

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
        bool reverse = false)
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
            var progress = EaseInOut(t);
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

    private void PlayZoomTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowTransformTransitionPlan transformPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var incomingStart = transformPlan.ResolveIncoming(0, w, h);
        var startScale = incomingStart.Scale;

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

    private void PlayPanTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowTransformTransitionPlan transformPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var incomingStart = transformPlan.ResolveIncoming(0, w, h);
        var transform = new MatrixTransform(Matrix.CreateScale(
            incomingStart.Scale,
            incomingStart.Scale)
            * Matrix.CreateTranslation(incomingStart.TranslateX, incomingStart.TranslateY));

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
            transformPlan,
            w,
            h,
            plan.DurationMs,
            onComplete: () =>
            {
                _slideCanvas.RenderTransform = null;
                _transitionBackImage.IsVisible = false;
            });
    }

    private void PlayGalleryTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowTransformTransitionPlan transformPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var incomingStart = transformPlan.ResolveIncoming(0, w, h);
        var incomingTransform = new MatrixTransform(Matrix.CreateScale(
            incomingStart.Scale,
            incomingStart.Scale)
            * Matrix.CreateTranslation(incomingStart.TranslateX, incomingStart.TranslateY));

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
            transformPlan,
            w,
            h,
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

    private void PlayConveyorTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowTransformTransitionPlan transformPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var incomingStart = transformPlan.ResolveIncoming(0, w, h);

        var incomingTransform = new MatrixTransform(Matrix.CreateScale(
            incomingStart.Scale,
            incomingStart.Scale)
            * Matrix.CreateRotation(incomingStart.RotationDegrees * Math.PI / 180)
            * Matrix.CreateTranslation(incomingStart.TranslateX, incomingStart.TranslateY));

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
            transformPlan,
            w,
            h,
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
        SlideShowTransformTransitionPlan transformPlan,
        double width,
        double height,
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
            var incomingState = transformPlan.ResolveIncoming(eased, width, height);
            incoming.Matrix = Matrix.CreateScale(incomingState.Scale, incomingState.Scale)
                * Matrix.CreateRotation(incomingState.RotationDegrees * Math.PI / 180)
                * Matrix.CreateTranslation(
                    incomingState.TranslateX,
                    incomingState.TranslateY);

            if (outgoing is not null)
            {
                var outgoingState = transformPlan.ResolveOutgoing(eased, width, height);
                outgoing.Matrix = Matrix.CreateScale(outgoingState.Scale, outgoingState.Scale)
                    * Matrix.CreateRotation(outgoingState.RotationDegrees * Math.PI / 180)
                    * Matrix.CreateTranslation(
                        outgoingState.TranslateX,
                        outgoingState.TranslateY);
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

    private void PlayWindowTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowTransformTransitionPlan transformPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var incomingStart = transformPlan.ResolveIncoming(0, w, h);
        var scale = new ScaleTransform(
            incomingStart.Scale,
            incomingStart.Scale);
        var clipRect = BuildWindowTransitionGeometry(
            w,
            h,
            incomingStart.ClipOpening ?? 1);

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
            transformPlan,
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
        SlideShowTransformTransitionPlan transformPlan,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            var complete = transformPlan.ResolveIncoming(1, width, height);
            scale.ScaleX = scale.ScaleY = complete.Scale;
            clipRect.Rect = BuildWindowTransitionGeometry(
                width,
                height,
                complete.ClipOpening ?? 1).Rect;
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
            var state = transformPlan.ResolveIncoming(eased, width, height);
            scale.ScaleX = scale.ScaleY = state.Scale;
            clipRect.Rect = BuildWindowTransitionGeometry(
                width,
                height,
                state.ClipOpening ?? 1).Rect;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                var complete = transformPlan.ResolveIncoming(1, width, height);
                scale.ScaleX = scale.ScaleY = complete.Scale;
                clipRect.Rect = BuildWindowTransitionGeometry(
                    width,
                    height,
                    complete.ClipOpening ?? 1).Rect;
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
        var w = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var rendererPlan = SlideShowMorphPlanner.BuildRendererPlan(
            transition,
            source,
            slide,
            w,
            h,
            _slideDipW,
            _slideDipH);
        if (!rendererPlan.CanRender)
        {
            PlayFadeTransition(slide, plan.DurationMs);
            return;
        }

        var snapshot = CaptureCurrentSlide();
        var prepared = new List<(Image Image, MatrixTransform Transform, double ScaleX, double ScaleY, double TranslateX, double TranslateY, uint ShapeId)>();

        void AddMorphOverlay(
            RenderTargetBitmap? bitmap,
            SlideShowMorphOverlayRendererPlan overlay)
        {
            if (bitmap is null)
                return;

            var scaleX = overlay.InitialScaleX;
            var scaleY = overlay.InitialScaleY;
            var translateX = overlay.InitialTranslateX;
            var translateY = overlay.InitialTranslateY;
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
                    overlay.TargetBounds.CenterX / w,
                    overlay.TargetBounds.CenterY / h,
                    RelativeUnit.Relative),
                RenderTransform = matrix
            };
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, 0);
            _animOverlay.Children.Add(image);
            _slideCanvas.SuppressedShapeIds.Add(overlay.ShapeId);
            prepared.Add((image, matrix, scaleX, scaleY, translateX, translateY, overlay.ShapeId));
        }

        foreach (var overlay in rendererPlan.Overlays)
        {
            AddMorphOverlay(
                RenderShapeToOverlayBitmap(slide, overlay.RenderShape, w, h),
                overlay);
        }

        if (prepared.Count == 0)
        {
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

    /// <summary>Native two-surface realization of the shared perspective plan.</summary>
    private void PlayPerspectiveTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPerspectiveTransitionPlan perspective)
    {
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

    private void PlayPolygonClipTransition(
        Slide slide,
        SlideShowTransitionPlaybackPlan playback,
        SlideShowPolygonClipTransitionPlan polygonPlan)
    {
        var snapshot = CaptureCurrentSlide();
        var width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        var height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = null;
        _slideCanvas.Clip = BuildPolygonClipGeometry(width, height, 0, polygonPlan);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.IsVisible = true;
        }

        var steps = SlideShowPolygonClipTransitionPlanner.ResolveTimerStepCount(
            playback.DurationMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(
                SlideShowPolygonClipTransitionPlanner.TimerFrameIntervalMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress =
                SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(frame, steps);
            _slideCanvas.Clip =
                BuildPolygonClipGeometry(width, height, progress, polygonPlan);
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

    private static Geometry BuildPolygonClipGeometry(
        double width,
        double height,
        double progress,
        SlideShowPolygonClipTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var polygon in plan.BuildPolygons(width, height, progress))
        {
            var points = polygon.Points.Select(ToPoint).ToArray();
            if (points.Length == 0)
                continue;

            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(points[0], isFilled: true);
                for (var index = 1; index < points.Length; index++)
                    context.LineTo(points[index]);
                context.EndFigure(isClosed: true);
            }
            geometry.Children.Add(path);
        }

        return geometry;
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
        SlideShowTransformTransitionPlan transformPlan,
        double width,
        double height,
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
            var incomingState = transformPlan.ResolveIncoming(eased, width, height);
            incoming.Matrix = Matrix.CreateScale(incomingState.Scale, incomingState.Scale)
                * Matrix.CreateTranslation(
                    incomingState.TranslateX,
                    incomingState.TranslateY);

            if (outgoing is not null)
            {
                var outgoingState = transformPlan.ResolveOutgoing(eased, width, height);
                outgoing.Matrix = Matrix.CreateScale(outgoingState.Scale, outgoingState.Scale)
                    * Matrix.CreateTranslation(
                        outgoingState.TranslateX,
                        outgoingState.TranslateY);
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
        SlideShowTransformTransitionPlan transformPlan,
        double width,
        double height,
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
            var state = transformPlan.ResolveIncoming(eased, width, height);
            transform.Matrix = Matrix.CreateScale(state.Scale, state.Scale)
                * Matrix.CreateTranslation(state.TranslateX, state.TranslateY);
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
        double endProgress = 1)
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
            var progress = startProgress + (endProgress - startProgress) * EaseInOut(t);
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
        Action? onComplete = null)
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
            double eased = EaseInOut(t);
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
        int durationMs, Action? onComplete = null)
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
            double eased = EaseInOut(t);
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
        _animationTargets.Clear();

        // DA1: clear any suppression from the previous slide.
        _slideCanvas.SuppressedShapeIds.Clear();

        var overlayPlan = _runtime.AnimationRendererSession.PlanOverlay(slide);
        if (overlayPlan.Shapes.Count == 0) return;

        double w = _slideCanvas.Bounds.Width  > 0 ? _slideCanvas.Bounds.Width  : 960;
        double h = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;

        _animOverlay.Width  = w;
        _animOverlay.Height = h;

        SlideShowAnimationOverlayMaterializer.Materialize<Control, RenderTargetBitmap>(
            overlayPlan,
            shape => RenderShapeToOverlayBitmap(slide, shape, w, h),
            (bitmap, elementPlan) => CreateAnimationOverlayElement(bitmap, w, h, elementPlan),
            element =>
            {
                Canvas.SetLeft(element, 0);
                Canvas.SetTop(element, 0);
                _animOverlay.Children.Add(element);
            },
            _animationTargets,
            _slideCanvas.SuppressedShapeIds);

        // DA1: trigger a repaint so the suppressed shapes are hidden from the base canvas.
        _slideCanvas.Refresh();
    }

    private static Control CreateAnimationOverlayElement(
        RenderTargetBitmap bitmap,
        double width,
        double height,
        SlideShowAnimationOverlayElementPlan plan)
    {
        if (plan.UsesOpacityMask)
        {
            return new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Colors.Transparent),
                Opacity = plan.InitialOpacity,
                OpacityMask = new ImageBrush(bitmap) { Stretch = Stretch.None },
                IsHitTestVisible = false,
            };
        }

        return new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.None,
            Opacity = plan.InitialOpacity,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// DA1: Called when a build step has finished animating a shape in.
    /// Removes the shape from the suppressed set so the base canvas renders it permanently,
    /// matching PowerPoint's behaviour where the shape is visible after its build completes.
    /// </summary>
    private void RevealShape(uint shapeId)
    {
        if (!_animationTargets.CanRevealBase(shapeId))
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
        var rendererPlan = _runtime.AnimationRendererSession.PlanStep(
            step,
            CurrentPresentationSlideIndex,
            _slideDipW,
            _slideDipH,
            BuildAnimationTargetAvailability(),
            _runtime.DisplaySlide?.ColorMapOverride);
        _runtime.AnimationRendererSession.ExecuteStep(
            rendererPlan,
            ResolveAnimationTarget,
            PlayFallbackAnimation,
            (element, operation) =>
            {
                element.Opacity = 1;
                _slideCanvas.SuppressedShapeIds.Add(operation.ShapeId);
                _slideCanvas.Refresh();
            },
            (element, operation) => PlayShapeAnimationWithTiming(
                element,
                operation.Playback,
                operation.RevealBaseUsingPlaybackTiming
                    ? () => RevealShape(operation.ShapeId)
                    : null));
    }

    private SlideShowAnimationPlaybackTargetAvailability BuildAnimationTargetAvailability() =>
        _animationTargets.BuildAvailability();

    private Control? ResolveAnimationTarget(SlideShowAnimationPlaybackOperation operation) =>
        _animationTargets.Resolve(operation);

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
        var repeat = SlideShowAnimationStepRendererPlanner.BuildRepeatPlan(plan);
        PlayShapeAnimationPass(element, plan, onReveal, repeat.PassCount, 0);
    }

    private void PlayShapeAnimationPass(
        Control element,
        SlideShowShapeAnimationPlaybackPlan basePlan,
        Action? onReveal,
        int? passCount,
        int passIndex)
    {
        var isFinalPass = passCount is int count && passIndex >= count - 1;
        var passPlan = _runtime.AnimationRendererSession.PlanRepeatPass(
            basePlan,
            passIndex,
            _runtime.DisplaySlide?.ColorMapOverride);

        PlayShapeAnimation(element, passPlan, isFinalPass ? onReveal : null);

        if (!isFinalPass)
        {
            var nextPassDelay = passPlan.DelayMs + passPlan.DurationMs;
            DelayedAction(nextPassDelay, () =>
                PlayShapeAnimationPass(element, basePlan, onReveal, passCount, passIndex + 1));
        }
    }

    private void PlayShapeAnimation(Control element, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        _runtime.AnimationRendererSession.PlanFrame(plan, 0, _slideDipW, _slideDipH);
        var route = SlideShowAnimationRendererRoutePlanner.Build(plan);

        if (route.Kind == SlideShowAnimationRendererRouteKind.MotionPath)
        {
            MotionPathEffect(element, plan, onReveal);
            return;
        }

        switch (route.Kind)
        {
            case SlideShowAnimationRendererRouteKind.Instant:
                if (route.InstantVisibility == SlideShowAnimationInstantVisibilityKind.Hide)
                    DisappearEffect(element, plan.DelayMs);
                else
                    AppearEffect(element, plan.DelayMs, CompleteReveal(plan, onReveal));
                break;

            case SlideShowAnimationRendererRouteKind.Opacity:
                FadeEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.Fly:
                FlyInEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.WipeMask:
                WipeEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.SplitMask:
                SplitEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.RandomBarsMask:
                RandomBarsEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.BlindsMask:
                BlindsEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.BoxMask:
                BoxEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.CheckerboardMask:
                CheckerboardEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.GeometricMask:
                GeometricMaskEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.DissolveMask:
                DissolveEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.Flash:
                FlashEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.ScalarTrack:
                InvokeRevealAtStart(plan, onReveal);
                ScalarTrackEffect(element, plan);
                break;

            case SlideShowAnimationRendererRouteKind.Trajectory:
                TrajectoryEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.Peek:
                PeekEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.Crawl:
                CrawlEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.Zoom:
                ZoomEffect(element, plan, onReveal);
                break;

            case SlideShowAnimationRendererRouteKind.TextStyle:
                FontStyleEffect(element, plan);
                break;

            case SlideShowAnimationRendererRouteKind.FontSize:
                FontSizeEffect(element, plan);
                break;

            case SlideShowAnimationRendererRouteKind.LineColor:
                LineColorEffect(element, plan);
                break;

            case SlideShowAnimationRendererRouteKind.FillColor:
                FillColorEffect(element, plan);
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
                onComplete: CompleteReveal(plan, onReveal)));
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
                        onComplete: CompleteReveal(plan, onReveal)))));
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
                reverse: isExit));
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
                isExit ? dx : 0, isExit ? dy : 0, plan.DurationMs);
            // Reveal in base canvas when the fade-in completes.
            AnimateOpacity(
                el,
                plan.FromOpacity,
                plan.ToOpacity,
                plan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal));
        });
    }

    private void TrajectoryEffect(
        Control element,
        SlideShowShapeAnimationPlaybackPlan playback,
        Action? onReveal = null)
    {
        if (playback.EffectKind == SlideShowShapeAnimationEffectKind.Bounce)
            InvokeRevealAtStart(playback, onReveal);

        double width = _slideCanvas.Bounds.Width > 0 ? _slideCanvas.Bounds.Width : 960;
        double height = _slideCanvas.Bounds.Height > 0 ? _slideCanvas.Bounds.Height : 540;
        var trajectory = SlideShowAnimationEffectFramePlanner.Build(
            playback.EffectKind,
            playback.Animation.Kind,
            playback.OffsetXFactor,
            playback.OffsetYFactor);
        var translate = new TranslateTransform(
            trajectory.Start.NormalizedX * width,
            trajectory.Start.NormalizedY * height);
        element.RenderTransform = translate;

        DelayedAction(playback.DelayMs, () =>
        {
            AnimateOpacity(element, playback.FromOpacity, playback.ToOpacity, playback.DurationMs);
            AnimateTrajectory(
                translate,
                trajectory,
                width,
                height,
                playback.DurationMs,
                CompleteReveal(playback, onReveal));
        });
    }

    private void AnimateTrajectory(
        TranslateTransform translate,
        SlideShowAnimationEffectFramePlan trajectory,
        double width,
        double height,
        int durationMs,
        Action? onComplete)
    {
        if (durationMs <= 0)
        {
            translate.X = trajectory.End.NormalizedX * width;
            translate.Y = trajectory.End.NormalizedY * height;
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
            var progress = Math.Min(1.0, (double)frame / steps);
            var point = SlideShowAnimationEffectFramePlanner.SampleSmooth(trajectory, progress);
            translate.X = point.NormalizedX * width;
            translate.Y = point.NormalizedY * height;
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                translate.X = trajectory.End.NormalizedX * width;
                translate.Y = trajectory.End.NormalizedY * height;
                onComplete?.Invoke();
            }
        };
        timer.Start();
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
                onComplete: CompleteReveal(plan, onReveal)));
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
                    onComplete: CompleteReveal(plan, onReveal)));
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
                    onComplete: CompleteReveal(plan, onReveal)));
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
                endProgress: toProgress));
    }

    private void RandomBarsEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var bars = new GeometryGroup();
        var animatedBars = new List<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)>();
        var rendererPlan = SlideShowMaskTimelinePlanner.BuildRandomBarsRendererPlan(plan, w, h);

        foreach (var elementPlan in rendererPlan.Elements)
        {
            var from = ToRect(elementPlan.From);
            var to = ToRect(elementPlan.To);
            var bar = new RectangleGeometry(from);
            bars.Children.Add(bar);
            animatedBars.Add((
                bar,
                from,
                to,
                elementPlan.StartOffsetMs,
                elementPlan.DurationMs));
        }

        el.Clip = bars;
        el.Opacity = rendererPlan.InitialOpacity;

        DelayedAction(rendererPlan.DelayMs, () =>
            AnimateRandomBarsClip(
                animatedBars,
                el,
                rendererPlan,
                onComplete: CompleteReveal(plan, onReveal)));
    }

    private void AnimateRandomBarsClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)> bars,
        Control opacityTarget,
        SlideShowRectMaskRendererPlan rendererPlan,
        Action? onComplete = null)
    {
        var durationMs = rendererPlan.DurationMs;
        if (durationMs <= 0)
        {
            foreach (var (geometry, _, to, _, _) in bars)
                geometry.Rect = to;
            opacityTarget.Opacity = SlideShowMaskTimelinePlanner.SampleOpacity(rendererPlan, 1);
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
                var eased = EaseInOut(t);
                geometry.Rect = new Rect(
                    from.X + (to.X - from.X) * eased,
                    from.Y + (to.Y - from.Y) * eased,
                    from.Width + (to.Width - from.Width) * eased,
                    from.Height + (to.Height - from.Height) * eased);
            }
            opacityTarget.Opacity = SlideShowMaskTimelinePlanner.SampleOpacity(
                rendererPlan,
                (double)elapsedMs / durationMs);

            if (elapsedMs >= durationMs)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                foreach (var (geometry, _, to, _, _) in bars)
                    geometry.Rect = to;
                opacityTarget.Opacity = SlideShowMaskTimelinePlanner.SampleOpacity(rendererPlan, 1);
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void BlindsEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var rendererPlan = SlideShowMaskTimelinePlanner.BuildBlindsRendererPlan(plan, w, h);
        var bands = new GeometryGroup();
        var animatedBands = new List<(RectangleGeometry Geometry, Rect From, Rect To)>(
            rendererPlan.Elements.Count);

        foreach (var elementPlan in rendererPlan.Elements)
        {
            var from = ToRect(elementPlan.From);
            var to = ToRect(elementPlan.To);
            var band = new RectangleGeometry(from);
            bands.Children.Add(band);
            animatedBands.Add((band, from, to));
        }

        el.Clip = bands;
        el.Opacity = rendererPlan.InitialOpacity;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(rendererPlan.DelayMs, () =>
            AnimateBlindsClip(
                animatedBands,
                rendererPlan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal)));
    }

    private void CheckerboardEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var rendererPlan = SlideShowMaskTimelinePlanner.BuildCheckerboardRendererPlan(plan, w, h);
        var cells = new GeometryGroup();
        var animatedCells = new List<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)>(
            rendererPlan.Elements.Count);

        foreach (var elementPlan in rendererPlan.Elements)
        {
            var from = ToRect(elementPlan.From);
            var to = ToRect(elementPlan.To);
            var cell = new RectangleGeometry(from);
            cells.Children.Add(cell);
            animatedCells.Add((
                cell,
                from,
                to,
                elementPlan.StartOffsetMs,
                elementPlan.DurationMs));
        }

        el.Clip = cells;
        el.Opacity = rendererPlan.InitialOpacity;
        InvokeRevealAtStart(plan, onReveal);

        DelayedAction(rendererPlan.DelayMs, () =>
            AnimateCheckerboardClip(
                animatedCells,
                rendererPlan.DurationMs,
                onComplete: CompleteReveal(plan, onReveal)));
    }

    private void AnimateCheckerboardClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)> cells,
        int durationMs,
        Action? onComplete = null)
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
                var e = EaseInOut(t);
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
        Action? onComplete = null)
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
            double e = EaseInOut(t);
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
                onComplete: CompleteReveal(plan, onReveal)));
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
                onComplete: CompleteReveal(plan, onReveal)));
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
        Action? onComplete = null)
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
            double eased = EaseInOut(t);
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
        Rect from, Rect to, int durationMs, Action? onComplete = null)
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
            double e = EaseInOut(t);
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
                onComplete: CompleteReveal(plan, onReveal));
            AnimateScale(el, scale, plan.FromScale, plan.ToScale, plan.DurationMs);
        });
    }

    private void AnimateScale(Control target, ScaleTransform scale,
        double from, double to, int durationMs) =>
        AnimateScaleAxes(scale, from, from, to, to, durationMs);

    private void AnimateScaleAxes(ScaleTransform scale,
        double fromX, double fromY, double toX, double toY, int durationMs)
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
            double eased = EaseInOut(t);
            scale.ScaleX = fromX + (toX - fromX) * eased;
            scale.ScaleY = fromY + (toY - fromY) * eased;
            if (frame >= steps) { timer.Stop(); _activeTimers.Remove(timer); scale.ScaleX = toX; scale.ScaleY = toY; }
        };
        timer.Start();
    }

    private void ScalarTrackEffect(
        Control element,
        SlideShowShapeAnimationPlaybackPlan playback)
    {
        var plan = _runtime.AnimationRendererSession.PlanEffectTracks(playback);
        element.Opacity = 1;
        element.RenderTransformOrigin = RelativePoint.Center;

        var rotationTrack = plan.FindTrack(SlideShowAnimationScalarPropertyKind.RotationDegrees);
        var horizontalScaleTrack = plan.FindTrack(SlideShowAnimationScalarPropertyKind.HorizontalScale);
        var scaleXTrack = plan.FindTrack(SlideShowAnimationScalarPropertyKind.ScaleX);
        var scaleYTrack = plan.FindTrack(SlideShowAnimationScalarPropertyKind.ScaleY);
        var translateTrack = plan.FindTrack(SlideShowAnimationScalarPropertyKind.TranslateXFactor);
        var matrixTransform = rotationTrack is not null && horizontalScaleTrack is not null
            ? new MatrixTransform(Matrix.Identity)
            : null;
        var rotateTransform = rotationTrack is not null && matrixTransform is null
            ? new RotateTransform()
            : null;
        var scaleTransform = scaleXTrack is not null && scaleYTrack is not null
            ? new ScaleTransform()
            : null;
        var translateTransform = translateTrack is not null
            ? new TranslateTransform()
            : null;

        element.RenderTransform = (Transform?)matrixTransform
            ?? (Transform?)rotateTransform
            ?? (Transform?)scaleTransform
            ?? (Transform?)translateTransform;

        void Apply(double progress)
        {
            var rotation = 0.0;
            var horizontalScale = 1.0;
            foreach (var track in plan.Tracks)
            {
                var value = SlideShowAnimationEffectTrackPlanner.Sample(track, progress);
                switch (track.PropertyKind)
                {
                    case SlideShowAnimationScalarPropertyKind.Opacity:
                        element.Opacity = value;
                        break;
                    case SlideShowAnimationScalarPropertyKind.ScaleX:
                        scaleTransform!.ScaleX = value;
                        break;
                    case SlideShowAnimationScalarPropertyKind.ScaleY:
                        scaleTransform!.ScaleY = value;
                        break;
                    case SlideShowAnimationScalarPropertyKind.RotationDegrees:
                        rotation = value;
                        if (rotateTransform is not null)
                            rotateTransform.Angle = value;
                        break;
                    case SlideShowAnimationScalarPropertyKind.HorizontalScale:
                        horizontalScale = value;
                        break;
                    case SlideShowAnimationScalarPropertyKind.TranslateXFactor:
                        translateTransform!.X = value * (_slideDipW > 0 ? _slideDipW : 960);
                        break;
                }
            }

            if (matrixTransform is not null)
            {
                matrixTransform.Matrix = Matrix.CreateScale(horizontalScale, 1)
                    * Matrix.CreateRotation(rotation * Math.PI / 180);
            }
        }

        Apply(0);
        if (plan.AddAuthoredColorOverlay)
            AddAuthoredColorOverlay(element, playback);
        DelayedAction(plan.DelayMs, () => AnimateScalarTracks(plan, Apply));
    }

    private void AnimateScalarTracks(
        SlideShowAnimationEffectTrackPlan plan,
        Action<double> apply)
    {
        if (plan.DurationMs <= 0)
        {
            apply(1);
            return;
        }

        var steps = SlideShowAnimationEffectTrackPlanner.ResolveTimerStepCount(plan.DurationMs);
        var frame = 0;
        var timer = TrackTimer(new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(
                SlideShowAnimationEffectTrackPlanner.TimerFrameIntervalMs)
        });
        timer.Tick += (_, _) =>
        {
            frame++;
            apply(Math.Min(1, frame / (double)steps));
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                apply(1);
            }
        };
        timer.Start();
    }

    private void FillColorEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        var track = SlideShowAnimationColorTrackPlanner.BuildFillColor(plan);
        if (element is not Rectangle rectangle
            || rectangle.Fill is not SolidColorBrush brush
            || track is null)
        {
            return;
        }

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateColorKeyframes(
                plan.DurationMs,
                track.Colors
                    .Select(keyFrame => (ToAnimationColor(keyFrame.Value), keyFrame.Progress))
                    .ToArray(),
                value => brush.Color = value);
            AnimateKeyframes(
                plan.DurationMs,
                track.Opacities
                    .Select(keyFrame => (keyFrame.Value, keyFrame.Progress))
                    .ToArray(),
                value => rectangle.Opacity = value);
        });
    }

    private void LineColorEffect(Control element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        element.Opacity = 0;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (0.0, 0.0), (1.0, 1.0) },
            value => element.Opacity = value));
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
        var track = SlideShowAnimationColorTrackPlanner.BuildAuthoredColorOverlay(plan);
        if (element is not Image image
            || image.Source is not Bitmap source
            || element.Parent is not Panel parent
            || track is null
            || track.Colors.Count == 0)
        {
            return;
        }

        var brush = new SolidColorBrush(ToAnimationColor(track.Colors[0].Value));
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
            AnimateColorKeyframes(
                plan.DurationMs,
                track.Colors
                    .Select(keyFrame => (ToAnimationColor(keyFrame.Value), keyFrame.Progress))
                    .ToArray(),
                value => brush.Color = value);
            AnimateKeyframes(
                plan.DurationMs,
                track.Opacities
                    .Select(keyFrame => (keyFrame.Value, keyFrame.Progress))
                    .ToArray(),
                value => tint.Opacity = value);
        });
    }

    private static Color ToAnimationColor(SrgbColor color) =>
        Color.FromRgb(color.R, color.G, color.B);

    private void AnimateColorKeyframes(
        int durationMs,
        IReadOnlyList<(Color Value, double Progress)> keyframes,
        Action<Color> apply)
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
                value = InterpolateAnimationColor(previous.Value, current.Value, EaseInOut(local));
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
        Action<double> apply)
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
                value = previous.Value + (current.Value - previous.Value) * EaseInOut(local);
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
    private void PlayFallbackAnimation(SlideShowAnimationPlaybackOperation operation)
    {
        var animation = operation.Playback.Animation;
        var visibilityPlan = operation.FallbackVisibility ??
            throw new InvalidOperationException("Fallback playback requires a visibility plan.");
        if (visibilityPlan.SuppressAtStart)
        {
            _slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);
            _slideCanvas.Refresh();
        }

        if (visibilityPlan.SuppressAtStart || visibilityPlan.SuppressAtCompletion)
        {
            DelayedAction(
                operation.Playback.DelayMs + operation.Playback.DurationMs,
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

        PlayFallbackAnimation(operation.FallbackAnimation);
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
        => _runtime.CloseRendererSession(nowUtc);

    /// <summary>Expose active-timer count for test assertions (DA2/DA3).</summary>
    internal int ActiveTimerCount => _activeTimers.Count;
}
