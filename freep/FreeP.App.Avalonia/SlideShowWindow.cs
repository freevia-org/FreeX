using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

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
/// the transition kind. Supported: Fade (cross-fade), Cut/None (instant), Push/Cover/Wipe/Uncover
/// (directional translate), others fall back to Fade.
///
/// Media
/// ─────
/// Media shapes display the poster bitmap + a play badge (same as the slide renderer).
/// Actual audio/video playback is DEFERRED — Avalonia has no built-in MediaElement;
/// cross-platform video would need LibVLCSharp (out of scope for Theme 24).
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
    // Presenter ink overlay: shared-plan-backed strokes and laser pointer above slide content.
    private readonly Canvas _inkOverlay;

    // Per-shape animation state for the current slide.
    // Maps shapeId → the Image element in _animOverlay that represents that shape.
    private readonly Dictionary<uint, Control> _animElements = new();

    // Track which shapes have been revealed.
    private readonly HashSet<uint> _revealedShapes = new();
    private List<uint> _entranceShapeIds = new();

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

        // Pre-compute slide DIP dimensions.
        var metrics = SlideShowHostPlanner.BuildSlideMetrics(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
        _slideDipW = metrics.WidthDip;
        _slideDipH = metrics.HeightDip;

        // Window chrome — fullscreen borderless.
        WindowState        = WindowState.FullScreen;
        ExtendClientAreaToDecorationsHint = true;
        Topmost            = true;
        Background         = Brushes.Black;
        Focusable          = true;
        CanResize          = false;

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
        stage.Children.Add(_inkOverlay);

        _root = new Panel { Background = Brushes.Black };
        _root.Children.Add(stage);

        Content = _root;

        // ── Auto-advance timer ─────────────────────────────────────────────────
        _autoAdvanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            IsEnabled = false,
        };
        _autoAdvanceTimer.Tick += (_, _) => DoAdvance();

        // ── Event wiring ───────────────────────────────────────────────────────
        KeyDown             += OnKeyDown;
        PointerPressed      += OnPointerPressed;
        PointerMoved        += OnPointerMoved;
        PointerReleased     += OnPointerReleased;
        Opened              += (_, _) => { Focus(); DisplayCurrentSlide(animated: false); };
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
                "Avalonia slideshow",
                "Avalonia Windows recording capture adapter",
                "ppt/media/freep-recordings/avalonia"));

    /// <summary>Exposes the slide canvas for test assertions (DA1 suppression).</summary>
    internal SlideCanvas CanvasForTest => _slideCanvas;

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var command = SlideShowHostPlanner.PlanKey(e.Key.ToString(), _controller, _playbackRoute.Slides);
        ApplyHostCommand(command);
        e.Handled = command.IsHandled;
    }

    // ── Pointer navigation ────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var slide = _controller.CurrentSlide;
        var pt = e.GetPosition(_slideCanvas);
        var inkResult = BeginPresenterInkStroke(pt.X, pt.Y);
        if (inkResult.IsHandled)
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

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var slide = _controller.CurrentSlide;
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
    /// Activates a hyperlink: external → open URL (http/https/mailto only);
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

    private void NavigateToSlide(Slide slide, int index, bool animated)
    {
        _ = slide;
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

    private void DisplayCurrentSlide(bool animated)
    {
        var plan = SlideShowHostPlanner.BuildDisplayPlan(_presentation, _controller, animated);
        _slideDipW = plan.Metrics.WidthDip;
        _slideDipH = plan.Metrics.HeightDip;
        RefreshInkOverlay();

        var slide = plan.Slide;
        if (slide is null) return;

        // DA2: cancel any in-flight transition/animation timers from the PREVIOUS slide so
        // their stale onComplete callbacks don't clobber the new slide's visual state.
        CancelActiveTimers();

        PrepareAnimationOverlay(slide);

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

    private void ShowSlideInstant(Slide slide)
    {
        _transitionBackImage.IsVisible = false;
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
        // Transition sound: Avalonia has no built-in MediaElement.
        // Sound playback on the Avalonia host is deferred / no-op.
        // (The sound bytes are preserved on the model and will re-emit on save.)

        var plan = SlideShowPlaybackPlanner.PlanTransition(t);
        switch (plan.ActionKind)
        {
            case SlideShowTransitionPlaybackActionKind.ShowInstant:
                ShowSlideInstant(slide);
                return;

            case SlideShowTransitionPlaybackActionKind.Fade:
                PlayFadeTransition(slide, plan.DurationMs);
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

            case SlideShowTransitionPlaybackActionKind.Dissolve:
                PlayDissolveTransition(slide, plan.DurationMs);
                return;

            case SlideShowTransitionPlaybackActionKind.Box:
                PlayBoxTransition(slide, plan);
                return;

            case SlideShowTransitionPlaybackActionKind.Push:
                PlayPushTransition(slide, plan);
                return;

            default:
                PlayFadeTransition(slide, plan.DurationMs);
                return;
        }
    }

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

    private void PlayPushTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
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
                _transitionBackImage.IsVisible = false;
            });
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

    private void AnimateDissolveTransitionClip(
        Control target,
        double width,
        double height,
        int durationMs,
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildDissolveTransitionGeometry(width, height, 1);
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
            target.Clip = BuildDissolveTransitionGeometry(width, height, EaseInOut(t));
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildDissolveTransitionGeometry(width, height, 1);
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

        _slideCanvas.Slide = slide;
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
        Action? onComplete = null)
    {
        if (durationMs <= 0)
        {
            target.Clip = BuildSplitGeometry(width, height, 1, horizontal, fromCenter);
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
            target.Clip = BuildSplitGeometry(width, height, EaseInOut(t), horizontal, fromCenter);
            if (frame >= steps)
            {
                timer.Stop();
                _activeTimers.Remove(timer);
                target.Clip = BuildSplitGeometry(width, height, 1, horizontal, fromCenter);
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
        _animElements.Clear();
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
            var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
            if (shape is null) continue;

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

        foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))
        {
            var anim = plan.Animation;
            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(anim, plan.DelayMs));
                continue;
            }

            var shapeId = anim.ShapeId;
            PlayShapeAnimation(element, plan, onReveal: anim.Kind == AnimationKind.Exit ? null : () =>
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
                FadeEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Flash:
                FadeEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Spiral:
                InvokeRevealAtStart(plan, onReveal);
                SpinEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Swivel:
                InvokeRevealAtStart(plan, onReveal);
                SpinEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.Bounce:
                FlyInEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Float:
                FlyInEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Swoop:
                FlyInEffect(element, plan, onReveal);
                break;

            case SlideShowShapeAnimationEffectKind.Boomerang:
                FlyInEffect(element, plan, onReveal);
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

            case SlideShowShapeAnimationEffectKind.Wave:
                InvokeRevealAtStart(plan, onReveal);
                WaveEffect(element, plan);
                break;

            case SlideShowShapeAnimationEffectKind.ColorPulse:
            case SlideShowShapeAnimationEffectKind.ChangeColor:
            case SlideShowShapeAnimationEffectKind.GrowWithColor:
            case SlideShowShapeAnimationEffectKind.Shimmer:
            case SlideShowShapeAnimationEffectKind.Bold:
            case SlideShowShapeAnimationEffectKind.Underline:
                InvokeRevealAtStart(plan, onReveal);
                EmphasisPulseEffect(element, plan);
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
                onComplete: CompleteReveal(plan, onReveal));
            el.Opacity = isExit ? 0.7 : 0.15;
            DelayedAction(plan.DurationMs / 5, () => el.Opacity = isExit ? 0.35 : 0.45);
            DelayedAction(plan.DurationMs / 2, () => el.Opacity = isExit ? 0.15 : 0.75);
            DelayedAction(plan.DurationMs, () => el.Opacity = plan.ToOpacity);
        });
    }

    private void AnimateRandomBarsClip(
        IReadOnlyList<(RectangleGeometry Geometry, Rect From, Rect To, int DelayMs, int DurationMs)> bars,
        int durationMs,
        Action? onComplete = null)
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
                var eased = EaseInOut(t);
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
                onComplete: CompleteReveal(plan, onReveal)));
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
        double from, double to, int durationMs)
    {
        if (durationMs <= 0) { scale.ScaleX = scale.ScaleY = to; return; }

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
            double v = from + (to - from) * eased;
            scale.ScaleX = scale.ScaleY = v;
            if (frame >= steps) { timer.Stop(); _activeTimers.Remove(timer); scale.ScaleX = scale.ScaleY = to; }
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
            AnimateScale(el, scale, 1.0, plan.PeakScale, plan.DurationMs / 2);
            DelayedAction(plan.DurationMs / 2, () =>
                AnimateScale(el, scale, plan.PeakScale, 1.0, plan.DurationMs / 2));
        });
    }

    private void GrowShrinkEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var scale = new ScaleTransform(plan.FromScale, plan.FromScale);
        el.RenderTransform = scale;

        DelayedAction(plan.DelayMs, () =>
        {
            AnimateScale(el, scale, plan.FromScale, plan.PeakScale, plan.DurationMs / 2);
            DelayedAction(plan.DurationMs / 2, () =>
                AnimateScale(el, scale, plan.PeakScale, plan.ToScale, plan.DurationMs / 2));
        });
    }

    private void SpinEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        el.RenderTransformOrigin = RelativePoint.Center;
        var rotate = new RotateTransform(0);
        el.RenderTransform = rotate;

        DelayedAction(plan.DelayMs, () =>
            AnimateRotate(rotate, 0, plan.RotationDegrees, plan.DurationMs));
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
            value => rotate.Angle = value));
    }

    private void BlinkEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.15, 0.25), (1.0, 0.5), (0.15, 0.75), (1.0, 1.0) },
            value => el.Opacity = value));
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
            value => translate.X = value));
    }

    private void EmphasisPulseEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan)
    {
        el.Opacity = 1;
        DelayedAction(plan.DelayMs, () => AnimateKeyframes(
            plan.DurationMs,
            new[] { (1.0, 0.0), (0.65, 0.5), (1.0, 1.0) },
            value => el.Opacity = value));

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.GrowWithColor)
        {
            el.RenderTransformOrigin = RelativePoint.Center;
            var scale = new ScaleTransform(1, 1);
            el.RenderTransform = scale;
            DelayedAction(plan.DelayMs, () =>
            {
                AnimateScale(el, scale, 1, plan.PeakScale, plan.DurationMs / 2);
                DelayedAction(plan.DurationMs / 2, () =>
                    AnimateScale(el, scale, plan.PeakScale, 1, plan.DurationMs / 2));
            });
        }
    }

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

    private void AnimateRotate(RotateTransform rotate, double from, double to, int durationMs)
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
            double eased = EaseInOut(t);
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
        if (_session.IsClosed)
        {
            return;
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        _session.Close(now);
        _autoAdvanceTimer.Stop();
        // DA3: stop ALL per-frame animation/transition timers so they don't keep
        // ticking against the closed window's canvas.  A running DispatcherTimer is
        // rooted by the dispatcher and will NOT be collected automatically.
        CancelActiveTimers();
    }

    /// <summary>Expose active-timer count for test assertions (DA2/DA3).</summary>
    internal int ActiveTimerCount => _activeTimers.Count;
}
