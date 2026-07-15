using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;


/// <summary>
/// A WPF panel that renders a single <see cref="Slide"/> using the framework-free
/// <see cref="SlideCompositor"/> to produce draw operations and converts them to WPF primitives.
///
/// Usage: set <see cref="Presentation"/> and <see cref="Slide"/>, call <see cref="Refresh"/>
/// or set the properties — the canvas redraws automatically.
///
/// The control uses OnRender (DrawingContext) to paint all operations directly, which avoids
/// creating a large visual tree of WPF elements for each shape.
///
/// Wave 3C: call <see cref="AttachEditing"/> once (and on every file new/open) to enable
/// selection, move/resize/rotate, and in-canvas text editing.
/// </summary>
public sealed class SlideCanvas : FrameworkElement
{
    // ── Dependency properties ──────────────────────────────────────────────────

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(Presentation),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnModelChanged));

    public static readonly DependencyProperty SlideProperty =
        DependencyProperty.Register(
            nameof(Slide),
            typeof(Slide),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnModelChanged));

    public Presentation? Presentation
    {
        get => (Presentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public Slide? Slide
    {
        get => (Slide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SlideCanvas)d).Refresh();

    // ── Editing (Wave 3C / 9A) ────────────────────────────────────────────────

    private CanvasGestureHandler?      _gestureHandler;
    private InCanvasTextEditor?        _textEditor;
    private InCanvasTableCellEditor?   _tableCellEditor;   // Wave 9A
    private Canvas?                    _textOverlay;   // WPF Canvas layered above SlideCanvas for text-edit overlay
    private PresentationViewShowState  _viewShowState = PresentationViewShowState.Default;
    private PresentationViewZoomState  _viewZoomState = PresentationViewZoomState.FitToWindow;

    /// <summary>
    /// The current slide→screen transform (updated on every render pass).
    /// Used by the gesture handler and adorner layer.
    /// </summary>
    public SlideTransform CurrentTransform { get; private set; } = SlideTransform.Identity;

    /// <summary>
    /// Attaches (or re-attaches) editing interaction to an <see cref="EditingSession"/>.
    /// Call once after constructing the canvas and once on every file new/open to wire up
    /// the new Editor instance.  The overlay canvas must already be in the visual tree.
    /// </summary>
    public void AttachEditing(EditingSession editor, Canvas textOverlay)
    {
        // Detach previous handler if any (don't re-add adorner on every call)
        _textEditor      = null;
        _tableCellEditor = null;
        _gestureHandler  = new CanvasGestureHandler(this, editor);
        ApplyViewShowState(_viewShowState);
        _textOverlay     = textOverlay;
        _textEditor      = new InCanvasTextEditor(this, editor, textOverlay);
        _tableCellEditor = new InCanvasTableCellEditor(this, editor, textOverlay); // Wave 9A
    }

    public PresentationViewShowState ViewShowState => _viewShowState;
    public PresentationViewZoomState ViewZoomState => _viewZoomState;

    public void ApplyViewShowState(PresentationViewShowState state)
    {
        _viewShowState = state;
        if (_gestureHandler is null)
            return;

        _gestureHandler.SnapToGrid = state.ShowGridlines;
        _gestureHandler.SnapToShapes = state.ShowGuides;
    }

    public void ApplyViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        InvalidateVisual();
    }

    // ── Wave 10A: active editor access for ribbon routing ──────────────────────

    /// <summary>
    /// The in-canvas shape text editor.  Null until <see cref="AttachEditing"/> is called.
    /// The ribbon routing in MainWindow uses this to forward Bold/Italic/Underline/Font/Size/Color
    /// to the active selection inside the RichTextBox while the editor is open.
    /// </summary>
    public InCanvasTextEditor? TextEditor => _textEditor;

    /// <summary>
    /// The in-canvas table cell editor.  Null until <see cref="AttachEditing"/> is called.
    /// </summary>
    public InCanvasTableCellEditor? TableCellEditor => _tableCellEditor;

    // ── Cached draw ops (invalidated on model change) ─────────────────────────

    private IReadOnlyList<DrawOp>? _cachedOps;
    private double _slideWidthDip;
    private double _slideHeightDip;

    /// <summary>Forces a recomposition and repaint.</summary>
    public void Refresh()
    {
        _cachedOps = null;
        InvalidateVisual();
    }

    // ── Layout: maintain slide aspect ratio ────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureOps();
        if (_slideWidthDip <= 0 || _slideHeightDip <= 0)
            return base.MeasureOverride(availableSize);

        double ratio = _slideWidthDip / _slideHeightDip;

        double w = double.IsInfinity(availableSize.Width) ? _slideWidthDip : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _slideHeightDip : availableSize.Height;

        // Fit inside available area preserving aspect ratio
        if (w / h > ratio)
            w = h * ratio;
        else
            h = w / ratio;

        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        RenderToDrawingContext(dc, ActualWidth, ActualHeight);
    }

    /// <summary>
    /// Renders the current slide into an arbitrary WPF drawing context.
    /// Used by <see cref="OnRender"/> and by off-screen rasterization.
    /// </summary>
    public void RenderToDrawingContext(DrawingContext dc, double renderW, double renderH)
    {
        EnsureOps();

        if (_cachedOps is null || _cachedOps.Count == 0 || _slideWidthDip <= 0)
            return;

        if (renderW <= 0 || renderH <= 0) return;

        // Scale slide DIP coordinates → actual render pixels (uniform fit).
        CurrentTransform = ComputeViewTransform(renderW, renderH, _slideWidthDip, _slideHeightDip);
        double scale = CurrentTransform.Scale;
        double offsetX = CurrentTransform.OffsetX;
        double offsetY = CurrentTransform.OffsetY;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));

        dc.PushTransform(transform);

        foreach (var op in _cachedOps)
            RenderOp(dc, op);

        dc.Pop();
    }

    private SlideTransform ComputeViewTransform(
        double renderW,
        double renderH,
        double slideWidthDip,
        double slideHeightDip)
    {
        var fit = SlideTransform.Compute(renderW, renderH, slideWidthDip, slideHeightDip);
        var multiplier = PresentationViewZoomPlanner.StageScaleMultiplierFor(_viewZoomState);
        if (Math.Abs(multiplier - 1.0) < 0.0001)
            return fit;

        var scale = fit.Scale * multiplier;
        var offsetX = (renderW - slideWidthDip * scale) / 2.0;
        var offsetY = (renderH - slideHeightDip * scale) / 2.0;
        return new SlideTransform(scale, offsetX, offsetY, slideWidthDip, slideHeightDip);
    }

    private void RenderOp(DrawingContext dc, DrawOp op)
    {
        switch (op)
        {
            case DrawOp.Background bg:
                RenderBackground(dc, bg);
                break;
            case DrawOp.Shape shape:
                RenderShape(dc, shape);
                break;
            case DrawOp.Picture pic:
                RenderPicture(dc, pic);
                break;
            case DrawOp.Table table:
                RenderTable(dc, table);
                break;
            case DrawOp.Chart chartOp:
                RenderChart(dc, chartOp);
                break;
        }
    }

    // ── Background ─────────────────────────────────────────────────────────────

    private static void RenderBackground(DrawingContext dc, DrawOp.Background bg)
    {
        var brush = MakeBrush(bg.Fill, bg.BoundsDip, easeGradientStops: true);
        if (brush is null) return;

        dc.DrawRectangle(brush, null,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ──────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null
            && (shape.ElbowRouteDip is null || shape.ElbowRouteDip.Count < 2)) return;

        var bounds = shape.BoundsDip;
        var renderTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        bool hasTransform = !renderTransform.IsIdentity;

        if (hasTransform)
        {
            dc.PushTransform(ToWpfTransform(renderTransform));
        }

        // Effects: draw before the shape (painter's algorithm — shadow behind shape)
        if (shape.Effects is not null)
            RenderShapeEffects(dc, shape);

        // Wave 26: if an explicit elbow route is provided, draw it as a polyline and
        // skip the bbox-derived elbow geometry.
        if (shape.ElbowRouteDip is { Count: >= 2 })
        {
            var pen = MakePen(shape.Outline);
            if (pen is not null)
            {
                var pg = new PathGeometry();
                var pf = new PathFigure { StartPoint = new Point(shape.ElbowRouteDip[0].X, shape.ElbowRouteDip[0].Y), IsFilled = false };
                for (int ri = 1; ri < shape.ElbowRouteDip.Count; ri++)
                    pf.Segments.Add(new LineSegment(new Point(shape.ElbowRouteDip[ri].X, shape.ElbowRouteDip[ri].Y), isStroked: true));
                pg.Figures.Add(pf);
                dc.DrawGeometry(null, pen, pg);
            }
        }
        else if (shape.Geometry.Contours.Count > 0)
        {
            // Draw geometry
            var geometry = ContourListToGeometry(shape.Geometry);
            var fillBrush = MakeBrush(shape.Fill, bounds);
            var pen = MakePen(shape.Outline);
            dc.DrawGeometry(fillBrush, pen, geometry);
        }

        // Bevel overlay: painted ON TOP of the fill (but before text)
        if (shape.Effects is not null)
            RenderShapeBevel(dc, shape);

        // Draw text overlay
        if (shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        if (hasTransform)
            dc.Pop();
    }

    private static void RenderShapeEffects(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0) return;
        if (shape.Text is not null && shape.Fill is ResolvedFill.None) return;
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects);

        if (plan.ShadowPasses.Count > 0)
        {
            var shadowGeo = ContourListToGeometry(shape.Geometry);
            foreach (var pass in plan.ShadowPasses)
            {
                var shadowBrush = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                if (shadowBrush.CanFreeze) shadowBrush.Freeze();
                dc.PushTransform(new TranslateTransform(pass.OffsetX, pass.OffsetY));
                dc.DrawGeometry(shadowBrush, null, shadowGeo);
                dc.Pop();
            }
        }

        if (plan.GlowPasses.Count > 0)
        {
            var glowGeo = ContourListToGeometry(shape.Geometry);
            foreach (var pass in plan.GlowPasses)
            {
                var glowBrush = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                if (glowBrush.CanFreeze) glowBrush.Freeze();
                var glowPen = new Pen(glowBrush, pass.StrokeWidthDip);
                if (glowPen.CanFreeze) glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, glowGeo);
            }
        }

        // Bevel: overlay highlight + shade stripes on the inner edge of the shape bounds.
        // This runs AFTER the shape fill/outline are drawn (the caller RenderShape draws
        // geometry after calling this method for shadows — but bevel must paint ON TOP of
        // the fill).  We therefore invoke this portion from a second call site in RenderShape
        // (RenderShapeBevel) so it can be layered correctly.
    }

    /// <summary>
    /// Renders the bevel highlight/shade overlay for a shape.
    /// Called AFTER the shape geometry has been painted so the overlay sits on top.
    /// Also draws the contour outline if one is requested.
    /// </summary>
    private static void RenderShapeBevel(DrawingContext dc, DrawOp.Shape shape)
    {
        var fx = shape.Effects;
        if (fx is null) return;

        bool hasBevel   = fx.BevelTop is not null || fx.BevelBottom is not null;
        bool hasContour = fx.ContourWidthDip > 0;
        if (!hasBevel && !hasContour) return;

        if (shape.Geometry.Contours.Count == 0) return;

        var geo    = ContourListToGeometry(shape.Geometry);
        var bounds = shape.BoundsDip;

        if (hasBevel && fx.BevelTop is not null)
        {
            var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, fx.BevelTop, fx.LightDirDeg);
            DrawBevelOverlay(dc, geo, bounds, highlight, shade, fx.BevelTop.WidthDip, fx.BevelTop.HeightDip);
        }

        // Contour outline (thin ring in contourColor)
        if (hasContour)
        {
            var cColor  = fx.ContourColor ?? new SrgbColor(0x60, 0x60, 0x60);
            var contourBrush = new SolidColorBrush(Color.FromArgb(200, cColor.R, cColor.G, cColor.B));
            if (contourBrush.CanFreeze) contourBrush.Freeze();
            var contourPen = new Pen(contourBrush, Math.Max(0.5, fx.ContourWidthDip));
            if (contourPen.CanFreeze) contourPen.Freeze();
            dc.DrawGeometry(null, contourPen, geo);
        }
    }

    private static void DrawBevelOverlay(
        DrawingContext dc,
        Geometry shapeGeo,
        LayoutRect bounds,
        BevelEdgeSet highlight,
        BevelEdgeSet shade,
        double bevelW,
        double bevelH)
    {
        // We draw simple trapezoidal / rectangular strips clipped to the shape geometry.
        // Highlight = near-white semi-transparent; Shade = near-black semi-transparent.
        var highlightBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        var shadeBrush     = new SolidColorBrush(Color.FromArgb(110, 0,   0,   0  ));
        if (highlightBrush.CanFreeze) highlightBrush.Freeze();
        if (shadeBrush.CanFreeze)     shadeBrush.Freeze();

        // Push clip to the shape boundary so bevel strips are contained within it
        dc.PushClip(shapeGeo);

        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var (bw, bh) = BevelGeometryHelper.GetRenderDimensions(bounds, bevelW, bevelH);

        // Draw trapezoidal wedge for each active edge
        void DrawWedge(bool active, Brush brush, Point tl, Point tr, Point bl, Point br)
        {
            if (!active) return;
            var pg = new StreamGeometry();
            using (var sgc = pg.Open())
            {
                sgc.BeginFigure(tl, isFilled: true, isClosed: true);
                sgc.LineTo(tr, isStroked: false, isSmoothJoin: true);
                sgc.LineTo(br, isStroked: false, isSmoothJoin: true);
                sgc.LineTo(bl, isStroked: false, isSmoothJoin: true);
            }
            pg.Freeze();
            dc.DrawGeometry(brush, null, pg);
        }

        // Top edge wedge (trapezoid: outer rect top edge, inner inset)
        DrawWedge(highlight.Top || shade.Top,
            highlight.Top ? highlightBrush : shadeBrush,
            new Point(x,      y),
            new Point(x + w,  y),
            new Point(x + w - bw, y + bh),
            new Point(x + bw, y + bh));

        // Bottom edge wedge
        DrawWedge(highlight.Bottom || shade.Bottom,
            highlight.Bottom ? highlightBrush : shadeBrush,
            new Point(x + bw, y + h - bh),
            new Point(x + w - bw, y + h - bh),
            new Point(x + w,  y + h),
            new Point(x,      y + h));

        // Left edge wedge
        DrawWedge(highlight.Left || shade.Left,
            highlight.Left ? highlightBrush : shadeBrush,
            new Point(x,      y),
            new Point(x + bw, y + bh),
            new Point(x + bw, y + h - bh),
            new Point(x,      y + h));

        // Right edge wedge
        DrawWedge(highlight.Right || shade.Right,
            highlight.Right ? highlightBrush : shadeBrush,
            new Point(x + w - bw, y + bh),
            new Point(x + w,      y),
            new Point(x + w,      y + h),
            new Point(x + w - bw, y + h - bh));

        dc.Pop(); // pop clip
    }

    private static Transform ToWpfTransform(ShapeAffineTransform transform)
    {
        var matrixTransform = new MatrixTransform(new Matrix(
            transform.M11,
            transform.M12,
            transform.M21,
            transform.M22,
            transform.OffsetX,
            transform.OffsetY));
        if (matrixTransform.CanFreeze) matrixTransform.Freeze();
        return matrixTransform;
    }

    // ── Picture ────────────────────────────────────────────────────────────────

    private static void RenderPicture(DrawingContext dc, DrawOp.Picture pic)
    {
        if (pic.Bytes.Length == 0) return;

        BitmapSource? bitmap = null;
        try
        {
            using var ms = new System.IO.MemoryStream(pic.Bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            if (img.CanFreeze) img.Freeze();
            bitmap = img;
        }
        catch
        {
            // Skip undecodable images rather than crashing the renderer.
            return;
        }

        var plan = PictureRenderPlanner.Plan(pic, bitmap.PixelWidth, bitmap.PixelHeight);

        // 18A: apply crop via CroppedBitmap (source sub-rect)
        if (plan.HasCrop)
        {
            var source = plan.SourceRectPixels;
            var cropped = new CroppedBitmap(
                bitmap,
                new Int32Rect(source.X, source.Y, source.Width, source.Height));
            if (cropped.CanFreeze) cropped.Freeze();
            bitmap = cropped;
        }

        // 18A: apply colour effects (grayscale, brightness/contrast, biLevel)
        var effectPlan = plan.ColorEffects;
        if (effectPlan.HasPixelEffects)
            bitmap = ApplyColorEffectsWpf(bitmap, effectPlan);

        var destination = plan.DestinationDip;
        var dest = new Rect(destination.X, destination.Y, destination.Width, destination.Height);

        bool hasRotation = pic.RotationDeg != 0;
        if (hasRotation)
        {
            double cx = dest.Left + dest.Width / 2;
            double cy = dest.Top + dest.Height / 2;
            dc.PushTransform(new RotateTransform(pic.RotationDeg, cx, cy));
        }

        // Wave 26: draw outer shadow behind the picture when effects are set.
        // Route the shadow-direction/blur math through the shared renderer-neutral planner
        // (ResolvedShapeEffectRenderPlanner) so WPF + Avalonia stay in lock-step and we don't duplicate it.
        foreach (var pass in plan.OuterEffects.ShadowPasses)
        {
            var shadowBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(
                    pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
            var shadowDest = new Rect(dest.X + pass.OffsetX, dest.Y + pass.OffsetY, dest.Width, dest.Height);
            if (pic.HasFrameClip && pic.PictureFrameGeometry == "roundRect")
            {
                double rx = Math.Min(dest.Width, dest.Height) * 0.18;
                dc.DrawRoundedRectangle(shadowBrush, null, shadowDest, rx, rx);
            }
            else if (pic.HasFrameClip && pic.PictureFrameGeometry == "ellipse")
                dc.DrawEllipse(shadowBrush, null, new System.Windows.Point(shadowDest.X + shadowDest.Width / 2, shadowDest.Y + shadowDest.Height / 2), shadowDest.Width / 2, shadowDest.Height / 2);
            else
                dc.DrawRectangle(shadowBrush, null, shadowDest);
        }

        // 18A: apply alpha opacity layer if needed
        bool hasAlpha = plan.HasAlphaOpacity;
        if (hasAlpha)
            dc.PushOpacity(plan.AlphaOpacity);

        // Wave 26: clip to frame geometry when a non-rect preset is specified.
        bool hasFrameClip = pic.HasFrameClip;
        if (hasFrameClip)
        {
            double rx = Math.Min(dest.Width, dest.Height) * 0.18;
            Geometry clipGeom = pic.PictureFrameGeometry switch
            {
                "ellipse" => new EllipseGeometry(new System.Windows.Point(dest.X + dest.Width / 2, dest.Y + dest.Height / 2), dest.Width / 2, dest.Height / 2),
                _         => new RectangleGeometry(dest, rx, rx), // roundRect + others
            };
            dc.PushClip(clipGeom);
        }

        dc.DrawImage(bitmap, dest);

        if (hasFrameClip) dc.Pop(); // pop clip

        if (hasAlpha) dc.Pop();

        // P3 / Wave 26: draw the picture frame outline (rounded when HasFrameClip).
        if (pic.Outline is ResolvedOutline.Visible visOutline)
        {
            var pen = MakePen(visOutline);
            if (pen is not null)
            {
                if (pic.HasFrameClip && pic.PictureFrameGeometry == "ellipse")
                    dc.DrawEllipse(null, pen, new System.Windows.Point(dest.X + dest.Width / 2, dest.Y + dest.Height / 2), dest.Width / 2, dest.Height / 2);
                else if (pic.HasFrameClip)
                {
                    double rx = Math.Min(dest.Width, dest.Height) * 0.18;
                    dc.DrawRoundedRectangle(null, pen, dest, rx, rx);
                }
                else
                    dc.DrawRectangle(null, pen, dest);
            }
        }

        // Draw play button overlay for media shapes (already in scaled coords since a transform is pushed).
        if (pic.IsMedia)
            DrawPlayButtonOverlay(dc, dest);

        if (hasRotation) dc.Pop();
    }

    /// <summary>
    /// 18A: Applies grayscale, biLevel and brightness/contrast effects to a decoded WPF bitmap.
    /// Returns a new (frozen) <see cref="WriteableBitmap"/> with effects applied.
    /// Alpha is handled separately via PushOpacity — only pixel-level effects are done here.
    /// </summary>
    private static BitmapSource ApplyColorEffectsWpf(BitmapSource src, PictureColorEffectPlan effectPlan)
    {
        // Convert to Bgra32 for direct pixel access
        var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int pw = bgra.PixelWidth;
        int ph = bgra.PixelHeight;
        int stride = pw * 4;
        var pixels = new byte[ph * stride];
        bgra.CopyPixels(pixels, stride, 0);

        PictureColorEffectPlanner.ApplyToBgra32(pixels, effectPlan);

        var wb = new WriteableBitmap(pw, ph, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, stride, 0);
        if (wb.CanFreeze) wb.Freeze();
        return wb;
    }

    private static void DrawPlayButtonOverlay(DrawingContext dc, Rect dest)
    {
        double cx = dest.Left + dest.Width  / 2;
        double cy = dest.Top  + dest.Height / 2;
        double r  = Math.Min(dest.Width, dest.Height) / 6;
        if (r < 4) r = 4;

        var circleBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
        circleBrush.Freeze();
        dc.DrawEllipse(circleBrush, null, new Point(cx, cy), r, r);

        // Triangle pointing right
        double tx = cx - r * 0.3;
        double ty = cy - r * 0.45;
        var triGeo = new StreamGeometry();
        using (var ctx = triGeo.Open())
        {
            ctx.BeginFigure(new Point(tx,              ty),              isFilled: true, isClosed: true);
            ctx.LineTo(     new Point(tx + r * 0.8,    cy),              true, false);
            ctx.LineTo(     new Point(tx,               cy + r * 0.45),  true, false);
        }
        triGeo.Freeze();
        dc.DrawGeometry(Brushes.White, null, triGeo);
    }

    // ── Table ──────────────────────────────────────────────────────────────────

    private static void RenderTable(DrawingContext dc, DrawOp.Table tableOp)
    {
        foreach (var cell in tableOp.Cells)
            RenderTableCell(dc, cell);
    }

    private static void RenderTableCell(DrawingContext dc, TableCellOp cell)
    {
        var rect = new Rect(cell.BoundsDip.X, cell.BoundsDip.Y, cell.BoundsDip.Width, cell.BoundsDip.Height);

        // Fill
        var fillBrush = MakeBrush(cell.Fill, cell.BoundsDip);
        if (fillBrush is not null)
            dc.DrawRectangle(fillBrush, null, rect);

        // Per-side borders (draw as single-pixel lines along each edge to avoid overlap issues).
        DrawCellBorder(dc, cell.BorderTop,
            new Point(rect.Left,  rect.Top), new Point(rect.Right, rect.Top));
        DrawCellBorder(dc, cell.BorderBottom,
            new Point(rect.Left,  rect.Bottom), new Point(rect.Right, rect.Bottom));
        DrawCellBorder(dc, cell.BorderLeft,
            new Point(rect.Left,  rect.Top), new Point(rect.Left,  rect.Bottom));
        DrawCellBorder(dc, cell.BorderRight,
            new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));

        // Text (respecting cell vertical anchor + insets).
        if (cell.Text is not null)
            RenderTableCellText(dc, cell.Text, cell.BoundsDip, cell.Anchor);
    }

    private static void DrawCellBorder(DrawingContext dc, ResolvedOutline outline, Point p1, Point p2)
    {
        var pen = MakePen(outline);
        if (pen is null) return;
        dc.DrawLine(pen, p1, p2);
    }

    private static void RenderTableCellText(
        DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds,
        FreeP.Core.Model.TableCellAnchor anchor)
    {
        var area = TextLayoutPlanner.GetTextArea(text, bounds);
        var formatted = new Dictionary<int, FormattedText>();
        var measures = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, area.Width, text.Wrap);
            formatted[i] = ft;
            measures.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanTableCellText(text, bounds, anchor, measures);
        foreach (var placement in plan.Paragraphs)
        {
            var ft = formatted[placement.ParagraphIndex];
            dc.DrawText(ft, new Point(placement.X, placement.Y));
        }
    }

    // ── Chart ──────────────────────────────────────────────────────────────────

    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOp)
    {
        var bounds = chartOp.BoundsDip;
        var chart  = chartOp.ChartShape;

        // ── Frame background (white) + border ──────────────────────────────────
        bool classicOfficeStyle = ChartRenderPlanner.UsesClassicOfficeChartStyle(chart);
        var frameBrush = FreezeBrush(new SolidColorBrush(Colors.White));
        Pen? framePen = classicOfficeStyle
            ? null
            : new Pen(FreezeBrush(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF))), 0.5);
        if (framePen?.CanFreeze == true) framePen.Freeze();
        var frameRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        dc.DrawRectangle(frameBrush, framePen, frameRect);

        // ── Layout areas ────────────────────────────────────────────────────────
        var frame = ChartRenderPlanner.BuildFramePlan(chart, ToPlanRect(bounds));
        bool isPie = frame.IsPie;
        bool isBar = frame.IsBar;
        bool isScatterLike = frame.IsScatterLike;
        bool isRadar = frame.IsRadar;
        // Title
        if (chart.Title is not null)
        {
            DrawChartLabel(dc, chart.Title, ToRect(frame.TitleBounds!.Value),
                isBold: !classicOfficeStyle, fontSize: ChartRenderPlanner.ResolveTitleFontSize(chart, 9.0), align: TextAlignment.Center);
        }

        if (!frame.HasPlot) return;

        var plot = frame.Plot;
        double plotX = plot.X;
        double plotY = plot.Y;
        double plotW = plot.Width;
        double plotH = plot.Height;

        // ── Gridlines (drawn before bars so they appear behind) ─────────────────
        var gridLinePlan = ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame);
        if (!ChartRenderPlanner.UsesProjectedSurfaceFrame(chart) && gridLinePlan.GridLines.Count > 0)
        {
            var gridPen = CreateChartGridLinePen(gridLinePlan);
            foreach (var gridLine in gridLinePlan.GridLines)
                dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));
        }

        // ── Dispatch to chart type ─────────────────────────────────────────────
        switch (chart.ChartType)
        {
            case FreeP.Core.Model.ChartType.ColumnClustered:
            case FreeP.Core.Model.ChartType.ColumnStacked:
            case FreeP.Core.Model.ChartType.ColumnStacked100:
                RenderColumnChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Surface:
            case FreeP.Core.Model.ChartType.Surface3D:
                RenderSurfaceChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.BarClustered:
            case FreeP.Core.Model.ChartType.BarStacked:
            case FreeP.Core.Model.ChartType.BarStacked100:
                RenderBarChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Line:
            case FreeP.Core.Model.ChartType.LineMarkers:
                RenderLineChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH,
                    withMarkers: chart.ChartType == FreeP.Core.Model.ChartType.LineMarkers);
                break;

            case FreeP.Core.Model.ChartType.Stock:
                RenderStockChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Pie:
                RenderPieChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Doughnut:
                RenderDoughnutChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Area:
            case FreeP.Core.Model.ChartType.AreaStacked:
                RenderAreaChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Scatter:
                RenderScatterChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Bubble:
                RenderBubbleChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Radar:
                RenderRadarChart(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
                break;

            default:
                // Unknown — render a placeholder rectangle
                dc.DrawRectangle(
                    FreezeBrush(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))),
                    null,
                    new Rect(plotX, plotY, plotW, plotH));
                break;
        }

        // ── Combo chart: render secondary-group series with their OverrideChartType ──
        // (e.g. a lineChart series overlaid on a barChart primary — rendered as a line).
        bool hasOverrideSeries = chart.Series.Any(s => s.OverrideChartType.HasValue);
        if (hasOverrideSeries && !isPie && !isBar && !isRadar && !isScatterLike)
        {
            RenderComboOverrideSeries(dc, chart, chartOp.SeriesColors, chartOp.FillPlans, plotX, plotY, plotW, plotH);
        }

        var tickPlan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);
        if (tickPlan.CategoryTicks.Count > 0 || tickPlan.ValueTicks.Count > 0)
        {
            var tickPen = CreateChartAxisTickPen(tickPlan);
            foreach (var tick in tickPlan.CategoryTicks)
                dc.DrawLine(tickPen, ToPoint(tick.Start), ToPoint(tick.End));
            foreach (var tick in tickPlan.ValueTicks)
                dc.DrawLine(tickPen, ToPoint(tick.Start), ToPoint(tick.End));
        }

        // ── Data labels ────────────────────────────────────────────────────────
        foreach (var label in ChartRenderPlanner.BuildDataLabelPlans(chart, plot))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                isBold: label.IsBold,
                fontSize: label.FontSize,
                align: ToTextAlignment(label.Alignment));
        }

        // ── Secondary value axis (right side) ──────────────────────────────────
        var dataTablePlan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame, chartOp.SeriesColors, chartOp.FillPlans);
        RenderChartDataTable(dc, dataTablePlan);

        var secondaryAxisPlan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);
        if (secondaryAxisPlan.Ticks.Count > 0 || secondaryAxisPlan.Labels.Count > 0)
        {
            var secondaryTickPen = CreateChartSecondaryAxisTickPen(secondaryAxisPlan);
            foreach (var tick in secondaryAxisPlan.Ticks)
                dc.DrawLine(secondaryTickPen, ToPoint(tick.Start), ToPoint(tick.End));
            foreach (var label in secondaryAxisPlan.Labels)
            {
                DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                    isBold: label.IsBold,
                    fontSize: label.FontSize,
                    align: ToTextAlignment(label.Alignment));
            }
        }

        if (secondaryAxisPlan.Title is { } secondaryAxisTitle)
            DrawChartAxisTitle(dc, secondaryAxisTitle);

        // ── Axis labels ────────────────────────────────────────────────────────
        foreach (var label in ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                isBold: label.IsBold,
                fontSize: label.FontSize,
                align: ToTextAlignment(label.Alignment));
        }

        // Value axis labels using nice tick values
        foreach (var label in ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                isBold: label.IsBold,
                fontSize: label.FontSize,
                align: ToTextAlignment(label.Alignment));
        }

        foreach (var title in ChartRenderPlanner.BuildAxisTitlePlans(chart, frame))
        {
            DrawChartAxisTitle(dc, title);
        }

        foreach (var item in ChartRenderPlanner.BuildLegendItemPlans(chart, frame, chartOp.SeriesColors, chartOp.FillPlans))
        {
            dc.DrawRectangle(
                ToBrush(item.Fill),
                null,
                ToRect(item.SwatchBounds));
            DrawChartLabel(dc, item.Label.Text, ToRect(item.Label.Bounds),
                isBold: item.Label.IsBold,
                fontSize: item.Label.FontSize,
                align: ToTextAlignment(item.Label.Alignment));
        }
    }

    // ── Column chart ─────────────────────────────────────────────────────────

    private static void RenderColumnChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildColumnPrimitives(chart, plot, seriesColors, fillPlans))
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }
    }

    // ── Combo-chart secondary series overlay ─────────────────────────────────
    /// <summary>
    /// Renders series that carry a per-series <see cref="FreeP.Core.Model.ChartSeries.OverrideChartType"/>
    /// (set by the IO reader for combo charts where a secondary chart-type group, e.g. a
    /// lineChart, is mixed with the primary type, e.g. barChart).
    /// Only Line / LineMarkers overrides are handled here; others are future-proofed silently.
    /// </summary>
    private static void RenderComboOverrideSeries(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildComboOverrideLineSeriesPrimitives(chart, plot, seriesColors, fillPlans))
            RenderLineSeriesPrimitive(dc, primitive);
    }

    // ── Bar (horizontal) chart ────────────────────────────────────────────────

    private static void RenderBarChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildBarPrimitives(chart, plot, seriesColors, fillPlans))
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }
    }

    // ── Line chart ────────────────────────────────────────────────────────────

    private static void RenderLineChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH,
        bool withMarkers)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildLineSeriesPrimitives(chart, plot, withMarkers, seriesColors, fillPlans))
            RenderLineSeriesPrimitive(dc, primitive);
    }

    private static void RenderLineSeriesPrimitive(
        DrawingContext dc,
        ChartLineSeriesPrimitive primitive)
    {
        if (primitive.Depth is { } depth)
        {
            foreach (var path in primitive.LinePaths)
            {
                var depthStroke = path.Stroke with { Alpha = depth.StrokeAlpha };
                dc.DrawGeometry(
                    null,
                    ToPen(depthStroke),
                    ToGeometry(path, depth));
            }
        }

        foreach (var path in primitive.LinePaths)
            dc.DrawGeometry(null, ToPen(path.Stroke), ToGeometry(path));

        foreach (var marker in primitive.Markers)
            DrawChartMarker(dc, marker);
    }

    // ── Pie chart ─────────────────────────────────────────────────────────────

    private static void RenderPieChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildPieSlicePrimitives(chart, plot, seriesColors, fillPlans))
        {
            // Resolve slice color: seriesColors is pre-expanded per-point by the compositor
            // (cycling accent1-6 from the theme) so point index gives the correct slice fill.
            SrgbColor sc = primitive.PointIndex < seriesColors.Count
                ? seriesColors[primitive.PointIndex]
                : new SrgbColor(0x4F, 0x81, 0xBD);

            var fill = primitive.Fill ?? new ChartFillPlan(sc, Alpha: 255);
            if (primitive.HasThreeDDepth)
            {
                var depthFill = fill.WithAlpha(ChartRenderPlanner.ThreeDPieDepthFillAlpha);
                dc.DrawGeometry(
                    ToBrush(depthFill),
                    null,
                    ToPieSliceGeometry(primitive, primitive.DepthOffsetY));
            }

            var brush = ToBrush(fill);
            var geo = ToPieSliceGeometry(primitive);

            var borderPen = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
            if (borderPen.CanFreeze) borderPen.Freeze();

            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    private static StreamGeometry ToPieSliceGeometry(ChartPieSlicePrimitive primitive, double offsetY = 0)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var center = new ChartPlanPoint(primitive.Center.X, primitive.Center.Y + offsetY);
            var start = new ChartPlanPoint(primitive.OuterStart.X, primitive.OuterStart.Y + offsetY);
            var end = new ChartPlanPoint(primitive.OuterEnd.X, primitive.OuterEnd.Y + offsetY);

            ctx.BeginFigure(ToPoint(center), isFilled: true, isClosed: true);
            ctx.LineTo(ToPoint(start), isStroked: false, isSmoothJoin: false);
            ctx.ArcTo(ToPoint(end), new Size(primitive.OuterRadius, primitive.OuterRadiusY), 0, primitive.IsLargeArc,
                SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
        }
        if (geo.CanFreeze) geo.Freeze();

        return geo;
    }

    // ── Area chart ────────────────────────────────────────────────────────────

    private static void RenderAreaChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildAreaSeriesPrimitives(chart, plot, seriesColors, fillPlans))
        {
            if (primitive.AreaPath.Fill is not { } fill)
                continue;

            if (primitive.Depth is { } depth)
            {
                var depthFill = fill.WithAlpha(depth.FillAlpha);
                dc.DrawGeometry(ToBrush(depthFill), null, ToAreaGeometry(OffsetPath(primitive.AreaPath, depth)));
            }

            var brush = ToBrush(fill);
            var geo = ToAreaGeometry(primitive.AreaPath);

            dc.DrawGeometry(brush, null, geo);
        }
    }

    // ── Doughnut chart ────────────────────────────────────────────────────────

    private static void RenderSurfaceChart(
        DrawingContext dc,
        FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX,
        double plotY,
        double plotW,
        double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(chart, plot, seriesColors);

        if (plan.Facets.Count > 0)
        {
            foreach (var facet in plan.Facets)
            {
                dc.DrawGeometry(
                    ToBrush(facet.Fill),
                    ToPen(facet.Stroke),
                    ToSurfaceFacetGeometry(facet));
            }
        }
        else
        {
            foreach (var primitive in plan.Cells)
            {
                dc.DrawRectangle(
                    ToBrush(primitive.Fill),
                    ToPen(primitive.Stroke),
                    ToRect(primitive.Bounds));
            }
        }

        foreach (var segment in plan.ContourSegments)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));

        foreach (var segment in plan.WireframeSegments)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
    }

    private static void RenderDoughnutChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var borderPen = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
        if (borderPen.CanFreeze) borderPen.Freeze();

        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildDoughnutSlicePrimitives(chart, plot, seriesColors, fillPlans))
        {
            SrgbColor sc = primitive.PointIndex < seriesColors.Count
                ? seriesColors[primitive.PointIndex]
                : GetSeriesColor(chart, primitive.PointIndex, 0, seriesColors);
            var brush = ToBrush(primitive.Fill ?? new ChartFillPlan(sc, Alpha: 255));

            // Build annular wedge: outer arc CW, then inner arc CCW.
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ToPoint(primitive.OuterStart), isFilled: true, isClosed: true);
                ctx.ArcTo(
                    ToPoint(primitive.OuterEnd),
                    new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.Clockwise,
                    isStroked: false,
                    isSmoothJoin: false);
                ctx.LineTo(ToPoint(primitive.InnerEnd), isStroked: false, isSmoothJoin: false);
                ctx.ArcTo(
                    ToPoint(primitive.InnerStart),
                    new Size(primitive.InnerRadius, primitive.InnerRadiusY),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.Counterclockwise,
                    isStroked: false,
                    isSmoothJoin: false);
            }
            if (geo.CanFreeze) geo.Freeze();
            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    // ── Scatter (XY) chart ────────────────────────────────────────────────────

    private static void RenderScatterChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot, seriesColors, fillPlans);
        var gridPen = ToPen(plan.GridLineStroke);

        foreach (var gridLine in plan.GridLines)
            dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));

        foreach (var primitive in plan.Series)
        {
            foreach (var path in primitive.LinePaths)
                dc.DrawGeometry(null, ToPen(path.Stroke), ToGeometry(path));

            foreach (var marker in primitive.Markers)
                DrawChartMarker(dc, marker);
        }

        foreach (var label in plan.XAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        foreach (var label in plan.YAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        foreach (var label in plan.DataLabels)
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment));
        }
    }

    // ── Bubble chart ──────────────────────────────────────────────────────────

    private static void RenderStockChart(
        DrawingContext dc,
        FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX,
        double plotY,
        double plotW,
        double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildStockVolumePrimitives(chart, plot, seriesColors))
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }

        var plan = ChartRenderPlanner.BuildStockPrimitivePlan(chart, plot);

        foreach (var segment in plan.HighLowLines)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        foreach (var tick in plan.OpenTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
        foreach (var tick in plan.CloseTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
    }

    private static void RenderBubbleChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot, seriesColors, fillPlans);
        var gridPen = ToPen(plan.GridLineStroke);

        foreach (var gridLine in plan.GridLines)
            dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));

        foreach (var primitive in plan.Bubbles)
        {
            dc.DrawEllipse(
                ToBrush(primitive.Fill),
                ToPen(primitive.Stroke),
                ToPoint(primitive.Center),
                primitive.Radius,
                primitive.Radius);
        }

        foreach (var label in plan.XAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        foreach (var label in plan.YAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
    }

    // ── Radar chart ───────────────────────────────────────────────────────────

    private static void RenderRadarChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartFillPlanSet fillPlans,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot, seriesColors, fillPlans);

        foreach (var ring in plan.Rings)
            dc.DrawGeometry(null, ToPen(ring.Stroke), ToGeometry(ring.Path));

        var spokePen = ToPen(plan.SpokeStroke);
        foreach (var spoke in plan.Spokes)
            dc.DrawLine(spokePen, ToPoint(spoke.Start), ToPoint(spoke.End));

        foreach (var label in plan.CategoryLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));

        foreach (var primitive in plan.Series)
        {
            var pen = ToPen(primitive.Stroke);
            foreach (var path in primitive.Paths)
            {
                dc.DrawGeometry(
                    path.Fill.HasValue ? ToBrush(path.Fill.Value) : null,
                    pen,
                    ToGeometry(path));
            }

            foreach (var marker in primitive.Markers)
                DrawChartMarker(dc, marker);
        }
    }

    // ── Chart helpers ─────────────────────────────────────────────────────────

    private static SrgbColor GetSeriesColor(
        FreeP.Core.Model.ChartShape chart, int seriesIndex, int pointIndex,
        IReadOnlyList<SrgbColor> seriesColors)
    {
        if (seriesIndex < seriesColors.Count) return seriesColors[seriesIndex];
        // Fallback cycle
        var fallbacks = new SrgbColor[]
        {
            new(0x4F, 0x81, 0xBD), new(0xC0, 0x50, 0x4D),
            new(0x9B, 0xBB, 0x59), new(0x80, 0x64, 0xA2),
            new(0x4B, 0xAC, 0xC6), new(0xF7, 0x96, 0x46)
        };
        return fallbacks[seriesIndex % fallbacks.Length];
    }

    /// <summary>
    /// Computes nice axis min/max/majorUnit matching PowerPoint's auto-scale algorithm:
    /// major unit is chosen from {1, 2, 2.5, 5} × 10^n so that there are ~4-6 intervals.
    /// Considers ONLY series that are NOT on the secondary axis (OnSecondaryAxis == false).
    /// Returns (min, max, majorUnit).
    /// </summary>
    internal static (double min, double max, double majorUnit) ComputeNiceAxisRange(
        FreeP.Core.Model.ChartShape chart) =>
        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

    /// <summary>
    /// Computes the nice axis range for the SECONDARY value axis using ONLY series that have
    /// OnSecondaryAxis == true.  Returns (0,1,1) when there are no secondary-axis series
    /// (avoids divide-by-zero).  CB1 fix.
    /// </summary>
    internal static (double min, double max, double majorUnit) ComputeNiceSecondaryAxisRange(
        FreeP.Core.Model.ChartShape chart) =>
        ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

    /// <summary>
    /// Nice-number axis range for scatter/bubble X axis (uses XValues) or Y axis (uses Values).
    /// </summary>
    internal static (double min, double max, double majorUnit) ComputeNiceScatterAxisRange(
        FreeP.Core.Model.ChartShape chart, bool useX) =>
        ChartRenderPlanner.ComputeScatterAxisRange(chart, useX);

    // Keep old signature for compatibility with existing callers that only need min/max
    private static (double min, double max) ComputeAxisRange(FreeP.Core.Model.ChartShape chart)
    {
        var (min, max, _) = ComputeNiceAxisRange(chart);
        return (min, max);
    }

    private static string FormatAxisValue(double v) =>
        ChartRenderPlanner.FormatAxisValue(v);

    private static void DrawChartLabel(
        DrawingContext dc, string text, Rect rect,
        bool isBold, double fontSize, TextAlignment align,
        bool isItalic = false,
        SrgbColor? textColor = null,
        string? fontFamily = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var color = textColor ?? new SrgbColor(0x40, 0x40, 0x40);
        var typeface = new Typeface(
            new FontFamily(fontFamily ?? "Calibri"),
            isItalic ? FontStyles.Italic : FontStyles.Normal,
            isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize * (96.0 / 72.0),
            FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))),
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0)
        {
            MaxTextWidth   = rect.Width,
            MaxLineCount   = 1,
            TextAlignment  = align,
            Trimming       = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(ft, new Point(rect.X, rect.Y));
    }

    // ── Text ────────────────────────────────────────────────────────────────────

    private static void RenderChartDataTable(
        DrawingContext dc,
        ChartDataTablePrimitivePlan plan)
    {
        if (!plan.Bounds.HasPositiveArea)
            return;

        if (plan.BackgroundFill.HasValue)
            dc.DrawRectangle(ToBrush(plan.BackgroundFill.Value), null, ToRect(plan.Bounds));

        var borderPen = ToPen(plan.BorderStroke);
        foreach (var border in plan.HorizontalBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));
        foreach (var border in plan.VerticalBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));
        foreach (var border in plan.OutlineBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));

        foreach (var cell in plan.Cells)
        {
            dc.PushClip(new RectangleGeometry(ToRect(cell.CellBounds)));

            if (cell.LegendKeyFill.HasValue && cell.LegendKeyBounds.HasValue)
            {
                dc.DrawRectangle(
                    ToBrush(cell.LegendKeyFill.Value),
                    null,
                    ToRect(cell.LegendKeyBounds.Value));
            }

            DrawChartLabel(
                dc,
                cell.Text,
                ToRect(cell.Bounds),
                cell.IsBold,
                cell.FontSize,
                ToTextAlignment(cell.Alignment),
                cell.IsItalic,
                cell.TextColor,
                cell.FontFamily);

            dc.Pop();
        }
    }

    private static void DrawChartAxisTitle(DrawingContext dc, ChartAxisTitlePlan title)
    {
        var label = title.Label;
        var rect = ToRect(label.Bounds);
        if (title.Orientation == ChartAxisTitleOrientation.Horizontal)
        {
            DrawChartLabel(dc, label.Text, rect, label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
            return;
        }

        double angle = title.Orientation == ChartAxisTitleOrientation.VerticalClockwise ? 90.0 : -90.0;
        double cx = rect.X + rect.Width * 0.5;
        double cy = rect.Y + rect.Height * 0.5;
        dc.PushTransform(new RotateTransform(angle, cx, cy));
        DrawChartLabel(
            dc,
            label.Text,
            new Rect(
                rect.X + (rect.Width - rect.Height) * 0.5,
                rect.Y + (rect.Height - rect.Width) * 0.5,
                rect.Height,
                rect.Width),
            label.IsBold,
            label.FontSize,
            ToTextAlignment(label.Alignment));
        dc.Pop();
    }

    private static void RenderText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        // Wave 18B: vertical text — rotate the text block around the shape center and swap
        // the effective text-area dimensions so layout uses the rotated extent.
        var orientation = TextLayoutPlanner.PlanTextOrientation(text, bounds);
        if (orientation.RenderMode == TextVerticalRenderMode.StackedUpright)
        {
            RenderStackedVerticalText(dc, text, bounds);
            return;
        }

        if (orientation.IsRotated)
        {
            dc.PushTransform(new RotateTransform(
                orientation.RotationAngleDegrees,
                orientation.RotationCenterX,
                orientation.RotationCenterY));
            RenderTextCore(dc, text, orientation.TextBounds);
            dc.Pop();
            return;
        }

        RenderTextCore(dc, text, orientation.TextBounds);
    }

    private static void RenderStackedVerticalText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        var initialPlan = TextLayoutPlanner.PlanStackedVerticalText(
            text,
            bounds,
            MeasureStackedGlyphWpf);
        var autoFitPlan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            initialPlan.Area.Height,
            initialPlan.Paragraphs);
        var renderText = TextLayoutPlanner.ApplyAutoFitPlan(text, autoFitPlan);
        var plan = TextLayoutPlanner.PlanStackedVerticalText(
            renderText,
            bounds,
            MeasureStackedGlyphWpf,
            autoFitPlan);

        foreach (var glyph in plan.Glyphs)
        {
            if ((uint)glyph.ParagraphIndex >= (uint)renderText.Paragraphs.Count)
                continue;
            var paragraph = renderText.Paragraphs[glyph.ParagraphIndex];
            if ((uint)glyph.RunIndex >= (uint)paragraph.Runs.Count)
                continue;

            DrawStackedGlyphWpf(dc, renderText, bounds, paragraph.Runs[glyph.RunIndex], glyph);
        }
    }

    // Wave 22B: multi-column text layout helper.
    // Greedy paragraph-level assignment: fill column 1 top-to-bottom, then column 2, etc.
    private static void RenderTextCoreColumns(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        var initialColumnLayout = TextLayoutPlanner.GetColumnLayout(text, bounds);
        var initialMeasured = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0)
            {
                continue;
            }
            var ft = BuildFormattedText(para, initialColumnLayout.ColumnWidthDip, text.Wrap);
            initialMeasured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var autoFitPlan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            TextLayoutPlanner.GetAutoFitCapacityHeight(initialColumnLayout),
            initialMeasured);
        var renderText = TextLayoutPlanner.ApplyAutoFitPlan(text, autoFitPlan);
        var columnLayout = TextLayoutPlanner.GetColumnLayout(renderText, bounds, autoFitPlan);
        var formatted = new Dictionary<int, FormattedText>();
        var measured = new List<TextParagraphMeasure>();

        for (int i = 0; i < renderText.Paragraphs.Count; i++)
        {
            var para = renderText.Paragraphs[i];
            if (para.Runs.Count == 0)
            {
                continue;
            }
            var ft = BuildFormattedText(para, columnLayout.ColumnWidthDip, renderText.Wrap);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt,
                columnLayout.LineSpacingScale));
        }

        var plan = TextLayoutPlanner.PlanColumns(renderText, columnLayout, measured);
        foreach (var placement in plan.Paragraphs)
        {
            var para = renderText.Paragraphs[placement.ParagraphIndex];
            var ft = formatted[placement.ParagraphIndex];
            if (placement.Bullet is { } bullet)
                DrawBulletPlacementWpf(dc, bullet);

            switch (TextLayoutPlanner.PlanParagraphRenderRoute(para, renderText))
            {
                case TextParagraphRenderRoute.Math:
                    RenderParaWithMath(dc, para, placement.X, placement.Y);
                    break;
                case TextParagraphRenderRoute.Effects:
                    RenderParaWithEffects(dc, para, placement.X, placement.Y, placement.MaxWidthDip, renderText.Wrap, renderText, bounds);
                    break;
                case TextParagraphRenderRoute.Tabs:
                    RenderParaWithTabs(dc, para, placement.X, placement.Y, para.TabStops);
                    break;
                default:
                    if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                        ft.MaxTextWidth = placement.MaxWidthDip;
                    dc.DrawText(ft, new Point(placement.X, placement.Y));
                    break;
            }
        }
    }

    private static void RenderTextCore(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        // Wave 22B: multi-column layout
        if (text.ColumnCount > 1)
        {
            RenderTextCoreColumns(dc, text, bounds);
            return;
        }

        var area = TextLayoutPlanner.GetTextArea(text, bounds);
        var initialMeasured = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;

            var ft = BuildFormattedText(para, area.Width, text.Wrap);
            initialMeasured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var autoFitPlan = TextLayoutPlanner.PlanNormalAutoFitOverflow(text, area.Height, initialMeasured);
        var renderText = TextLayoutPlanner.ApplyAutoFitPlan(text, autoFitPlan);
        area = TextLayoutPlanner.GetTextArea(renderText, bounds);

        var formatted = new Dictionary<int, FormattedText>();
        var measured = new List<TextParagraphMeasure>();
        for (int i = 0; i < renderText.Paragraphs.Count; i++)
        {
            var para = renderText.Paragraphs[i];
            if (para.Runs.Count == 0) continue;

            var ft = BuildFormattedText(para, area.Width, renderText.Wrap);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanBodyText(renderText, bounds, measured, autoFitPlan);
        foreach (var placement in plan.Paragraphs)
        {
            var para = renderText.Paragraphs[placement.ParagraphIndex];
            var ft = formatted[placement.ParagraphIndex];

            if (placement.Bullet is { } bullet)
            {
                DrawBulletPlacementWpf(dc, bullet);
            }

            switch (TextLayoutPlanner.PlanParagraphRenderRoute(para, renderText))
            {
                case TextParagraphRenderRoute.Math:
                    RenderParaWithMath(dc, para, placement.X, placement.Y);
                    break;
                case TextParagraphRenderRoute.Effects:
                    RenderParaWithEffects(dc, para, placement.X, placement.Y, placement.MaxWidthDip, renderText.Wrap, renderText, bounds);
                    break;
                case TextParagraphRenderRoute.Tabs:
                    RenderParaWithTabs(dc, para, placement.X, placement.Y, para.TabStops);
                    break;
                default:
                    if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                        ft.MaxTextWidth = placement.MaxWidthDip;
                    dc.DrawText(ft, new Point(placement.X, placement.Y));
                    break;
            }
        }
    }

    /// <summary>
    /// Wave 19A: draws a bullet glyph or number string at the given position.
    /// </summary>
    private static void DrawBulletPlacementWpf(DrawingContext dc, TextBulletPlacement bullet)
    {
        if (bullet.Image is { Bytes.Length: > 0 } image)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(image.Bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();

                double size = Math.Max(1.0, bullet.FontSizePt * (96.0 / 72.0));
                dc.DrawImage(bitmap, new Rect(bullet.X, bullet.Y, size, size));
            }
            catch
            {
                // Keep text rendering resilient when an imported bullet image cannot be decoded.
            }

            return;
        }

        DrawBulletWpf(dc, bullet.Text, bullet.FontFamily, bullet.FontSizePt,
            bullet.Color, bullet.X, bullet.Y);
    }

    private static void DrawBulletWpf(
        DrawingContext dc,
        string bulletText,
        string fontFamily,
        double fontSizePt,
        SrgbColor color,
        double x,
        double y)
    {
        if (string.IsNullOrEmpty(bulletText)) return;
        var typeface = new Typeface(new FontFamily(fontFamily),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double emPx = fontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        if (brush.CanFreeze) brush.Freeze();
        var ft = new FormattedText(
            bulletText,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface, emPx, brush,
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    /// <summary>
    /// Renders a paragraph run-by-run, expanding tab characters to the next tab stop position.
    /// Default tab interval is 96 DIP (1 inch at 96 DPI) when tab stops are exhausted.
    /// </summary>
    private static void RenderParaWithTabs(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX,
        double startY,
        IReadOnlyList<ResolvedTabStop> tabStops)
    {
        var plan = TextLayoutPlanner.PlanTabStops(
            para,
            startX,
            tabStops,
            (run, text) => BuildSingleRunFormattedTextAt(run, text).Width);

        foreach (var segment in plan.Segments)
        {
            var ft = BuildSingleRunFormattedTextAt(para.Runs[segment.RunIndex], segment.Text);
            dc.DrawText(ft, new Point(segment.X, startY));
        }
    }

    // ── Theme 27: math rendering ────────────────────────────────────────────────

    /// <summary>
    /// Renders a paragraph that contains one or more OMML math runs by calling
    /// <see cref="MathBoxRenderPlanner.Plan"/> for each math run and drawing the
    /// resulting renderer-neutral ops as WPF primitives.
    /// Non-math runs in the same paragraph are drawn with plain FormattedText.
    /// ALL layout is in the shared MathBoxRenderPlanner; only WPF draw calls live here.
    ///
    /// HB4: math and text runs are BASELINE-aligned, not top-aligned. We first
    /// measure every run's ascent (math box Ascent, or FormattedText.Baseline
    /// for plain text), take the line's shared baseline as the max ascent, then
    /// draw every run's top at (baselineY - runAscent) so all runs share one
    /// baseline — matching how a mixed "text + fraction" line is typeset.
    /// Marked internal (not private) so FreeP.App.Host.Tests can call it directly.
    /// </summary>
    internal static void RenderParaWithMath(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX, double startY)
    {
        // Pass 1: measure each run's ascent to find the line's common baseline.
        double lineAscent = 0;
        var formatted = new FormattedText?[para.Runs.Count];
        for (int i = 0; i < para.Runs.Count; i++)
        {
            var run = para.Runs[i];
            if (run.IsMathRun && run.MathLayout is not null)
            {
                lineAscent = Math.Max(lineAscent, run.MathLayout.Metrics.Ascent);
            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                var ft = BuildSingleRunFormattedTextAt(run, run.Text);
                formatted[i] = ft;
                lineAscent = Math.Max(lineAscent, ft.Baseline);
            }
        }

        double baselineY = ComputeBaselineY(startY, lineAscent);

        // Pass 2: draw each run with its top placed so its ascent lands on baselineY.
        double x = startX;
        for (int i = 0; i < para.Runs.Count; i++)
        {
            var run = para.Runs[i];
            if (run.IsMathRun && run.MathLayout is not null)
            {
                double runY = ComputeRunTopY(baselineY, run.MathLayout.Metrics.Ascent);

                // Plan the math draw ops using the shared engine (renderer-neutral).
                var mathOps = MathBoxRenderPlanner.Plan(
                    run.MathLayout, x, runY, run.Color, run.FontFamily);

                foreach (var op in mathOps)
                    DrawMathOpWpf(dc, op);

                x += run.MathLayout.Metrics.Width;
            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                // Plain text run inline with math, baseline-aligned with it.
                var ft = formatted[i]!;
                double runY = ComputeRunTopY(baselineY, ft.Baseline);
                dc.DrawText(ft, new Point(x, runY));
                x += ft.Width;
            }
        }
    }

    /// <summary>
    /// HB4 pure helper: the shared line baseline (in slide-space DIP) given the
    /// paragraph's top Y and the max ascent across all its runs (text or math).
    /// Exposed internal so tests can validate the baseline math without needing
    /// a live DrawingContext.
    /// </summary>
    internal static double ComputeBaselineY(double startY, double lineAscent) => startY + lineAscent;

    /// <summary>
    /// HB4 pure helper: the top Y at which a run with the given ascent must be
    /// drawn so its own baseline lands exactly on <paramref name="baselineY"/>.
    /// </summary>
    internal static double ComputeRunTopY(double baselineY, double runAscent) => baselineY - runAscent;

    /// <summary>
    /// Draws a single <see cref="MathDrawOp"/> as WPF primitives.
    /// All math layout decisions are already made by <see cref="MathBoxRenderPlanner"/>;
    /// this method only translates to WPF draw calls.
    /// </summary>
    private static void DrawMathOpWpf(DrawingContext dc, MathDrawOp op)
    {
        switch (op)
        {
            case MathDrawOp.DrawGlyph g:
            {
                var typeface = new Typeface(
                    new FontFamily(g.FontFamily),
                    g.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                    g.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal);
                double emPx = g.FontSizePt * (96.0 / 72.0);
                var brush = FreezeBrush(new SolidColorBrush(
                    Color.FromRgb(g.Color.R, g.Color.G, g.Color.B)));
                var ft = new FormattedText(
                    g.Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface, emPx, brush,
                    numberSubstitution: null,
                    textFormattingMode: TextFormattingMode.Display,
                    pixelsPerDip: 1.0);
                dc.DrawText(ft, new Point(g.X, g.Y));
                break;
            }

            case MathDrawOp.DrawHRule hr:
            {
                var pen = new Pen(FreezeBrush(new SolidColorBrush(
                    Color.FromRgb(hr.Color.R, hr.Color.G, hr.Color.B))), hr.Thickness);
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawLine(pen, new Point(hr.X, hr.Y), new Point(hr.X + hr.Width, hr.Y));
                break;
            }

            case MathDrawOp.DrawLine line:
            {
                var pen = new Pen(FreezeBrush(new SolidColorBrush(
                    Color.FromRgb(line.Color.R, line.Color.G, line.Color.B))), line.Thickness);
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawLine(pen, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                break;
            }

            case MathDrawOp.DrawBracket br:
            {
                // Scale the bracket character to match the required height.
                // We draw it as FormattedText scaled via a transform.
                double naturalEm = br.ScaledHeight * 0.85;
                var typeface = new Typeface(
                    new FontFamily(br.FontFamily.Length > 0 ? br.FontFamily : "Cambria Math"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var brush = FreezeBrush(new SolidColorBrush(
                    Color.FromRgb(br.Color.R, br.Color.G, br.Color.B)));
                var ft = new FormattedText(
                    br.Character,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface, naturalEm, brush,
                    numberSubstitution: null,
                    textFormattingMode: TextFormattingMode.Display,
                    pixelsPerDip: 1.0);
                dc.DrawText(ft, new Point(br.X, br.Y));
                break;
            }

            case MathDrawOp.DrawRadical rad:
            {
                var pen = new Pen(FreezeBrush(new SolidColorBrush(
                    Color.FromRgb(rad.Color.R, rad.Color.G, rad.Color.B))),
                    rad.OverlineThickness);
                if (pen.CanFreeze) pen.Freeze();

                // Draw the √ check-mark as a path:
                //   Start at top-left shoulder, down-left to foot, up-right to radicand base-left, then up to overline.
                double x0 = rad.X;
                double x1 = rad.X + rad.SignWidth * 0.25;  // foot x
                double x2 = rad.X + rad.SignWidth;           // right edge of sign = start of overline
                double xOvEnd = x2 + rad.OverlineWidth;
                double yTop  = rad.Y + rad.OverlineThickness / 2.0;
                double yFoot = rad.Y + rad.Height * 0.85;
                double yBase = rad.Y + rad.OverlineThickness;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x0, yTop + (yFoot - yTop) * 0.4), isFilled: false, isClosed: false);
                    ctx.LineTo(new Point(x1, yFoot), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(x2, yBase), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(xOvEnd, yBase), isStroked: true, isSmoothJoin: false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
        }
    }

    /// <summary>Builds a single-run FormattedText for the given text segment (may be a tab-split piece).</summary>
    private static FormattedText BuildSingleRunFormattedTextAt(ResolvedRun run, string text)
    {
        var typeface = new Typeface(new FontFamily(run.FontFamily),
            run.Italic ? FontStyles.Italic : FontStyles.Normal,
            run.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        double emSizePx = run.FontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        if (brush.CanFreeze) brush.Freeze();
        var ft = new FormattedText(
            text.Length > 0 ? text : " ",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface, emSizePx, brush,
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, ft.Text.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, ft.Text.Length);
        return ft;
    }

    private static TextGlyphMeasure MeasureStackedGlyphWpf(ResolvedRun run, string text)
    {
        var ft = BuildSingleRunFormattedTextAt(run, text);
        return new TextGlyphMeasure(ft.Width, ft.Height);
    }

    private static void DrawStackedGlyphWpf(
        DrawingContext dc,
        ResolvedTextLayout text,
        LayoutRect bounds,
        ResolvedRun run,
        TextStackedGlyphPlacement glyph)
    {
        var glyphRun = CopyRunWithText(run, glyph.Text);
        var glyphParagraph = new ResolvedParagraph
        {
            Runs = new[] { glyphRun }
        };

        if (TextLayoutPlanner.PlanParagraphRenderRoute(glyphParagraph, text) == TextParagraphRenderRoute.Effects)
        {
            RenderParaWithEffects(
                dc,
                glyphParagraph,
                glyph.X,
                glyph.Y,
                Math.Max(1, glyph.WidthDip),
                wrap: false,
                text,
                bounds);
            return;
        }

        dc.DrawText(BuildSingleRunFormattedTextAt(glyphRun, glyph.Text), new Point(glyph.X, glyph.Y));
    }

    private static ResolvedRun CopyRunWithText(ResolvedRun run, string text) =>
        new()
        {
            Text = text,
            FontFamily = run.FontFamily,
            FontSizePt = run.FontSizePt,
            Bold = run.Bold,
            Italic = run.Italic,
            Underline = run.Underline,
            Strikethrough = run.Strikethrough,
            Color = run.Color,
            TextFill = run.TextFill,
            TextOutline = run.TextOutline,
            TextShadow = run.TextShadow,
            TextReflection = run.TextReflection,
            TextGlow = run.TextGlow,
            TextSoftEdge = run.TextSoftEdge
        };

    private static FormattedText BuildFormattedText(ResolvedParagraph para, double maxWidth, bool wrap)
    {
        // Combine all runs into a single string (FormattedText supports range formatting).
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Runs)
            sb.Append(run.Text);

        string text = sb.ToString();
        if (text.Length == 0) text = " "; // keep non-empty for height measurement

        // Use the first run's properties as the base typeface.
        var firstRun = para.Runs[0];
        var typeface = new Typeface(new FontFamily(firstRun.FontFamily),
            firstRun.Italic ? FontStyles.Italic : FontStyles.Normal,
            firstRun.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double emSizePx = firstRun.FontSizePt * (96.0 / 72.0);

        var brush = new SolidColorBrush(
            Color.FromRgb(firstRun.Color.R, firstRun.Color.G, firstRun.Color.B));
        if (brush.CanFreeze) brush.Freeze();

        // P1: Use Display formatting mode for GDI-compatible metrics (matches PowerPoint's
        // pixel-grid-snapped text rendering at 96 DPI). pixelsPerDip = 1.0 is correct for
        // RenderTargetBitmap at 96 DPI.
        //
        // Wave 6A investigation: GlyphRun-based text rendering was evaluated as an alternative
        // to FormattedText. Baseline parity measurements on text-heavy decks showed:
        //   01-title-slide   : 1.3158% mean channel diff
        //   03-mixed-text    : 1.0469% mean channel diff
        //   04-picture       : 0.1498% mean channel diff
        // Visual inspection and heatmap analysis confirmed the residual diff is pure ClearType
        // sub-pixel antialiasing fringing (red/green halos on glyph stroke edges), not position
        // or metrics errors. PowerPoint's PNG export uses DirectWrite directly; WPF's rendering
        // pipeline (both FormattedText and GlyphRun) applies a different ClearType configuration.
        // GlyphRun uses the same WPF rasterizer so it cannot reduce this AA-floor residual.
        // Decision: keep FormattedText. The ~1% residual is an antialiasing floor, not fixable
        // via metrics changes within the WPF rendering pipeline.
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush,
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0);

        if (wrap && maxWidth > 0)
            ft.MaxTextWidth = maxWidth;

        ft.TextAlignment = para.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            TextAlign.Justify or TextAlign.Distributed => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        // Apply per-run formatting as ranges.
        int pos = 0;
        foreach (var run in para.Runs)
        {
            int len = run.Text.Length;
            if (len == 0) continue;

            if (run.Bold)
                ft.SetFontWeight(FontWeights.Bold, pos, len);
            if (run.Italic)
                ft.SetFontStyle(FontStyles.Italic, pos, len);
            if (run.Underline)
                ft.SetTextDecorations(TextDecorations.Underline, pos, len);
            if (run.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough, pos, len);

            ft.SetFontFamily(new FontFamily(run.FontFamily), pos, len);
            ft.SetFontSize(run.FontSizePt * (96.0 / 72.0), pos, len);
            var runBrush = new SolidColorBrush(
                Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
            if (runBrush.CanFreeze) runBrush.Freeze();
            ft.SetForegroundBrush(runBrush, pos, len);

            pos += len;
        }

        return ft;
    }

    // ── Text-effects geometry helpers (Wave 16A) ──────────────────────────────

    /// <summary>
    /// Builds a WPF geometry brush for a resolved fill bounded to <paramref name="bounds"/>.
    /// Used to fill glyph geometry with gradient/other fills.
    /// </summary>
    private static Brush MakeFillBrushForText(ResolvedFill fill, Rect bounds)
    {
        switch (fill)
        {
            case ResolvedFill.Solid s:
                var sb = new SolidColorBrush(Color.FromArgb(s.Alpha, s.Color.R, s.Color.G, s.Color.B));
                if (sb.CanFreeze) sb.Freeze();
                return sb;
            case ResolvedFill.Gradient g:
                // Map gradient to the glyph bounding box using Absolute coordinates
                if (g.Kind == GradientKind.Radial)
                {
                    var rb = new RadialGradientBrush(BuildGradientStops(g))
                    {
                        Center         = new Point(0.5, 0.5),
                        GradientOrigin = new Point(0.5, 0.5),
                        RadiusX        = 0.5,
                        RadiusY        = 0.5,
                        MappingMode    = BrushMappingMode.RelativeToBoundingBox,
                    };
                    if (rb.CanFreeze) rb.Freeze();
                    return rb;
                }
                else
                {
                    double angleRad = g.AngleDegrees * Math.PI / 180.0;
                    double dx = Math.Cos(angleRad), dy = Math.Sin(angleRad);
                    var lb = new LinearGradientBrush(BuildGradientStops(g),
                        new Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy),
                        new Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy))
                    {
                        MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    };
                    if (lb.CanFreeze) lb.Freeze();
                    return lb;
                }
            default:
                return Brushes.Black;
        }
    }

    /// <summary>
    /// Renders a single paragraph using glyph geometry so that per-run text fill/outline/shadow
    /// effects can be applied.  Falls back to simple DrawText when no effects are active.
    /// </summary>
    private static void RenderParaWithEffects(
        DrawingContext dc,
        ResolvedParagraph para,
        double x, double y,
        double maxWidth,
        bool wrap,
        ResolvedTextLayout text,
        LayoutRect shapeBounds)
    {
        int pos = 0;
        foreach (var run in para.Runs)
        {
            int len = run.Text.Length;

            var runFt2 = BuildSingleRunFormattedText(run, wrap ? maxWidth : 0);
            double runOffX = ComputeRunOffsetX(para, run, pos, maxWidth, wrap);
            double drawX = x + runOffX;

            var runFt = runFt2;   // already built above

            double progress = shapeBounds.Width > 0 ? (drawX - shapeBounds.X) / shapeBounds.Width : 0;
            var plan = TextRunEffectRenderPlanner.Plan(
                run,
                new LayoutRect(drawX, y, runFt.Width, runFt.Height),
                progress,
                shapeBounds,
                text);
            var geo = runFt.BuildGeometry(new Point(plan.GlyphBoundsDip.X, plan.GlyphBoundsDip.Y));

            bool pushedWarpTransform = false;
            if (plan.WarpTransform is { HasAffineTransform: true } warp)
            {
                dc.PushTransform(BuildWordArtWarpTransform(warp, plan.GlyphBoundsDip));
                pushedWarpTransform = true;
            }

            try
            {
                foreach (var pass in plan.Passes)
                {
                    switch (pass)
                    {
                        case TextRunEffectPass.Shadow shadow:
                        {
                            var shadowBrush = new SolidColorBrush(Color.FromArgb(shadow.Alpha, shadow.Color.R, shadow.Color.G, shadow.Color.B));
                            if (shadowBrush.CanFreeze) shadowBrush.Freeze();
                            dc.PushTransform(new TranslateTransform(shadow.OffsetX, shadow.OffsetY));
                            dc.DrawGeometry(shadowBrush, null, geo);
                            dc.Pop();
                            break;
                        }
                        case TextRunEffectPass.Reflection reflection:
                        {
                            var geoRect = geo.Bounds;
                            var r2 = new Rect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height));
                            dc.PushTransform(BuildTextReflectionTransform(reflection, plan.GlyphBoundsDip));
                            dc.PushOpacity(reflection.Alpha / 255.0);
                            dc.DrawGeometry(MakeFillBrushForText(reflection.FillBrush, r2), null, geo);
                            dc.Pop();
                            dc.Pop();
                            break;
                        }
                        case TextRunEffectPass.Glow glow:
                        {
                            var glowBrush = new SolidColorBrush(Color.FromArgb(glow.Alpha, glow.Color.R, glow.Color.G, glow.Color.B));
                            if (glowBrush.CanFreeze) glowBrush.Freeze();
                            var glowPen = new Pen(glowBrush, glow.StrokeWidthDip);
                            if (glowPen.CanFreeze) glowPen.Freeze();
                            dc.DrawGeometry(null, glowPen, geo);
                            break;
                        }
                        case TextRunEffectPass.SoftEdge softEdge:
                        {
                            var geoRect = geo.Bounds;
                            var r2 = new Rect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height));
                            dc.PushOpacity(softEdge.Alpha / 255.0);
                            dc.PushTransform(new TranslateTransform(softEdge.OffsetX, softEdge.OffsetY));
                            dc.DrawGeometry(MakeFillBrushForText(softEdge.FillBrush, r2), null, geo);
                            dc.Pop();
                            dc.Pop();
                            break;
                        }
                        case TextRunEffectPass.Fill fill:
                        {
                            var geoRect = geo.Bounds;
                            var r2 = new Rect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height));
                            dc.DrawGeometry(MakeFillBrushForText(fill.FillBrush, r2), null, geo);
                            break;
                        }
                        case TextRunEffectPass.Outline outline:
                            dc.DrawGeometry(null, MakePen(outline.OutlinePen), geo);
                            break;
                    }
                }
            }
            finally
            {
                if (pushedWarpTransform)
                    dc.Pop();
            }

            pos += len;
        }
    }

    private static Transform BuildWordArtWarpTransform(
        WordArtWarpTransform warp,
        LayoutRect glyphBounds)
    {
        double cx = glyphBounds.X + glyphBounds.Width / 2.0;
        double cy = glyphBounds.Y + glyphBounds.Height / 2.0;
        var group = new TransformGroup();
        if (Math.Abs(warp.ScaleY - 1.0) > 0.001)
            group.Children.Add(new ScaleTransform(1.0, warp.ScaleY, cx, cy));
        if (Math.Abs(warp.RotationDeg) > 0.001)
            group.Children.Add(new RotateTransform(warp.RotationDeg, cx, cy));
        if (group.CanFreeze) group.Freeze();
        return group;
    }

    private static Transform BuildTextReflectionTransform(
        TextRunEffectPass.Reflection reflection,
        LayoutRect glyphBounds)
    {
        double cx = glyphBounds.X + glyphBounds.Width / 2.0;
        double pivotY = glyphBounds.Y + glyphBounds.Height;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1.0, reflection.ScaleY, cx, pivotY));
        group.Children.Add(new TranslateTransform(reflection.OffsetX, reflection.OffsetY));
        if (group.CanFreeze) group.Freeze();
        return group;
    }

    /// <summary>Compute the X offset of a run within a paragraph (sum of widths of preceding runs).</summary>
    private static double ComputeRunOffsetX(ResolvedParagraph para, ResolvedRun targetRun, int targetPos, double maxWidth, bool wrap)
    {
        double accX = 0;
        int p = 0;
        foreach (var run in para.Runs)
        {
            if (p == targetPos) break;
            // Measure run width by building single-run FormattedText
            var prev = BuildSingleRunFormattedText(run, 0 /*no-wrap for width measurement*/);
            accX += prev.Width;
            p += run.Text.Length;
        }
        return accX;
    }

    /// <summary>Builds a FormattedText for a single run (used for glyph geometry extraction).</summary>
    private static FormattedText BuildSingleRunFormattedText(ResolvedRun run, double maxWidth)
    {
        string txt = run.Text.Length == 0 ? " " : run.Text;
        var typeface = new Typeface(
            new FontFamily(run.FontFamily),
            run.Italic ? FontStyles.Italic : FontStyles.Normal,
            run.Bold   ? FontWeights.Bold  : FontWeights.Normal,
            FontStretches.Normal);
        double emPx = run.FontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        if (brush.CanFreeze) brush.Freeze();

        var ft = new FormattedText(txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, emPx, brush,
            numberSubstitution: null, textFormattingMode: TextFormattingMode.Display, pixelsPerDip: 1.0);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, txt.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, txt.Length);
        if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
        return ft;
    }

    // ── WPF primitive helpers ──────────────────────────────────────────────────

    private static Brush? MakeBrush(ResolvedFill fill, LayoutRect bounds, bool easeGradientStops = false) => fill switch
    {
        ResolvedFill.None => null,
        ResolvedFill.Solid s => FreezeBrush(
            new SolidColorBrush(Color.FromArgb(s.Alpha, s.Color.R, s.Color.G, s.Color.B))),
        ResolvedFill.Gradient g when g.Kind == GradientKind.Radial => MakeRadialGradientBrush(g),
        ResolvedFill.Gradient g => MakeLinearGradientBrush(g, easeGradientStops),
        ResolvedFill.Picture p => MakePictureBrush(p),
        ResolvedFill.PatternFill pat => MakePatternBrush(pat),
        _ => null
    };

    private static GradientStopCollection BuildGradientStops(ResolvedFill.Gradient g, bool easePositions = false)
    {
        var stops = new GradientStopCollection();
        for (int index = 0; index < g.Stops.Count; index++)
        {
            var start = g.Stops[index];
            stops.Add(new System.Windows.Media.GradientStop(
                Color.FromArgb(start.Alpha, start.Color.R, start.Color.G, start.Color.B),
                start.Position));
            if (index == g.Stops.Count - 1)
                continue;

            var end = g.Stops[index + 1];
            for (int step = 1; step < 16; step++)
            {
                double fraction = step / 16.0;
                var color = GradientColorInterpolation.InterpolateLinearLight(
                    start.Color,
                    end.Color,
                    easePositions ? GradientColorInterpolation.EasePowerPointPosition(fraction) : fraction);
                var alpha = (byte)Math.Round(start.Alpha + (end.Alpha - start.Alpha) * fraction);
                stops.Add(new System.Windows.Media.GradientStop(
                    Color.FromArgb(alpha, color.R, color.G, color.B),
                    start.Position + (end.Position - start.Position) * fraction));
            }
        }
        return stops;
    }

    private static Brush MakeLinearGradientBrush(ResolvedFill.Gradient g, bool easePositions = false)
    {
        // OOXML a:lin ang convention (stored in model as AngleDegrees = ang/60000):
        //   0°  = gradient flows east  (left → right):   Start=(0, 0.5), End=(1, 0.5)
        //  90°  = gradient flows south (top  → bottom):  Start=(0.5, 0), End=(0.5, 1)
        // 180°  = gradient flows west  (right→ left):    Start=(1, 0.5), End=(0, 0.5)
        // 270°  = gradient flows north (bottom → top):   Start=(0.5, 1), End=(0.5, 0)
        // Direction vector in screen coords (x right, y down): d = (cos θ, sin θ).
        // Start = centre - 0.5·d,  End = centre + 0.5·d.
        double angleRad = g.AngleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(angleRad);
        double dy = Math.Sin(angleRad);

        var startPoint = new Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy);
        var endPoint   = new Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy);

        var brush = new LinearGradientBrush(BuildGradientStops(g, easePositions), startPoint, endPoint);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Brush MakeRadialGradientBrush(ResolvedFill.Gradient g)
    {
        // PowerPoint radial gradients are center-out.
        var brush = new RadialGradientBrush(BuildGradientStops(g))
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Brush MakePictureBrush(ResolvedFill.Picture p)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(p.ImageBytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();

            var brush = new ImageBrush(bmp)
            {
                Stretch = p.Tile ? Stretch.None : Stretch.Fill,
                TileMode = p.Tile ? TileMode.Tile : TileMode.None
            };
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        catch
        {
            // If image decode fails, fall back to transparent
            return Brushes.Transparent;
        }
    }

    private static Brush MakePatternBrush(ResolvedFill.PatternFill pat)
    {
        // Render common patterns as a small DrawingBrush tile.
        var fg = Color.FromRgb(pat.ForegroundColor.R, pat.ForegroundColor.G, pat.ForegroundColor.B);
        var bg = Color.FromRgb(pat.BackgroundColor.R, pat.BackgroundColor.G, pat.BackgroundColor.B);

        // Select pattern geometry based on preset
        return pat.Preset switch
        {
            "pct5" => BuildDotPatternBrush(bg, fg, 4, 4, 1, 0.25),
            "pct10" => BuildDotPatternBrush(bg, fg, 4, 4, 1, 0.5),
            "pct20" => BuildDotPatternBrush(bg, fg, 4, 4, 2, 0.75),
            "pct25" => BuildDotPatternBrush(bg, fg, 4, 4, 2, 1.0),
            "pct30" => BuildDotPatternBrush(bg, fg, 4, 4, 2, 1.25),
            "pct40" => BuildCheckerPatternBrush(bg, fg),
            "pct50" => BuildHalfHalfBrush(bg, fg, horizontal: false),
            "pct60" => BuildDotPatternBrush(fg, bg, 4, 4, 3, 1.5),
            "pct75" => BuildDotPatternBrush(fg, bg, 4, 4, 2, 1.0),
            "pct90" => BuildDotPatternBrush(fg, bg, 4, 4, 1, 0.25),
            "horzStripe" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "vertStripe" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "ltHorz" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "ltVert" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "dashHorz" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "dashVert" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "diagStripe" or "ltDnDiag" or "dnDiag" => BuildDiagPatternBrush(bg, fg, down: true),
            "upDiag" or "ltUpDiag" => BuildDiagPatternBrush(bg, fg, down: false),
            "cross" => BuildCrossPatternBrush(bg, fg),
            "diagCross" or "smConfetti" => BuildDiagCrossPatternBrush(bg, fg),
            "smGrid" => BuildCrossPatternBrush(bg, fg),
            "wave" or "trellis" => BuildDiagCrossPatternBrush(bg, fg),
            _ => new SolidColorBrush(fg) // unrecognized: solid foreground color
        };
    }

    private static DrawingBrush BuildDotPatternBrush(
        Color bg, Color fgColor, double tileW, double tileH, int dotCount, double dotSize)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, tileW, tileH))));
        double spacing = tileW / dotCount;
        for (int i = 0; i < dotCount; i++)
        {
            double cx = spacing * i + spacing / 2;
            double cy = tileH / 2;
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fgColor), null,
                new EllipseGeometry(new Point(cx, cy), dotSize / 2, dotSize / 2)));
        }
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileW, tileH),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildHalfHalfBrush(Color bg, Color fg, bool horizontal)
    {
        var dg = new DrawingGroup();
        if (horizontal)
        {
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
                new RectangleGeometry(new Rect(0, 0, 4, 2))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
                new RectangleGeometry(new Rect(0, 2, 4, 2))));
        }
        else
        {
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
                new RectangleGeometry(new Rect(0, 0, 2, 4))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
                new RectangleGeometry(new Rect(2, 0, 2, 4))));
        }
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildCheckerPatternBrush(Color bg, Color fg)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, 4, 4))));
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            if ((x + y) % 2 == 0)
                dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
                    new RectangleGeometry(new Rect(x, y, 1, 1))));
        }
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildStripePatternBrush(Color bg, Color fg, bool horizontal)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, 6, 6))));
        if (horizontal)
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
                new RectangleGeometry(new Rect(0, 2, 6, 2))));
        else
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
                new RectangleGeometry(new Rect(2, 0, 2, 6))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildDiagPatternBrush(Color bg, Color fg, bool down)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, 6, 6))));
        var pen = new Pen(new SolidColorBrush(fg), 1.5);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (down) { ctx.BeginFigure(new Point(0, 0), false, false); ctx.LineTo(new Point(6, 6), true, false); }
            else       { ctx.BeginFigure(new Point(0, 6), false, false); ctx.LineTo(new Point(6, 0), true, false); }
        }
        dg.Children.Add(new GeometryDrawing(null, pen, geo));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildCrossPatternBrush(Color bg, Color fg)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, 6, 6))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
            new RectangleGeometry(new Rect(2, 0, 2, 6))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
            new RectangleGeometry(new Rect(0, 2, 6, 2))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush BuildDiagCrossPatternBrush(Color bg, Color fg)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, 6, 6))));
        var pen = new Pen(new SolidColorBrush(fg), 1.5);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, 0), false, false);
            ctx.LineTo(new Point(6, 6), true, false);
            ctx.BeginFigure(new Point(6, 0), false, false);
            ctx.LineTo(new Point(0, 6), true, false);
        }
        dg.Children.Add(new GeometryDrawing(null, pen, geo));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static Pen? MakePen(ResolvedOutline outline)
    {
        if (outline is ResolvedOutline.Visible vis)
        {
            var brush = new SolidColorBrush(Color.FromArgb(vis.Alpha, vis.Color.R, vis.Color.G, vis.Color.B));
            if (brush.CanFreeze) brush.Freeze();
            var pen = new Pen(brush, vis.WidthDip);
            pen.DashStyle = MapDashStyleWpf(vis.Dash);
            if (pen.CanFreeze) pen.Freeze();
            return pen;
        }

        // Wave 22B: gradient outline — build a LinearGradientBrush for the stroke.
        if (outline is ResolvedOutline.Gradient grad)
        {
            Brush gradBrush = grad.Fill.Kind == GradientKind.Radial
                ? MakeRadialGradientBrush(grad.Fill)
                : MakeLinearGradientBrush(grad.Fill);
            if (gradBrush.CanFreeze) gradBrush.Freeze();
            var pen = new Pen(gradBrush, grad.WidthDip);
            pen.DashStyle = MapDashStyleWpf(grad.Dash);
            if (pen.CanFreeze) pen.Freeze();
            return pen;
        }

        return null;
    }

    private static DashStyle MapDashStyleWpf(OutlineDash dash) => dash switch
    {
        OutlineDash.Dash           => DashStyles.Dash,
        OutlineDash.Dot            => DashStyles.Dot,
        OutlineDash.DashDot        => DashStyles.DashDot,
        OutlineDash.LongDash       => new DashStyle(new[] { 8.0, 3.0 }, 0),
        OutlineDash.LongDashDot    => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0 }, 0),
        OutlineDash.LongDashDotDot => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0, 1.0, 3.0 }, 0),
        OutlineDash.SystemDash     => DashStyles.Dash,
        OutlineDash.SystemDot      => DashStyles.Dot,
        OutlineDash.SystemDashDot  => DashStyles.DashDot,
        _                          => DashStyles.Solid
    };

    private static T FreezeBrush<T>(T brush) where T : Brush
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    // ── ShapeGeometry → WPF StreamGeometry ─────────────────────────────────────

    private static System.Windows.Media.Geometry ContourListToGeometry(ShapeGeometry shape)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var contour in shape.Contours)
                AppendContour(ctx, contour);
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static void AppendContour(StreamGeometryContext ctx, ShapeContour contour)
    {
        ctx.BeginFigure(ToPoint(contour.Start), contour.Filled, contour.Closed);
        foreach (var seg in contour.Segments)
        {
            switch (seg.Kind)
            {
                case ShapeSegmentKind.Line:
                    ctx.LineTo(ToPoint(seg.End), isStroked: true, isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.CubicBezier:
                    ctx.BezierTo(ToPoint(seg.Control1), ToPoint(seg.Control2), ToPoint(seg.End),
                                 isStroked: true, isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.Arc:
                    ctx.ArcTo(
                        ToPoint(seg.End),
                        new Size(seg.RadiusX, seg.RadiusY),
                        rotationAngle: 0,
                        seg.LargeArc,
                        seg.SweepClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        isStroked: true,
                        isSmoothJoin: false);
                    break;
            }
        }
    }

    private static ChartPlanRect ToPlanRect(LayoutRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToRect(ChartPlanRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(ChartPlanPoint point) =>
        new(point.X, point.Y);

    private static ChartPlanPoint OffsetPoint(
        ChartPlanPoint point,
        ChartClassicThreeDDepthPlan depth) =>
        new(point.X + depth.OffsetX, point.Y + depth.OffsetY);

    private static ChartPathPrimitive OffsetPath(
        ChartPathPrimitive path,
        ChartClassicThreeDDepthPlan depth) =>
        path with
        {
            Points = path.Points
                .Select(point => OffsetPoint(point, depth))
                .ToArray()
        };

    private static Geometry ToGeometry(
        ChartLinePathFigurePrimitive figure,
        ChartClassicThreeDDepthPlan? depth = null)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(OffsetIfNeeded(figure.Start, depth)), isFilled: false, isClosed: false);
            foreach (var segment in figure.Segments)
            {
                switch (segment.Kind)
                {
                    case ChartLinePathSegmentKind.CubicBezier:
                        ctx.BezierTo(
                            ToPoint(OffsetIfNeeded(segment.Control1, depth)),
                            ToPoint(OffsetIfNeeded(segment.Control2, depth)),
                            ToPoint(OffsetIfNeeded(segment.End, depth)),
                            isStroked: true,
                            isSmoothJoin: true);
                        break;

                    default:
                        ctx.LineTo(
                            ToPoint(OffsetIfNeeded(segment.End, depth)),
                            isStroked: true,
                            isSmoothJoin: true);
                        break;
                }
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static ChartPlanPoint OffsetIfNeeded(
        ChartPlanPoint point,
        ChartClassicThreeDDepthPlan? depth) =>
        depth.HasValue ? OffsetPoint(point, depth.Value) : point;

    private static Brush ToBrush(ChartFillPlan fill) =>
        fill.Fill switch
        {
            ResolvedFill.Gradient gradient when gradient.Kind == GradientKind.Radial => MakeRadialGradientBrush(gradient),
            ResolvedFill.Gradient gradient => MakeLinearGradientBrush(gradient),
            ResolvedFill.PatternFill pattern => MakePatternBrush(pattern),
            ResolvedFill.Solid solid => FreezeBrush(new SolidColorBrush(Color.FromArgb(
                fill.Alpha,
                solid.Color.R,
                solid.Color.G,
                solid.Color.B))),
            _ => FreezeBrush(new SolidColorBrush(Color.FromArgb(
                fill.Alpha,
                fill.Color.R,
                fill.Color.G,
                fill.Color.B)))
        };

    internal static Pen CreateChartGridLinePen(ChartMajorGridLinePrimitivePlan plan) =>
        ToPen(plan.Stroke);

    internal static Pen CreateChartAxisTickPen(ChartMajorAxisTickPrimitivePlan plan) =>
        ToPen(plan.Stroke);

    internal static Pen CreateChartSecondaryAxisTickPen(ChartSecondaryValueAxisPrimitivePlan plan) =>
        ToPen(plan.TickStroke);

    private static Pen ToPen(ChartStrokePlan stroke)
    {
        var pen = new Pen(
            ToBrush(new ChartFillPlan(stroke.Color, stroke.Alpha) { Fill = stroke.Fill }),
            stroke.Thickness);
        pen.DashStyle = MapDashStyleWpf(stroke.Dash);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static void DrawChartMarker(DrawingContext dc, ChartCirclePrimitive marker)
    {
        var center = ToPoint(marker.Center);
        var fill = marker.Fill.HasValue ? ToBrush(marker.Fill.Value) : null;
        var stroke = marker.Stroke.HasValue ? ToPen(marker.Stroke.Value) : null;
        var linePen = stroke ?? (marker.Fill.HasValue
            ? ToPen(new ChartStrokePlan(marker.Fill.Value.Color, marker.Fill.Value.Alpha, Math.Max(0.75, marker.Radius / 3.0)))
            : null);

        switch (marker.Symbol)
        {
            case ChartMarkerPrimitiveSymbol.Square:
                dc.DrawRectangle(fill, stroke, new Rect(center.X - marker.Radius, center.Y - marker.Radius, marker.Radius * 2, marker.Radius * 2));
                break;
            case ChartMarkerPrimitiveSymbol.Diamond:
                dc.DrawGeometry(fill, stroke, MarkerPolygonGeometry(
                    new Point(center.X, center.Y - marker.Radius),
                    new Point(center.X + marker.Radius, center.Y),
                    new Point(center.X, center.Y + marker.Radius),
                    new Point(center.X - marker.Radius, center.Y)));
                break;
            case ChartMarkerPrimitiveSymbol.Triangle:
                dc.DrawGeometry(fill, stroke, MarkerPolygonGeometry(
                    new Point(center.X, center.Y - marker.Radius),
                    new Point(center.X + marker.Radius, center.Y + marker.Radius),
                    new Point(center.X - marker.Radius, center.Y + marker.Radius)));
                break;
            case ChartMarkerPrimitiveSymbol.Dash:
                if (linePen is not null)
                    dc.DrawLine(linePen, new Point(center.X - marker.Radius, center.Y), new Point(center.X + marker.Radius, center.Y));
                break;
            case ChartMarkerPrimitiveSymbol.Plus:
            case ChartMarkerPrimitiveSymbol.Star:
                if (linePen is not null)
                {
                    dc.DrawLine(linePen, new Point(center.X - marker.Radius, center.Y), new Point(center.X + marker.Radius, center.Y));
                    dc.DrawLine(linePen, new Point(center.X, center.Y - marker.Radius), new Point(center.X, center.Y + marker.Radius));
                }
                break;
            case ChartMarkerPrimitiveSymbol.X:
                if (linePen is not null)
                {
                    dc.DrawLine(linePen, new Point(center.X - marker.Radius, center.Y - marker.Radius), new Point(center.X + marker.Radius, center.Y + marker.Radius));
                    dc.DrawLine(linePen, new Point(center.X + marker.Radius, center.Y - marker.Radius), new Point(center.X - marker.Radius, center.Y + marker.Radius));
                }
                break;
            default:
                dc.DrawEllipse(fill, stroke, center, marker.Radius, marker.Radius);
                break;
        }
    }

    private static StreamGeometry MarkerPolygonGeometry(params Point[] points)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            for (int index = 1; index < points.Length; index++)
                ctx.LineTo(points[index], isStroked: true, isSmoothJoin: false);
        }
        if (geo.CanFreeze) geo.Freeze();
        return geo;
    }

    private static StreamGeometry ToGeometry(ChartPathPrimitive path)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int pointIndex = 0; pointIndex < path.Points.Count; pointIndex++)
            {
                var point = ToPoint(path.Points[pointIndex]);
                if (pointIndex == 0)
                    ctx.BeginFigure(point, path.Fill.HasValue, path.IsClosed);
                else
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: true);
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry ToSurfaceFacetGeometry(ChartSurfaceFacetPrimitive facet)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var points = facet.Points;
            if (points.Count > 0)
            {
                ctx.BeginFigure(ToPoint(points[0]), isFilled: true, isClosed: true);
                for (int index = 1; index < points.Count; index++)
                    ctx.LineTo(ToPoint(points[index]), isStroked: true, isSmoothJoin: false);
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry ToAreaGeometry(ChartPathPrimitive path)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int pointIndex = 0; pointIndex < path.Points.Count; pointIndex++)
            {
                var point = ToPoint(path.Points[pointIndex]);
                if (pointIndex == 0)
                    ctx.BeginFigure(point, path.Fill.HasValue, path.IsClosed);
                else
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: false);
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static TextAlignment ToTextAlignment(ChartPlanTextAlignment alignment) =>
        alignment switch
        {
            ChartPlanTextAlignment.Left => TextAlignment.Left,
            ChartPlanTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Center
        };

    private static Point ToPoint(LayoutPoint p) => new(p.X, p.Y);

    // ── Composition helper ──────────────────────────────────────────────────────

    private void EnsureOps()
    {
        if (_cachedOps is not null) return;

        var presentation = Presentation;
        var slide = Slide;

        if (presentation is null || slide is null)
        {
            _slideWidthDip = 0;
            _slideHeightDip = 0;
            _cachedOps = Array.Empty<DrawOp>();
            return;
        }

        _slideWidthDip = presentation.SlideSizeCxEmu / 9525.0;
        _slideHeightDip = presentation.SlideSizeCyEmu / 9525.0;
        int slideIndex = presentation.Slides.IndexOf(slide);
        _cachedOps = SlideCompositor.Compose(presentation, slide, slideIndex < 0 ? 0 : slideIndex);
    }
}
