using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Handles all pointer/keyboard interactions on the <see cref="SlideCanvas"/> for editing:
/// selection (click / Ctrl+click / Shift+click), marquee drag, move, resize, rotate,
/// delete key, and arrow-key nudge.
///
/// Architecture:
/// <list type="bullet">
///   <item>Attaches to <see cref="SlideCanvas"/> mouse/keyboard events.</item>
///   <item>Maintains a live-preview offset/resize/rotate state during drag; commits one
///         command to <see cref="FreeP.App.Compositor.EditingSession"/> on mouse-up.</item>
///   <item>Drives a <see cref="SelectionAdorner"/> for visual feedback.</item>
///   <item>All coordinate work is delegated to the framework-free helpers
///         <see cref="SlideTransform"/> and <see cref="ShapeHitTester"/> so the logic
///         is fully unit-testable.</item>
/// </list>
/// </summary>
public sealed class CanvasGestureHandler
{
    // ── Wiring ────────────────────────────────────────────────────────────────────────────────

    private readonly SlideCanvas       _canvas;
    private readonly EditingSession    _editor;
    private readonly SelectionAdorner  _adorner;

    // ── Drag state ────────────────────────────────────────────────────────────────────────────

    private enum GestureKind { None, Move, Resize, Rotate, Marquee }

    private GestureKind                     _gesture = GestureKind.None;
    private Point                           _dragStartScreen;          // screen px at start
    private Point                           _dragStartSlide;           // slide DIP at start

    // ── Move ──────────────────────────────────────────────────────────────────────────────────
    private Dictionary<uint, (long ox, long oy)>? _moveStartPositions; // shape id → original EMU pos

    // ── Resize ────────────────────────────────────────────────────────────────────────────────
    private uint                    _resizeShapeId;
    private long                    _resizeOrigX, _resizeOrigY, _resizeOrigCx, _resizeOrigCy;
    private SelectionAdorner.HandleKind _resizeHandle;

    // ── Rotate ────────────────────────────────────────────────────────────────────────────────
    private uint   _rotateShapeId;
    private double _rotateOrigDeg;
    private Point  _rotateCenterSlide;  // shape center in slide DIP

    // ── Marquee ───────────────────────────────────────────────────────────────────────────────
    private Point  _marqueeStartSlide;

    // ── Small nudge step ──────────────────────────────────────────────────────────────────────
    private const long SmallNudgeEmu = 91440L;    // ~0.1 inch
    private const long LargeNudgeEmu = 914400L;   // 1 inch

    // ── Construction / attach ─────────────────────────────────────────────────────────────────

    public CanvasGestureHandler(SlideCanvas canvas, EditingSession editor)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        // Add adorner to the adorner layer
        var layer = AdornerLayer.GetAdornerLayer(_canvas);
        _adorner  = new SelectionAdorner(_canvas);
        layer?.Add(_adorner);

        // Hook canvas events
        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseLeftButtonUp   += OnMouseUp;
        _canvas.MouseMove           += OnMouseMove;
        _canvas.KeyDown             += OnKeyDown;
        _canvas.Focusable           = true;

