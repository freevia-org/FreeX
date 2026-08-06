using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Borderless fullscreen window that plays a FreeP presentation as a slide show.
///
/// Rendering model
/// ───────────────
/// The window contains a black <see cref="Grid"/> that letter-boxes the slide content.
/// We layer two <see cref="SlideCanvas"/> instances ("back" + "front") for cross-fade
/// and directional transitions, and an animation overlay <see cref="Canvas"/> where per-shape
/// entrance/emphasis/exit effects run as WPF Storyboards.
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
/// a WPF Storyboard. The per-shape visuals come from dedicated UIElement overlays rendered
/// via RenderTargetBitmap (one per shape) so we can animate them individually without
/// decomposing SlideCanvas's OnRender internals.
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
/// </summary>
public sealed class SlideShowWindow : Window, ISlideShowTransitionPlaybackRenderer, ISlideShowDisplayRenderer
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly Presentation    _presentation;
    private readonly SlideShowRuntimeApplication _runtime;
    private readonly Action<int, string?>? _setSlideNotesText;
    private readonly DispatcherTimer  _autoAdvanceTimer;
    private readonly DispatcherTimer  _kioskRestartTimer;
    private long _autoAdvanceDisplayVersion;
    private PresenterViewWindow? _presenterViewWindow;
    private bool _zoomShowBackgroundForTransition = true;
    private SlideShowShapeAnimationVisualFramePlan? _lastAnimationFramePlan;
    private IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> _lastAnimationStepFrameEvidence = Array.Empty<SlideShowAnimationStepVisualCheckpointPlan>();
    private SlideShowAnimationStepPlaybackReadinessPlan? _lastAnimationStepPlaybackReadinessPlan;

    // ── Visual tree ───────────────────────────────────────────────────────────────

    // Root: black grid filling the whole window.
    private readonly Grid _root;

    // Transition layers: back (snapshot of outgoing slide), front (incoming slide canvas).
    private readonly Image      _transitionBackImage; // snapshot bitmap of outgoing slide
    private readonly SlideCanvas _slideCanvas;         // live rendered current slide (front layer)

    // Shape animation overlay: a Canvas placed on top of _slideCanvas; populated per-slide.
    private readonly Canvas _animOverlay;

    // Media playback overlay: a Canvas layered above _animOverlay; populated per-slide.
    private readonly Canvas _mediaOverlay;

    // Presenter ink overlay: shared-plan-backed strokes and laser pointer above slide content.
    private readonly Canvas _inkOverlay;
    private readonly Rectangle _transitionFlashOverlay;
    private readonly Rectangle _screenModeOverlay;

    // Manages MediaElement lifecycle for the current slide's media shapes.
    private readonly SlideShowMediaController _mediaController;

    // Per-shape animation state for the current slide.
    // Maps shapeId → the Image element in _animOverlay that represents that shape.
    private readonly Dictionary<uint, FrameworkElement> _animElements = new();
    private readonly Dictionary<uint, FrameworkElement> _animFillElements = new();
    private readonly Dictionary<uint, FrameworkElement> _animLineElements = new();
    private readonly Dictionary<uint, FrameworkElement> _animFontStyleElements = new();
    private readonly Dictionary<uint, FrameworkElement> _animFontSizeElements = new();
    private readonly Dictionary<uint, IReadOnlyList<FrameworkElement>> _paragraphAnimElements = new();

    // Track which shapes have been revealed so the live canvas can hide/show correctly.
    private readonly HashSet<uint> _revealedShapes = new();
    private List<uint> _entranceShapeIds = new(); // shapes with Entrance animations on current slide

    // Current slide dimensions in DIP (computed once when the slide is displayed).
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

        // Pre-compute slide DIP dimensions so HitTestHyperlink works even before the first
        // DisplayCurrentSlide call (e.g. in unit tests that construct but don't show the window).
        var metrics = _runtime.InitialSlideMetrics;
        _slideDipW = metrics.WidthDip;
        _slideDipH = metrics.HeightDip;

        // PowerPoint's speaker and kiosk modes use a borderless presentation window;
        // individual browsing remains a normal, resizable window for document-style review.
        var windowPlan = _runtime.WindowPlan;
        var isBrowseWindow = windowPlan.IsBrowseWindow;
        WindowStyle  = windowPlan.IsBorderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowState  = isBrowseWindow ? WindowState.Normal : WindowState.Maximized;
        Topmost      = windowPlan.IsTopmost;
        if (isBrowseWindow)
        {
            Width = Math.Min(1024, SystemParameters.WorkArea.Width * 0.85);
            Height = Math.Min(768, SystemParameters.WorkArea.Height * 0.85);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Background   = Brushes.Black;
        Focusable    = true;
        ResizeMode   = windowPlan.AllowsResize ? ResizeMode.CanResize : ResizeMode.NoResize;

        // ── Visual tree ────────────────────────────────────────────────────────

        _root = new Grid { Background = Brushes.Black };

        // Back layer: snapshot image for transition outgoing state
        _transitionBackImage = new Image
        {
            Stretch           = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Visibility        = Visibility.Collapsed,
        };
        _root.Children.Add(_transitionBackImage);

        // Front layer: the live SlideCanvas
        _slideCanvas = new SlideCanvas
        {
            Presentation      = _presentation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        // Wrap canvas + anim overlay in a Grid so they share the same coordinate space
        var stage = new Grid();
        stage.Children.Add(_slideCanvas);

        _animOverlay = new Canvas
        {
            IsHitTestVisible  = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        stage.Children.Add(_animOverlay);

        // Media overlay: sits above anim overlay so MediaElements are on top.
        // IsHitTestVisible=false here; we do our own hit-testing in the click handler.
        _mediaOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        stage.Children.Add(_mediaOverlay);

        _inkOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        stage.Children.Add(_inkOverlay);

        _transitionFlashOverlay = new Rectangle
        {
            Fill = Brushes.White,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        stage.Children.Add(_transitionFlashOverlay);

        _screenModeOverlay = new Rectangle
        {
            Fill = Brushes.Black,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetZIndex(_screenModeOverlay, 4);
        stage.Children.Add(_screenModeOverlay);

        // Media controller: created now; EnterSlide is called per-slide in DisplayCurrentSlide.
        _mediaController = new SlideShowMediaController(_mediaOverlay);

        if (isBrowseWindow)
        {
            stage.Width = _slideDipW;
            stage.Height = _slideDipH;
            var browser = new ScrollViewer
            {
                Background = Brushes.Black,
                HorizontalScrollBarVisibility = windowPlan.ShowBrowseScrollbars
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = windowPlan.ShowBrowseScrollbars
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled,
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
            IsEnabled = false
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
            StopTransitionAudio: StopTransitionSound,
            TeardownMedia: _mediaController.Teardown),
            this);

        // ── Event wiring ───────────────────────────────────────────────────────
        KeyDown              += OnKeyDown;
        MouseLeftButtonDown  += OnMouseLeftButtonDown;
        MouseLeftButtonUp    += OnMouseLeftButtonUp;
        MouseMove            += OnMouseMove;
        SizeChanged          += (_, _) => SyncMediaOverlayLayout();
        Loaded               += (_, _) =>
        {
            Focus();
            DisplayCurrentSlide(animated: false);
            _runtime.StartRendererSession();
        };
        Closed               += (_, _) => Teardown();
    }

    // ── Public API (callable by test code without showing the window) ─────────────

    /// <summary>
    /// Execute a single logical advance step and return what happened.
    /// Drives the state machine and applies visual effects if the window is loaded.
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
        _screenModeOverlay.Visibility = plan.IsBlank
            ? Visibility.Visible
            : Visibility.Collapsed;
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
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest => _lastAnimationFramePlan;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest => _lastAnimationStepFrameEvidence;
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest => _lastAnimationStepPlaybackReadinessPlan;
    internal SlideShowPlaybackRoute PlaybackRoute => _runtime.PlaybackRoute;
    internal int CurrentPresentationSlideIndex => _runtime.CurrentPresentationSlideIndex;

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
        window.Owner = this;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_presenterViewWindow, window))
            {
                _presenterViewWindow = null;
                _runtime.NotifyPresenterViewClosed();
            }
        };
        window.Show();
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

    private static ISlideShowRecordingCaptureBackend CreateDefaultRecordingCaptureBackend() =>
        new WindowsRecordingCaptureBackend(
            new WindowsRecordingHostMetadata(
                "WPF slideshow",
                "WPF Windows recording capture adapter",
                "ppt/media/freep-recordings/wpf"),
            new WindowsNativeRecordingDeviceCatalog(),
            new WindowsNativeRecordingCaptureEngine("WPF Windows recording capture adapter"));

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = _runtime.HandleKeyboardInput(
            e.Key.ToString(),
            controlPressed: (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control);
    }

    // ── Navigation helpers ────────────────────────────────────────────────────────

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slide = _runtime.DisplaySlide;
        var clickPt = e.GetPosition(_slideCanvas);
        var inkResult = BeginPresenterInkStroke(clickPt.X, clickPt.Y);
        if (inkResult.IsHandled)
        {
            e.Handled = true;
            return;
        }

        // Check if the click lands on a media shape — toggle play/pause and consume the click
        // so it does NOT also advance the slideshow.
        if (slide is not null && SlideShapeTraversal.EnumerateDepthFirst(slide).Any(s => s.Kind == SlideShapeKind.Media))
        {
            double cw = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
            double ch = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
            if (_mediaController.TryHandleClick(clickPt.X, clickPt.Y, slide, cw, ch))
            {
                e.Handled = true;
                return;
            }
        }

        e.Handled = _runtime.HandlePointerInput(CreateCanvasPointer(clickPt.X, clickPt.Y));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_slideCanvas);
        var inkResult = EndPresenterInkStroke(pt.X, pt.Y);
        e.Handled = inkResult.IsHandled;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var slide = _runtime.DisplaySlide;
        if (slide is null) { Cursor = Cursors.Arrow; return; }
        var pt = e.GetPosition(_slideCanvas);
        if (e.LeftButton == MouseButtonState.Pressed)
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
        Cursor = hlink is not null ? Cursors.Hand : CursorForPresenterInk();
    }

    // ── Hyperlink hit-testing & activation ─────────────────────────────────────────

    /// <summary>
    /// Hit-tests the click point against shapes that carry a hyperlink.
    /// Returns the first matching hyperlink, or null.
    /// Run-precise hit-testing for run-level links is approximated to the containing shape (v1).
    /// Recurses into group children (BB2 fix) so hyperlinks on grouped shapes are reachable.
    /// </summary>
    internal Hyperlink? HitTestHyperlink(Slide slide, double canvasX, double canvasY)
        => _runtime.HitTestHyperlink(slide, CreateCanvasPointer(canvasX, canvasY));

    /// <summary>
    /// Activates a hyperlink: external → open URL or local file;
    /// internal → navigate the controller to the target slide.
    /// </summary>
    internal void ActivateHyperlink(Hyperlink hlink)
        => _runtime.ActivateHyperlink(hlink);

    /// <summary>
    /// Opens an external URL in the default browser through the shared URI allowlist.
    /// Blocked schemes and launch failures are silently ignored so a bad slideshow link never crashes playback.
    /// </summary>
    internal static void OpenExternalUrl(string url)
    {
        ExternalUriLauncher.Open(
            url,
            uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            }));
    }

    private SlideShowCanvasPointer CreateCanvasPointer(double canvasX, double canvasY) =>
        new(
            canvasX,
            canvasY,
            _slideCanvas.ActualWidth,
            _slideCanvas.ActualHeight,
            CurrentSlideMetrics());

    private void RefreshInkOverlay()
    {
        _inkOverlay.Children.Clear();

        var canvasWidth = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : _slideDipW;
        var canvasHeight = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
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

        var polyline = new System.Windows.Shapes.Polyline
        {
            Stroke = InkBrush(primitive.InkState),
            StrokeThickness = primitive.StrokeThicknessDip,
            StrokeStartLineCap = primitive.UseRoundLineCaps ? PenLineCap.Round : PenLineCap.Flat,
            StrokeEndLineCap = primitive.UseRoundLineCaps ? PenLineCap.Round : PenLineCap.Flat,
            StrokeLineJoin = primitive.UseRoundLineJoin ? PenLineJoin.Round : PenLineJoin.Miter,
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

        var dot = new System.Windows.Shapes.Ellipse
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

    private static Brush InkBrush(SlideShowInkState inkState)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(inkState.ColorHex));
        }
        catch (FormatException)
        {
            return Brushes.Red;
        }
    }

    private static Brush InkOutlineBrush(SlideShowInkOverlayPrimitive primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive.OutlineColorHex))
        {
            return Brushes.Transparent;
        }

        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(primitive.OutlineColorHex));
        }
        catch (FormatException)
        {
            return Brushes.White;
        }
    }

    private Cursor CursorForPresenterInk() =>
        _runtime.InkExecutionState.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter => Cursors.Pen,
            SlideShowPresenterPointerMode.Eraser => Cursors.Cross,
            _ => Cursors.Arrow
        };

    private void CloseSlideShow(DateTimeOffset nowUtc)
    {
        Teardown(nowUtc);
        Close();
    }

    // ── Slide display + transitions ───────────────────────────────────────────────

    /// <summary>
    /// Renders the controller's current slide with the optional entry transition.
    /// When <paramref name="animated"/> is false (first display, Home/End, Back), skip the transition.
    /// </summary>
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
        double mediaCanvasW = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
        double mediaCanvasH = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        _mediaController.EnterSlide(
            plan.Slide!,
            _slideDipW,
            _slideDipH,
            mediaCanvasW,
            mediaCanvasH,
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

    void ISlideShowDisplayRenderer.CancelVisualOperations()
    {
        foreach (var storyboard in _pendingStoryboards)
        {
            try { storyboard.Stop(); } catch { /* ignore */ }
        }
        _pendingStoryboards.Clear();
    }

    void ISlideShowDisplayRenderer.RefreshInkOverlay() => RefreshInkOverlay();

    void ISlideShowDisplayRenderer.PrepareAnimationOverlay(Slide slide) =>
        PrepareAnimationOverlay(slide);

    void ISlideShowDisplayRenderer.PlayTransition(Slide slide, SlideTransition transition) =>
        PlayTransition(slide, transition);

    void ISlideShowDisplayRenderer.ShowSlideInstant(Slide slide) => ShowSlideInstant(slide);

    private SlideShowSlideMetrics CurrentSlideMetrics() => new(_slideDipW, _slideDipH);

    private void SyncMediaOverlayLayout()
    {
        var slide = _runtime.DisplaySlide;
        if (slide is null)
            return;

        var width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : _slideDipW;
        var height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        _mediaController.UpdateLayout(slide, width, height);
    }

    /// <summary>Instantly shows a slide without any transition animation.</summary>
    private void ShowSlideInstant(Slide slide)
    {
        _transitionBackImage.Visibility = Visibility.Collapsed;
        _transitionBackImage.Clip = null;
        _transitionBackImage.RenderTransform = Transform.Identity;
        _transitionBackImage.BeginAnimation(UIElement.OpacityProperty, null);
        Grid.SetZIndex(_transitionBackImage, 0);
        _transitionFlashOverlay.Visibility = Visibility.Collapsed;
        _transitionFlashOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        _transitionFlashOverlay.Opacity = 0;
        Grid.SetZIndex(_transitionFlashOverlay, 0);
        Grid.SetZIndex(_slideCanvas, 0);
        _slideCanvas.RenderSlideBackground = true;
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Clip = null;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Refresh();
    }

    /// <summary>
    /// Captures the currently displayed slide as a bitmap snapshot for transition use.
    /// Returns null if the canvas has no valid size (e.g., not yet loaded).
    /// </summary>
    private BitmapSource? CaptureCurrentSlide()
    {
        // Measure/arrange at the window's available size so we get a valid ActualWidth/Height.
        var available = new Size(ActualWidth > 0 ? ActualWidth : 1280, ActualHeight > 0 ? ActualHeight : 720);
        _slideCanvas.Measure(available);
        _slideCanvas.Arrange(new Rect(available));
        _slideCanvas.UpdateLayout();

        double w = _slideCanvas.ActualWidth;
        double h = _slideCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return null;

        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(_slideCanvas);
        rtb.Freeze();
        return rtb;
    }

    // ── Transition effects ────────────────────────────────────────────────────────

    private void PlayTransition(Slide slide, SlideTransition t)
    {
        SlideShowTransitionPlaybackCoordinator.Play(_presentation, slide, t, this);
    }

    void ISlideShowTransitionPlaybackRenderer.PlayTransitionSound(SlideTransition transition) =>
        PlayTransitionSound(transition);

    void ISlideShowTransitionPlaybackRenderer.ResetTransitionVisuals()
    {
        _transitionBackImage.Visibility = Visibility.Collapsed;
        _transitionBackImage.Clip = null;
        _transitionBackImage.RenderTransform = Transform.Identity;
        _transitionBackImage.BeginAnimation(UIElement.OpacityProperty, null);
        Grid.SetZIndex(_transitionBackImage, 0);
        _transitionFlashOverlay.Visibility = Visibility.Collapsed;
        _transitionFlashOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        _transitionFlashOverlay.Opacity = 0;
        Grid.SetZIndex(_transitionFlashOverlay, 0);
        Grid.SetZIndex(_slideCanvas, 0);
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

    // ── Transition sound playback ─────────────────────────────────────────────────

    private System.Windows.Media.MediaPlayer? _transitionSoundPlayer;
    private string? _transitionSoundTempPath;

    private void StopTransitionSound()
    {
        var player = _transitionSoundPlayer;
        _transitionSoundPlayer = null;
        try { player?.Stop(); player?.Close(); } catch { /* ignore */ }

        var path = _transitionSoundTempPath;
        _transitionSoundTempPath = null;
        if (path is not null)
            TransitionSoundTempFile.Delete(path);
    }

    /// <summary>
    /// Plays the transition sound (if any) using WPF MediaPlayer on a temp file.
    /// Fire-and-forget; errors are silently swallowed.
    /// </summary>
    private void PlayTransitionSound(SlideTransition t)
    {
        if (t.Sound?.AudioBytes is not { Length: > 0 }) return;

        try
        {
            // Stop any previous transition sound and release its owned file.
            StopTransitionSound();

            // Write audio to a temp file (MediaPlayer requires a URI/file path).
            var sound = t.Sound;
            var tmpPath = TransitionSoundTempFile.Write(sound.AudioBytes, sound.ContentType);
            _transitionSoundTempPath = tmpPath;

            var player = new System.Windows.Media.MediaPlayer();
            _transitionSoundPlayer = player;
            player.Open(new Uri(tmpPath, UriKind.Absolute));
            player.Play();

            // Restart looping transition sounds; otherwise clean up after playback.
            player.MediaEnded += (_, _) =>
            {
                if (sound.Loop)
                {
                    player.Position = TimeSpan.Zero;
                    player.Play();
                    return;
                }

                player.Close();
                if (ReferenceEquals(_transitionSoundPlayer, player))
                {
                    _transitionSoundPlayer = null;
                    if (_transitionSoundTempPath == tmpPath)
                        _transitionSoundTempPath = null;
                }
                TransitionSoundTempFile.Delete(tmpPath);
            };
        }
        catch
        {
            StopTransitionSound();
            // Never crash the slideshow over audio.
        }
    }

    private void PlayFadeTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();

        // Show the incoming slide underneath.
        _slideCanvas.Slide    = slide;
        _slideCanvas.Opacity  = 0;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source     = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        // Fade the incoming slide canvas from 0 → 1.
        var anim = new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(durationMs)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        anim.Completed += (_, _) =>
        {
            _transitionBackImage.Visibility = Visibility.Collapsed;
            _slideCanvas.Opacity = 1;
        };
        _slideCanvas.BeginAnimation(OpacityProperty, anim);
    }

    private void PlayFlashTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();

        // Keep the incoming slide below the outgoing snapshot and peak a white
        // surface once between them. This makes Flash distinct from Fade while
        // remaining deterministic for both slideshow hosts.
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Refresh();
        Grid.SetZIndex(_slideCanvas, 1);

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
            _transitionBackImage.Opacity = 1;
            Grid.SetZIndex(_transitionBackImage, 2);
        }

        _transitionFlashOverlay.Visibility = Visibility.Visible;
        _transitionFlashOverlay.Opacity = 0;
        Grid.SetZIndex(_transitionFlashOverlay, 3);

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var outgoing = new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var flash = new DoubleAnimationUsingKeyFrames();
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        flash.KeyFrames.Add(new SplineDoubleKeyFrame(
            1, KeyTime.FromPercent(0.45), new KeySpline(0.2, 0, 0.8, 1)));
        flash.KeyFrames.Add(new SplineDoubleKeyFrame(
            0, KeyTime.FromPercent(1), new KeySpline(0.2, 0, 0.8, 1)));
        flash.Duration = duration;
        flash.Completed += (_, _) =>
        {
            _transitionBackImage.Visibility = Visibility.Collapsed;
            _transitionBackImage.Opacity = 1;
            Grid.SetZIndex(_transitionBackImage, 0);
            _transitionFlashOverlay.Visibility = Visibility.Collapsed;
            _transitionFlashOverlay.Opacity = 0;
            Grid.SetZIndex(_transitionFlashOverlay, 0);
            Grid.SetZIndex(_slideCanvas, 0);
        };

        if (snapshot is not null)
            _transitionBackImage.BeginAnimation(UIElement.OpacityProperty, outgoing);
        _transitionFlashOverlay.BeginAnimation(UIElement.OpacityProperty, flash);
    }

    private void PlayCoverTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();

        var dx = plan.IncomingOffsetX;
        var dy = plan.IncomingOffsetY;

        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        // Start the incoming slide off-screen, shifted in the incoming direction.
        var incomingTranslate = new TranslateTransform(dx * w, dy * h);
        _slideCanvas.RenderTransform = incomingTranslate;
        _slideCanvas.Slide   = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source     = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease     = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Animate incoming slide from off-screen to center.
        var animX = new DoubleAnimation(dx * w, 0, duration) { EasingFunction = ease };
        var animY = new DoubleAnimation(dy * h, 0, duration) { EasingFunction = ease };

        animX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };

        incomingTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
        incomingTranslate.BeginAnimation(TranslateTransform.YProperty, animY);
    }

    private void PlayPushTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var dx = plan.IncomingOffsetX;
        var dy = plan.IncomingOffsetY;
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        var incomingTranslate = new TranslateTransform(dx * w, dy * h);
        _slideCanvas.RenderTransform = incomingTranslate;
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.Refresh();

        TranslateTransform? outgoingTranslate = null;
        if (snapshot is not null)
        {
            outgoingTranslate = new TranslateTransform(0, 0);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransform = outgoingTranslate;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var incomingX = new DoubleAnimation(dx * w, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var incomingY = new DoubleAnimation(dy * h, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        incomingX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };

        incomingTranslate.BeginAnimation(TranslateTransform.XProperty, incomingX);
        incomingTranslate.BeginAnimation(TranslateTransform.YProperty, incomingY);

        if (outgoingTranslate is not null)
        {
            outgoingTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, dx * w, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
            outgoingTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, dy * h, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
        }
    }

    private void PlayDissolveTransition(Slide slide, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildDissolveTransitionGeometry(w, h, 0);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildDissolveTransitionGeometry(w, h, progress),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildDissolveTransitionGeometry(
        double width,
        double height,
        double progress)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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

    private void PlayHoneycombTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var honeycomb = SlideShowHoneycombTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildHoneycombTransitionGeometry(w, h, 0, honeycomb);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildHoneycombTransitionGeometry(w, h, progress, honeycomb),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayGlitterTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var glitter = SlideShowGlitterTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildGlitterTransitionGeometry(w, h, 0, glitter);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildGlitterTransitionGeometry(w, h, progress, glitter),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayRippleTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var ripple = SlideShowRippleTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildRippleTransitionGeometry(w, h, 0, ripple);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildRippleTransitionGeometry(w, h, progress, ripple),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayWindTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var wind = SlideShowWindTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildWindTransitionGeometry(w, h, 0, wind);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWindTransitionGeometry(w, h, progress, wind),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayCurtainsTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var curtains = SlideShowCurtainsTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildCurtainsTransitionGeometry(w, h, 0, curtains);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildCurtainsTransitionGeometry(w, h, progress, curtains),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayShredTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var shred = SlideShowShredTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildShredTransitionGeometry(w, h, 0, shred);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildShredTransitionGeometry(w, h, progress, shred),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayDrapeTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var drape = SlideShowDrapeTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildDrapeTransitionGeometry(w, h, 0, drape);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildDrapeTransitionGeometry(w, h, progress, drape),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayVortexTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var vortex = SlideShowVortexTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildVortexTransitionGeometry(w, h, 0, vortex);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildVortexTransitionGeometry(w, h, progress, vortex),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayWarpTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var warp = SlideShowWarpTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildWarpTransitionGeometry(w, h, 0, warp);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWarpTransitionGeometry(w, h, progress, warp),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayFractureTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var fracture = SlideShowFractureTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildFractureTransitionGeometry(w, h, 0, fracture);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildFractureTransitionGeometry(w, h, progress, fracture),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayCrushTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var crush = SlideShowCrushTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildCrushTransitionGeometry(w, h, 0, crush);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildCrushTransitionGeometry(w, h, progress, crush),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayPrismTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var prism = SlideShowPrismTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildPrismTransitionGeometry(w, h, 0, prism);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildPrismTransitionGeometry(w, h, progress, prism),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayPrestigeTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var prestige = SlideShowPrestigeTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildPrestigeTransitionGeometry(w, h, 0, prestige);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildPrestigeTransitionGeometry(w, h, progress, prestige),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildHoneycombTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowHoneycombTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var polygon in SlideShowHoneycombTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            var points = polygon.Points.Select(ToPoint).ToArray();
            if (points.Length == 0)
                continue;

            var cell = new StreamGeometry();
            using (var context = cell.Open())
            {
                context.BeginFigure(points[0], isFilled: true, isClosed: true);
                for (var index = 1; index < points.Length; index++)
                    context.LineTo(points[index], isStroked: true, isSmoothJoin: false);
            }

            geometry.Children.Add(cell);
        }

        return geometry;
    }

    private static Geometry BuildGlitterTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowGlitterTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var polygon in SlideShowGlitterTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildGlitterPolygon(polygon.Points));
        }

        return geometry;
    }

    private static PathGeometry BuildGlitterPolygon(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();
        if (points.Length == 0)
            return new PathGeometry();

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        for (var index = 1; index < points.Length; index++)
            figure.Segments.Add(new LineSegment(points[index], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Geometry BuildRippleTransitionGeometry(
        double width,
        double height,
        double progress,
        SlideShowRippleTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var polygon in SlideShowPrestigeTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static PathGeometry BuildRipplePolygon(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();
        if (points.Length == 0)
            return new PathGeometry();

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        for (var index = 1; index < points.Length; index++)
            figure.Segments.Add(new LineSegment(points[index], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void PlayPageCurlTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var curl = SlideShowPageCurlTransitionPlanner.Plan(transition);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = null;
        _slideCanvas.Refresh();

        if (snapshot is null)
            return;

        _transitionBackImage.Source = snapshot;
        _transitionBackImage.Visibility = Visibility.Visible;
        _transitionBackImage.Clip = BuildPageCurlGeometry(w, h, 0, curl);
        Grid.SetZIndex(_transitionBackImage, 1);

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildPageCurlGeometry(w, h, progress, curl),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _transitionBackImage.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_transitionBackImage, 0);
        };
        Storyboard.SetTarget(animation, _transitionBackImage);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildPageCurlGeometry(
        double width,
        double height,
        double progress,
        SlideShowPageCurlTransitionPlan plan)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var polygon in SlideShowPageCurlTransitionPlanner.BuildPolygons(
                     width, height, progress, plan))
        {
            var points = polygon.Points.Select(ToPoint).ToArray();
            if (points.Length == 0)
                continue;

            var cell = new StreamGeometry();
            using (var context = cell.Open())
            {
                context.BeginFigure(points[0], isFilled: true, isClosed: true);
                for (var index = 1; index < points.Length; index++)
                    context.LineTo(points[index], isStroked: true, isSmoothJoin: false);
            }

            geometry.Children.Add(cell);
        }

        return geometry;
    }

    private void PlayBoxTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildBoxTransitionGeometry(w, h, 0, plan.BoxExpandsFromCenter);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildBoxTransitionGeometry(w, h, progress, plan.BoxExpandsFromCenter),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildBoxTransitionGeometry(
        double width,
        double height,
        double progress,
        bool expandsFromCenter) =>
        new RectangleGeometry(ToRect(SlideShowMaskGeometryPlanner.BuildBoxTransitionRect(
            width, height, progress, expandsFromCenter)));

    private void PlayRevealTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildRevealTransitionGeometry(w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildRevealTransitionGeometry(w, h, progress, plan.IncomingOffsetX, plan.IncomingOffsetY),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildRevealTransitionGeometry(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY) =>
        new RectangleGeometry(ToRect(SlideShowMaskGeometryPlanner.BuildRevealTransitionRect(
            width, height, progress, incomingOffsetX, incomingOffsetY)));

    private void PlayUncoverTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = null;
        _slideCanvas.Refresh();

        if (snapshot is null)
            return;

        _transitionBackImage.Source = snapshot;
        _transitionBackImage.Visibility = Visibility.Visible;
        Grid.SetZIndex(_transitionBackImage, 2);
        _transitionBackImage.Clip = BuildUncoverTransitionGeometry(w, h, 0, plan.IncomingOffsetX, plan.IncomingOffsetY);

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildUncoverTransitionGeometry(w, h, progress, plan.IncomingOffsetX, plan.IncomingOffsetY),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _transitionBackImage.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_transitionBackImage, 0);
        };
        Storyboard.SetTarget(animation, _transitionBackImage);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildUncoverTransitionGeometry(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY) =>
        new RectangleGeometry(ToRect(SlideShowMaskGeometryPlanner.BuildUncoverTransitionRect(
            width, height, progress, incomingOffsetX, incomingOffsetY)));

    // ── Shape animation overlay ───────────────────────────────────────────────────

    /// <summary>
    /// Sets up per-shape animated elements for a new slide:
    ///  1. Identifies shapes with Entrance animations → renders each to a bitmap
    ///     and places it as an Image in _animOverlay, hidden.
    ///  2. Updates _slideCanvas so entrance-animated shapes show when revealed.
    /// </summary>
    private void PlaySplitTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildSplitGeometry(w, h, 0, plan.SplitHorizontal, plan.SplitOut);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildSplitGeometry(w, h, progress, plan.SplitHorizontal, plan.SplitOut),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildSplitGeometry(
        double width, double height, double progress, bool horizontal, bool fromCenter)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildSplitRects(
                     width, height, progress, horizontal, fromCenter))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayBlindsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildBlindsTransitionGeometry(w, h, 0, plan.BlindsHorizontal);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildBlindsTransitionGeometry(w, h, progress, plan.BlindsHorizontal),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildBlindsTransitionGeometry(
        double width, double height, double progress, bool horizontal)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildBlindsTransitionRects(
                     width, height, SlideShowPlaybackPlanner.BlindsBandCount, progress, horizontal))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayRandomBarsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildRandomBarsTransitionGeometry(w, h, 0, plan.RandomBarsHorizontal);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildRandomBarsTransitionGeometry(w, h, progress, plan.RandomBarsHorizontal),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildRandomBarsTransitionGeometry(
        double width, double height, double progress, bool horizontal)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in SlideShowMaskGeometryPlanner.BuildRandomBarsTransitionRects(
                     width, height, SlideShowPlaybackPlanner.RandomBarsBandCount, progress, horizontal))
            geometry.Children.Add(new RectangleGeometry(ToRect(rect)));
        return geometry;
    }

    private void PlayStripsTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildStripsTransitionGeometry(
            w, h, 0, plan.StripsSlopeDown);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildStripsTransitionGeometry(w, h, progress, plan.StripsSlopeDown),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildStripsTransitionGeometry(
        double width, double height, double progress, bool slopeDown)
    {
        var stripPlan = SlideShowMaskGeometryPlanner.BuildStrips(
            width, height, progress, SlideShowPlaybackPlanner.StripsBandCount, slopeDown);
        if (stripPlan.IsFullyOpen)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var polygon in stripPlan.Polygons)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(polygon.Points[0].X, polygon.Points[0].Y),
                IsClosed = true,
                IsFilled = true
            };
            for (var index = 1; index < polygon.Points.Count; index++)
                figure.Segments.Add(new LineSegment(
                    new Point(polygon.Points[index].X, polygon.Points[index].Y),
                    isStroked: true));
            geometry.Figures.Add(figure);
        }
        return geometry;
    }

    private void PlayWheelTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = Transform.Identity;
        _slideCanvas.Clip = BuildWheelTransitionGeometry(
            w, h, 0, plan.WheelSpokeCount, plan.WheelReverse);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWheelTransitionGeometry(
                    w, h, progress, plan.WheelSpokeCount, plan.WheelReverse),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };
        Storyboard.SetTarget(animation, _slideCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildWheelTransitionGeometry(
        double width,
        double height,
        double progress,
        int spokeCount,
        bool reverse) =>
        BuildWheelGeometry(width, height, progress, spokeCount, reverse);

    private void PlayZoomTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var startScale = plan.ZoomIn
            ? SlideShowPlaybackPlanner.ZoomInStartScale
            : SlideShowPlaybackPlanner.ZoomOutStartScale;

        // Capture the outgoing slide with its own background, then apply showBg to the
        // incoming destination surface only.
        _slideCanvas.Slide = slide;
        _slideCanvas.RenderSlideBackground = _zoomShowBackgroundForTransition;
        _slideCanvas.Opacity = 1;
        var transform = new ScaleTransform(startScale, startScale, w / 2, h / 2);
        _slideCanvas.RenderTransform = transform;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var animationX = new DoubleAnimation(startScale, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var animationY = new DoubleAnimation(startScale, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animationX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _slideCanvas.RenderSlideBackground = true;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animationX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animationY);
    }

    private void PlayPanTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var dx = plan.IncomingOffsetX * w;
        var dy = plan.IncomingOffsetY * h;

        var scale = new ScaleTransform(
            SlideShowPlaybackPlanner.PanStartScale,
            SlideShowPlaybackPlanner.PanStartScale,
            w / 2,
            h / 2);
        var translate = new TranslateTransform(dx, dy);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = transform;
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var scaleX = new DoubleAnimation(
            SlideShowPlaybackPlanner.PanStartScale, 1, duration) { EasingFunction = ease };
        var scaleY = scaleX.Clone();
        var translateX = new DoubleAnimation(dx, 0, duration) { EasingFunction = ease };
        var translateY = new DoubleAnimation(dy, 0, duration) { EasingFunction = ease };
        scaleX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };

        Storyboard.SetTarget(scaleX, _slideCanvas);
        Storyboard.SetTarget(scaleY, _slideCanvas);
        Storyboard.SetTarget(translateX, _slideCanvas);
        Storyboard.SetTarget(translateY, _slideCanvas);
        Storyboard.SetTargetProperty(scaleX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(scaleY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(translateX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(translateY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(translateX);
        storyboard.Children.Add(translateY);
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayGalleryTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var travelX = plan.IncomingOffsetX * w * SlideShowPlaybackPlanner.GalleryTravelFactor;
        var travelY = plan.IncomingOffsetY * h * SlideShowPlaybackPlanner.GalleryTravelFactor;

        var incomingScale = new ScaleTransform(
            SlideShowPlaybackPlanner.GalleryStartScale,
            SlideShowPlaybackPlanner.GalleryStartScale,
            w / 2,
            h / 2);
        var incomingTranslate = new TranslateTransform(travelX, travelY);
        var incomingTransform = new TransformGroup();
        incomingTransform.Children.Add(incomingScale);
        incomingTransform.Children.Add(incomingTranslate);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = incomingTransform;
        Grid.SetZIndex(_slideCanvas, 1);
        _slideCanvas.Refresh();

        TransformGroup? outgoingTransform = null;
        if (snapshot is not null)
        {
            var outgoingScale = new ScaleTransform(1, 1, w / 2, h / 2);
            var outgoingTranslate = new TranslateTransform(0, 0);
            outgoingTransform = new TransformGroup();
            outgoingTransform.Children.Add(outgoingScale);
            outgoingTransform.Children.Add(outgoingTranslate);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransform = outgoingTransform;
            _transitionBackImage.Visibility = Visibility.Visible;
            Grid.SetZIndex(_transitionBackImage, 0);
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var incomingScaleX = new DoubleAnimation(
            SlideShowPlaybackPlanner.GalleryStartScale, 1, duration) { EasingFunction = ease };
        var incomingScaleY = incomingScaleX.Clone();
        var incomingX = new DoubleAnimation(travelX, 0, duration) { EasingFunction = ease };
        var incomingY = new DoubleAnimation(travelY, 0, duration) { EasingFunction = ease };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(incomingScaleX, _slideCanvas);
        Storyboard.SetTarget(incomingScaleY, _slideCanvas);
        Storyboard.SetTarget(incomingX, _slideCanvas);
        Storyboard.SetTarget(incomingY, _slideCanvas);
        Storyboard.SetTargetProperty(incomingScaleX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(incomingScaleY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(incomingX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(incomingY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
        storyboard.Children.Add(incomingScaleX);
        storyboard.Children.Add(incomingScaleY);
        storyboard.Children.Add(incomingX);
        storyboard.Children.Add(incomingY);

        if (outgoingTransform is not null)
        {
            var outgoingScaleX = new DoubleAnimation(
                1, SlideShowPlaybackPlanner.GalleryOutgoingEndScale, duration) { EasingFunction = ease };
            var outgoingScaleY = outgoingScaleX.Clone();
            var outgoingX = new DoubleAnimation(0, travelX, duration) { EasingFunction = ease };
            var outgoingY = new DoubleAnimation(0, travelY, duration) { EasingFunction = ease };
            Storyboard.SetTarget(outgoingScaleX, _transitionBackImage);
            Storyboard.SetTarget(outgoingScaleY, _transitionBackImage);
            Storyboard.SetTarget(outgoingX, _transitionBackImage);
            Storyboard.SetTarget(outgoingY, _transitionBackImage);
            Storyboard.SetTargetProperty(outgoingScaleX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(outgoingScaleY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTargetProperty(outgoingX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
            Storyboard.SetTargetProperty(outgoingY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
            storyboard.Children.Add(outgoingScaleX);
            storyboard.Children.Add(outgoingScaleY);
            storyboard.Children.Add(outgoingX);
            storyboard.Children.Add(outgoingY);
        }

        incomingScaleX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_slideCanvas, 0);
            Grid.SetZIndex(_transitionBackImage, 0);
        };

        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayConveyorTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
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

        var incomingScale = new ScaleTransform(
            SlideShowPlaybackPlanner.ConveyorStartScale,
            SlideShowPlaybackPlanner.ConveyorStartScale,
            w / 2,
            h / 2);
        var incomingRotate = new RotateTransform(tilt, w / 2, h / 2);
        var incomingTranslate = new TranslateTransform(endX, endY);
        var incomingTransform = new TransformGroup();
        incomingTransform.Children.Add(incomingScale);
        incomingTransform.Children.Add(incomingRotate);
        incomingTransform.Children.Add(incomingTranslate);

        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = incomingTransform;
        Grid.SetZIndex(_slideCanvas, 1);
        _slideCanvas.Refresh();

        TransformGroup? outgoingTransform = null;
        if (snapshot is not null)
        {
            var outgoingScale = new ScaleTransform(1, 1, w / 2, h / 2);
            var outgoingRotate = new RotateTransform(0, w / 2, h / 2);
            var outgoingTranslate = new TranslateTransform(0, 0);
            outgoingTransform = new TransformGroup();
            outgoingTransform.Children.Add(outgoingScale);
            outgoingTransform.Children.Add(outgoingRotate);
            outgoingTransform.Children.Add(outgoingTranslate);
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransform = outgoingTransform;
            _transitionBackImage.Visibility = Visibility.Visible;
            Grid.SetZIndex(_transitionBackImage, 0);
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var incomingScaleX = new DoubleAnimation(
            SlideShowPlaybackPlanner.ConveyorStartScale, 1, duration) { EasingFunction = ease };
        var incomingScaleY = incomingScaleX.Clone();
        var incomingAngle = new DoubleAnimation(tilt, 0, duration) { EasingFunction = ease };
        var incomingX = new DoubleAnimation(endX, 0, duration) { EasingFunction = ease };
        var incomingY = new DoubleAnimation(endY, 0, duration) { EasingFunction = ease };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(incomingScaleX, _slideCanvas);
        Storyboard.SetTarget(incomingScaleY, _slideCanvas);
        Storyboard.SetTarget(incomingAngle, _slideCanvas);
        Storyboard.SetTarget(incomingX, _slideCanvas);
        Storyboard.SetTarget(incomingY, _slideCanvas);
        Storyboard.SetTargetProperty(incomingScaleX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(incomingScaleY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(incomingAngle,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(RotateTransform.Angle)"));
        Storyboard.SetTargetProperty(incomingX,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(incomingY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(TranslateTransform.Y)"));
        storyboard.Children.Add(incomingScaleX);
        storyboard.Children.Add(incomingScaleY);
        storyboard.Children.Add(incomingAngle);
        storyboard.Children.Add(incomingX);
        storyboard.Children.Add(incomingY);

        if (outgoingTransform is not null)
        {
            var outgoingScaleX = new DoubleAnimation(
                1, SlideShowPlaybackPlanner.ConveyorOutgoingEndScale, duration) { EasingFunction = ease };
            var outgoingScaleY = outgoingScaleX.Clone();
            var outgoingAngle = new DoubleAnimation(0, -tilt, duration) { EasingFunction = ease };
            var outgoingX = new DoubleAnimation(0, endX, duration) { EasingFunction = ease };
            var outgoingY = new DoubleAnimation(0, endY, duration) { EasingFunction = ease };
            Storyboard.SetTarget(outgoingScaleX, _transitionBackImage);
            Storyboard.SetTarget(outgoingScaleY, _transitionBackImage);
            Storyboard.SetTarget(outgoingAngle, _transitionBackImage);
            Storyboard.SetTarget(outgoingX, _transitionBackImage);
            Storyboard.SetTarget(outgoingY, _transitionBackImage);
            Storyboard.SetTargetProperty(outgoingScaleX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(outgoingScaleY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTargetProperty(outgoingAngle,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(RotateTransform.Angle)"));
            Storyboard.SetTargetProperty(outgoingX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(TranslateTransform.X)"));
            Storyboard.SetTargetProperty(outgoingY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(TranslateTransform.Y)"));
            storyboard.Children.Add(outgoingScaleX);
            storyboard.Children.Add(outgoingScaleY);
            storyboard.Children.Add(outgoingAngle);
            storyboard.Children.Add(outgoingX);
            storyboard.Children.Add(outgoingY);
        }

        incomingScaleX.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_slideCanvas, 0);
            Grid.SetZIndex(_transitionBackImage, 0);
        };

        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private void PlayWindowTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
    {
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        var scale = new ScaleTransform(
            SlideShowPlaybackPlanner.WindowStartScale,
            SlideShowPlaybackPlanner.WindowStartScale,
            w / 2,
            h / 2);
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = scale;
        _slideCanvas.Clip = BuildWindowTransitionGeometry(w, h, 0);
        Grid.SetZIndex(_slideCanvas, 1);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Visibility = Visibility.Visible;
            Grid.SetZIndex(_transitionBackImage, 0);
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var scaleX = new DoubleAnimation(
            SlideShowPlaybackPlanner.WindowStartScale, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var scaleY = scaleX.Clone();
        var clip = new ObjectAnimationUsingKeyFrames { Duration = duration };
        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            clip.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWindowTransitionGeometry(w, h, progress),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        Storyboard.SetTarget(scaleX, _slideCanvas);
        Storyboard.SetTarget(scaleY, _slideCanvas);
        Storyboard.SetTarget(clip, _slideCanvas);
        Storyboard.SetTargetProperty(scaleX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(scaleY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(clip, new PropertyPath(UIElement.ClipProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(clip);
        scaleX.Completed += (_, _) =>
        {
            _slideCanvas.Clip = null;
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_slideCanvas, 0);
            Grid.SetZIndex(_transitionBackImage, 0);
        };

        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Geometry BuildWindowTransitionGeometry(double width, double height, double progress)
    {
        var opening = SlideShowPlaybackPlanner.WindowInitialOpenFactor
            + (1 - SlideShowPlaybackPlanner.WindowInitialOpenFactor) * Math.Clamp(progress, 0, 1);
        return BuildBoxTransitionGeometry(width, height, opening, expandsFromCenter: true);
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
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var transform = SlideTransform.Compute(w, h, _slideDipW, _slideDipH);
        var prepared = new List<(Image Image, ScaleTransform Scale, TranslateTransform Translate, uint ShapeId)>();

        void AddMorphOverlay(BitmapSource? bitmap, Rect sourceRect, Rect targetRect, uint shapeId)
        {
            if (bitmap is null || sourceRect.Width < 0.5 || sourceRect.Height < 0.5
                || targetRect.Width < 0.5 || targetRect.Height < 0.5)
                return;

            var scale = new ScaleTransform(
                sourceRect.Width / targetRect.Width,
                sourceRect.Height / targetRect.Height,
                targetRect.Left + targetRect.Width / 2,
                targetRect.Top + targetRect.Height / 2);
            var translate = new TranslateTransform(
                sourceRect.Left + sourceRect.Width / 2 - (targetRect.Left + targetRect.Width / 2),
                sourceRect.Top + sourceRect.Height / 2 - (targetRect.Top + targetRect.Height / 2));
            var image = new Image
            {
                Source = bitmap,
                Width = w,
                Height = h,
                Stretch = Stretch.None,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransform = new TransformGroup { Children = { scale, translate } }
            };
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, 0);
            _animOverlay.Children.Add(image);
            _slideCanvas.SuppressedShapeIds.Add(shapeId);
            prepared.Add((image, scale, translate, shapeId));
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
        _slideCanvas.RenderTransform = Transform.Identity;
        Grid.SetZIndex(_slideCanvas, 1);
        _slideCanvas.Refresh();

        if (snapshot is not null)
        {
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.Visibility = Visibility.Visible;
            Grid.SetZIndex(_transitionBackImage, 0);
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var storyboard = new Storyboard();

        if (snapshot is not null)
        {
            var backgroundFade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
            Storyboard.SetTarget(backgroundFade, _transitionBackImage);
            Storyboard.SetTargetProperty(backgroundFade, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(backgroundFade);
        }

        foreach (var item in prepared)
        {
            var scaleX = new DoubleAnimation(item.Scale.ScaleX, 1, duration) { EasingFunction = ease };
            var scaleY = new DoubleAnimation(item.Scale.ScaleY, 1, duration) { EasingFunction = ease };
            var translateX = new DoubleAnimation(item.Translate.X, 0, duration) { EasingFunction = ease };
            var translateY = new DoubleAnimation(item.Translate.Y, 0, duration) { EasingFunction = ease };
            var opacity = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };

            Storyboard.SetTarget(scaleX, item.Image);
            Storyboard.SetTarget(scaleY, item.Image);
            Storyboard.SetTarget(translateX, item.Image);
            Storyboard.SetTarget(translateY, item.Image);
            Storyboard.SetTarget(opacity, item.Image);
            Storyboard.SetTargetProperty(scaleX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(scaleY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTargetProperty(translateX,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
            Storyboard.SetTargetProperty(translateY,
                new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
            Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(translateX);
            storyboard.Children.Add(translateY);
            storyboard.Children.Add(opacity);
        }

        storyboard.Completed += (_, _) =>
        {
            foreach (var item in prepared)
            {
                _animOverlay.Children.Remove(item.Image);
                _slideCanvas.SuppressedShapeIds.Remove(item.ShapeId);
            }

            _transitionBackImage.Opacity = 1;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_transitionBackImage, 0);
            Grid.SetZIndex(_slideCanvas, 0);
            _slideCanvas.Refresh();
        };

        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static Rect MorphShapeScreenRect(SlideShape shape, SlideTransform transform)
    {
        var topLeft = transform.SlideToScreen(
            SlideTransform.EmuToDip(shape.OffsetXEmu),
            SlideTransform.EmuToDip(shape.OffsetYEmu));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            transform.ScaleDipToScreen(SlideTransform.EmuToDip(shape.ExtentCxEmu)),
            transform.ScaleDipToScreen(SlideTransform.EmuToDip(shape.ExtentCyEmu)));
    }

    private static Rect MorphTokenScreenRect(
        SlideShape shape,
        SlideShowMorphTokenMatch token,
        bool source,
        SlideTransform transform)
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

    /// <summary>
    /// Projects Flip, Cube, and Rotate into a shared two-surface perspective
    /// exchange. The scale collapse preserves the card/cube silhouette while
    /// the host remains framework-neutral and does not pretend to have a 3-D
    /// camera or face-lighting model.
    /// </summary>
    private void PlayPerspectiveTransition(
        Slide slide,
        SlideTransition transition,
        SlideShowTransitionPlaybackPlan plan)
    {
        var perspective = SlideShowPerspectiveTransitionPlanner.Plan(transition);
        var snapshot = CaptureCurrentSlide();
        var w = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        var h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var centerX = w / 2;
        var centerY = h / 2;
        var travelX = plan.IncomingOffsetX * w * perspective.TravelFactor;
        var travelY = plan.IncomingOffsetY * h * perspective.TravelFactor;

        var incomingScaleX = perspective.HorizontalAxis ? perspective.StartScale : 1;
        var incomingScaleY = perspective.HorizontalAxis ? 1 : perspective.StartScale;
        if (!perspective.IsAxisCollapsed)
        {
            incomingScaleX = perspective.StartScale;
            incomingScaleY = perspective.StartScale;
        }

        var incomingScale = new ScaleTransform(incomingScaleX, incomingScaleY, centerX, centerY);
        var incomingRotate = new RotateTransform(perspective.StartRotationDegrees, centerX, centerY);
        var incomingTranslate = new TranslateTransform(travelX, travelY);
        var incomingTransform = new TransformGroup
        {
            Children = { incomingScale, incomingRotate, incomingTranslate }
        };
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
        _slideCanvas.RenderTransform = incomingTransform;
        Grid.SetZIndex(_slideCanvas, 1);
        _slideCanvas.Refresh();

        TransformGroup? outgoingTransform = null;
        if (snapshot is not null)
        {
            var outgoingScaleX = perspective.IsAxisCollapsed && perspective.HorizontalAxis
                ? perspective.StartScale
                : incomingScaleX;
            var outgoingScaleY = perspective.IsAxisCollapsed && !perspective.HorizontalAxis
                ? perspective.StartScale
                : incomingScaleY;
            var outgoingScale = new ScaleTransform(outgoingScaleX, outgoingScaleY, centerX, centerY);
            var outgoingRotate = new RotateTransform(-perspective.StartRotationDegrees, centerX, centerY);
            var outgoingTranslate = new TranslateTransform(-travelX, -travelY);
            outgoingTransform = new TransformGroup
            {
                Children = { outgoingScale, outgoingRotate, outgoingTranslate }
            };
            _transitionBackImage.Source = snapshot;
            _transitionBackImage.RenderTransform = outgoingTransform;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.Visibility = Visibility.Visible;
            Grid.SetZIndex(_transitionBackImage, 0);
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var storyboard = new Storyboard();
        AddPerspectiveAnimation(storyboard, _slideCanvas, incomingScaleX, incomingScaleY,
            perspective.StartRotationDegrees, travelX, travelY, duration, ease);

        if (outgoingTransform is not null)
        {
            AddPerspectiveAnimation(storyboard, _transitionBackImage, 1, 1,
                0, 0, 0, duration, ease,
                endScaleX: incomingScaleX, endScaleY: incomingScaleY,
                endRotation: -perspective.StartRotationDegrees,
                endTranslateX: -travelX, endTranslateY: -travelY);
            var opacity = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
            Storyboard.SetTarget(opacity, _transitionBackImage);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(opacity);
        }

        storyboard.Completed += (_, _) =>
        {
            _slideCanvas.RenderTransform = Transform.Identity;
            _transitionBackImage.RenderTransform = Transform.Identity;
            _transitionBackImage.Opacity = 1;
            _transitionBackImage.Visibility = Visibility.Collapsed;
            Grid.SetZIndex(_slideCanvas, 0);
            Grid.SetZIndex(_transitionBackImage, 0);
        };
        _pendingStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }

    private static void AddPerspectiveAnimation(
        Storyboard storyboard,
        UIElement target,
        double startScaleX,
        double startScaleY,
        double startRotation,
        double startTranslateX,
        double startTranslateY,
        Duration duration,
        IEasingFunction ease,
        double? endScaleX = null,
        double? endScaleY = null,
        double? endRotation = null,
        double? endTranslateX = null,
        double? endTranslateY = null)
    {
        var scaleX = new DoubleAnimation(startScaleX, endScaleX ?? 1, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(startScaleY, endScaleY ?? 1, duration) { EasingFunction = ease };
        var rotation = new DoubleAnimation(startRotation, endRotation ?? 0, duration) { EasingFunction = ease };
        var translateX = new DoubleAnimation(startTranslateX, endTranslateX ?? 0, duration) { EasingFunction = ease };
        var translateY = new DoubleAnimation(startTranslateY, endTranslateY ?? 0, duration) { EasingFunction = ease };

        Storyboard.SetTarget(scaleX, target);
        Storyboard.SetTarget(scaleY, target);
        Storyboard.SetTarget(rotation, target);
        Storyboard.SetTarget(translateX, target);
        Storyboard.SetTarget(translateY, target);
        var transformPrefix = "(UIElement.RenderTransform).(TransformGroup.Children)";
        Storyboard.SetTargetProperty(scaleX,
            new PropertyPath($"{transformPrefix}[0].(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(scaleY,
            new PropertyPath($"{transformPrefix}[0].(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(rotation,
            new PropertyPath($"{transformPrefix}[1].(RotateTransform.Angle)"));
        Storyboard.SetTargetProperty(translateX,
            new PropertyPath($"{transformPrefix}[2].(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(translateY,
            new PropertyPath($"{transformPrefix}[2].(TranslateTransform.Y)"));
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(rotation);
        storyboard.Children.Add(translateX);
        storyboard.Children.Add(translateY);
    }

    private void PrepareAnimationOverlay(Slide slide)
    {
        _animOverlay.Children.Clear();
        _animElements.Clear();
        _animFillElements.Clear();
        _animLineElements.Clear();
        _animFontStyleElements.Clear();
        _animFontSizeElements.Clear();
        _paragraphAnimElements.Clear();
        _revealedShapes.Clear();
        _slideCanvas.SuppressedShapeIds.Clear();

        // Only hide shapes whose ONLY animations are non-trigger (main-sequence) entrances/motions.
        // A shape whose sole animation is an interactive trigger should be visible at slide entry;
        // the trigger animation plays on the already-visible shape when the user clicks the trigger.
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

        // Emphasis overlays stay visible over the base canvas; non-trigger entrance/motion
        // overlays start hidden, while trigger-bound ones remain visible until clicked.
        if (animatedShapeIds.Count == 0) return;

        // Render the whole slide to get per-shape bitmaps via a temporary canvas.
        // We create one overlay Image per entrance-animated shape.
        // We need the slide pixel size for the overlay canvas sizing.
        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        // Place the overlay canvas at the same size/position as the slide canvas.
        _animOverlay.Width  = w;
        _animOverlay.Height = h;

        foreach (var shapeId in animatedShapeIds)
        {
            var shape = SlideShapeTraversal.FindById(slide, shapeId);
            if (shape is null) continue;

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

                    var paragraphElements = new List<FrameworkElement>(paragraphShapes.Count);
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

            // Render the shape by rendering the whole slide and cropping to the shape bounds.
            var shapeBitmap = RenderShapeToOverlayBitmap(slide, shape, w, h);
            if (shapeBitmap is null) continue;

            var img = new Image
            {
                Source = shapeBitmap,
                Width  = w,
                Height = h,
                Stretch = Stretch.None,
                Opacity = _entranceShapeIds.Contains(shapeId) ? 0 : 1,
                IsHitTestVisible = false,
                Tag = shapeId,
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

            if (slide.Animations.Any(a => a.ShapeId == shapeId
                                          && (a.Kind == AnimationKind.Entrance
                                              || a.Kind == AnimationKind.Motion)))
            {
                _slideCanvas.SuppressedShapeIds.Add(shapeId);
            }
        }

        _slideCanvas.Refresh();
    }

    private readonly List<Storyboard> _pendingStoryboards = new();

    /// <summary>
    /// Renders a single shape (by building a temporary canvas that only contains that shape)
    /// into a bitmap at the full slide canvas size, so the overlay Image can be positioned
    /// simply at (0,0) on top of the main canvas.
    /// </summary>
    private BitmapSource? RenderShapeToOverlayBitmap(Slide slide, SlideShape shape, double w, double h)
    {
        try
        {
            // Build a temporary single-shape slide.
            var tempSlide = new Slide { Background = null };
            tempSlide.Shapes.Add(shape);

            var tempCanvas = new SlideCanvas
            {
                Presentation = _presentation,
                Slide        = tempSlide,
            };
            tempCanvas.Measure(new Size(w, h));
            tempCanvas.Arrange(new Rect(0, 0, w, h));
            tempCanvas.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(tempCanvas);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    private void RevealShape(uint shapeId)
    {
        if (_paragraphAnimElements.ContainsKey(shapeId))
            return;

        if (_slideCanvas.SuppressedShapeIds.Remove(shapeId))
            _slideCanvas.Refresh();
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

        var effectiveColorMap = _runtime.DisplaySlide?.ColorMapOverride;
        foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step, _presentation, effectiveColorMap))
        {
            var anim = plan.Animation;
            if (_paragraphAnimElements.TryGetValue(anim.ShapeId, out var paragraphElements))
            {
                for (var index = 0; index < paragraphElements.Count; index++)
                {
                    var paragraphPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
                        anim,
                        plan.DelayMs + index * plan.DurationMs,
                        _presentation,
                        effectiveColorMap);
                    PlayShapeAnimation(paragraphElements[index], paragraphPlan);
                }

                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                // Keep the logical visibility transition even when a shape cannot be
                // rasterized into an overlay.  This is deliberately geometry-neutral: the
                // shape is shown/hidden at the authored timing without inventing a motion
                // path or clip for a visual we could not render safely.
                PlayFallbackAnimation(anim, plan.DelayMs, plan.DurationMs);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFillColor
                && _animFillElements.TryGetValue(anim.ShapeId, out var fillElement))
            {
                PlayShapeAnimation(fillElement, plan);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeLineColor
                && _animLineElements.TryGetValue(anim.ShapeId, out var lineElement))
            {
                PlayShapeAnimation(lineElement, plan);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind is (SlideShowShapeAnimationEffectKind.ChangeFontStyle
                    or SlideShowShapeAnimationEffectKind.Bold
                    or SlideShowShapeAnimationEffectKind.Underline)
                && _animFontStyleElements.TryGetValue(anim.ShapeId, out var fontStyleElement))
            {
                PlayShapeAnimation(fontStyleElement, plan);
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFontSize)
            {
                if (_animFontSizeElements.TryGetValue(anim.ShapeId, out var fontSizeElement))
                    PlayShapeAnimation(fontSizeElement, plan);
                else
                    PlayShapeAnimation(element, plan with { EffectKind = SlideShowShapeAnimationEffectKind.GrowShrink });
                _revealedShapes.Add(anim.ShapeId);
                continue;
            }

            if (anim.Kind is AnimationKind.Entrance or AnimationKind.Motion or AnimationKind.Exit)
            {
                element.Opacity = 1;
                _slideCanvas.SuppressedShapeIds.Add(anim.ShapeId);
                _slideCanvas.Refresh();
            }

            PlayShapeAnimation(element, plan);
            _revealedShapes.Add(anim.ShapeId);
        }
    }

    private void PlayShapeAnimation(FrameworkElement element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        _lastAnimationFramePlan = SlideShowPlaybackFramePlanner.PlanFrame(plan, 0, _slideDipW, _slideDipH);

        var sb = new Storyboard();

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.MotionPath)
        {
            MotionPathEffect(sb, element, plan);
            ApplyRepeatTiming(sb, plan);
            AttachEntranceCompletion(sb, plan);
            _pendingStoryboards.Add(sb);
            sb.Begin(element, isControllable: true);
            return;
        }

        switch (plan.EffectKind)
        {
            case SlideShowShapeAnimationEffectKind.Appear:
                if (plan.Animation.Kind == AnimationKind.Exit)
                    DisappearEffect(sb, element, plan.DelayMs);
                else
                    AppearEffect(sb, element, plan.DelayMs);
                break;

            case SlideShowShapeAnimationEffectKind.Fade:
                FadeEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.FlyIn:
                FlyInEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Wipe:
                WipeEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Split:
                SplitEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.RandomBars:
                RandomBarsEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Blinds:
                BlindsEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Box:
                BoxEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Checkerboard:
                CheckerboardEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Circle:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Diamond:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Plus:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Strips:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Wedge:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Wheel:
                GeometricMaskEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Dissolve:
                DissolveEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Flash:
                FlashEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Spiral:
                SpiralEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Swivel:
                SwivelEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bounce:
            case SlideShowShapeAnimationEffectKind.Float:
            case SlideShowShapeAnimationEffectKind.Swoop:
            case SlideShowShapeAnimationEffectKind.Boomerang:
                TrajectoryEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Peek:
                PeekEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Crawl:
                CrawlEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Zoom:
                ZoomEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Pulse:
                PulseEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.GrowShrink:
                GrowShrinkEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Spin:
                SpinEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Teeter:
                TeeterEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Blink:
                BlinkEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.FlashBulb:
                FlashBulbEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Flicker:
                FlickerEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Wave:
                WaveEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorPulse:
            case SlideShowShapeAnimationEffectKind.ChangeColor:
                EmphasisPulseEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFontStyle:
                FontStyleEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFontSize:
                FontSizeEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorWave:
                ColorWaveEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeLineColor:
                LineColorEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ChangeFillColor:
                FillColorEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.GrowWithColor:
            case SlideShowShapeAnimationEffectKind.Shimmer:
                EmphasisPulseEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bold:
            case SlideShowShapeAnimationEffectKind.Underline:
                FontStyleEffect(sb, element, plan);
                break;

            default:
                // Unknown preset → instant appear
                AppearEffect(sb, element, plan.DelayMs);
                break;
        }

        ApplyRepeatTiming(sb, plan);
        AttachEntranceCompletion(sb, plan);
        _pendingStoryboards.Add(sb);
        sb.Begin(element, isControllable: true);
    }

    private static void ApplyRepeatTiming(
        Storyboard storyboard,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        if (plan.RepeatIndefinitely || plan.RepeatCount is > 1)
        {
            var repeatBehavior = plan.RepeatIndefinitely
                ? RepeatBehavior.Forever
                : new RepeatBehavior(plan.RepeatCount!.Value);

            foreach (var timeline in storyboard.Children)
                timeline.RepeatBehavior = repeatBehavior;
        }

        if (plan.AutoReverse)
        {
            foreach (var timeline in storyboard.Children)
                timeline.AutoReverse = true;
        }
    }

    private void AttachEntranceCompletion(
        Storyboard storyboard,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        if (plan.Animation.Kind is AnimationKind.Entrance or AnimationKind.Motion)
            storyboard.Completed += (_, _) => RevealShape(plan.Animation.ShapeId);
    }

    private static void AppearEffect(Storyboard sb, FrameworkElement el, int delayMs)
    {
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.Zero))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs)
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private static void FadeEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var anim = new DoubleAnimation(
            plan.FromOpacity,
            plan.ToOpacity,
            new Duration(TimeSpan.FromMilliseconds(plan.DurationMs)))
        {
            BeginTime     = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private void FlyInEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        var translate = new TranslateTransform(0, 0);
        el.RenderTransform = translate;

        double dx = plan.OffsetXFactor * w;
        double dy = plan.OffsetYFactor * h;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var animX = new DoubleAnimation(isExit ? 0 : dx, isExit ? dx : 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animY = new DoubleAnimation(isExit ? 0 : dy, isExit ? dy : 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animOp = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs) };

        Storyboard.SetTarget(animX,  el);
        Storyboard.SetTarget(animY,  el);
        Storyboard.SetTarget(animOp, el);

        Storyboard.SetTargetProperty(animX,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(animY,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        Storyboard.SetTargetProperty(animOp, new PropertyPath(OpacityProperty));

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOp);
    }

    private void TrajectoryEffect(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan playback)
    {
        double width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        double height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
        var trajectory = SlideShowAnimationEffectFramePlanner.Build(
            playback.EffectKind,
            playback.Animation.Kind,
            playback.OffsetXFactor,
            playback.OffsetYFactor);
        element.RenderTransform = new TranslateTransform(
            trajectory.Start.NormalizedX * width,
            trajectory.Start.NormalizedY * height);

        var duration = new Duration(TimeSpan.FromMilliseconds(playback.DurationMs));
        AddTrajectoryAxisAnimation(
            storyboard,
            element,
            trajectory,
            width,
            duration,
            playback.DelayMs,
            useX: true,
            "(UIElement.RenderTransform).(TranslateTransform.X)");
        AddTrajectoryAxisAnimation(
            storyboard,
            element,
            trajectory,
            height,
            duration,
            playback.DelayMs,
            useX: false,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");

        var opacity = new DoubleAnimation(playback.FromOpacity, playback.ToOpacity, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(playback.DelayMs),
            EasingFunction = playback.EffectKind == SlideShowShapeAnimationEffectKind.Bounce
                ? new CubicEase { EasingMode = EasingMode.EaseInOut }
                : null
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static void AddTrajectoryAxisAnimation(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowAnimationEffectFramePlan trajectory,
        double scale,
        Duration duration,
        int delayMs,
        bool useX,
        string propertyPath)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = duration
        };
        foreach (var frame in trajectory.Frames)
        {
            var value = (useX ? frame.NormalizedX : frame.NormalizedY) * scale;
            var keyTime = KeyTime.FromPercent(frame.Progress);
            DoubleKeyFrame keyFrame = frame.StoryboardInterpolation switch
            {
                SlideShowAnimationEffectFrameInterpolation.Discrete =>
                    new DiscreteDoubleKeyFrame(value, keyTime),
                SlideShowAnimationEffectFrameInterpolation.Linear =>
                    new LinearDoubleKeyFrame(value, keyTime),
                SlideShowAnimationEffectFrameInterpolation.Spline when frame.StoryboardSpline is { } spline =>
                    new SplineDoubleKeyFrame(
                        value,
                        keyTime,
                        new KeySpline(
                            spline.ControlPoint1X,
                            spline.ControlPoint1Y,
                            spline.ControlPoint2X,
                            spline.ControlPoint2Y)),
                _ => new LinearDoubleKeyFrame(value, keyTime)
            };
            animation.KeyFrames.Add(keyFrame);
        }

        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        storyboard.Children.Add(animation);
    }
    private void PeekEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        var translate = new TranslateTransform(0, 0);
        el.RenderTransform = translate;
        el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));
        el.Opacity = 1;

        double dx = plan.OffsetXFactor * w;
        double dy = plan.OffsetYFactor * h;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var fromX = isExit ? 0 : dx;
        var fromY = isExit ? 0 : dy;
        var toX = isExit ? dx : 0;
        var toY = isExit ? dy : 0;
        translate.X = fromX;
        translate.Y = fromY;
        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation(fromX, toX, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animY = new DoubleAnimation(fromY, toY, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };

        Storyboard.SetTarget(animX, el);
        Storyboard.SetTarget(animY, el);
        Storyboard.SetTargetProperty(animX,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(animY,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        sb.Children.Add(animX);
        sb.Children.Add(animY);
    }

    private void CrawlEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan) =>
        PeekEffect(sb, el, plan);

    private static void WipeEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        // Wipe: reveal via a clip RectangleGeometry that grows from 0 to full.
        // Direction determines which edge to wipe from.
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var clip  = new RectangleGeometry(new Rect(0, 0, 0, 0));
        el.Clip   = clip;

        var dur  = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Make visible first.
        el.Opacity = 1;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        if (plan.WipeHorizontal)
        {
            var from = isExit ? new Rect(0, 0, w, h) : new Rect(0, 0, 0, h);
            var to = isExit ? new Rect(0, 0, 0, h) : new Rect(0, 0, w, h);
            clip.Rect = from;
            var a = new RectAnimation(
                from, to, dur)
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(a, el);
            Storyboard.SetTargetProperty(a, new PropertyPath("Clip.Rect"));
            sb.Children.Add(a);
        }
        else
        {
            var from = isExit ? new Rect(0, 0, w, h) : new Rect(0, 0, w, 0);
            var to = isExit ? new Rect(0, 0, w, 0) : new Rect(0, 0, w, h);
            clip.Rect = from;
            var a = new RectAnimation(
                from, to, dur)
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(a, el);
            Storyboard.SetTargetProperty(a, new PropertyPath("Clip.Rect"));
            sb.Children.Add(a);
        }
    }

    private static void SplitEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var fromProgress = isExit ? 1 : 0;
        var toProgress = isExit ? 0 : 1;
        var clip = (GeometryGroup)BuildSplitGeometry(
            w, h, fromProgress, plan.SplitHorizontal, plan.SplitFromCenter);
        el.Clip = clip;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fromRects = SlideShowMaskGeometryPlanner.BuildSplitRects(
            w, h, fromProgress, plan.SplitHorizontal, plan.SplitFromCenter);
        var toRects = SlideShowMaskGeometryPlanner.BuildSplitRects(
            w, h, toProgress, plan.SplitHorizontal, plan.SplitFromCenter);
        for (var i = 0; i < clip.Children.Count; i++)
        {
            AddRectAnimation(
                sb,
                (RectangleGeometry)clip.Children[i],
                ToRect(fromRects[i]),
                ToRect(toRects[i]),
                dur,
                ease,
                plan.DelayMs);
        }
    }

    private static void RandomBarsEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var bars = new GeometryGroup();
        el.Clip = bars;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        el.Opacity = isExit ? plan.FromOpacity : 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
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
            var barDurationMs = Math.Max(1, plan.DurationMs - randomBar.Order * barStaggerMs);
            var barAnimation = new RectAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(barDurationMs)))
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs + randomBar.Order * barStaggerMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(barAnimation, bar);
            Storyboard.SetTargetProperty(barAnimation, new PropertyPath(RectangleGeometry.RectProperty));
            sb.Children.Add(barAnimation);
        }

        var opacityAnim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        if (isExit)
        {
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(plan.FromOpacity, KeyTime.FromPercent(0)));
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.7, KeyTime.FromPercent(0.2)));
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.35, KeyTime.FromPercent(0.55)));
        }
        else
        {
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.35, KeyTime.FromPercent(0.2)));
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.7, KeyTime.FromPercent(0.55)));
        }
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(plan.ToOpacity, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(opacityAnim, el);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));
        sb.Children.Add(opacityAnim);
    }

    private static void BlindsEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var opens = plan.ToOpacity >= plan.FromOpacity;
        var bandCount = Math.Max(1, plan.BlindsBandCount);
        var bands = new GeometryGroup();

        el.Clip = bands;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

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

            var anim = new RectAnimation(from, to, dur)
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(anim, band);
            Storyboard.SetTargetProperty(anim, new PropertyPath(RectangleGeometry.RectProperty));
            sb.Children.Add(anim);
        }
    }

    private static void CheckerboardEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;
        var opens = plan.ToOpacity >= plan.FromOpacity;
        var rowCount = Math.Max(1, plan.CheckerboardRowCount);
        var columnCount = Math.Max(1, plan.CheckerboardColumnCount);
        var cells = new GeometryGroup();
        var phaseDelayMs = Math.Max(0, plan.DurationMs / 3);
        var cellDurationMs = Math.Max(1, plan.DurationMs - phaseDelayMs);

        el.Clip = cells;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(cellDurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

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

                var anim = new RectAnimation(from, to, dur)
                {
                    BeginTime = TimeSpan.FromMilliseconds(
                        plan.DelayMs + (SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(row, column) ? phaseDelayMs : 0)),
                    EasingFunction = ease
                };
                Storyboard.SetTarget(anim, cell);
                Storyboard.SetTargetProperty(anim, new PropertyPath(RectangleGeometry.RectProperty));
                sb.Children.Add(anim);
            }
        }
    }

    private static void BoxEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var center = new Rect(w / 2, h / 2, 0, 0);
        var full = new Rect(0, 0, w, h);
        var from = plan.BoxExpandsFromCenter ? center : full;
        var to = plan.BoxExpandsFromCenter ? full : center;

        var clip = new RectangleGeometry(from);
        el.Clip = clip;
        el.Opacity = 1;

        var anim = new RectAnimation(
            from,
            to,
            new Duration(TimeSpan.FromMilliseconds(plan.DurationMs)))
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Clip.Rect"));
        sb.Children.Add(anim);
    }

    private static void GeometricMaskEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        switch (plan.GeometricMaskKind)
        {
            case SlideShowGeometricMaskKind.Circle:
                CircleEffect(sb, el, plan);
                break;

            case SlideShowGeometricMaskKind.Diamond:
                DiamondEffect(sb, el, plan);
                break;

            case SlideShowGeometricMaskKind.Plus:
                PlusEffect(sb, el, plan);
                break;

            case SlideShowGeometricMaskKind.Strips:
                StripsEffect(sb, el, plan);
                break;

            case SlideShowGeometricMaskKind.Wedge:
                WedgeEffect(sb, el, plan);
                break;

            case SlideShowGeometricMaskKind.Wheel:
                WheelEffect(sb, el, plan);
                break;

            default:
                AppearEffect(sb, el, plan.DelayMs);
                break;
        }
    }

    private static void CircleEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        var circlePlan = SlideShowMaskGeometryPlanner.BuildCircle(w, h, fromProgress);
        var clip = new EllipseGeometry(
            ToPoint(circlePlan.Center),
            circlePlan.RadiusX,
            circlePlan.RadiusY);

        el.Clip = clip;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        AddDoubleAnimation(
            sb,
            clip,
            EllipseGeometry.RadiusXProperty,
            w / 2 * fromProgress,
            w / 2 * toProgress,
            dur,
            ease,
            plan.DelayMs);
        AddDoubleAnimation(
            sb,
            clip,
            EllipseGeometry.RadiusYProperty,
            h / 2 * fromProgress,
            h / 2 * toProgress,
            dur,
            ease,
            plan.DelayMs);
    }

    private static void DiamondEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        var clip = BuildDiamondGeometry(
            w,
            h,
            fromProgress,
            out var figure,
            out var rightSegment,
            out var bottomSegment,
            out var leftSegment);

        el.Clip = clip;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        AddDiamondPointAnimation(
            sb,
            figure,
            PathFigure.StartPointProperty,
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 0, progress: fromProgress)),
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 0, progress: toProgress)),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            rightSegment,
            LineSegment.PointProperty,
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 1, progress: fromProgress)),
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 1, progress: toProgress)),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            bottomSegment,
            LineSegment.PointProperty,
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 2, progress: fromProgress)),
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 2, progress: toProgress)),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            leftSegment,
            LineSegment.PointProperty,
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 3, progress: fromProgress)),
            ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(w, h, vertexIndex: 3, progress: toProgress)),
            dur,
            ease,
            plan.DelayMs);
    }

    private static PathGeometry BuildDiamondGeometry(
        double width,
        double height,
        double progress,
        out PathFigure figure,
        out LineSegment rightSegment,
        out LineSegment bottomSegment,
        out LineSegment leftSegment)
    {
        figure = new PathFigure
        {
            StartPoint = ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 0, progress: progress)),
            IsClosed = true,
            IsFilled = true
        };
        rightSegment = new LineSegment(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 1, progress: progress)), true);
        bottomSegment = new LineSegment(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 2, progress: progress)), true);
        leftSegment = new LineSegment(ToPoint(SlideShowMaskGeometryPlanner.BuildDiamondPoint(width, height, vertexIndex: 3, progress: progress)), true);
        figure.Segments.Add(rightSegment);
        figure.Segments.Add(bottomSegment);
        figure.Segments.Add(leftSegment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void PlusEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        var fromPlan = SlideShowMaskGeometryPlanner.BuildPlusRects(w, h, fromProgress);
        var toPlan = SlideShowMaskGeometryPlanner.BuildPlusRects(w, h, toProgress);
        var fromVertical = ToRect(fromPlan.Closed);
        var fromHorizontal = ToRect(fromPlan.Open);
        var toVertical = ToRect(toPlan.Closed);
        var toHorizontal = ToRect(toPlan.Open);

        var clip = new GeometryGroup { FillRule = FillRule.Nonzero };
        var vertical = new RectangleGeometry(fromVertical);
        var horizontal = new RectangleGeometry(fromHorizontal);
        clip.Children.Add(vertical);
        clip.Children.Add(horizontal);

        el.Clip = clip;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        AddRectAnimation(sb, vertical, fromVertical, toVertical, dur, ease, plan.DelayMs);
        AddRectAnimation(sb, horizontal, fromHorizontal, toHorizontal, dur, ease, plan.DelayMs);
    }

    private static void StripsEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        el.Clip = BuildStripsGeometry(
            w,
            h,
            fromProgress,
            plan.GeometricMaskStripCount,
            plan.GeometricMaskStripsSlopeDown);
        el.Opacity = 1;

        var anim = new ObjectAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };

        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var t = frame / (double)frameCount;
            var progress = fromProgress + (toProgress - fromProgress) * t;
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildStripsGeometry(
                    w,
                    h,
                    progress,
                    plan.GeometricMaskStripCount,
                    plan.GeometricMaskStripsSlopeDown),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * t))));
        }

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.ClipProperty));
        sb.Children.Add(anim);
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

        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var polygon in stripPlan.Polygons)
        {
            geometry.Children.Add(BuildStripGeometry(polygon.Points));
        }

        return geometry;
    }

    private static PathGeometry BuildStripGeometry(
        IReadOnlyList<SlideShowMaskPoint> maskPoints)
    {
        var points = maskPoints.Select(ToPoint).ToArray();

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(points[1], true));
        figure.Segments.Add(new LineSegment(points[2], true));
        figure.Segments.Add(new LineSegment(points[3], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void WedgeEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        el.Clip = BuildWedgeGeometry(w, h, fromProgress);
        el.Opacity = 1;

        var anim = new ObjectAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };

        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var t = frame / (double)frameCount;
            var progress = fromProgress + (toProgress - fromProgress) * t;
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWedgeGeometry(w, h, progress),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * t))));
        }

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.ClipProperty));
        sb.Children.Add(anim);
    }

    private static Geometry BuildWedgeGeometry(double width, double height, double progress)
    {
        var wedgePlan = SlideShowMaskGeometryPlanner.BuildWedge(width, height, progress);
        if (wedgePlan.IsFullyOpen)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        if (wedgePlan.IsCollapsed)
        {
            var center = ToPoint(new SlideShowMaskPoint(width / 2, height / 2));
            return new PathGeometry(new[]
            {
                new PathFigure(center, new PathSegment[] { new LineSegment(center, true) }, closed: true)
            });
        }

        var arc = wedgePlan.Arcs[0];
        var centerPoint = ToPoint(arc.Center);
        var figure = new PathFigure
        {
            StartPoint = centerPoint,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(ToPoint(arc.Start), true));
        figure.Segments.Add(new ArcSegment(
            ToPoint(arc.End),
            new Size(arc.Radius, arc.Radius),
            rotationAngle: 0,
            isLargeArc: arc.IsLargeArc,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void WheelEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var fromProgress = plan.GeometricMaskExpandsFromCenter ? 0.0 : 1.0;
        var toProgress = plan.GeometricMaskExpandsFromCenter ? 1.0 : 0.0;
        el.Clip = BuildWheelGeometry(w, h, fromProgress, plan.GeometricMaskSpokeCount);
        el.Opacity = 1;

        var anim = new ObjectAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };

        const int frameCount = 24;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var t = frame / (double)frameCount;
            var progress = fromProgress + (toProgress - fromProgress) * t;
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildWheelGeometry(w, h, progress, plan.GeometricMaskSpokeCount),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * t))));
        }

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.ClipProperty));
        sb.Children.Add(anim);
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
            return new PathGeometry(new[]
            {
                new PathFigure(center, new PathSegment[] { new LineSegment(center, true) }, closed: true)
            });
        }

        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };

        foreach (var arc in wheelPlan.Arcs)
        {
            geometry.Children.Add(BuildWheelSpokeGeometry(arc));
        }

        return geometry;
    }

    private static PathGeometry BuildWheelSpokeGeometry(
        SlideShowMaskArc arc)
    {
        var center = ToPoint(arc.Center);
        var figure = new PathFigure
        {
            StartPoint = center,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(ToPoint(arc.Start), true));
        figure.Segments.Add(new ArcSegment(
            ToPoint(arc.End),
            new Size(arc.Radius, arc.Radius),
            rotationAngle: 0,
            isLargeArc: arc.IsLargeArc,
            sweepDirection: arc.IsClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Rect ToRect(SlideShowMaskRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(SlideShowMaskPoint point) =>
        new(point.X, point.Y);

    private static void AddRectAnimation(
        Storyboard storyboard,
        RectangleGeometry target,
        Rect from,
        Rect to,
        Duration duration,
        IEasingFunction easing,
        int delayMs)
    {
        var anim = new RectAnimation(from, to, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(RectangleGeometry.RectProperty));
        storyboard.Children.Add(anim);
    }

    private static void AddDiamondPointAnimation(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty pointProperty,
        Point from,
        Point to,
        Duration duration,
        IEasingFunction easing,
        int delayMs)
    {
        var anim = new PointAnimation(from, to, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(pointProperty));
        storyboard.Children.Add(anim);
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        Duration duration,
        IEasingFunction easing,
        int delayMs)
    {
        var anim = new DoubleAnimation(from, to, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(property));
        storyboard.Children.Add(anim);
    }

    private void ZoomEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double cx = (el.Width  > 0 ? el.Width  : _slideCanvas.ActualWidth)  / 2;
        double cy = (el.Height > 0 ? el.Height : _slideCanvas.ActualHeight) / 2;

        var scale = new ScaleTransform(plan.FromScale, plan.FromScale, cx, cy);
        el.RenderTransform = scale;

        var dur  = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animSX = new DoubleAnimation(plan.FromScale, plan.ToScale, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animSY = new DoubleAnimation(plan.FromScale, plan.ToScale, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animOp = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs) };

        Storyboard.SetTarget(animSX,  el);
        Storyboard.SetTarget(animSY,  el);
        Storyboard.SetTarget(animOp,  el);
        Storyboard.SetTargetProperty(animSX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(animSY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(animOp, new PropertyPath(OpacityProperty));

        sb.Children.Add(animSX);
        sb.Children.Add(animSY);
        sb.Children.Add(animOp);
    }

    private static void PulseEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        // Ensure visible
        el.Opacity = 1;

        double cx = el.Width  / 2;
        double cy = el.Height / 2;
        var scale = new ScaleTransform(1, 1, cx, cy);
        el.RenderTransform = scale;

        var halfDur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs / 2));

        var animSXUp = new DoubleAnimation(1, plan.PeakScale, halfDur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), AutoReverse = true };

        Storyboard.SetTarget(animSXUp, el);
        Storyboard.SetTargetProperty(animSXUp,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

        var animSYUp = animSXUp.Clone();
        Storyboard.SetTarget(animSYUp, el);
        Storyboard.SetTargetProperty(animSYUp,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        sb.Children.Add(animSXUp);
        sb.Children.Add(animSYUp);
    }

    private static void GrowShrinkEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;

        double cx = el.Width  / 2;
        double cy = el.Height / 2;
        var scale = new ScaleTransform(plan.FromScaleX, plan.FromScaleY, cx, cy);
        el.RenderTransform = scale;

        var animSX = BuildGrowShrinkScaleAnimation(plan, plan.FromScaleX, plan.PeakScaleX, plan.ToScaleX);
        Storyboard.SetTarget(animSX, el);
        Storyboard.SetTargetProperty(animSX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

        var animSY = BuildGrowShrinkScaleAnimation(plan, plan.FromScaleY, plan.PeakScaleY, plan.ToScaleY);
        Storyboard.SetTarget(animSY, el);
        Storyboard.SetTargetProperty(animSY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        sb.Children.Add(animSX);
        sb.Children.Add(animSY);
    }

    private static DoubleAnimationUsingKeyFrames BuildGrowShrinkScaleAnimation(
        SlideShowShapeAnimationPlaybackPlan plan,
        double fromScale,
        double peakScale,
        double toScale)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(fromScale, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            peakScale,
            KeyTime.FromPercent(0.5),
            new KeySpline(0.2, 0, 0.2, 1)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            toScale,
            KeyTime.FromPercent(1),
            new KeySpline(0.4, 0, 0.2, 1)));
        return anim;
    }

    private static void SpinEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;

        double cx = el.Width  / 2;
        double cy = el.Height / 2;
        var rotate = new RotateTransform(0, cx, cy);
        el.RenderTransform = rotate;

        var anim = new DoubleAnimation(0, plan.RotationDegrees, new Duration(TimeSpan.FromMilliseconds(plan.DurationMs)))
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        sb.Children.Add(anim);
    }

    private static void SpiralEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;

        double cx = el.Width / 2;
        double cy = el.Height / 2;
        var rotate = new RotateTransform(0, cx, cy);
        el.RenderTransform = rotate;
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            plan.RotationDegrees * 0.82,
            KeyTime.FromPercent(0.7),
            new KeySpline(0.15, 0, 0.35, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            plan.RotationDegrees,
            KeyTime.FromPercent(1),
            new KeySpline(0.25, 0, 0.2, 1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        sb.Children.Add(animation);
    }

    private static void SwivelEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;

        double cx = el.Width / 2;
        double cy = el.Height / 2;
        var scale = new ScaleTransform(1, 1, cx, cy);
        var rotate = new RotateTransform(0, cx, cy);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(rotate);
        el.RenderTransform = transform;

        var rotation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        rotation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        rotation.KeyFrames.Add(new LinearDoubleKeyFrame(
            plan.RotationDegrees * 0.25, KeyTime.FromPercent(0.25)));
        rotation.KeyFrames.Add(new LinearDoubleKeyFrame(
            plan.RotationDegrees * 0.5, KeyTime.FromPercent(0.5)));
        rotation.KeyFrames.Add(new LinearDoubleKeyFrame(
            plan.RotationDegrees * 0.75, KeyTime.FromPercent(0.75)));
        rotation.KeyFrames.Add(new LinearDoubleKeyFrame(
            plan.RotationDegrees, KeyTime.FromPercent(1)));

        var horizontalScale = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        horizontalScale.KeyFrames.Add(new LinearDoubleKeyFrame(
            SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(0), KeyTime.FromPercent(0)));
        horizontalScale.KeyFrames.Add(new LinearDoubleKeyFrame(
            SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(0.25), KeyTime.FromPercent(0.25)));
        horizontalScale.KeyFrames.Add(new LinearDoubleKeyFrame(
            SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(0.5), KeyTime.FromPercent(0.5)));
        horizontalScale.KeyFrames.Add(new LinearDoubleKeyFrame(
            SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(0.75), KeyTime.FromPercent(0.75)));
        horizontalScale.KeyFrames.Add(new LinearDoubleKeyFrame(
            SlideShowPlaybackFramePlanner.ResolveSwivelHorizontalScale(1), KeyTime.FromPercent(1)));

        Storyboard.SetTarget(rotation, el);
        Storyboard.SetTarget(horizontalScale, el);
        Storyboard.SetTargetProperty(rotation,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(RotateTransform.Angle)"));
        Storyboard.SetTargetProperty(horizontalScale,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        sb.Children.Add(rotation);
        sb.Children.Add(horizontalScale);
    }

    private static void FlashEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };

        // Flash briefly reveals and dims the object before settling at its
        // authored opacity. Exit effects use the same pulse in reverse.
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            isExit ? plan.FromOpacity : 0,
            KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            0.7,
            KeyTime.FromPercent(0.2),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            0.35,
            KeyTime.FromPercent(0.55),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(
            plan.ToOpacity,
            KeyTime.FromPercent(1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
        sb.Children.Add(animation);
    }

    private static void DissolveEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double width = el.Width > 0 ? el.Width : 960;
        double height = el.Height > 0 ? el.Height : 540;
        var isExit = plan.Animation.Kind == AnimationKind.Exit;

        el.Opacity = isExit ? plan.FromOpacity : 1;
        el.Clip = BuildDissolveAnimationGeometry(width, height, isExit ? 1 : 0);

        var animation = new ObjectAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs))
        };
        const int frameCount = 30;
        for (var frame = 0; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            var maskProgress = isExit ? 1 - progress : progress;
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(
                BuildDissolveAnimationGeometry(width, height, maskProgress),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * progress))));
        }

        animation.Completed += (_, _) =>
        {
            el.Clip = null;
            el.Opacity = plan.ToOpacity;
        };
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.ClipProperty));
        sb.Children.Add(animation);
    }

    private static Geometry BuildDissolveAnimationGeometry(
        double width,
        double height,
        double progress)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
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

    private static void DisappearEffect(Storyboard sb, FrameworkElement el, int delayMs)
    {
        var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.Zero))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs)
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private static void TeeterEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var rotate = new RotateTransform(0, el.Width / 2, el.Height / 2);
        el.RenderTransform = rotate;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-10, KeyTime.FromPercent(0.2)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.FromPercent(0.4)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-10, KeyTime.FromPercent(0.6)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        sb.Children.Add(anim);
    }

    private static void BlinkEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromPercent(0.25)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.5)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromPercent(0.75)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private static void FlashBulbEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.05, KeyTime.FromPercent(0.08)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.16)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.70, KeyTime.FromPercent(0.30)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private static void FlickerEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.20, KeyTime.FromPercent(0.20)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.80, KeyTime.FromPercent(0.35)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromPercent(0.50)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.65, KeyTime.FromPercent(0.65)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.25, KeyTime.FromPercent(0.80)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private static void WaveEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var translate = new TranslateTransform();
        el.RenderTransform = translate;
        var amplitude = (el.Width > 0 ? el.Width : 960) * 0.00625;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-amplitude, KeyTime.FromPercent(0.2)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(amplitude, KeyTime.FromPercent(0.4)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-amplitude, KeyTime.FromPercent(0.6)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        sb.Children.Add(anim);
    }

    private static void EmphasisPulseEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.5)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);

        AddAuthoredColorOverlay(sb, el, plan);

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.GrowWithColor)
        {
            var scale = new ScaleTransform(1, 1, el.Width / 2, el.Height / 2);
            el.RenderTransform = scale;
            var scaleX = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
            };
            scaleX.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            scaleX.KeyFrames.Add(new LinearDoubleKeyFrame(plan.PeakScale, KeyTime.FromPercent(0.5)));
            scaleX.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            var scaleY = scaleX.Clone();
            Storyboard.SetTarget(scaleX, el);
            Storyboard.SetTarget(scaleY, el);
            Storyboard.SetTargetProperty(scaleX,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(scaleY,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
        }
    }

    private static void ColorWaveEffect(Storyboard sb, FrameworkElement el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.25)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.50)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.75)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
        AddAuthoredColorOverlay(sb, el, plan);
    }

    private static void FillColorEffect(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
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

        var color = new ColorAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs))
        };
        color.KeyFrames.Add(new LinearColorKeyFrame(from, KeyTime.FromPercent(0)));
        color.KeyFrames.Add(new LinearColorKeyFrame(to, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(color, brush);
        Storyboard.SetTargetProperty(color, new PropertyPath(SolidColorBrush.ColorProperty));
        storyboard.Children.Add(color);

        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs)),
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, plan.DurationMs)),
        };
        Storyboard.SetTarget(opacity, rectangle);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static void LineColorEffect(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs)),
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, plan.DurationMs)),
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static void FontStyleEffect(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs)),
            Duration = TimeSpan.FromMilliseconds(1),
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static void FontSizeEffect(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs)),
            Duration = TimeSpan.FromMilliseconds(1),
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static void AddAuthoredColorOverlay(
        Storyboard storyboard,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        if (plan.ColorFromHex is null
            || plan.ColorToHex is null
            || element is not Image image
            || image.Source is not ImageSource source
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
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        Canvas.SetLeft(tint, double.IsNaN(left) ? 0 : left);
        Canvas.SetTop(tint, double.IsNaN(top) ? 0 : top);
        parent.Children.Add(tint);

        var color = new ColorAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs))
        };
        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ColorWave)
        {
            color.KeyFrames.Add(new LinearColorKeyFrame(from, KeyTime.FromPercent(0)));
            color.KeyFrames.Add(new LinearColorKeyFrame(to, KeyTime.FromPercent(0.25)));
            color.KeyFrames.Add(new LinearColorKeyFrame(from, KeyTime.FromPercent(0.50)));
            color.KeyFrames.Add(new LinearColorKeyFrame(to, KeyTime.FromPercent(0.75)));
            color.KeyFrames.Add(new LinearColorKeyFrame(from, KeyTime.FromPercent(1)));
        }
        else
        {
            color.KeyFrames.Add(new LinearColorKeyFrame(from, KeyTime.FromPercent(0)));
            color.KeyFrames.Add(new LinearColorKeyFrame(to, KeyTime.FromPercent(0.5)));
            color.KeyFrames.Add(new LinearColorKeyFrame(
                plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeColor ? to : from,
                KeyTime.FromPercent(1)));
        }
        Storyboard.SetTarget(color, brush);
        Storyboard.SetTargetProperty(color, new PropertyPath(SolidColorBrush.ColorProperty));
        storyboard.Children.Add(color);

        var opacity = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, plan.DelayMs))
        };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ColorWave)
        {
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.25)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.50)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.75)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        }
        else
        {
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.5)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
                plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeColor ? 0.65 : 0,
                KeyTime.FromPercent(1)));
        }
        Storyboard.SetTarget(opacity, tint);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);
    }

    private static bool TryParseAnimationColor(string value, out Color color)
    {
        color = default;
        if (!DrawingMlRgbColor.TryParseHexRgb(value, out var rgb))
            return false;

        color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        return true;
    }

    private static bool TryParseAnimationColorHex(string value, out SrgbColor color)
    {
        if (DrawingMlRgbColor.TryParseHexRgb(value, out var rgb))
        {
            color = new SrgbColor(rgb.R, rgb.G, rgb.B);
            return true;
        }

        color = SrgbColor.Black;
        return false;
    }

    /// <summary>
    /// Motion-path animation: translates the shape along the normalized path in DIP space.
    /// The path coords (0..1 relative to shape center) are scaled to actual slide DIP dimensions.
    /// We sample the path using DoubleAnimationUsingKeyFrames at 30 discrete frames.
    /// The element must be visible (Opacity=1) from the start.
    /// </summary>
    private void MotionPathEffect(
        Storyboard sb,
        FrameworkElement element,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double slideW = _slideDipW > 0 ? _slideDipW : 960;
        double slideH = _slideDipH > 0 ? _slideDipH : 540;

        // Ensure visible
        element.Opacity = 1;

        var translate = new TranslateTransform(0, 0);
        element.RenderTransform = translate;

        var delay    = TimeSpan.FromMilliseconds(plan.DelayMs);

        var animX = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        var animY = new DoubleAnimationUsingKeyFrames { BeginTime = delay };

        foreach (var frame in plan.MotionKeyFrames)
        {
            double dxDip = frame.OffsetXFactor * slideW;
            double dyDip = frame.OffsetYFactor * slideH;

            var keyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * frame.Progress));
            animX.KeyFrames.Add(new LinearDoubleKeyFrame(dxDip, keyTime));
            animY.KeyFrames.Add(new LinearDoubleKeyFrame(dyDip, keyTime));
        }

        Storyboard.SetTarget(animX, element);
        Storyboard.SetTarget(animY, element);
        Storyboard.SetTargetProperty(animX,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        Storyboard.SetTargetProperty(animY,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        sb.Children.Add(animX);
        sb.Children.Add(animY);
    }

    /// <summary>
    /// Best-effort fallback for shapes without an overlay element.  Entrance and motion
    /// shapes stay suppressed until their step completes; exit shapes are suppressed at
    /// completion.  Emphasis retains the existing slide-wide flash because there is no
    /// shape surface on which to paint the effect.
    /// </summary>
    private void PlayFallbackAnimation(ShapeAnimation animation, int delayMs, int durationMs)
    {
        var visibilityPlan = SlideShowPlaybackPlanner.PlanFallbackVisibility(animation);
        if (visibilityPlan.SuppressAtStart || visibilityPlan.SuppressAtCompletion)
        {
            if (visibilityPlan.SuppressAtStart)
            {
                _slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);
                _slideCanvas.Refresh();
            }

            var visibility = new Storyboard();
            var hold = new DoubleAnimation(
                1,
                1,
                new Duration(TimeSpan.FromMilliseconds(Math.Max(0, durationMs))))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delayMs))
            };
            Storyboard.SetTarget(hold, _slideCanvas);
            Storyboard.SetTargetProperty(hold, new PropertyPath(OpacityProperty));
            visibility.Children.Add(hold);
            visibility.Completed += (_, _) =>
            {
                if (visibilityPlan.SuppressAtCompletion)
                    _slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);
                else
                    RevealShape(animation.ShapeId);
                _slideCanvas.Refresh();
            };
            _pendingStoryboards.Add(visibility);
            visibility.Begin(_slideCanvas, isControllable: true);
            return;
        }

        PlayFallbackAnimation(
            SlideShowPlaybackPlanner.PlanFallbackAnimation(animation, delayMs));
    }

    private void PlayFallbackAnimation(SlideShowFallbackAnimationPlaybackPlan? plan)
    {
        if (plan is null) return;

        var sb = new Storyboard();
        var halfDur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs / 2));

        var flashAnim = new DoubleAnimation(plan.FromOpacity, plan.FlashOpacity, halfDur)
        {
            AutoReverse    = true,
            BeginTime      = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(flashAnim, _slideCanvas);
        Storyboard.SetTargetProperty(flashAnim, new PropertyPath(OpacityProperty));
        sb.Children.Add(flashAnim);
        _pendingStoryboards.Add(sb);
        sb.Begin(_slideCanvas, isControllable: true);
    }

    // ── Teardown ──────────────────────────────────────────────────────────────────

    private void Teardown(DateTimeOffset? nowUtc = null)
        => _runtime.CloseRendererSession(nowUtc);
}
