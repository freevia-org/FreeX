using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private readonly SlideShowController _controller;
    private readonly DispatcherTimer  _autoAdvanceTimer;

    // ── Visual tree ───────────────────────────────────────────────────────────────

    // Root: black grid filling the whole window.
    private readonly Grid _root;

    // Transition layers: back (snapshot of outgoing slide), front (incoming slide canvas).
    private readonly Image      _transitionBackImage; // snapshot bitmap of outgoing slide
    private readonly SlideCanvas _slideCanvas;         // live rendered current slide (front layer)

    // Shape animation overlay: a Canvas placed on top of _slideCanvas; populated per-slide.
    private readonly Canvas _animOverlay;

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
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _controller   = new SlideShowController(presentation.Slides, startIndex);

        // Pre-compute slide DIP dimensions so HitTestHyperlink works even before the first
        // DisplayCurrentSlide call (e.g. in unit tests that construct but don't show the window).
        _slideDipW = presentation.SlideSizeCxEmu / 9525.0;
        _slideDipH = presentation.SlideSizeCyEmu / 9525.0;

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
        MouseMove            += OnMouseMove;
        Loaded               += (_, _) => { Focus(); DisplayCurrentSlide(animated: false); };
        Closed               += (_, _) => Teardown();
    }

    // ── Public API (callable by test code without showing the window) ─────────────

    /// <summary>
    /// Execute a single logical advance step and return what happened.
    /// Drives the state machine and applies visual effects if the window is loaded.
    /// </summary>
    public AdvanceResult ExecuteAdvance()
    {
        var result = _controller.Advance();
        switch (result)
        {
            case AdvanceResult.PlayStep ps:
                PlayAnimationStep(ps.Step);
                break;
            case AdvanceResult.NavigateToSlide nav:
                NavigateToSlide(nav.Slide, nav.SlideIndex);
                break;
            case AdvanceResult.AtEnd:
                CloseSlideShow();
                break;
        }
        return result;
    }

    /// <summary>Execute a logical back step and return what happened.</summary>
    public BackResult ExecuteBack()
    {
        var result = _controller.Back();
        if (result is BackResult.NavigateToSlide nav)
            NavigateToSlide(nav.Slide, nav.SlideIndex);
        return result;
    }

    /// <summary>The underlying state machine (for test assertions).</summary>
    public SlideShowController Controller => _controller;

    // ── Keyboard navigation ───────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSlideShow();
                e.Handled = true;
                break;

            // Advance / forward
            case Key.Right:
            case Key.Space:
            case Key.PageDown:
            case Key.Enter:
                DoAdvance();
                e.Handled = true;
                break;

            // Back
            case Key.Left:
            case Key.PageUp:
            case Key.Back:
                DoBack();
                e.Handled = true;
                break;

            case Key.Home:
                _autoAdvanceTimer.Stop();
                _controller.GoToSlide(0);
                DisplayCurrentSlide(animated: false);
                e.Handled = true;
                break;

            case Key.End:
                _autoAdvanceTimer.Stop();
                _controller.GoToSlide(_presentation.Slides.Count - 1);
                DisplayCurrentSlide(animated: false);
                e.Handled = true;
                break;
        }
    }

    // ── Navigation helpers ────────────────────────────────────────────────────────

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slide = _controller.CurrentSlide;

        // Check if the click lands on a trigger shape first.
        if (slide is not null && slide.Animations.Any(a => a.TriggerShapeId is not null))
        {
            var clickPt = e.GetPosition(_slideCanvas);
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
            var clickPt = e.GetPosition(_slideCanvas);
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

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var slide = _controller.CurrentSlide;
        if (slide is null) { Cursor = Cursors.Arrow; return; }
        var pt = e.GetPosition(_slideCanvas);
        var hlink = HitTestHyperlink(slide, pt.X, pt.Y);
        Cursor = hlink is not null ? Cursors.Hand : Cursors.Arrow;
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
        double slideX = CanvasToSlideX(canvasX);
        double slideY = CanvasToSlideY(canvasY);

        return HitTestHyperlinkInShapes(slide.Shapes, slideX, slideY);
    }

    /// <summary>
    /// Recursively searches <paramref name="shapes"/> (and their group children) for a shape
    /// that contains (<paramref name="slideX"/>, <paramref name="slideY"/>) and carries a
    /// hyperlink.  Group bounds are checked first so we only recurse when inside the group.
    /// </summary>
    private static Hyperlink? HitTestHyperlinkInShapes(
        IReadOnlyList<SlideShape> shapes, double slideX, double slideY)
    {
        foreach (var shape in shapes)
        {
            if (!HitTestShape(shape, slideX, slideY)) continue;

            // Shape-level hyperlink takes priority.
            if (shape.Hyperlink is not null) return shape.Hyperlink;

            // Recurse into group children — they share the same coordinate space as the
            // parent slide (group children use absolute EMU offsets, not relative to the group),
            // so no coordinate transform is needed; the same slideX/slideY is correct.
            if (shape.Children.Count > 0)
            {
                var groupResult = HitTestHyperlinkInShapes(shape.Children, slideX, slideY);
                if (groupResult is not null) return groupResult;
            }

            // Run-level: return the first hyperlink found in any run (shape-level approximation).
            if (shape.TextBody is not null)
            {
                foreach (var para in shape.TextBody.Paragraphs)
                    foreach (var run in para.Runs)
                        if (run.Hyperlink is not null) return run.Hyperlink;
            }
        }
        return null;
    }

    private static bool HitTestShape(SlideShape shape, double slideX, double slideY)
    {
        double sx  = shape.OffsetXEmu / 9525.0;
        double sy  = shape.OffsetYEmu / 9525.0;
        double scx = shape.ExtentCxEmu / 9525.0;
        double scy = shape.ExtentCyEmu / 9525.0;
        return slideX >= sx && slideX <= sx + scx && slideY >= sy && slideY <= sy + scy;
    }

    private double CanvasToSlideX(double canvasX)
    {
        double cw = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
        return canvasX * (_slideDipW / cw);
    }

    private double CanvasToSlideY(double canvasY)
    {
        double ch = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        return canvasY * (_slideDipH / ch);
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
            var targetIdx = _presentation.Slides
                .FindIndex(s => s.Id == hlink.TargetSlideId);
            if (targetIdx >= 0)
            {
                _autoAdvanceTimer.Stop();
                _controller.GoToSlide(targetIdx);
                DisplayCurrentSlide(animated: false);
            }
        }
    }

    /// <summary>
    /// Opens an external URL in the default browser.
    /// Only http, https, and mailto schemes are allowed; all others are silently ignored.
    /// </summary>
    internal static void OpenExternalUrl(string url)
    {
        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            if (uri.Scheme is not ("http" or "https" or "mailto"))
                return; // security guard: reject file:// and other schemes
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Swallow — never crash the slideshow over a bad URL.
        }
    }

    /// <summary>
    /// Hit-tests the click point (in slide-canvas DIP coords) against trigger shapes on the slide.
    /// Returns the TriggerShapeId if a trigger shape was hit, null otherwise.
    /// </summary>
    private uint? HitTestTriggerShape(Slide slide, double canvasX, double canvasY)
    {
        // Convert canvas DIP coords to slide DIP coords.
        // The canvas renders the slide at the canonical slide size (_slideDipW x _slideDipH)
        // scaled to fit _slideCanvas.ActualWidth x _slideCanvas.ActualHeight.
        double cw = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : _slideDipW;
        double ch = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : _slideDipH;
        double scaleX = _slideDipW / cw;
        double scaleY = _slideDipH / ch;
        double slideX = canvasX * scaleX;
        double slideY = canvasY * scaleY;

        // Get all unique trigger shape ids.
        var triggerShapeIds = slide.Animations
            .Where(a => a.TriggerShapeId is not null)
            .Select(a => a.TriggerShapeId!.Value)
            .Distinct();

        foreach (var spid in triggerShapeIds)
        {
            var shape = slide.Shapes.FirstOrDefault(s => s.Id == spid);
            if (shape is null) continue;

            double shapeX  = shape.OffsetXEmu / 9525.0;
            double shapeY  = shape.OffsetYEmu / 9525.0;
            double shapeCx = shape.ExtentCxEmu / 9525.0;
            double shapeCy = shape.ExtentCyEmu / 9525.0;

            if (slideX >= shapeX && slideX <= shapeX + shapeCx &&
                slideY >= shapeY && slideY <= shapeY + shapeCy)
                return spid;
        }
        return null;
    }

    /// <summary>
    /// Advances the interactive sequence for <paramref name="triggerShapeId"/> by ONE step,
    /// mirroring how the main sequence advances one click-step at a time.
    /// Subsequent clicks on the same trigger shape advance further through its step list.
    /// </summary>
    private void PlayTriggerGroup(uint triggerShapeId)
    {
        var step = _controller.AdvanceTrigger(triggerShapeId);
        if (step is not null)
            PlayAnimationStep(step);
    }

    private void DoAdvance()
    {
        _autoAdvanceTimer.Stop();
        ExecuteAdvance();
    }

    private void DoBack()
    {
        _autoAdvanceTimer.Stop();
        ExecuteBack();
    }

    private void CloseSlideShow()
    {
        Teardown();
        Close();
    }

    private void NavigateToSlide(Slide slide, int index)
    {
        _ = slide;  // passed for callers that need it; we use _controller.CurrentSlide
        _ = index;
        DisplayCurrentSlide(animated: true);
    }

    // ── Slide display + transitions ───────────────────────────────────────────────

    /// <summary>
    /// Renders the controller's current slide with the optional entry transition.
    /// When <paramref name="animated"/> is false (first display, Home/End, Back), skip the transition.
    /// </summary>
    private void DisplayCurrentSlide(bool animated)
    {
        var slide = _controller.CurrentSlide;
        if (slide is null) return;

        // Compute slide dimensions in DIP.
        _slideDipW = _presentation.SlideSizeCxEmu / 9525.0;
        _slideDipH = _presentation.SlideSizeCyEmu / 9525.0;

        // Prepare animation overlay for the new slide.
        PrepareAnimationOverlay(slide);

        // Apply transition if requested.
        if (animated && slide.Transition is { Kind: not TransitionKind.None } t)
            PlayTransition(slide, t);
        else
            ShowSlideInstant(slide);

        // Wire auto-advance timer.
        _autoAdvanceTimer.Stop();
        if (slide.Transition?.AdvanceAfterMs is int advMs && advMs > 0)
        {
            _autoAdvanceTimer.Interval = TimeSpan.FromMilliseconds(advMs);
            _autoAdvanceTimer.Start();
        }
    }

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
        int ms = Math.Max(50, t.DurationMs);

        switch (t.Kind)
        {
            case TransitionKind.Cut:
                ShowSlideInstant(slide);
                return;

            case TransitionKind.Fade:
            case TransitionKind.Dissolve:
                PlayFadeTransition(slide, ms);
                return;

            case TransitionKind.Push:
            case TransitionKind.Cover:
            case TransitionKind.Wipe:
            case TransitionKind.Uncover:
                PlayPushTransition(slide, t, ms);
                return;

            default:
                // All other kinds fall back to Fade.
                PlayFadeTransition(slide, ms);
                return;
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

    private void PlayPushTransition(Slide slide, SlideTransition t, int durationMs)
    {
        var snapshot = CaptureCurrentSlide();

        // Determine direction vector for the push.
        var (dx, dy) = GetDirectionVector(t.Direction, t.Kind);

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

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
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

    /// <summary>
    /// Returns the (dx, dy) unit vector for the incoming slide's starting position
    /// (before the push animation brings it to centre).
    /// For Push/Cover: slide comes from the opposite edge it pushes toward.
    /// </summary>
    private static (double dx, double dy) GetDirectionVector(
        TransitionDirection? dir, TransitionKind kind)
    {
        // Push/Cover right: incoming comes from the right → starts at +1,0
        // Push left: incoming comes from the left → starts at -1,0
        return dir switch
        {
            TransitionDirection.Right => (-1, 0),   // slide going right, incoming from left
            TransitionDirection.Left  => ( 1, 0),   // slide going left,  incoming from right
            TransitionDirection.Down  => ( 0,-1),   // slide going down,  incoming from top
            TransitionDirection.Up    => ( 0, 1),   // slide going up,    incoming from bottom
            _                         => ( 1, 0),   // default: slide from right
        };
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
        foreach (var anim in step.Animations)
        {
            if (!_animElements.TryGetValue(anim.ShapeId, out var element))
            {
                // No overlay element (shape has no entrance overlay or is emphasis/exit):
                // handle emphasis / exit on the live canvas best-effort.
                PlayFallbackAnimation(anim);
                continue;
            }

            PlayShapeAnimation(element, anim);
            _revealedShapes.Add(anim.ShapeId);
        }
    }

    private void PlayShapeAnimation(FrameworkElement element, ShapeAnimation anim)
    {
        int durationMs = Math.Max(50, anim.DurationMs);
        int delayMs    = Math.Max(0,  anim.DelayMs);

        var sb = new Storyboard();

        // Motion-path animation takes priority over preset.
        if (anim.Kind == AnimationKind.Motion && anim.Motion is not null)
        {
            MotionPathEffect(sb, element, anim.Motion, durationMs, delayMs);
            _pendingStoryboards.Add(sb);
            sb.Begin(element, isControllable: true);
            return;
        }

        switch (anim.Preset)
        {
            case AnimationPreset.Appear:
                AppearEffect(sb, element, delayMs);
                break;

            case AnimationPreset.Fade:
                FadeEffect(sb, element, anim.Kind, durationMs, delayMs);
                break;

            case AnimationPreset.FlyIn:
                FlyInEffect(sb, element, anim, durationMs, delayMs);
                break;

            case AnimationPreset.Wipe:
                WipeEffect(sb, element, anim, durationMs, delayMs);
                break;

            case AnimationPreset.Zoom:
                ZoomEffect(sb, element, anim.Kind, durationMs, delayMs);
                break;

            // Emphasis effects on overlay image (best-effort)
            case AnimationPreset.Pulse:
            case AnimationPreset.Grow:
                PulseEffect(sb, element, durationMs, delayMs);
                break;

            case AnimationPreset.Spin:
                SpinEffect(sb, element, durationMs, delayMs);
                break;

            default:
                // Unknown preset → instant appear
                AppearEffect(sb, element, delayMs);
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
        AnimationKind kind, int durationMs, int delayMs)
    {
        double from = kind == AnimationKind.Exit ? 1 : 0;
        double to   = kind == AnimationKind.Exit ? 0 : 1;

        var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(durationMs)))
        {
            BeginTime     = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        sb.Children.Add(anim);
    }

    private void FlyInEffect(Storyboard sb, FrameworkElement el,
        ShapeAnimation anim, int durationMs, int delayMs)
    {
        double w = _slideCanvas.ActualWidth  > 0 ? _slideCanvas.ActualWidth  : 960;
        double h = _slideCanvas.ActualHeight > 0 ? _slideCanvas.ActualHeight : 540;

        var translate = new TranslateTransform(0, 0);
        el.RenderTransform = translate;

        // Determine starting offset direction.
        var (dx, dy) = anim.Direction switch
        {
            AnimationDirection.FromLeft        => (-w,  0),
            AnimationDirection.FromRight       => ( w,  0),
            AnimationDirection.FromTop         => ( 0, -h),
            AnimationDirection.FromBottom      => ( 0,  h),
            AnimationDirection.FromTopLeft     => (-w, -h),
            AnimationDirection.FromTopRight    => ( w, -h),
            AnimationDirection.FromBottomLeft  => (-w,  h),
            AnimationDirection.FromBottomRight => ( w,  h),
            AnimationDirection.Left            => (-w,  0),
            AnimationDirection.Right           => ( w,  0),
            AnimationDirection.Up              => ( 0, -h),
            AnimationDirection.Down            => ( 0,  h),
            _                                  => ( 0,  h),  // default from bottom
        };

        var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation(dx, 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };
        var animY = new DoubleAnimation(dy, 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };
        var animOp = new DoubleAnimation(0, 1, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs) };

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
        ShapeAnimation anim, int durationMs, int delayMs)
    {
        // Wipe: reveal via a clip RectangleGeometry that grows from 0 to full.
        // Direction determines which edge to wipe from.
        double w = el.Width  > 0 ? el.Width  : 960;
        double h = el.Height > 0 ? el.Height : 540;

        var clip  = new RectangleGeometry(new Rect(0, 0, 0, 0));
        el.Clip   = clip;

        var dur  = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Make visible first.
        el.Opacity = 1;

        // Animate clip rect width or height depending on direction.
        bool horizontal = anim.Direction is
            AnimationDirection.Left or AnimationDirection.Right or
            AnimationDirection.FromLeft or AnimationDirection.FromRight or
            AnimationDirection.Horizontal or null;

        if (horizontal)
        {
            clip.Rect = new Rect(0, 0, 0, h);
            var a = new RectAnimation(
                new Rect(0, 0, 0, h), new Rect(0, 0, w, h), dur)
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
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
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(a, el);
            Storyboard.SetTargetProperty(a, new PropertyPath("Clip.Rect"));
            sb.Children.Add(a);
        }
    }

    private void ZoomEffect(Storyboard sb, FrameworkElement el,
        AnimationKind kind, int durationMs, int delayMs)
    {
        double cx = (el.Width  > 0 ? el.Width  : _slideCanvas.ActualWidth)  / 2;
        double cy = (el.Height > 0 ? el.Height : _slideCanvas.ActualHeight) / 2;

        var scale = new ScaleTransform(0, 0, cx, cy);
        el.RenderTransform = scale;

        double fromScale = kind == AnimationKind.Exit ? 1.0 : 0.0;
        double toScale   = kind == AnimationKind.Exit ? 0.0 : 1.0;

        var dur  = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animSX = new DoubleAnimation(fromScale, toScale, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };
        var animSY = new DoubleAnimation(fromScale, toScale, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };
        var animOp = new DoubleAnimation(kind == AnimationKind.Exit ? 1 : 0, kind == AnimationKind.Exit ? 0 : 1, dur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs) };

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

    private static void PulseEffect(Storyboard sb, FrameworkElement el, int durationMs, int delayMs)
    {
        // Ensure visible
        el.Opacity = 1;

        double cx = el.Width  / 2;
        double cy = el.Height / 2;
        var scale = new ScaleTransform(1, 1, cx, cy);
        el.RenderTransform = scale;

        var halfDur = new Duration(TimeSpan.FromMilliseconds(durationMs / 2));

        var animSXUp = new DoubleAnimation(1, 1.2, halfDur)
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), AutoReverse = true };

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

    private static void SpinEffect(Storyboard sb, FrameworkElement el, int durationMs, int delayMs)
    {
        el.Opacity = 1;

        double cx = el.Width  / 2;
        double cy = el.Height / 2;
        var rotate = new RotateTransform(0, cx, cy);
        el.RenderTransform = rotate;

        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(durationMs)))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
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
    /// We sample the path using DoubleAnimationUsingKeyFrames at 20 discrete frames.
    /// The element must be visible (Opacity=1) from the start.
    /// </summary>
    private void MotionPathEffect(Storyboard sb, FrameworkElement element,
        MotionPath path, int durationMs, int delayMs)
    {
        double slideW = _slideDipW > 0 ? _slideDipW : 960;
        double slideH = _slideDipH > 0 ? _slideDipH : 540;

        // Ensure visible
        element.Opacity = 1;

        var translate = new TranslateTransform(0, 0);
        element.RenderTransform = translate;

        var dur      = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var delay    = TimeSpan.FromMilliseconds(delayMs);
        const int frames = 30;

        var animX = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        var animY = new DoubleAnimationUsingKeyFrames { BeginTime = delay };

        for (int f = 0; f <= frames; f++)
        {
            double t = f / (double)frames;
            var (dx, dy) = MotionPathEvaluator.Sample(path, t);

            double dxDip = dx * slideW;
            double dyDip = dy * slideH;

            var keyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * t));
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
    private void PlayFallbackAnimation(ShapeAnimation anim)
    {
        if (anim.Kind != AnimationKind.Emphasis) return;

        var sb = new Storyboard();
        int ms = Math.Max(100, anim.DurationMs);
        var halfDur = new Duration(TimeSpan.FromMilliseconds(ms / 2));

        var flashAnim = new DoubleAnimation(1, 0.5, halfDur)
        {
            AutoReverse    = true,
            BeginTime      = TimeSpan.FromMilliseconds(anim.DelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(flashAnim, _slideCanvas);
        Storyboard.SetTargetProperty(flashAnim, new PropertyPath(OpacityProperty));
        sb.Children.Add(flashAnim);
        _pendingStoryboards.Add(sb);
        sb.Begin(_slideCanvas, isControllable: true);
    }

    // ── Teardown ──────────────────────────────────────────────────────────────────

    private void Teardown()
    {
        _autoAdvanceTimer.Stop();
        foreach (var sb in _pendingStoryboards)
        {
            try { sb.Stop(); } catch { /* ignore */ }
        }
        _pendingStoryboards.Clear();
    }
}