        // React to selection changes from the editor (e.g. SelectAll via ribbon)
        _editor.SelectionChanged += (_, _) => RefreshAdorner();
        _editor.Changed          += () => RefreshAdorner();
        _editor.CurrentSlideChanged += (_, _) => RefreshAdorner();
    }

    // ── Mouse down ────────────────────────────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        var pt   = e.GetPosition(_canvas);
        var xf   = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null)
            return;

        // Determine what was hit first for the existing selection (handles take priority)
        if (_editor.SelectedShapeIds.Count > 0)
        {
            // Try handles of single selection (resize/rotate only supported for single)
            if (_editor.SelectedShapeIds.Count == 1)
            {
                var selId   = _editor.SelectedShapeIds[0];
                var selRect = GetSelectionScreenRect(selId, slide, xf);
                if (selRect.HasValue)
                {
                    var hitHandle = _adorner.HitTestHandle(selRect.Value, pt);
                    if (hitHandle == SelectionAdorner.HandleKind.Rotate)
                    {
                        StartRotate(selId, slide, xf, pt);
                        e.Handled = true;
                        return;
                    }
                    if (hitHandle != SelectionAdorner.HandleKind.None &&
                        hitHandle != SelectionAdorner.HandleKind.Body)
                    {
                        StartResize(selId, slide, hitHandle, pt);
                        e.Handled = true;
                        return;
                    }
                    if (hitHandle == SelectionAdorner.HandleKind.Body)
                    {
                        // Body of already-selected shape: start move
                        StartMove(slide, xf, pt);
                        e.Handled = true;
                        return;
                    }
                }
            }
            else
            {
                // Multi-selection: hit any selected body -> move
                var slidePt = xf.ScreenToSlide(pt.X, pt.Y);
                bool hitAny = false;
                foreach (var sid in _editor.SelectedShapeIds)
                {
                    var b = ShapeHitTester.GetShapeBoundsDip(
                        slide.Shapes.First(s => s.Id == sid),
                        _editor.Presentation);
                    if (slidePt.X >= b.Left && slidePt.X <= b.Right &&
                        slidePt.Y >= b.Top  && slidePt.Y <= b.Bottom)
                    {
                        hitAny = true;
                        break;
                    }
                }
                if (hitAny)
                {
                    StartMove(slide, xf, pt);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Hit-test slide shapes
        var slidePt2 = xf.ScreenToSlide(pt.X, pt.Y);
        var hitId    = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt2.X, slidePt2.Y);

        if (hitId.HasValue)
        {
            bool addToSelection = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            _editor.Select(hitId.Value, addToSelection);
            // If not multi-select, prepare for possible move
            if (!addToSelection || _editor.SelectedShapeIds.Count <= 1)
            {
                StartMove(slide, xf, pt);
            }
        }
        else
        {
            // Click on empty -> start marquee or clear
            _editor.ClearSelection();
            StartMarquee(xf, pt);
        }

        e.Handled = true;
    }

    // ── Mouse move ────────────────────────────────────────────────────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateCursor(e.GetPosition(_canvas));
            return;
        }

        var pt  = e.GetPosition(_canvas);
        var xf  = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;
        if (slide is null) return;

        switch (_gesture)
        {
            case GestureKind.Move:
                PreviewMove(pt, xf, slide);
                break;
            case GestureKind.Resize:
                PreviewResize(pt, xf);
                break;
            case GestureKind.Rotate:
                PreviewRotate(pt, xf);
                break;
            case GestureKind.Marquee:
                PreviewMarquee(pt, xf);
                break;
        }
    }

    // ── Mouse up ──────────────────────────────────────────────────────────────────────────────

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var xf = _canvas.CurrentTransform;

        switch (_gesture)
        {
            case GestureKind.Move:
                CommitMove(pt, xf);
                break;
            case GestureKind.Resize:
                CommitResize(pt, xf);
                break;
            case GestureKind.Rotate:
                CommitRotate(pt, xf);
                break;
            case GestureKind.Marquee:
                CommitMarquee(pt, xf);
                break;
        }

        _gesture = GestureKind.None;
        _adorner.UpdatePreview(null);
        _adorner.UpdateMarquee(null);
        _canvas.ReleaseMouseCapture();
    }

    // ── Move gesture ──────────────────────────────────────────────────────────────────────────

    private void StartMove(Slide slide, SlideTransform xf, Point screenPt)
    {
        _gesture          = GestureKind.Move;
        _dragStartScreen  = screenPt;
        _dragStartSlide   = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        _moveStartPositions = new Dictionary<uint, (long, long)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is not null)
                _moveStartPositions[id] = (s.OffsetXEmu, s.OffsetYEmu);
        }
        _canvas.CaptureMouse();
    }

    private void PreviewMove(Point screenPt, SlideTransform xf, Slide slide)
    {
        double ddx = screenPt.X - _dragStartScreen.X;
        double ddy = screenPt.Y - _dragStartScreen.Y;

        // Build preview rects based on start positions + delta
        var rects = new List<(uint, Rect)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            if (_moveStartPositions is null || !_moveStartPositions.TryGetValue(id, out var orig))
                continue;
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            double origXDip = SlideTransform.EmuToDip(orig.ox);
            double origYDip = SlideTransform.EmuToDip(orig.oy);
            double cxDip    = SlideTransform.EmuToDip(s.ExtentCxEmu);
            double cyDip    = SlideTransform.EmuToDip(s.ExtentCyEmu);
            double newXScr  = origXDip * xf.Scale + xf.OffsetX + ddx;
            double newYScr  = origYDip * xf.Scale + xf.OffsetY + ddy;
            rects.Add((id, new Rect(newXScr, newYScr, cxDip * xf.Scale, cyDip * xf.Scale)));
        }

        // Show preview as adorner overlay (single first rect)
        if (rects.Count == 1)
            _adorner.UpdatePreview(rects[0].Item2);
        else if (rects.Count > 1)
        {
            // For multi-select, union rect
            double l = rects.Min(r => r.Item2.Left);
            double t = rects.Min(r => r.Item2.Top);
            double rt = rects.Max(r => r.Item2.Right);
            double bt = rects.Max(r => r.Item2.Bottom);
            _adorner.UpdatePreview(new Rect(l, t, rt - l, bt - t));
        }
    }

    private void CommitMove(Point screenPt, SlideTransform xf)
    {
        if (_moveStartPositions is null) return;
        double ddx = screenPt.X - _dragStartScreen.X;
        double ddy = screenPt.Y - _dragStartScreen.Y;
        if (Math.Abs(ddx) < 1 && Math.Abs(ddy) < 1) return; // no meaningful move

        long dxEmu = xf.ScreenDeltaToEmu(ddx);
        long dyEmu = xf.ScreenDeltaToEmu(ddy);
        _editor.MoveSelected(dxEmu, dyEmu);
    }

    // ── Resize gesture ────────────────────────────────────────────────────────────────────────

    private void StartResize(uint shapeId, Slide slide, SelectionAdorner.HandleKind handle, Point screenPt)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;

        _gesture        = GestureKind.Resize;
        _dragStartScreen= screenPt;
        _resizeShapeId  = shapeId;
        _resizeOrigX    = s.OffsetXEmu;
        _resizeOrigY    = s.OffsetYEmu;
        _resizeOrigCx   = s.ExtentCxEmu;
        _resizeOrigCy   = s.ExtentCyEmu;
        _resizeHandle   = handle;
        _canvas.CaptureMouse();
    }

    private void PreviewResize(Point screenPt, SlideTransform xf)
    {
        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf);
        var r = BoundsToScreenRect(nx, ny, ncx, ncy, xf);
        _adorner.UpdatePreview(r);
    }

    private void CommitResize(Point screenPt, SlideTransform xf)
    {
        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf);
        _editor.ResizeShape(_resizeShapeId, nx, ny, ncx, ncy);
    }

    /// <summary>
    /// Given the current drag screen point, computes the new shape bounds in EMU.
    /// Handles all 8 resize directions.
    /// </summary>
    public (long newX, long newY, long newCx, long newCy) ComputeResizeBounds(
        Point screenPt, SlideTransform xf)
    {
        double dxPx = screenPt.X - _dragStartScreen.X;
        double dyPx = screenPt.Y - _dragStartScreen.Y;
        long   dx   = xf.ScreenDeltaToEmu(dxPx);
        long   dy   = xf.ScreenDeltaToEmu(dyPx);

        long x  = _resizeOrigX;
        long y  = _resizeOrigY;
        long cx = _resizeOrigCx;
        long cy = _resizeOrigCy;

        const long MinEmu = 91440L; // 0.1 inch minimum size

        switch (_resizeHandle)
        {
            case SelectionAdorner.HandleKind.ResizeN:
                y  = _resizeOrigY  + dy;
                cy = Math.Max(MinEmu, _resizeOrigCy - dy);
                break;
            case SelectionAdorner.HandleKind.ResizeS:
                cy = Math.Max(MinEmu, _resizeOrigCy + dy);
                break;
            case SelectionAdorner.HandleKind.ResizeW:
                x  = _resizeOrigX  + dx;
                cx = Math.Max(MinEmu, _resizeOrigCx - dx);
                break;
            case SelectionAdorner.HandleKind.ResizeE:
                cx = Math.Max(MinEmu, _resizeOrigCx + dx);
                break;
            case SelectionAdorner.HandleKind.ResizeNE:
                y  = _resizeOrigY  + dy;
                cy = Math.Max(MinEmu, _resizeOrigCy - dy);
                cx = Math.Max(MinEmu, _resizeOrigCx + dx);
                break;
            case SelectionAdorner.HandleKind.ResizeNW:
                x  = _resizeOrigX  + dx;
                y  = _resizeOrigY  + dy;
                cx = Math.Max(MinEmu, _resizeOrigCx - dx);
                cy = Math.Max(MinEmu, _resizeOrigCy - dy);
                break;
            case SelectionAdorner.HandleKind.ResizeSE:
                cx = Math.Max(MinEmu, _resizeOrigCx + dx);
                cy = Math.Max(MinEmu, _resizeOrigCy + dy);
                break;
            case SelectionAdorner.HandleKind.ResizeSW:
                x  = _resizeOrigX  + dx;
                cx = Math.Max(MinEmu, _resizeOrigCx - dx);
                cy = Math.Max(MinEmu, _resizeOrigCy + dy);
                break;
        }

        return (x, y, cx, cy);
    }

    // ── Rotate gesture ────────────────────────────────────────────────────────────────────────

    private void StartRotate(uint shapeId, Slide slide, SlideTransform xf, Point screenPt)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;

        _gesture        = GestureKind.Rotate;
        _dragStartScreen= screenPt;
        _rotateShapeId  = shapeId;
        _rotateOrigDeg  = s.RotationDeg;

        // Shape center in slide DIP
        double cx = SlideTransform.EmuToDip(s.OffsetXEmu + s.ExtentCxEmu / 2);
        double cy = SlideTransform.EmuToDip(s.OffsetYEmu + s.ExtentCyEmu / 2);
        _rotateCenterSlide = new Point(cx, cy);

        _canvas.CaptureMouse();
    }

    private void PreviewRotate(Point screenPt, SlideTransform xf)
    {
        double angle = ComputeRotationAngle(screenPt, xf);
        // Show preview using selection rect with rotation hint
        if (_editor.CurrentSlide is not null && _editor.Presentation is not null)
        {
            var s = _editor.CurrentSlide.Shapes.FirstOrDefault(sh => sh.Id == _rotateShapeId);
            if (s is not null)
            {
                var r = GetSelectionScreenRect(_rotateShapeId, _editor.CurrentSlide, xf);
                if (r.HasValue)
                    _adorner.UpdatePreview(r.Value, angle);
            }
        }
    }

    private void CommitRotate(Point screenPt, SlideTransform xf)
    {
        double angle = ComputeRotationAngle(screenPt, xf);
        _editor.RotateShape(_rotateShapeId, angle);
    }

    /// <summary>
    /// Computes the new absolute rotation angle in degrees given the current drag position.
    /// Snaps to 15° increments when Shift is held.
    /// </summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransform xf)
    {
        // Center in screen space
        double cx = _rotateCenterSlide.X * xf.Scale + xf.OffsetX;
        double cy = _rotateCenterSlide.Y * xf.Scale + xf.OffsetY;

        double angle = Math.Atan2(screenPt.Y - cy, screenPt.X - cx) * (180.0 / Math.PI) + 90.0;
        angle = ((angle % 360) + 360) % 360; // normalize to [0,360)
        angle = _rotateOrigDeg + (angle - _rotateOrigDeg); // keep relative

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            angle = Math.Round(angle / 15.0) * 15.0;

        return angle;
    }

    // ── Marquee gesture ───────────────────────────────────────────────────────────────────────

    private void StartMarquee(SlideTransform xf, Point screenPt)
    {
        _gesture            = GestureKind.Marquee;
        _dragStartScreen    = screenPt;
        _marqueeStartSlide  = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        _canvas.CaptureMouse();
    }

    private void PreviewMarquee(Point screenPt, SlideTransform xf)
    {
        var r = new Rect(_dragStartScreen, screenPt);
        _adorner.UpdateMarquee(r);
    }

    private void CommitMarquee(Point screenPt, SlideTransform xf)
    {
        _adorner.UpdateMarquee(null);
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

    // ── Keyboard ──────────────────────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_editor.SelectedShapeIds.Count == 0) return;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        long step  = shift ? LargeNudgeEmu : SmallNudgeEmu;

        switch (e.Key)
        {
            case Key.Left:
                _editor.MoveSelected(-step, 0);
                e.Handled = true;
                break;
            case Key.Right:
                _editor.MoveSelected(step, 0);
                e.Handled = true;
                break;
            case Key.Up:
                _editor.MoveSelected(0, -step);
                e.Handled = true;
                break;
            case Key.Down:
                _editor.MoveSelected(0, step);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                _editor.DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    // ── Cursor feedback ───────────────────────────────────────────────────────────────────────

    private void UpdateCursor(Point screenPt)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _canvas.Cursor = Cursors.Arrow;
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
                    SelectionAdorner.HandleKind.Rotate              => Cursors.Cross,
                    SelectionAdorner.HandleKind.ResizeN or
                    SelectionAdorner.HandleKind.ResizeS             => Cursors.SizeNS,
                    SelectionAdorner.HandleKind.ResizeE or
                    SelectionAdorner.HandleKind.ResizeW             => Cursors.SizeWE,
                    SelectionAdorner.HandleKind.ResizeNE or
                    SelectionAdorner.HandleKind.ResizeSW            => Cursors.SizeNESW,
                    SelectionAdorner.HandleKind.ResizeNW or
                    SelectionAdorner.HandleKind.ResizeSE            => Cursors.SizeNWSE,
                    SelectionAdorner.HandleKind.Body                => Cursors.SizeAll,
                    _                                               => Cursors.Arrow
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
                _canvas.Cursor = Cursors.SizeAll;
                return;
            }
        }

        // Check any shape for hover
        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        _canvas.Cursor = hitId.HasValue ? Cursors.Hand : Cursors.Arrow;
    }

    // ── Adorner refresh ───────────────────────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private Rect? GetSelectionScreenRect(uint shapeId, Slide slide, SlideTransform xf)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null || _editor.Presentation is null) return null;
        var b = ShapeHitTester.GetShapeBoundsDip(s, _editor.Presentation);
        return BoundsToScreenRect(
            SlideTransform.DipToEmu(b.Left), SlideTransform.DipToEmu(b.Top),
            SlideTransform.DipToEmu(b.Width), SlideTransform.DipToEmu(b.Height), xf);
    }

    private static Rect BoundsToScreenRect(long offX, long offY, long cx, long cy, SlideTransform xf)
    {
        double x = SlideTransform.EmuToDip(offX) * xf.Scale + xf.OffsetX;
        double y = SlideTransform.EmuToDip(offY) * xf.Scale + xf.OffsetY;
        double w = SlideTransform.EmuToDip(cx) * xf.Scale;
        double h = SlideTransform.EmuToDip(cy) * xf.Scale;
        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }
}
