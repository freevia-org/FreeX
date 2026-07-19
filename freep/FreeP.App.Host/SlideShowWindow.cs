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
/// and Push (bidirectional displacement), and Flash (white-flash). Others fall back to Fade.
/// </summary>
public sealed class SlideShowWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly Presentation    _presentation;
    private readonly SlideShowPlaybackRoute _playbackRoute;
    private readonly SlideShowController _controller;
    private readonly SlideShowSessionController _session;
    private readonly DispatcherTimer  _autoAdvanceTimer;
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

    // Manages MediaElement lifecycle for the current slide's media shapes.
    private readonly SlideShowMediaController _mediaController;

    // Per-shape animation state for the current slide.
    // Maps shapeId → the Image element in _animOverlay that represents that shape.
    private readonly Dictionary<uint, FrameworkElement> _animElements = new();

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
        : this(presentation, playbackRoute, captureBackend: null)
    {
    }

    internal SlideShowWindow(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        ISlideShowRecordingCaptureBackend? captureBackend)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _playbackRoute = playbackRoute ?? throw new ArgumentNullException(nameof(playbackRoute));
        _controller   = new SlideShowController(_playbackRoute.Slides, _playbackRoute.StartIndex);
        _session = new SlideShowSessionController(
            _presentation,
            _playbackRoute,
            DateTimeOffset.UtcNow,
            captureBackend ?? CreateDefaultRecordingCaptureBackend());

        // Pre-compute slide DIP dimensions so HitTestHyperlink works even before the first
        // DisplayCurrentSlide call (e.g. in unit tests that construct but don't show the window).
        var metrics = SlideShowHostPlanner.BuildSlideMetrics(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
        _slideDipW = metrics.WidthDip;
        _slideDipH = metrics.HeightDip;

        // Window chrome
        WindowStyle  = WindowStyle.None;
        WindowState  = WindowState.Maximized;
        Topmost      = true;
        Background   = Brushes.Black;
        Focusable    = true;
        ResizeMode   = ResizeMode.NoResize;

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

        // Media controller: created now; EnterSlide is called per-slide in DisplayCurrentSlide.
        _mediaController = new SlideShowMediaController(_mediaOverlay);

        _root.Children.Add(stage);

        Content = _root;

        // ── Auto-advance timer ─────────────────────────────────────────────────
        _autoAdvanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            IsEnabled = false
        };
        _autoAdvanceTimer.Tick += (_, _) => DoAdvance();

        // ── Event wiring ───────────────────────────────────────────────────────
        KeyDown              += OnKeyDown;
        MouseLeftButtonDown  += OnMouseLeftButtonDown;
        MouseLeftButtonUp    += OnMouseLeftButtonUp;
        MouseMove            += OnMouseMove;
        Loaded               += (_, _) => { Focus(); DisplayCurrentSlide(animated: false); };
        Closed               += (_, _) => Teardown();
    }

    // ── Public API (callable by test code without showing the window) ─────────────

    /// <summary>
    /// Execute a single logical advance step and return what happened.
    /// Drives the state machine and applies visual effects if the window is loaded.
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

    /// <summary>The underlying state machine (for test assertions).</summary>
    public SlideShowController Controller => _controller;

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

    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest => _lastAnimationFramePlan;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest => _lastAnimationStepFrameEvidence;
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest => _lastAnimationStepPlaybackReadinessPlan;
    internal SlideShowPlaybackRoute PlaybackRoute => _playbackRoute;
    internal int CurrentPresentationSlideIndex => _session.CurrentPresentationSlideIndex;

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

    private static ISlideShowRecordingCaptureBackend CreateDefaultRecordingCaptureBackend() =>
        new WindowsRecordingCaptureBackend(
            new WindowsRecordingHostMetadata(
                "WPF slideshow",
                "WPF Windows recording capture adapter",
                "ppt/media/freep-recordings/wpf"));

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var command = SlideShowHostPlanner.PlanKey(e.Key.ToString(), _controller, _playbackRoute.Slides);
        ApplyHostCommand(command);
        e.Handled = command.IsHandled;
    }

    // ── Navigation helpers ────────────────────────────────────────────────────────

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slide = _controller.CurrentSlide;
        var clickPt = e.GetPosition(_slideCanvas);
        var inkResult = BeginPresenterInkStroke(clickPt.X, clickPt.Y);
        if (inkResult.IsHandled)
        {
            e.Handled = true;
            return;
        }

        // Check if the click lands on a media shape — toggle play/pause and consume the click
        // so it does NOT also advance the slideshow.
        if (slide is not null && slide.Shapes.Any(s => s.Kind == SlideShapeKind.Media))
        {
            double cw = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
            double ch = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
            if (_mediaController.TryHandleClick(clickPt.X, clickPt.Y, slide, cw, ch))
            {
                e.Handled = true;
                return;
            }
        }

        var pointerIntent = SlideShowHostPlanner.PlanPointerClick(
            slide,
            SlideShowHostPlanner.MapCanvasPointToSlide(
                clickPt.X,
                clickPt.Y,
                _slideCanvas.ActualWidth,
                _slideCanvas.ActualHeight,
                CurrentSlideMetrics()),
            _presentation);
        switch (pointerIntent.Kind)
        {
            case SlideShowPointerClickIntentKind.Trigger when pointerIntent.TriggerShapeId is uint triggerShapeId:
                PlayTriggerGroup(triggerShapeId);
                break;
            case SlideShowPointerClickIntentKind.Zoom when pointerIntent.TargetSlideIndex is int targetSlideIndex:
                ApplyHostCommand(SlideShowHostPlanner.PlanZoomNavigation(
                    _controller, _presentation.Slides, targetSlideIndex));
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

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_slideCanvas);
        var inkResult = EndPresenterInkStroke(pt.X, pt.Y);
        e.Handled = inkResult.IsHandled;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var slide = _controller.CurrentSlide;
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
    {
        var slidePoint = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.ActualWidth,
            _slideCanvas.ActualHeight,
            CurrentSlideMetrics());
        return SlideShowHostPlanner.HitTestHyperlink(slide, slidePoint);
    }

    /// <summary>
    /// Activates a hyperlink: external → open URL in browser (http/https/mailto only);
    /// internal → navigate the controller to the target slide.
    /// </summary>
    internal void ActivateHyperlink(Hyperlink hlink)
    {
        if (hlink.IsExternal)
        {
            OpenExternalUrl(hlink.Url!);
        }
        else if (hlink.TargetSlideId is not null)
        {
            ApplyHostCommand(SlideShowHostPlanner.PlanInternalSlideJump(
                _controller,
                _playbackRoute.Slides,
                hlink.TargetSlideId));
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
            uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            }));
    }

    /// <summary>
    /// Hit-tests the click point (in slide-canvas DIP coords) against trigger shapes on the slide.
    /// Returns the TriggerShapeId if a trigger shape was hit, null otherwise.
    /// </summary>
    private uint? HitTestTriggerShape(Slide slide, double canvasX, double canvasY)
    {
        var slidePoint = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.ActualWidth,
            _slideCanvas.ActualHeight,
            CurrentSlideMetrics());
        return SlideShowHostPlanner.HitTestTriggerShape(slide, slidePoint);
    }

    private SlideShowInkPoint MapPresenterInkPoint(double canvasX, double canvasY)
    {
        var point = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX,
            canvasY,
            _slideCanvas.ActualWidth,
            _slideCanvas.ActualHeight,
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

        var canvasWidth = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : _slideDipW;
        var canvasHeight = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
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
        _session.InkExecutionState.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter => Cursors.Pen,
            SlideShowPresenterPointerMode.Eraser => Cursors.Cross,
            _ => Cursors.Arrow
        };

    /// <summary>
    /// Advances the interactive sequence for <paramref name="triggerShapeId"/> by ONE step,
    /// mirroring how the main sequence advances one click-step at a time.
    /// Subsequent clicks on the same trigger shape advance further through its step list.
    /// </summary>
    private void PlayTriggerGroup(uint triggerShapeId)
    {
        ApplyHostCommand(SlideShowHostPlanner.PlanTrigger(_controller, triggerShapeId));
    }

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

    private void NavigateToSlide(Slide slide, int index, bool animated)
    {
        _ = slide;  // passed for callers that need it; we use _controller.CurrentSlide
        _ = index;
        DisplayCurrentSlide(animated);
    }

    private void ApplyHostCommand(SlideShowHostCommand command, DateTimeOffset? nowUtc = null)
    {
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
                NavigateToSlide(command.Slide, command.SlideIndex, command.AnimateSlide);
                break;
        }
    }

    private void MovePresenterTimingToSlide(int slideIndex, DateTimeOffset nowUtc)
    {
        _session.MoveToSlide(slideIndex, nowUtc);
    }

    // ── Slide display + transitions ───────────────────────────────────────────────

    /// <summary>
    /// Renders the controller's current slide with the optional entry transition.
    /// When <paramref name="animated"/> is false (first display, Home/End, Back), skip the transition.
    /// </summary>
    private void DisplayCurrentSlide(bool animated)
    {
        var plan = SlideShowHostPlanner.BuildDisplayPlan(_presentation, _controller, animated);
        _slideDipW = plan.Metrics.WidthDip;
        _slideDipH = plan.Metrics.HeightDip;
        // Ink state follows the route through the shared session controller.
        RefreshInkOverlay();

        var slide = plan.Slide;
        if (slide is null) return;

        // Prepare animation overlay for the new slide.
        PrepareAnimationOverlay(slide);

        // Set up media playback for any media shapes on the new slide.
        // Use actual canvas dimensions when available; fall back to slide DIP size.
        double mediaCanvasW = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
        double mediaCanvasH = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        _mediaController.EnterSlide(slide, _slideDipW, _slideDipH, mediaCanvasW, mediaCanvasH);

        // Apply transition if requested.
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
        // Play transition sound first (fire-and-forget; swallowed on error).
        PlayTransitionSound(t);

        var plan = SlideShowPlaybackPlanner.PlanTransition(t);
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
        switch (plan.ActionKind)
        {
            case SlideShowTransitionPlaybackActionKind.ShowInstant:
                ShowSlideInstant(slide);
                return;

            case SlideShowTransitionPlaybackActionKind.Fade:
                PlayFadeTransition(slide, plan.DurationMs);
                return;

            case SlideShowTransitionPlaybackActionKind.Flash:
                PlayFlashTransition(slide, plan.DurationMs);
                return;

            case SlideShowTransitionPlaybackActionKind.Dissolve:
                PlayDissolveTransition(slide, plan.DurationMs);
                return;

            case SlideShowTransitionPlaybackActionKind.Box:
                PlayBoxTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Reveal:
                PlayRevealTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Uncover:
                PlayUncoverTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Cover:
                PlayCoverTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Split:
                PlaySplitTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Blinds:
                PlayBlindsTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.RandomBars:
                PlayRandomBarsTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Strips:
                PlayStripsTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Wheel:
                PlayWheelTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Zoom:
                PlayZoomTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Push:
                PlayPushTransition(slide, plan);
                return;

            default:
                PlayFadeTransition(slide, plan.DurationMs);
                return;
        }
    }

    // ── Transition sound playback ─────────────────────────────────────────────────

    private System.Windows.Media.MediaPlayer? _transitionSoundPlayer;

    /// <summary>
    /// Plays the transition sound (if any) using WPF MediaPlayer on a temp file.
    /// Fire-and-forget; errors are silently swallowed.
    /// </summary>
    private void PlayTransitionSound(SlideTransition t)
    {
        if (t.Sound?.AudioBytes is not { Length: > 0 }) return;

        try
        {
            // Stop any previous transition sound.
            _transitionSoundPlayer?.Stop();
            _transitionSoundPlayer?.Close();
            _transitionSoundPlayer = null;

            // Write audio to a temp file (MediaPlayer requires a URI/file path).
            var sound = t.Sound;
            var ext   = sound.ContentType switch
            {
                "audio/mpeg" or "audio/mp3" => ".mp3",
                "audio/wav"                 => ".wav",
                "audio/ogg"                 => ".ogg",
                "audio/aac"                 => ".aac",
                "audio/x-ms-wma"            => ".wma",
                _                           => ".mp3"
            };
            var tmpPath = System.IO.Path.GetTempFileName() + ext;
            System.IO.File.WriteAllBytes(tmpPath, sound.AudioBytes);

            var player = new System.Windows.Media.MediaPlayer();
            player.Open(new Uri(tmpPath, UriKind.Absolute));
            player.Play();
            _transitionSoundPlayer = player;

            // Clean up temp file after playback (best-effort).
            player.MediaEnded += (_, _) =>
            {
                player.Close();
                try { System.IO.File.Delete(tmpPath); } catch { /* ignore */ }
            };
        }
        catch
        {
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

        _slideCanvas.Slide = slide;
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
            _transitionBackImage.Visibility = Visibility.Collapsed;
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animationX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animationY);
    }

    private void PrepareAnimationOverlay(Slide slide)
    {
        // Clear previous overlay.
        foreach (var sb in _pendingStoryboards) sb.Stop();
        _pendingStoryboards.Clear();

        _animOverlay.Children.Clear();
        _animElements.Clear();
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
            var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
            if (shape is null) continue;

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

        foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))
        {
            var anim = plan.Animation;
            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                // Shapes without a renderable overlay retain the coarse fallback rather than
                // guessing a direction or clip geometry.
                PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(anim, plan.DelayMs));
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
                SpinEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bounce:
                BounceEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Float:
                FloatEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Swoop:
                SwoopEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Boomerang:
                BoomerangEffect(sb, element, plan);
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

            case SlideShowShapeAnimationEffectKind.Wave:
                WaveEffect(sb, element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorPulse:
            case SlideShowShapeAnimationEffectKind.ChangeColor:
            case SlideShowShapeAnimationEffectKind.GrowWithColor:
            case SlideShowShapeAnimationEffectKind.Shimmer:
            case SlideShowShapeAnimationEffectKind.Bold:
            case SlideShowShapeAnimationEffectKind.Underline:
                EmphasisPulseEffect(sb, element, plan);
                break;

            default:
                // Unknown preset → instant appear
                AppearEffect(sb, element, plan.DelayMs);
                break;
        }

        AttachEntranceCompletion(sb, plan);
        _pendingStoryboards.Add(sb);
        sb.Begin(element, isControllable: true);
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

    private void FloatEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        double height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
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
        el.RenderTransform = translate;
        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        AddFloatAxisAnimation(
            sb,
            el,
            startX,
            midX,
            endX,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.X)");
        AddFloatAxisAnimation(
            sb,
            el,
            startY,
            midY,
            endY,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");
        var opacity = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        Storyboard.SetTarget(opacity, el);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        sb.Children.Add(opacity);
    }

    private static void AddFloatAxisAnimation(
        Storyboard sb,
        FrameworkElement el,
        double start,
        double middle,
        double end,
        Duration duration,
        int delayMs,
        string propertyPath)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = duration
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            middle,
            KeyTime.FromPercent(0.72),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            end,
            KeyTime.FromPercent(1),
            new KeySpline(0.2, 0, 0.2, 1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        sb.Children.Add(animation);
    }

    private void SwoopEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        double height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
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
        el.RenderTransform = translate;
        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        AddSwoopAxisAnimation(
            sb,
            el,
            startX,
            midX,
            endX,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.X)");
        AddSwoopAxisAnimation(
            sb,
            el,
            startY,
            midY,
            endY,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");
        var opacity = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        Storyboard.SetTarget(opacity, el);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        sb.Children.Add(opacity);
    }

    private static void AddSwoopAxisAnimation(
        Storyboard sb,
        FrameworkElement el,
        double start,
        double middle,
        double end,
        Duration duration,
        int delayMs,
        string propertyPath)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = duration
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            middle,
            KeyTime.FromPercent(0.55),
            new KeySpline(0.1, 0, 0.25, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            end,
            KeyTime.FromPercent(1),
            new KeySpline(0.25, 0, 0.2, 1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        sb.Children.Add(animation);
    }

    private void BoomerangEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        double height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
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
        el.RenderTransform = translate;
        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        AddBoomerangAxisAnimation(
            sb,
            el,
            startX,
            overshootX,
            endX,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.X)");
        AddBoomerangAxisAnimation(
            sb,
            el,
            startY,
            overshootY,
            endY,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");
        var opacity = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        Storyboard.SetTarget(opacity, el);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        sb.Children.Add(opacity);
    }

    private static void AddBoomerangAxisAnimation(
        Storyboard sb,
        FrameworkElement el,
        double start,
        double overshoot,
        double end,
        Duration duration,
        int delayMs,
        string propertyPath)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = duration
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            overshoot,
            KeyTime.FromPercent(0.78),
            new KeySpline(0.2, 0, 0.3, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            end,
            KeyTime.FromPercent(1),
            new KeySpline(0.2, 0, 0.2, 1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        sb.Children.Add(animation);
    }

    private void BounceEffect(Storyboard sb, FrameworkElement el,
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        double width = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : 960;
        double height = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;
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
        el.RenderTransform = translate;
        var duration = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        AddBounceAxisAnimation(
            sb,
            el,
            startX,
            endX,
            overshootX,
            reboundX,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.X)");
        AddBounceAxisAnimation(
            sb,
            el,
            startY,
            endY,
            overshootY,
            reboundY,
            duration,
            plan.DelayMs,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");

        var opacity = new DoubleAnimation(plan.FromOpacity, plan.ToOpacity, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(opacity, el);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        sb.Children.Add(opacity);
    }

    private static void AddBounceAxisAnimation(
        Storyboard sb,
        FrameworkElement el,
        double start,
        double end,
        double overshoot,
        double rebound,
        Duration duration,
        int delayMs,
        string propertyPath)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = duration
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            end,
            KeyTime.FromPercent(0.55),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            overshoot,
            KeyTime.FromPercent(0.72),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            rebound,
            KeyTime.FromPercent(0.86),
            new KeySpline(0.2, 0, 0.4, 1)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(animation, el);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        sb.Children.Add(animation);
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

        var clip = new RectangleGeometry();
        el.Clip = clip;
        el.Opacity = 1;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        Rect from;
        Rect to;

        var isExit = plan.Animation.Kind == AnimationKind.Exit;
        if (plan.WipeHorizontal)
        {
            from = isExit ? new Rect(0, 0, w, h) : new Rect(w / 2, 0, 0, h);
            to = isExit ? new Rect(w / 2, 0, 0, h) : new Rect(0, 0, w, h);
        }
        else
        {
            from = isExit ? new Rect(0, 0, w, h) : new Rect(0, h / 2, w, 0);
            to = isExit ? new Rect(0, h / 2, w, 0) : new Rect(0, 0, w, h);
        }

        clip.Rect = from;
        var anim = new RectAnimation(from, to, dur)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Clip.Rect"));
        sb.Children.Add(anim);
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
        var scale = new ScaleTransform(plan.FromScale, plan.FromScale, cx, cy);
        el.RenderTransform = scale;

        var animSX = BuildGrowShrinkScaleAnimation(plan);
        Storyboard.SetTarget(animSX, el);
        Storyboard.SetTargetProperty(animSX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

        var animSY = BuildGrowShrinkScaleAnimation(plan);
        Storyboard.SetTarget(animSY, el);
        Storyboard.SetTargetProperty(animSY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        sb.Children.Add(animSX);
        sb.Children.Add(animSY);
    }

    private static DoubleAnimationUsingKeyFrames BuildGrowShrinkScaleAnimation(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(plan.FromScale, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            plan.PeakScale,
            KeyTime.FromPercent(0.5),
            new KeySpline(0.2, 0, 0.2, 1)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            plan.ToScale,
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
    /// Best-effort fallback for shapes without an overlay element (emphasis, exit, or
    /// entrance shapes that failed to render into the overlay).
    /// Applies a brief opacity flash on the main SlideCanvas (coarse but visible).
    /// </summary>
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
    {
        if (_session.IsClosed)
        {
            return;
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        _session.Close(now);
        _autoAdvanceTimer.Stop();
        foreach (var sb in _pendingStoryboards)
        {
            try { sb.Stop(); } catch { /* ignore */ }
        }
        _pendingStoryboards.Clear();

        // Stop transition sound player.
        try { _transitionSoundPlayer?.Stop(); _transitionSoundPlayer?.Close(); } catch { /* ignore */ }
        _transitionSoundPlayer = null;

        // Stop all media players and delete temp files.
        _mediaController.Teardown();
    }
}
