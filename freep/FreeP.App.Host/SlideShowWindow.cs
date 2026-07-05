using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
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
/// the transition kind. Supported: Fade (cross-fade), Cut/None (instant), Push/Cover/Wipe/Uncover
/// (directional translate + optional clip). Others fall back to Fade.
/// </summary>
public sealed class SlideShowWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly Presentation    _presentation;
    private readonly SlideShowPlaybackRoute _playbackRoute;
    private readonly SlideShowController _controller;
    private readonly DateTimeOffset _presenterStartedAtUtc;
    private readonly DispatcherTimer  _autoAdvanceTimer;
    private SlideShowPresenterToolPlan _presenterToolPlan = SlideShowPresenterToolPlanner.BuildPlan();
    private SlideShowTimingRecorderState _timingRecorderState;
    private SlideShowRecordingExecutionState _recordingExecutionState;
    private SlideShowInkExecutionState _inkExecutionState;
    private bool _isTornDown;

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
            SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex))
    {
    }

    /// <param name="presentation">The presentation that owns slide size, theme, and timing state.</param>
    /// <param name="playbackRoute">The ordered slide route to play.</param>
    public SlideShowWindow(Presentation presentation, SlideShowPlaybackRoute playbackRoute)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _playbackRoute = playbackRoute ?? throw new ArgumentNullException(nameof(playbackRoute));
        _controller   = new SlideShowController(_playbackRoute.Slides, _playbackRoute.StartIndex);
        _presenterStartedAtUtc = DateTimeOffset.UtcNow;
        _timingRecorderState = SlideShowTimingRecorderPlanner.CreateState(
            CurrentPresentationSlideIndex,
            _presenterStartedAtUtc);
        _recordingExecutionState = SlideShowRecordingExecutionPlanner.CreateState(
            _presenterToolPlan,
            CurrentPresentationSlideIndex,
            _presenterStartedAtUtc,
            SlideShowRecordingHostCapabilities.Deferred("WPF slideshow"));
        _inkExecutionState = SlideShowInkExecutionPlanner.CreateState(
            _controller.CurrentSlideIndex,
            _presenterToolPlan.PointerInk);

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

    public DateTimeOffset PresenterStartedAtUtc => _presenterStartedAtUtc;

    public SlideShowPresenterToolPlan PresenterToolPlan => _presenterToolPlan;

    public IReadOnlyList<SlideShowPresenterWorkflowAction> PresenterWorkflowActions =>
        _presenterToolPlan.WorkflowActions;

    public IReadOnlyList<SlideShowPresenterCommandState> PresenterCommandStates =>
        _presenterToolPlan.CommandStates;

    public SlideShowTimingRecorderState TimingRecorderState => _timingRecorderState;

    public SlideShowRecordingExecutionState RecordingExecutionState => _recordingExecutionState;

    public IReadOnlyList<SlideShowRecordingExecutionAction> RecordingExecutionActions =>
        _recordingExecutionState.LastActions;

    public bool IsPresenterSessionClosed => _isTornDown;

    public SlideShowInkExecutionState InkExecutionState => _inkExecutionState;
    public SlideShowPresenterSessionSummary PresenterSessionSummary =>
        SlideShowPresenterSessionSummaryPlanner.BuildSummary(
            _recordingExecutionState,
            _inkExecutionState,
            _presentation,
            _playbackRoute.GetSourceSlideIndex);

    public SlideShowRecordingReviewPlan RecordingReviewPlan =>
        SlideShowRecordingReviewPlanner.BuildPlan(_presentation, _recordingExecutionState);

    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal SlideShowPlaybackRoute PlaybackRoute => _playbackRoute;
    internal int CurrentPresentationSlideIndex => _playbackRoute.GetSourceSlideIndex(_controller.CurrentSlideIndex);

    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        SlideShowHostPlanner.BuildPresenterState(
            _presentation,
            _controller,
            _playbackRoute.Slides,
            _presenterStartedAtUtc,
            nowUtc,
            displayIntent,
            _presenterToolPlan);

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
        var timingIntentChanged = _presenterToolPlan.Recording.TimingIntent != timingIntent;
        if (timingIntentChanged)
        {
            FinalizePresenterTiming(now);
        }

        _presenterToolPlan = SlideShowPresenterToolPlanner.BuildPlan(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision);
        _recordingExecutionState = SlideShowRecordingExecutionPlanner.ApplyToolPlan(
            _recordingExecutionState,
            _presenterToolPlan,
            CurrentPresentationSlideIndex,
            now);
        _inkExecutionState = SlideShowInkExecutionPlanner.SelectPointerInk(
            _inkExecutionState,
            _presenterToolPlan.PointerInk);
        if (timingIntentChanged)
        {
            _timingRecorderState = SlideShowTimingRecorderPlanner.EnterSlide(
                _timingRecorderState,
                CurrentPresentationSlideIndex,
                now).State;
        }

        RefreshInkOverlay();
        return _presenterToolPlan;
    }

    public SlideShowInkExecutionResult BeginPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.Begin(
            _inkExecutionState,
            MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult AppendPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.Append(
            _inkExecutionState,
            MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult EndPresenterInkStroke(double canvasX, double canvasY) =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.End(
            _inkExecutionState,
            MapPresenterInkPoint(canvasX, canvasY)));

    public SlideShowInkExecutionResult ClearPresenterInkStrokes() =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.ClearCurrentSlide(_inkExecutionState));

    public SlideShowInkExecutionResult UndoLastPresenterInkStroke() =>
        ApplyInkExecution(SlideShowInkExecutionPlanner.UndoLastStroke(_inkExecutionState));

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

        // Check if the click lands on a trigger shape first.
        if (slide is not null && slide.Animations.Any(a => a.TriggerShapeId is not null))
        {
            var triggerShapeId = HitTestTriggerShape(slide, clickPt.X, clickPt.Y);
            if (triggerShapeId is not null)
            {
                PlayTriggerGroup(triggerShapeId.Value);
                e.Handled = true;
                return;
            }
        }

        // Check if the click lands on a hyperlinked shape.
        if (slide is not null)
        {
            var hlink = HitTestHyperlink(slide, clickPt.X, clickPt.Y);
            if (hlink is not null)
            {
                ActivateHyperlink(hlink);
                e.Handled = true;
                return;
            }
        }

        // Not a trigger or hyperlink — regular advance.
        DoAdvance();
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
        _inkExecutionState = result.State;
        RefreshInkOverlay();
        return result;
    }

    private void RefreshInkOverlay()
    {
        _inkOverlay.Children.Clear();

        var canvasWidth = _slideCanvas.ActualWidth > 0 ? _slideCanvas.ActualWidth : _slideDipW;
        var canvasHeight = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        var plan = SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(
            _inkExecutionState,
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
        _inkExecutionState.ActivePointerMode switch
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
        FinalizePresenterTiming(nowUtc);
        _timingRecorderState = SlideShowTimingRecorderPlanner.EnterSlide(
            _timingRecorderState,
            _playbackRoute.GetSourceSlideIndex(slideIndex),
            nowUtc).State;
        _recordingExecutionState = SlideShowRecordingExecutionPlanner.MoveToSlide(
            _recordingExecutionState,
            _playbackRoute.GetSourceSlideIndex(slideIndex),
            nowUtc);
    }

    private void FinalizePresenterTiming(DateTimeOffset nowUtc)
    {
        var result = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            _timingRecorderState,
            _presenterToolPlan,
            nowUtc);
        _timingRecorderState = result.State;
        SlideShowTimingRecorderPlanner.ApplyTimings(_presentation, result.Mutations);
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
        _inkExecutionState = SlideShowInkExecutionPlanner.MoveToSlide(
            _inkExecutionState,
            _controller.CurrentSlideIndex);
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
        _slideCanvas.Slide = slide;
        _slideCanvas.Opacity = 1;
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
        switch (plan.ActionKind)
        {
            case SlideShowTransitionPlaybackActionKind.ShowInstant:
                ShowSlideInstant(slide);
                return;

            case SlideShowTransitionPlaybackActionKind.Fade:
                PlayFadeTransition(slide, plan.DurationMs);
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

    private void PlayPushTransition(Slide slide, SlideShowTransitionPlaybackPlan plan)
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

    // ── Shape animation overlay ───────────────────────────────────────────────────

    /// <summary>
    /// Sets up per-shape animated elements for a new slide:
    ///  1. Identifies shapes with Entrance animations → renders each to a bitmap
    ///     and places it as an Image in _animOverlay, hidden.
    ///  2. Updates _slideCanvas so entrance-animated shapes show when revealed.
    /// </summary>
    private void PrepareAnimationOverlay(Slide slide)
    {
        // Clear previous overlay.
        foreach (var sb in _pendingStoryboards) sb.Stop();
        _pendingStoryboards.Clear();

        _animOverlay.Children.Clear();
        _animElements.Clear();
        _revealedShapes.Clear();

        // Only hide shapes whose ONLY animations are non-trigger (main-sequence) entrances/motions.
        // A shape whose sole animation is an interactive trigger should be visible at slide entry;
        // the trigger animation plays on the already-visible shape when the user clicks the trigger.
        _entranceShapeIds = slide.Animations
            .Where(a => (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion)
                        && a.TriggerShapeId == null)
            .Select(a => a.ShapeId)
            .Distinct()
            .ToList();

        // If no entrance animations, nothing special to prepare.
        if (_entranceShapeIds.Count == 0) return;

        // Render the whole slide to get per-shape bitmaps via a temporary canvas.
        // We create one overlay Image per entrance-animated shape.
        // We need the slide pixel size for the overlay canvas sizing.
        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        // Place the overlay canvas at the same size/position as the slide canvas.
        _animOverlay.Width  = w;
        _animOverlay.Height = h;

        foreach (var shapeId in _entranceShapeIds)
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
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = shapeId,
            };

            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);

            _animOverlay.Children.Add(img);
            _animElements[shapeId] = img;
        }
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

    // ── Animation step playback ───────────────────────────────────────────────────

    private void PlayAnimationStep(AnimationStep step)
    {
        foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))
        {
            var anim = plan.Animation;
            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                // No overlay element (shape has no entrance overlay or is emphasis/exit):
                // handle emphasis / exit on the live canvas best-effort.
                PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(anim, plan.DelayMs));
                continue;
            }

            PlayShapeAnimation(element, plan);
            _revealedShapes.Add(anim.ShapeId);
        }
    }

    private void PlayShapeAnimation(FrameworkElement element, SlideShowShapeAnimationPlaybackPlan plan)
    {
        var sb = new Storyboard();

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.MotionPath)
        {
            MotionPathEffect(sb, element, plan);
            _pendingStoryboards.Add(sb);
            sb.Begin(element, isControllable: true);
            return;
        }

        switch (plan.EffectKind)
        {
            case SlideShowShapeAnimationEffectKind.Appear:
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

            case SlideShowShapeAnimationEffectKind.Diamond:
                DiamondEffect(sb, element, plan);
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

            default:
                // Unknown preset → instant appear
                AppearEffect(sb, element, plan.DelayMs);
                break;
        }

        _pendingStoryboards.Add(sb);
        sb.Begin(element, isControllable: true);
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

        var animX = new DoubleAnimation(dx, 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs), EasingFunction = ease };
        var animY = new DoubleAnimation(dy, 0, dur)
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

        if (plan.WipeHorizontal)
        {
            clip.Rect = new Rect(0, 0, 0, h);
            var a = new RectAnimation(
                new Rect(0, 0, 0, h), new Rect(0, 0, w, h), dur)
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
            clip.Rect = new Rect(0, 0, w, 0);
            var a = new RectAnimation(
                new Rect(0, 0, w, 0), new Rect(0, 0, w, h), dur)
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

        if (plan.WipeHorizontal)
        {
            from = new Rect(w / 2, 0, 0, h);
            to = new Rect(0, 0, w, h);
        }
        else
        {
            from = new Rect(0, h / 2, w, 0);
            to = new Rect(0, 0, w, h);
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

        var clip = new RectangleGeometry();
        el.Clip = clip;
        el.Opacity = 0;

        var dur = new Duration(TimeSpan.FromMilliseconds(plan.DurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var from = plan.WipeHorizontal ? new Rect(0, 0, 0, h) : new Rect(0, 0, w, 0);
        var to = new Rect(0, 0, w, h);

        clip.Rect = from;
        var clipAnim = new RectAnimation(from, to, dur)
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(clipAnim, el);
        Storyboard.SetTargetProperty(clipAnim, new PropertyPath("Clip.Rect"));
        sb.Children.Add(clipAnim);

        var opacityAnim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(plan.DelayMs)
        };
        opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.35, KeyTime.FromPercent(0.2)));
        opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.7, KeyTime.FromPercent(0.55)));
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
            var (closed, open) = BuildBlindsBand(w, h, bandCount, i, plan.BlindsHorizontal);
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

    private static (Rect Closed, Rect Open) BuildBlindsBand(
        double width,
        double height,
        int bandCount,
        int index,
        bool horizontal)
    {
        if (horizontal)
        {
            var y = height * index / bandCount;
            var nextY = height * (index + 1) / bandCount;
            return (
                new Rect(0, y, width, 0),
                new Rect(0, y, width, Math.Max(0, nextY - y)));
        }

        var x = width * index / bandCount;
        var nextX = width * (index + 1) / bandCount;
        return (
            new Rect(x, 0, 0, height),
            new Rect(x, 0, Math.Max(0, nextX - x), height));
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
                var (closed, open) = BuildCheckerboardCell(
                    w,
                    h,
                    rowCount,
                    columnCount,
                    row,
                    column,
                    plan.CheckerboardHorizontal);
                var from = opens ? closed : open;
                var to = opens ? open : closed;
                var cell = new RectangleGeometry(from);
                cells.Children.Add(cell);

                var anim = new RectAnimation(from, to, dur)
                {
                    BeginTime = TimeSpan.FromMilliseconds(
                        plan.DelayMs + (IsSecondCheckerboardPhase(row, column) ? phaseDelayMs : 0)),
                    EasingFunction = ease
                };
                Storyboard.SetTarget(anim, cell);
                Storyboard.SetTargetProperty(anim, new PropertyPath(RectangleGeometry.RectProperty));
                sb.Children.Add(anim);
            }
        }
    }

    private static (Rect Closed, Rect Open) BuildCheckerboardCell(
        double width,
        double height,
        int rowCount,
        int columnCount,
        int row,
        int column,
        bool horizontal)
    {
        var x = width * column / columnCount;
        var nextX = width * (column + 1) / columnCount;
        var y = height * row / rowCount;
        var nextY = height * (row + 1) / rowCount;
        var cellWidth = Math.Max(0, nextX - x);
        var cellHeight = Math.Max(0, nextY - y);

        return horizontal
            ? (new Rect(x, y, 0, cellHeight), new Rect(x, y, cellWidth, cellHeight))
            : (new Rect(x, y, cellWidth, 0), new Rect(x, y, cellWidth, cellHeight));
    }

    private static bool IsSecondCheckerboardPhase(int row, int column) =>
        ((row + column) & 1) == 1;

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
            BuildDiamondPoint(w, h, vertexIndex: 0, progress: fromProgress),
            BuildDiamondPoint(w, h, vertexIndex: 0, progress: toProgress),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            rightSegment,
            LineSegment.PointProperty,
            BuildDiamondPoint(w, h, vertexIndex: 1, progress: fromProgress),
            BuildDiamondPoint(w, h, vertexIndex: 1, progress: toProgress),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            bottomSegment,
            LineSegment.PointProperty,
            BuildDiamondPoint(w, h, vertexIndex: 2, progress: fromProgress),
            BuildDiamondPoint(w, h, vertexIndex: 2, progress: toProgress),
            dur,
            ease,
            plan.DelayMs);
        AddDiamondPointAnimation(
            sb,
            leftSegment,
            LineSegment.PointProperty,
            BuildDiamondPoint(w, h, vertexIndex: 3, progress: fromProgress),
            BuildDiamondPoint(w, h, vertexIndex: 3, progress: toProgress),
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
            StartPoint = BuildDiamondPoint(width, height, vertexIndex: 0, progress: progress),
            IsClosed = true,
            IsFilled = true
        };
        rightSegment = new LineSegment(BuildDiamondPoint(width, height, vertexIndex: 1, progress: progress), true);
        bottomSegment = new LineSegment(BuildDiamondPoint(width, height, vertexIndex: 2, progress: progress), true);
        leftSegment = new LineSegment(BuildDiamondPoint(width, height, vertexIndex: 3, progress: progress), true);
        figure.Segments.Add(rightSegment);
        figure.Segments.Add(bottomSegment);
        figure.Segments.Add(leftSegment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point BuildDiamondPoint(double width, double height, int vertexIndex, double progress)
    {
        var center = new Point(width / 2, height / 2);
        var full = vertexIndex switch
        {
            0 => new Point(width / 2, 0),
            1 => new Point(width, height / 2),
            2 => new Point(width / 2, height),
            _ => new Point(0, height / 2)
        };

        return new Point(
            center.X + (full.X - center.X) * progress,
            center.Y + (full.Y - center.Y) * progress);
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
        if (_isTornDown)
        {
            return;
        }

        _isTornDown = true;
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        FinalizePresenterTiming(now);
        _recordingExecutionState = SlideShowRecordingExecutionPlanner.EndSession(
            _recordingExecutionState,
            now);
        _inkExecutionState = SlideShowInkPersistencePlanner.ApplyRetentionOnExit(
            _presentation,
            _inkExecutionState,
            _playbackRoute.GetSourceSlideIndex).State;
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
