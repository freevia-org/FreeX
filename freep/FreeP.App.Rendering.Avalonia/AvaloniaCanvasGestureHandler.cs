using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Handles all pointer/keyboard interactions on the Avalonia <see cref="SlideCanvas"/> for editing:
/// selection (click / Ctrl+click / Shift+click), marquee drag, move, resize, rotate,
/// delete key, and arrow-key nudge.
///
/// Architecture:
/// <list type="bullet">
///   <item>Attaches to <see cref="SlideCanvas"/> pointer/keyboard events.</item>
///   <item>Maintains live-preview state during drag; commits one command to
///         <see cref="EditingSession"/> on pointer-up.</item>
///   <item>Drives a <see cref="SelectionAdornerLayer"/> for visual feedback.</item>
///   <item>All coordinate work uses the framework-free helpers
///         <see cref="SlideTransformCore"/> and <see cref="ShapeHitTester"/>.</item>
/// </list>
/// </summary>
public sealed class AvaloniaCanvasGestureHandler
{
    // ── Wiring ─────────────────────────────────────────────────────────────────

    private readonly SlideCanvas            _canvas;
    private readonly EditingSession         _editor;
    private readonly SelectionAdornerLayer  _adorner;

    // ── Drag state ─────────────────────────────────────────────────────────────

    private enum GestureKind { None, Move, Resize, Rotate, Marquee }

    private GestureKind _gesture = GestureKind.None;
    private Point       _dragStartScreen;
    private bool        _dragStarted;   // true once pointer has moved > threshold

    // ── Move ───────────────────────────────────────────────────────────────────
    private Dictionary<uint, (long ox, long oy)>? _moveStartPositions;

    // ── Resize ─────────────────────────────────────────────────────────────────
    private uint                              _resizeShapeId;
    private long                              _resizeOrigX, _resizeOrigY, _resizeOrigCx, _resizeOrigCy;
    private SelectionAdornerLayer.HandleKind  _resizeHandle;

    // ── Rotate ─────────────────────────────────────────────────────────────────
    private uint   _rotateShapeId;
    private double _rotateOrigDeg;
    private Point  _rotateCenterSlide; // shape center in slide DIP

    // ── Marquee ────────────────────────────────────────────────────────────────
    private Point _marqueeStartSlide;

    // ── Nudge steps ────────────────────────────────────────────────────────────
    private const long SmallNudgeEmu = 91440L;   // ~0.1 inch
    private const long LargeNudgeEmu = 914400L;  // 1 inch

    // ── Snap settings ──────────────────────────────────────────────────────────
    /// <summary>When true (default), shapes snap to the background grid during move/resize.</summary>
    public bool SnapToGrid   { get; set; } = true;

    /// <summary>When true (default), shapes snap to other shapes' edges and centers during move/resize.</summary>
    public bool SnapToShapes { get; set; } = true;

    // ── Construction / attach ──────────────────────────────────────────────────

    public AvaloniaCanvasGestureHandler(SlideCanvas canvas, EditingSession editor,
                                         SelectionAdornerLayer adorner)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _adorner = adorner ?? throw new ArgumentNullException(nameof(adorner));

        _canvas.PointerPressed      += OnPointerPressed;
        _canvas.PointerReleased     += OnPointerReleased;
        _canvas.PointerMoved        += OnPointerMoved;
        _canvas.PointerCaptureLost  += OnPointerCaptureLost;

        // Keyboard events are raised on the top-level window; caller must subscribe
        // the canvas's parent (the window) and delegate to HandleKeyDown.

        _editor.SelectionChanged    += (_, _) => RefreshAdorner();
        _editor.Changed             += ()     => RefreshAdorner();
        _editor.CurrentSlideChanged += (_, _) => RefreshAdorner();
    }

    // ── Keyboard (called by MainWindow.KeyDown handler) ────────────────────────

    /// <summary>
    /// Process a key-down event forwarded from the main window.
    /// Returns true if the key was handled and the event should be marked as handled.
    /// </summary>
    public bool HandleKeyDown(Key key, KeyModifiers modifiers)
    {
        if (_editor.SelectedShapeIds.Count == 0) return false;

        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        long step  = shift ? LargeNudgeEmu : SmallNudgeEmu;

        switch (key)
        {
            case Key.Left:  _editor.MoveSelected(-step, 0);  return true;
            case Key.Right: _editor.MoveSelected( step, 0);  return true;
            case Key.Up:    _editor.MoveSelected(0, -step);  return true;
            case Key.Down:  _editor.MoveSelected(0,  step);  return true;
            case Key.Delete:
            case Key.Back:
                _editor.DeleteSelected();
                return true;
        }
        return false;
    }

    // ── Pointer capture lost ───────────────────────────────────────────────────

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Guard against re-entrancy: releasing capture (in cancel/commit path) can fire
        // CaptureLost which would re-enter here. Check + clear _gesture atomically first.
        if (_gesture == GestureKind.None) return;
        _gesture     = GestureKind.None;
        _dragStarted = false;
        _adorner.UpdatePreview(null);
        _adorner.UpdateMarquee(null);
        _adorner.UpdateSnapGuides(null, SlideTransformCore.Identity);
        // Do NOT call e.Pointer.Capture(null) here — we are already in the capture-lost
        // callback, so the capture was already released by the framework (or by our caller
        // before we got here).
    }

    // ── Pointer down ───────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;

        _canvas.Focus();
        var pt    = e.GetPosition(_canvas);
        var xf    = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null) return;

        // Double-click is handled by InCanvasTextEditor; skip here.
        if (e.ClickCount >= 2) return;

        // Handle single selection: check handles first.
        if (_editor.SelectedShapeIds.Count == 1)
        {
            var selId   = _editor.SelectedShapeIds[0];
            var selRect = GetSelectionScreenRect(selId, slide, xf);
            if (selRect.HasValue)
            {
                var hitHandle = _adorner.HitTestHandle(selRect.Value, pt);
                if (hitHandle == SelectionAdornerLayer.HandleKind.Rotate)
                {
                    StartRotate(selId, slide, xf, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
                if (hitHandle != SelectionAdornerLayer.HandleKind.None &&
                    hitHandle != SelectionAdornerLayer.HandleKind.Body)
                {
                    StartResize(selId, slide, hitHandle, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
                if (hitHandle == SelectionAdornerLayer.HandleKind.Body)
                {
                    StartMove(slide, xf, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
            }
        }
        else if (_editor.SelectedShapeIds.Count > 1)
        {
            // Multi-select: hit any selected body → move
            var slidePt = xf.ScreenToSlide(pt.X, pt.Y);
            bool hitAny = false;
            foreach (var sid in _editor.SelectedShapeIds)
            {
                var s = slide.Shapes.FirstOrDefault(sh => sh.Id == sid);
                if (s is null) continue;
                var b = ShapeHitTester.GetShapeBoundsDip(s, _editor.Presentation);
                if (slidePt.X >= b.Left && slidePt.X <= b.Right &&
                    slidePt.Y >= b.Top  && slidePt.Y <= b.Bottom)
                { hitAny = true; break; }
            }
            if (hitAny)
            {
                StartMove(slide, xf, pt, e.Pointer);
                e.Handled = true;
                return;
            }
        }

        // Hit-test slide shapes
        var slidePt2 = xf.ScreenToSlide(pt.X, pt.Y);
        var hitId    = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt2.X, slidePt2.Y);

        if (hitId.HasValue)
        {
            bool addToSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) != 0;
            _editor.Select(hitId.Value, addToSelection);
            if (!addToSelection || _editor.SelectedShapeIds.Count <= 1)
                StartMove(slide, xf, pt, e.Pointer);
        }
        else
        {
            _editor.ClearSelection();
            StartMarquee(xf, pt, e.Pointer);
        }

        e.Handled = true;
    }

    // ── Pointer move ───────────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pt = e.GetPosition(_canvas);

        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            UpdateCursor(pt);
            return;
        }

        var xf        = _canvas.CurrentTransform;
        var slide     = _editor.CurrentSlide;
        var modifiers = e.KeyModifiers;
        if (slide is null) return;

        switch (_gesture)
        {
            case GestureKind.Move:    PreviewMove(pt, xf, slide, modifiers);    break;
            case GestureKind.Resize:  PreviewResize(pt, xf, modifiers);         break;
            case GestureKind.Rotate:  PreviewRotate(pt, xf);                    break;
            case GestureKind.Marquee: PreviewMarquee(pt, xf);                   break;
        }
    }

    // ── Pointer up ─────────────────────────────────────────────────────────────

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        var pt        = e.GetPosition(_canvas);
        var xf        = _canvas.CurrentTransform;
        var modifiers = e.KeyModifiers;

        switch (_gesture)
        {
            case GestureKind.Move:    CommitMove(pt, xf, modifiers);    break;
            case GestureKind.Resize:  CommitResize(pt, xf, modifiers);  break;
            case GestureKind.Rotate:  CommitRotate(pt, xf);             break;
            case GestureKind.Marquee: CommitMarquee(pt, xf);            break;
        }

        // Reset gesture state BEFORE releasing capture to prevent CaptureLost re-entry.
        _gesture     = GestureKind.None;
        _dragStarted = false;
        _adorner.UpdatePreview(null);
        _adorner.UpdateMarquee(null);
        _adorner.UpdateSnapGuides(null, SlideTransformCore.Identity);
        // Release pointer capture (capture-lost handler is guarded by _gesture == None check above).
        e.Pointer.Capture(null);
    }

    // ── Move gesture ───────────────────────────────────────────────────────────

    private void StartMove(Slide slide, SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        _gesture          = GestureKind.Move;
        _dragStartScreen  = screenPt;
        _dragStarted      = false;
        _moveStartPositions = new Dictionary<uint, (long, long)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is not null)
                _moveStartPositions[id] = (s.OffsetXEmu, s.OffsetYEmu);
        }
        pointer.Capture(_canvas);
    }

    private void PreviewMove(Point screenPt, SlideTransformCore xf, Slide slide, KeyModifiers modifiers)
    {
        double ddxPx = screenPt.X - _dragStartScreen.X;
        double ddyPx = screenPt.Y - _dragStartScreen.Y;
        if (!_dragStarted && Math.Abs(ddxPx) < 3 && Math.Abs(ddyPx) < 3) return;
        _dragStarted = true;

        double ddxDip = xf.ScaleScreenToDip(ddxPx);
        double ddyDip = xf.ScaleScreenToDip(ddyPx);

        // Alt key disables snapping (PowerPoint convention), matching WPF CanvasGestureHandler.
        bool altHeld    = (modifiers & KeyModifiers.Alt) != 0;
        bool snapEnabled = (SnapToGrid || SnapToShapes) && !altHeld;

        SnapResult snap = SnapResult.None;
        if (snapEnabled && _moveStartPositions is not null && _editor.SelectedShapeIds.Count > 0)
        {
            var firstId = _editor.SelectedShapeIds[0];
            if (_moveStartPositions.TryGetValue(firstId, out var firstOrig))
            {
                var firstShape = slide.Shapes.FirstOrDefault(s => s.Id == firstId);
                if (firstShape is not null)
                {
                    double newL = SlideTransformCore.EmuToDip(firstOrig.ox) + ddxDip;
                    double newT = SlideTransformCore.EmuToDip(firstOrig.oy) + ddyDip;
                    double newR = newL + SlideTransformCore.EmuToDip(firstShape.ExtentCxEmu);
                    double newB = newT + SlideTransformCore.EmuToDip(firstShape.ExtentCyEmu);
                    var candidates = SnapToShapes
                        ? SnapEngine.BuildShapeCandidates(slide, _editor.SelectedShapeIds)
                        : null;
                    snap = SnapEngine.Snap(
                        (newL, newT, newR, newB), candidates,
                        xf.SlideWidthDip, xf.SlideHeightDip,
                        snapEnabled: true,
                        gridPitchDip: SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0);
                }
            }
        }

        double snapDxPx = snap.SnapDx * xf.Scale;
        double snapDyPx = snap.SnapDy * xf.Scale;

        var rects = new List<(uint, Rect)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            if (_moveStartPositions is null || !_moveStartPositions.TryGetValue(id, out var orig)) continue;
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            double ox = SlideTransformCore.EmuToDip(orig.ox) * xf.Scale + xf.OffsetX + ddxPx + snapDxPx;
            double oy = SlideTransformCore.EmuToDip(orig.oy) * xf.Scale + xf.OffsetY + ddyPx + snapDyPx;
            double cw = SlideTransformCore.EmuToDip(s.ExtentCxEmu) * xf.Scale;
            double ch = SlideTransformCore.EmuToDip(s.ExtentCyEmu) * xf.Scale;
            rects.Add((id, new Rect(ox, oy, cw, ch)));
        }

        if (rects.Count == 1)
            _adorner.UpdatePreview(rects[0].Item2);
        else if (rects.Count > 1)
        {
            double l  = rects.Min(r => r.Item2.Left);
            double t  = rects.Min(r => r.Item2.Top);
            double rt = rects.Max(r => r.Item2.Right);
            double bt = rects.Max(r => r.Item2.Bottom);
            _adorner.UpdatePreview(new Rect(l, t, rt - l, bt - t));
        }

        _adorner.UpdateSnapGuides(snap.Guides.Count > 0 ? snap.Guides : null, xf);
    }

    private void CommitMove(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        if (_moveStartPositions is null || !_dragStarted) return;
        double ddxPx = screenPt.X - _dragStartScreen.X;
        double ddyPx = screenPt.Y - _dragStartScreen.Y;
        if (Math.Abs(ddxPx) < 1 && Math.Abs(ddyPx) < 1) return;

        // Alt key disables snapping (PowerPoint convention), matching WPF CanvasGestureHandler.
        bool altHeld     = (modifiers & KeyModifiers.Alt) != 0;
        bool snapEnabled = (SnapToGrid || SnapToShapes) && !altHeld;

        double snapDxPx = 0, snapDyPx = 0;
        var slide = _editor.CurrentSlide;
        if (snapEnabled && slide is not null && _editor.SelectedShapeIds.Count > 0)
        {
            double ddxDip = xf.ScaleScreenToDip(ddxPx);
            double ddyDip = xf.ScaleScreenToDip(ddyPx);
            var firstId   = _editor.SelectedShapeIds[0];
            if (_moveStartPositions.TryGetValue(firstId, out var firstOrig))
            {
                var firstShape = slide.Shapes.FirstOrDefault(s => s.Id == firstId);
                if (firstShape is not null)
                {
                    double newL = SlideTransformCore.EmuToDip(firstOrig.ox) + ddxDip;
                    double newT = SlideTransformCore.EmuToDip(firstOrig.oy) + ddyDip;
                    double newR = newL + SlideTransformCore.EmuToDip(firstShape.ExtentCxEmu);
                    double newB = newT + SlideTransformCore.EmuToDip(firstShape.ExtentCyEmu);
                    var candidates = SnapToShapes
                        ? SnapEngine.BuildShapeCandidates(slide, _editor.SelectedShapeIds)
                        : null;
                    var snap = SnapEngine.Snap(
                        (newL, newT, newR, newB), candidates,
                        xf.SlideWidthDip, xf.SlideHeightDip,
                        snapEnabled: true,
                        gridPitchDip: SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0);
                    snapDxPx = snap.SnapDx * xf.Scale;
                    snapDyPx = snap.SnapDy * xf.Scale;
                }
            }
        }

        long dxEmu = xf.ScreenDeltaToEmu(ddxPx + snapDxPx);
        long dyEmu = xf.ScreenDeltaToEmu(ddyPx + snapDyPx);
        _editor.MoveSelected(dxEmu, dyEmu);
    }

    // ── Resize gesture ─────────────────────────────────────────────────────────

    private void StartResize(uint shapeId, Slide slide, SelectionAdornerLayer.HandleKind handle, Point screenPt, IPointer pointer)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;
        _gesture         = GestureKind.Resize;
        _dragStartScreen = screenPt;
        _dragStarted     = false;
        _resizeShapeId   = shapeId;
        _resizeOrigX     = s.OffsetXEmu;
        _resizeOrigY     = s.OffsetYEmu;
        _resizeOrigCx    = s.ExtentCxEmu;
        _resizeOrigCy    = s.ExtentCyEmu;
        _resizeHandle    = handle;
        pointer.Capture(_canvas);
    }

    private void PreviewResize(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        if (!_dragStarted)
        {
            double ddxPx = screenPt.X - _dragStartScreen.X;
            double ddyPx = screenPt.Y - _dragStartScreen.Y;
            if (Math.Abs(ddxPx) < 3 && Math.Abs(ddyPx) < 3) return;
            _dragStarted = true;
        }
        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf, modifiers);
        var r = BoundsToScreenRect(nx, ny, ncx, ncy, xf);
        _adorner.UpdatePreview(r);
    }

    private void CommitResize(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        if (!_dragStarted) return;
        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf, modifiers);
        _editor.ResizeShape(_resizeShapeId, nx, ny, ncx, ncy);
    }

    /// <summary>Computes new shape bounds in EMU given the current drag point.</summary>
    /// <param name="modifiers">
    /// Key modifiers at the time of the drag event.
    /// When <see cref="KeyModifiers.Alt"/> is set, snapping is bypassed
    /// (PowerPoint convention for precise off-grid placement).
    /// </param>
    public (long newX, long newY, long newCx, long newCy) ComputeResizeBounds(
        Point screenPt, SlideTransformCore xf, KeyModifiers modifiers = KeyModifiers.None)
    {
        double dxPx = screenPt.X - _dragStartScreen.X;
        double dyPx = screenPt.Y - _dragStartScreen.Y;

        double origXDip  = SlideTransformCore.EmuToDip(_resizeOrigX);
        double origYDip  = SlideTransformCore.EmuToDip(_resizeOrigY);
        double origCxDip = SlideTransformCore.EmuToDip(_resizeOrigCx);
        double origCyDip = SlideTransformCore.EmuToDip(_resizeOrigCy);
        double dxDip     = xf.ScaleScreenToDip(dxPx);
        double dyDip     = xf.ScaleScreenToDip(dyPx);

        // Alt key disables snapping (PowerPoint convention), matching WPF CanvasGestureHandler.
        bool altHeld     = (modifiers & KeyModifiers.Alt) != 0;
        bool snapEnabled = (SnapToGrid || SnapToShapes) && !altHeld;

        if (snapEnabled && _editor.CurrentSlide is not null)
        {
            var slide      = _editor.CurrentSlide;
            var candidates = SnapToShapes
                ? SnapEngine.BuildShapeCandidates(slide, new[] { _resizeShapeId })
                : null;
            double slideW  = xf.SlideWidthDip;
            double slideH  = xf.SlideHeightDip;
            double pitch   = SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0;

            switch (_resizeHandle)
            {
                case SelectionAdornerLayer.HandleKind.ResizeN:
                {
                    double dy = origYDip + dyDip;
                    var snap = SnapEngine.Snap((origXDip, dy, origXDip + origCxDip, dy),
                        candidates, slideW, slideH, true, pitch);
                    dyDip += snap.SnapDy; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeS:
                {
                    double dy = origYDip + origCyDip + dyDip;
                    var snap = SnapEngine.Snap((origXDip, dy, origXDip + origCxDip, dy),
                        candidates, slideW, slideH, true, pitch);
                    dyDip += snap.SnapDy; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeW:
                {
                    double dx = origXDip + dxDip;
                    var snap = SnapEngine.Snap((dx, origYDip, dx, origYDip + origCyDip),
                        candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeE:
                {
                    double dx = origXDip + origCxDip + dxDip;
                    var snap = SnapEngine.Snap((dx, origYDip, dx, origYDip + origCyDip),
                        candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeNE:
                {
                    double dx = origXDip + origCxDip + dxDip;
                    double dy = origYDip + dyDip;
                    var snap = SnapEngine.Snap((dx, dy, dx, dy), candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; dyDip += snap.SnapDy; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeNW:
                {
                    double dx = origXDip + dxDip;
                    double dy = origYDip + dyDip;
                    var snap = SnapEngine.Snap((dx, dy, dx, dy), candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; dyDip += snap.SnapDy; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeSE:
                {
                    double dx = origXDip + origCxDip + dxDip;
                    double dy = origYDip + origCyDip + dyDip;
                    var snap = SnapEngine.Snap((dx, dy, dx, dy), candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; dyDip += snap.SnapDy; break;
                }
                case SelectionAdornerLayer.HandleKind.ResizeSW:
                {
                    double dx = origXDip + dxDip;
                    double dy = origYDip + origCyDip + dyDip;
                    var snap = SnapEngine.Snap((dx, dy, dx, dy), candidates, slideW, slideH, true, pitch);
                    dxDip += snap.SnapDx; dyDip += snap.SnapDy; break;
                }
            }
        }

        long ddx = xf.ScreenDeltaToEmu(dxDip * xf.Scale);
        long ddy = xf.ScreenDeltaToEmu(dyDip * xf.Scale);

        long x  = _resizeOrigX;
        long y  = _resizeOrigY;
        long cx = _resizeOrigCx;
        long cy = _resizeOrigCy;
        const long MinEmu = 91440L; // 0.1 inch

        switch (_resizeHandle)
        {
            case SelectionAdornerLayer.HandleKind.ResizeN:
                y = _resizeOrigY + ddy;  cy = Math.Max(MinEmu, _resizeOrigCy - ddy); break;
            case SelectionAdornerLayer.HandleKind.ResizeS:
                cy = Math.Max(MinEmu, _resizeOrigCy + ddy); break;
            case SelectionAdornerLayer.HandleKind.ResizeW:
                x = _resizeOrigX + ddx;  cx = Math.Max(MinEmu, _resizeOrigCx - ddx); break;
            case SelectionAdornerLayer.HandleKind.ResizeE:
                cx = Math.Max(MinEmu, _resizeOrigCx + ddx); break;
            case SelectionAdornerLayer.HandleKind.ResizeNE:
                y = _resizeOrigY + ddy;  cy = Math.Max(MinEmu, _resizeOrigCy - ddy);
                cx = Math.Max(MinEmu, _resizeOrigCx + ddx); break;
            case SelectionAdornerLayer.HandleKind.ResizeNW:
                x = _resizeOrigX + ddx;  y = _resizeOrigY + ddy;
                cx = Math.Max(MinEmu, _resizeOrigCx - ddx); cy = Math.Max(MinEmu, _resizeOrigCy - ddy); break;
            case SelectionAdornerLayer.HandleKind.ResizeSE:
                cx = Math.Max(MinEmu, _resizeOrigCx + ddx); cy = Math.Max(MinEmu, _resizeOrigCy + ddy); break;
            case SelectionAdornerLayer.HandleKind.ResizeSW:
                x = _resizeOrigX + ddx;  cx = Math.Max(MinEmu, _resizeOrigCx - ddx);
                cy = Math.Max(MinEmu, _resizeOrigCy + ddy); break;
        }
        return (x, y, cx, cy);
    }

    // ── Rotate gesture ─────────────────────────────────────────────────────────

    private void StartRotate(uint shapeId, Slide slide, SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;
        _gesture         = GestureKind.Rotate;
        _dragStartScreen = screenPt;
        _dragStarted     = false;
        _rotateShapeId   = shapeId;
        _rotateOrigDeg   = s.RotationDeg;
        double cx = SlideTransformCore.EmuToDip(s.OffsetXEmu + s.ExtentCxEmu / 2);
        double cy = SlideTransformCore.EmuToDip(s.OffsetYEmu + s.ExtentCyEmu / 2);
        _rotateCenterSlide = new Point(cx, cy);
        pointer.Capture(_canvas);
    }

    private void PreviewRotate(Point screenPt, SlideTransformCore xf)
    {
        _dragStarted = true;
        double angle = ComputeRotationAngle(screenPt, xf, KeyModifiers.None);
        if (_editor.CurrentSlide is not null && _editor.Presentation is not null)
        {
            var r = GetSelectionScreenRect(_rotateShapeId, _editor.CurrentSlide, xf);
            if (r.HasValue)
                _adorner.UpdatePreview(r.Value, angle);
        }
    }

    private void CommitRotate(Point screenPt, SlideTransformCore xf)
    {
        if (!_dragStarted) return;
        double angle = ComputeRotationAngle(screenPt, xf, KeyModifiers.None);
        _editor.RotateShape(_rotateShapeId, angle);
    }

    /// <summary>Computes new absolute rotation angle in degrees.</summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        double cx = _rotateCenterSlide.X * xf.Scale + xf.OffsetX;
        double cy = _rotateCenterSlide.Y * xf.Scale + xf.OffsetY;

        double angle = Math.Atan2(screenPt.Y - cy, screenPt.X - cx) * (180.0 / Math.PI) + 90.0;
        angle = ((angle % 360) + 360) % 360;
        angle = _rotateOrigDeg + (angle - _rotateOrigDeg);

        if ((modifiers & KeyModifiers.Shift) != 0)
            angle = Math.Round(angle / 15.0) * 15.0;

        return angle;
    }

    // ── Marquee gesture ────────────────────────────────────────────────────────

    private void StartMarquee(SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        _gesture           = GestureKind.Marquee;
        _dragStartScreen   = screenPt;
        _dragStarted       = false;
        _marqueeStartSlide = new Point(xf.ScreenToSlide(screenPt.X, screenPt.Y).X,
                                       xf.ScreenToSlide(screenPt.X, screenPt.Y).Y);
        pointer.Capture(_canvas);
    }

    private void PreviewMarquee(Point screenPt, SlideTransformCore xf)
    {
        double ddxPx = screenPt.X - _dragStartScreen.X;
        double ddyPx = screenPt.Y - _dragStartScreen.Y;
        if (!_dragStarted && Math.Abs(ddxPx) < 3 && Math.Abs(ddyPx) < 3) return;
        _dragStarted = true;

        double l = Math.Min(_dragStartScreen.X, screenPt.X);
        double t = Math.Min(_dragStartScreen.Y, screenPt.Y);
        double r = Math.Max(_dragStartScreen.X, screenPt.X);
        double b = Math.Max(_dragStartScreen.Y, screenPt.Y);
        _adorner.UpdateMarquee(new Rect(l, t, r - l, b - t));
    }

    private void CommitMarquee(Point screenPt, SlideTransformCore xf)
    {
        _adorner.UpdateMarquee(null);
        if (!_dragStarted) return;
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var endSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        var ids = ShapeHitTester.MarqueeHitTest(
            slide, _editor.Presentation,
            _marqueeStartSlide.X, _marqueeStartSlide.Y,
            endSlide.X, endSlide.Y);

        if (ids.Count == 0) return;
        _editor.ClearSelection();
        foreach (var id in ids)
            _editor.Select(id, addToSelection: true);
    }

    // ── Cursor feedback ────────────────────────────────────────────────────────

    private void UpdateCursor(Point screenPt)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _canvas.Cursor = Cursor.Default;
            return;
        }

        var xf = _canvas.CurrentTransform;

        if (_editor.SelectedShapeIds.Count == 1)
        {
            var selId = _editor.SelectedShapeIds[0];
            var selRect = GetSelectionScreenRect(selId, slide, xf);
            if (selRect.HasValue)
            {
                var handle = _adorner.HitTestHandle(selRect.Value, screenPt);
                _canvas.Cursor = handle switch
                {
                    SelectionAdornerLayer.HandleKind.Rotate              => new Cursor(StandardCursorType.Cross),
                    SelectionAdornerLayer.HandleKind.ResizeN or
                    SelectionAdornerLayer.HandleKind.ResizeS             => new Cursor(StandardCursorType.SizeNorthSouth),
                    SelectionAdornerLayer.HandleKind.ResizeE or
                    SelectionAdornerLayer.HandleKind.ResizeW             => new Cursor(StandardCursorType.SizeWestEast),
                    SelectionAdornerLayer.HandleKind.ResizeNE or
                    SelectionAdornerLayer.HandleKind.ResizeSW            => new Cursor(StandardCursorType.TopRightCorner),
                    SelectionAdornerLayer.HandleKind.ResizeNW or
                    SelectionAdornerLayer.HandleKind.ResizeSE            => new Cursor(StandardCursorType.TopLeftCorner),
                    SelectionAdornerLayer.HandleKind.Body                => new Cursor(StandardCursorType.SizeAll),
                    _                                                    => Cursor.Default
                };
                return;
            }
        }

        // Check if hovering over any selected body
        var slidePt = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        foreach (var id in _editor.SelectedShapeIds)
        {
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            var b = ShapeHitTester.GetShapeBoundsDip(s, _editor.Presentation);
            if (slidePt.X >= b.Left && slidePt.X <= b.Right &&
                slidePt.Y >= b.Top  && slidePt.Y <= b.Bottom)
            {
                _canvas.Cursor = new Cursor(StandardCursorType.SizeAll);
                return;
            }
        }

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        _canvas.Cursor = hitId.HasValue ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    // ── Adorner refresh ────────────────────────────────────────────────────────

    private void RefreshAdorner()
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _adorner.UpdateSelection(Array.Empty<(uint, Rect)>());
            return;
        }

        var xf    = _canvas.CurrentTransform;
        var rects = new List<(uint, Rect)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            var r = GetSelectionScreenRect(id, slide, xf);
            if (r.HasValue)
                rects.Add((id, r.Value));
        }
        _adorner.UpdateSelection(rects);
    }

    // ── Test seeding (InternalsVisibleTo test project) ─────────────────────────

    /// <summary>
    /// Seeds the internal resize state so that
    /// <see cref="ComputeResizeBounds"/> can be exercised in unit tests
    /// without requiring live pointer events.
    /// </summary>
    internal void SeedResizeState(Point startScreen, SlideShape shape, SelectionAdornerLayer.HandleKind handle)
    {
        _dragStartScreen = startScreen;
        _dragStarted     = true;
        _resizeShapeId   = shape.Id;
        _resizeOrigX     = shape.OffsetXEmu;
        _resizeOrigY     = shape.OffsetYEmu;
        _resizeOrigCx    = shape.ExtentCxEmu;
        _resizeOrigCy    = shape.ExtentCyEmu;
        _resizeHandle    = handle;
        _gesture         = GestureKind.Resize;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Rect? GetSelectionScreenRect(uint shapeId, Slide slide, SlideTransformCore xf)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null || _editor.Presentation is null) return null;
        var b = ShapeHitTester.GetShapeBoundsDip(s, _editor.Presentation);
        return BoundsToScreenRect(
            SlideTransformCore.DipToEmu(b.Left), SlideTransformCore.DipToEmu(b.Top),
            SlideTransformCore.DipToEmu(b.Width), SlideTransformCore.DipToEmu(b.Height), xf);
    }

    private static Rect BoundsToScreenRect(long offX, long offY, long cx, long cy, SlideTransformCore xf)
    {
        double x = SlideTransformCore.EmuToDip(offX) * xf.Scale + xf.OffsetX;
        double y = SlideTransformCore.EmuToDip(offY) * xf.Scale + xf.OffsetY;
        double w = SlideTransformCore.EmuToDip(cx) * xf.Scale;
        double h = SlideTransformCore.EmuToDip(cy) * xf.Scale;
        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

}
