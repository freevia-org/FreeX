using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.Core.Model;

// Alias to disambiguate from FreeP.Core.Model.GradientStop
using AvGradientStop = Avalonia.Media.GradientStop;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// An Avalonia <see cref="Control"/> that renders a single <see cref="Slide"/> using the
/// framework-free <see cref="SlideCompositor"/> to produce draw operations and converts them
/// to Avalonia primitives via <see cref="DrawingContext"/>.
///
/// Usage (14B host contract):
///   canvas.Presentation = myPresentation;
///   canvas.Slide         = mySlide;
///   canvas.SlideIndex    = 0;
///   // The control invalidates itself and repaints on the next layout cycle.
///
/// The control owns rendering and coordinate transforms; the host attaches
/// <see cref="AvaloniaCanvasGestureHandler"/> and <see cref="SelectionAdornerLayer"/>
/// for interactive editing. The slide is scaled uniformly to fit the control's
/// available size (letterboxed).
/// </summary>
public sealed class SlideCanvas : Control
{
    private const double PowerPointDefaultLineSpacingFactor = 1.18;
    private const double PowerPointFixedTextLineSpacingFactor = 1.20;
    private const double ImportedRadarValueLabelAvaloniaYCompensation = 3.0;
    private const double AvaloniaImportedRadarAgilityLabelOffsetX = 35.0;
    private const double AvaloniaImportedRadarStaminaLabelOffsetX = -51.0;
    private const double AvaloniaImportedRadarLowerLabelOffsetY = -2.0;

    // ── Styled / direct properties ──────────────────────────────────────────

    public static readonly DirectProperty<SlideCanvas, Presentation?> PresentationProperty =
        AvaloniaProperty.RegisterDirect<SlideCanvas, Presentation?>(
            nameof(Presentation),
            o => o.Presentation,
            (o, v) => o.Presentation = v);

    public static readonly DirectProperty<SlideCanvas, Slide?> SlideProperty =
        AvaloniaProperty.RegisterDirect<SlideCanvas, Slide?>(
            nameof(Slide),
            o => o.Slide,
            (o, v) => o.Slide = v);

    public static readonly DirectProperty<SlideCanvas, int> SlideIndexProperty =
        AvaloniaProperty.RegisterDirect<SlideCanvas, int>(
            nameof(SlideIndex),
            o => o.SlideIndex,
            (o, v) => o.SlideIndex = v);

    public static readonly DirectProperty<SlideCanvas, uint?> ActiveTextEditShapeIdProperty =
        AvaloniaProperty.RegisterDirect<SlideCanvas, uint?>(
            nameof(ActiveTextEditShapeId),
            o => o.ActiveTextEditShapeId,
            (o, v) => o.ActiveTextEditShapeId = v);

    public static readonly DirectProperty<SlideCanvas, bool> RenderSlideBackgroundProperty =
        AvaloniaProperty.RegisterDirect<SlideCanvas, bool>(
            nameof(RenderSlideBackground),
            o => o.RenderSlideBackground,
            (o, v) => o.RenderSlideBackground = v);

    private Presentation? _presentation;
    private Slide? _slide;
    private int _slideIndex;
    private uint? _activeTextEditShapeId;
    private bool _renderSlideBackground = true;

    public Presentation? Presentation
    {
        get => _presentation;
        set { SetAndRaise(PresentationProperty, ref _presentation, value); Refresh(); }
    }

    public Slide? Slide
    {
        get => _slide;
        set { SetAndRaise(SlideProperty, ref _slide, value); Refresh(); }
    }

    /// <summary>Whether the compositor paints the slide background.</summary>
    public bool RenderSlideBackground
    {
        get => _renderSlideBackground;
        set
        {
            if (_renderSlideBackground == value)
                return;
            SetAndRaise(RenderSlideBackgroundProperty, ref _renderSlideBackground, value);
            Refresh();
        }
    }

    public int SlideIndex
    {
        get => _slideIndex;
        set { SetAndRaise(SlideIndexProperty, ref _slideIndex, value); Refresh(); }
    }

    /// <summary>Shape whose base text is hidden while its rich editor overlay is active.</summary>
    public uint? ActiveTextEditShapeId
    {
        get => _activeTextEditShapeId;
        set
        {
            if (_activeTextEditShapeId == value)
                return;
            SetAndRaise(ActiveTextEditShapeIdProperty, ref _activeTextEditShapeId, value);
            InvalidateVisual();
        }
    }

    // ── Current slide→screen transform (updated on every render pass) ───────────

    /// <summary>
    /// The current slide→screen transform.  Updated every time <see cref="Render"/> runs.
    /// The gesture handler and adorner layer read this to map between coordinate spaces.
    /// </summary>
    public SlideTransformCore CurrentTransform { get; private set; } = SlideTransformCore.Identity;

    // ── Cached draw ops ──────────────────────────────────────────────────────

    private IReadOnlyList<DrawOp>? _cachedOps;
    private IReadOnlyDictionary<uint, DrawOp>? _liveTransformPreviewOps;
    private double _slideWidthDip;
    private double _slideHeightDip;
    private PresentationViewZoomState _viewZoomState = PresentationViewZoomState.FitToWindow;
    private AvaloniaCanvasGestureHandler? _gestureHandler;
    private bool _editPointsEnabled = true;

    public PresentationViewZoomState ViewZoomState => _viewZoomState;

    public void ApplyViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        InvalidateVisual();
    }

    /// <summary>Whether supported preset shapes expose draggable edit points in the host.</summary>
    public bool EditPointsEnabled
    {
        get => _editPointsEnabled;
        set
        {
            if (_editPointsEnabled == value)
                return;
            _editPointsEnabled = value;
            if (_gestureHandler is not null)
                _gestureHandler.EditPointsEnabled = value;
        }
    }

    /// <summary>Sets the Edit Points interaction mode for the attached host handler.</summary>
    public void SetEditPointsMode(bool enabled) => EditPointsEnabled = enabled;

    /// <summary>Connects the host gesture handler to the canvas mode property.</summary>
    public void AttachGestureHandler(AvaloniaCanvasGestureHandler handler)
    {
        _gestureHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        handler.EditPointsEnabled = _editPointsEnabled;
    }

    // ── Slideshow entrance-animation suppression ──────────────────────────────

    /// <summary>
    /// Shape IDs that the slideshow window has marked as "not yet revealed".
    /// Any <see cref="DrawOp"/> whose <c>ShapeId</c> is in this set is silently
    /// skipped during <see cref="Render"/> so the entrance animation overlay is
    /// the only visible copy of that shape until the build step plays.
    ///
    /// Call <see cref="Refresh"/> after mutating this set.
    /// </summary>
    public HashSet<uint> SuppressedShapeIds { get; } = new();

    /// <summary>Forces a recomposition and repaint.</summary>
    public void Refresh()
    {
        _cachedOps = null;
        _liveTransformPreviewOps = null;
        // Slide/presentation changes can also change the canvas' desired aspect-ratio size.
        // InvalidateMeasure is required here because the canvas may already have been measured
        // before a slideshow window assigns its first slide.
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Replaces selected source draw operations with compositor-created copies for a live
    /// multi-selection transform. Passing an empty plan clears the transient preview.
    /// </summary>
    public void UpdateTransformPreview(CanvasMultiTransformPlan plan)
    {
        EnsureOps();
        _liveTransformPreviewOps = plan.Shapes.Count == 0 || _cachedOps is null
            ? null
            : CanvasTransformPreviewComposer.Compose(_cachedOps, plan);
        InvalidateVisual();
    }

    internal bool HasLiveTransformPreviewForTests =>
        _liveTransformPreviewOps is { Count: > 0 };

    // ── Layout: maintain slide aspect ratio ──────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureOps();
        if (_slideWidthDip <= 0 || _slideHeightDip <= 0)
            return base.MeasureOverride(availableSize);

        double ratio = _slideWidthDip / _slideHeightDip;
        double w = double.IsInfinity(availableSize.Width)  ? _slideWidthDip  : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _slideHeightDip : availableSize.Height;

        if (w / h > ratio) w = h * ratio;
        else                h = w / ratio;

        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureOps();

        if (_cachedOps is null || _cachedOps.Count == 0 || _slideWidthDip <= 0) return;

        double renderW = Bounds.Width;
        double renderH = Bounds.Height;
        if (renderW <= 0 || renderH <= 0) return;

        // Expose the slide→screen transform so the editing layer can use it.
        CurrentTransform = ComputeViewTransform(renderW, renderH, _slideWidthDip, _slideHeightDip);

        var matrix = Matrix.CreateScale(CurrentTransform.Scale, CurrentTransform.Scale)
            * Matrix.CreateTranslation(CurrentTransform.OffsetX, CurrentTransform.OffsetY);
        using var _ = context.PushTransform(matrix);

        foreach (var op in _cachedOps)
            RenderOp(context, op);
    }

    private SlideTransformCore ComputeViewTransform(
        double renderW,
        double renderH,
        double slideWidthDip,
        double slideHeightDip)
    {
        var fit = SlideTransformCore.Compute(renderW, renderH, slideWidthDip, slideHeightDip);
        var multiplier = PresentationViewZoomPlanner.StageScaleMultiplierFor(_viewZoomState);
        if (Math.Abs(multiplier - 1.0) < 0.0001)
            return fit;

        var scale = fit.Scale * multiplier;
        var offsetX = (renderW - slideWidthDip * scale) / 2.0;
        var offsetY = (renderH - slideHeightDip * scale) / 2.0;
        return new SlideTransformCore(scale, offsetX, offsetY, slideWidthDip, slideHeightDip);
    }

    private void RenderOp(DrawingContext dc, DrawOp op)
    {
        if (_liveTransformPreviewOps is not null
            && CanvasTransformPreviewComposer.TryGetShapeId(op, out var shapeId)
            && _liveTransformPreviewOps.TryGetValue(shapeId, out var preview))
        {
            RenderOpCore(dc, preview);
            return;
        }

        RenderOpCore(dc, op);
    }

    private void RenderOpCore(DrawingContext dc, DrawOp op)
    {
        switch (op)
        {
            case DrawOp.Background bg:
                RenderBackground(dc, bg);
                break;
            case DrawOp.Shape shape:
                // DA1: skip shapes that the slideshow has not yet revealed (entrance animation).
                if (shape.ShapeId != 0 && SuppressedShapeIds.Contains(shape.ShapeId)) break;
                RenderShape(dc, shape, shape.ShapeId != 0 && shape.ShapeId == ActiveTextEditShapeId);
                break;
            case DrawOp.Picture pic:
                if (pic.ShapeId != 0 && SuppressedShapeIds.Contains(pic.ShapeId)) break;
                RenderPicture(dc, pic);
                break;
            case DrawOp.Table table:
                if (table.ShapeId != 0 && SuppressedShapeIds.Contains(table.ShapeId)) break;
                RenderTableWithTransform(dc, table);
                break;
            case DrawOp.Chart chartOp:
                if (chartOp.ShapeId != 0 && SuppressedShapeIds.Contains(chartOp.ShapeId)) break;
                RenderChart(dc, chartOp);
                break;
        }
    }

    // ── Background ───────────────────────────────────────────────────────────

    private static void RenderBackground(DrawingContext dc, DrawOp.Background bg)
    {
        var brush = MakeBrush(bg.Fill, bg.BoundsDip, easeGradientStops: true);
        if (brush is null) return;
        dc.FillRectangle(brush,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape, bool suppressText)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null
            && (shape.ElbowRouteDip is null || shape.ElbowRouteDip.Count < 2)) return;

        var bounds = shape.BoundsDip;
        var renderTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        bool hasTransform = !renderTransform.IsIdentity;

        IDisposable? transformScope = null;
        if (hasTransform)
            transformScope = dc.PushTransform(ToAvaloniaMatrix(renderTransform));

        if (shape.Effects is not null)
            RenderShapeEffects(dc, shape);

        var materialPlan = ShapeMaterialRenderPlanner.Plan(shape);
        if (materialPlan.Kind == ImportedShapeMaterialKind.IsometricCrossDepth)
            RenderImportedShapeDepth(dc, shape, materialPlan);

        // Wave 26: draw explicit elbow polyline route when available (overrides bbox geometry)
        if (shape.ElbowRouteDip is { Count: >= 2 })
        {
            var pen = MakePen(shape.Outline);
            if (pen is not null)
            {
                var pg = new PathGeometry();
                var pf = new PathFigure
                {
                    StartPoint = new Point(shape.ElbowRouteDip[0].X, shape.ElbowRouteDip[0].Y),
                    IsFilled = false,
                };
                for (int ri = 1; ri < shape.ElbowRouteDip.Count; ri++)
                    pf.Segments!.Add(new LineSegment
                    {
                        Point = new Point(shape.ElbowRouteDip[ri].X, shape.ElbowRouteDip[ri].Y)
                    });
                pg.Figures!.Add(pf);
                dc.DrawGeometry(null, pen, pg);
            }
        }
        else if (shape.Geometry.Contours.Count > 0)
        {
            var geometry  = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (geometry is not null)
            {
                var fillBrush = MakeBrush(shape.Fill, bounds);
                var pen       = shape.Effects?.HasSoftEdge == true ? null : MakePen(shape.Outline);
                dc.DrawGeometry(fillBrush, pen, geometry);

                if (materialPlan.Kind == ImportedShapeMaterialKind.Circle)
                    RenderImportedShapeMaterial(dc, materialPlan, geometry);
            }
        }

        if (shape.Effects is not null)
            RenderShapeBevel(dc, shape);

        if (materialPlan.Kind is ImportedShapeMaterialKind.RelaxedInset or ImportedShapeMaterialKind.Angle)
        {
            var geometry = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (geometry is not null)
                RenderImportedShapeMaterial(dc, materialPlan, geometry);
        }

        if (!suppressText && shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        transformScope?.Dispose();
    }

    private static void RenderShapeEffects(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0) return;
        if (shape.Text is not null && shape.Fill is ResolvedFill.None) return;
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);

        if (plan.ShadowPasses.Count > 0)
        {
            var shadowGeo = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (shadowGeo is null) return;

            foreach (var pass in plan.ShadowPasses)
            {
                var shadowBrush = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                using var shadowScope = dc.PushTransform(Matrix.CreateTranslation(pass.OffsetX, pass.OffsetY));
                dc.DrawGeometry(shadowBrush, null, shadowGeo);
            }
        }

        if (plan.GlowPasses.Count > 0)
        {
            var glowGeo = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (glowGeo is null) return;
            foreach (var pass in plan.GlowPasses)
            {
                var glowBrush  = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                var glowPen    = new Pen(glowBrush, pass.StrokeWidthDip);
                dc.DrawGeometry(null, glowPen, glowGeo);
            }
        }

        if (plan.SoftEdgePasses.Count > 0)
        {
            var softEdgeGeo = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            var fillBrush = MakeBrush(shape.Fill, shape.BoundsDip);
            if (softEdgeGeo is not null && fillBrush is not null)
            {
                foreach (var pass in plan.SoftEdgePasses)
                {
                    using var opacityScope = dc.PushOpacity(pass.Alpha / 255.0);
                    dc.DrawGeometry(null, new Pen(fillBrush, pass.StrokeWidthDip), softEdgeGeo);
                }
            }
        }
    }

    private static void RenderImportedShapeDepth(
        DrawingContext dc,
        DrawOp.Shape shape,
        ShapeMaterialRenderPlan plan)
    {
        var color = plan.ExtrusionColor!.Value;
        var brush = new SolidColorBrush(Color.FromArgb(
            plan.FaceAlpha, color.R, color.G, color.B));
        var geometry = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
        if (geometry is null) return;

        using var transformScope = dc.PushTransform(
            Matrix.CreateTranslation(plan.DepthOffsetDip, plan.DepthOffsetDip));
        dc.DrawGeometry(brush, null, geometry);
    }

    private static void RenderImportedShapeMaterial(
        DrawingContext dc,
        ShapeMaterialRenderPlan plan,
        Geometry shapeGeo)
    {
        var bounds = plan.Bounds;
        var coreBrush = new SolidColorBrush(Color.FromRgb(
            plan.FaceColor.R, plan.FaceColor.G, plan.FaceColor.B));

        using var clipScope = dc.PushGeometryClip(shapeGeo);
        dc.FillRectangle(coreBrush, new Rect(
            bounds.X + 1,
            bounds.Y + 1,
            Math.Max(0, bounds.Width - 2),
            Math.Max(0, bounds.Height - 2)));

        foreach (var band in plan.Bands)
            dc.FillRectangle(CreateMaterialBrush(band), new Rect(
                band.Bounds.X,
                band.Bounds.Y,
                band.Bounds.Width,
                band.Bounds.Height));
    }

    private static LinearGradientBrush CreateMaterialBrush(ShapeMaterialBandPlan band)
    {
        var stops = new GradientStops();
        foreach (var stop in band.Stops)
            stops.Add(new AvGradientStop(
                Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B),
                stop.Position));

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = band.IsVertical
                ? new RelativePoint(0, 1, RelativeUnit.Relative)
                : new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = stops,
        };
    }

    private static void RenderShapeBevel(DrawingContext dc, DrawOp.Shape shape)
    {
        var fx = shape.Effects;
        if (fx is null) return;

        bool hasBevel   = fx.BevelTop is not null || fx.BevelBottom is not null;
        bool hasContour = fx.ContourWidthDip > 0;
        if (!hasBevel && !hasContour) return;
        if (shape.Geometry.Contours.Count == 0) return;

        var geo    = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
        var bounds = shape.BoundsDip;

        if (hasBevel && fx.BevelTop is not null && geo is not null)
        {
            var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, fx.BevelTop, fx.LightDirDeg);
            DrawBevelOverlay(dc, geo, bounds, highlight, shade, fx.BevelTop.WidthDip, fx.BevelTop.HeightDip);
        }

        if (hasContour && geo is not null)
        {
            var cColor     = fx.ContourColor ?? new SrgbColor(0x60, 0x60, 0x60);
            var cBrush     = new SolidColorBrush(Color.FromArgb(255, cColor.R, cColor.G, cColor.B));
            var contourPen = new Pen(cBrush, Math.Max(0.5, fx.ContourWidthDip));
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
        var highlightBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        var shadeBrush     = new SolidColorBrush(Color.FromArgb(110, 0,   0,   0  ));

        using var clipScope = dc.PushGeometryClip(shapeGeo);

        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var (bw, bh) = BevelGeometryHelper.GetRenderDimensions(bounds, bevelW, bevelH);

        void DrawWedge(bool active, IBrush brush, Point tl, Point tr, Point br, Point bl)
        {
            if (!active) return;
            var pg = new StreamGeometry();
            using (var sgc = pg.Open())
            {
                sgc.BeginFigure(tl, isFilled: true);
                sgc.LineTo(tr);
                sgc.LineTo(br);
                sgc.LineTo(bl);
                sgc.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, null, pg);
        }

        DrawWedge(highlight.Top || shade.Top,
            highlight.Top ? highlightBrush : shadeBrush,
            new Point(x, y), new Point(x + w, y),
            new Point(x + w - bw, y + bh), new Point(x + bw, y + bh));

        DrawWedge(highlight.Bottom || shade.Bottom,
            highlight.Bottom ? highlightBrush : shadeBrush,
            new Point(x + bw, y + h - bh), new Point(x + w - bw, y + h - bh),
            new Point(x + w, y + h), new Point(x, y + h));

        DrawWedge(highlight.Left || shade.Left,
            highlight.Left ? highlightBrush : shadeBrush,
            new Point(x, y), new Point(x + bw, y + bh),
            new Point(x + bw, y + h - bh), new Point(x, y + h));

        DrawWedge(highlight.Right || shade.Right,
            highlight.Right ? highlightBrush : shadeBrush,
            new Point(x + w - bw, y + bh), new Point(x + w, y),
            new Point(x + w, y + h), new Point(x + w - bw, y + h - bh));
    }

    private static Matrix ToAvaloniaMatrix(ShapeAffineTransform transform) =>
        new(
            transform.M11,
            transform.M12,
            transform.M21,
            transform.M22,
            transform.OffsetX,
            transform.OffsetY);

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

    private static StreamGeometry ToGeometry(
        ChartLinePathFigurePrimitive figure,
        ChartClassicThreeDDepthPlan? depth = null)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(OffsetIfNeeded(figure.Start, depth)), isFilled: false);
            foreach (var segment in figure.Segments)
            {
                switch (segment.Kind)
                {
                    case ChartLinePathSegmentKind.CubicBezier:
                        ctx.CubicBezierTo(
                            ToPoint(OffsetIfNeeded(segment.Control1, depth)),
                            ToPoint(OffsetIfNeeded(segment.Control2, depth)),
                            ToPoint(OffsetIfNeeded(segment.End, depth)));
                        break;

                    default:
                        ctx.LineTo(ToPoint(OffsetIfNeeded(segment.End, depth)));
                        break;
                }
            }

            ctx.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private static ChartPlanPoint OffsetIfNeeded(
        ChartPlanPoint point,
        ChartClassicThreeDDepthPlan? depth) =>
        depth.HasValue ? OffsetPoint(point, depth.Value) : point;

    private static IBrush ToBrush(ChartFillPlan fill) =>
        fill.Fill switch
        {
            ResolvedFill.Gradient gradient when gradient.Kind == GradientKind.Radial => MakeRadialGradientBrush(gradient),
            ResolvedFill.Gradient gradient => MakeLinearGradientBrush(gradient),
            ResolvedFill.PatternFill pattern => MakePatternBrush(pattern),
            ResolvedFill.Solid solid => new SolidColorBrush(Color.FromArgb(
                fill.Alpha,
                solid.Color.R,
                solid.Color.G,
                solid.Color.B)),
            _ => new SolidColorBrush(Color.FromArgb(
                fill.Alpha,
                fill.Color.R,
                fill.Color.G,
                fill.Color.B))
        };

    internal static Pen CreateChartGridLinePen(ChartMajorGridLinePrimitivePlan plan) =>
        ToPen(plan.Stroke);

    internal static Pen CreateChartAxisTickPen(ChartMajorAxisTickPrimitivePlan plan) =>
        ToPen(plan.Stroke);

    internal static Pen CreateChartSecondaryAxisTickPen(ChartSecondaryValueAxisPrimitivePlan plan) =>
        ToPen(plan.TickStroke);

    private static Pen ToPen(ChartStrokePlan stroke) =>
        new(
            ToBrush(new ChartFillPlan(stroke.Color, stroke.Alpha) { Fill = stroke.Fill }),
            stroke.Thickness)
        {
            DashStyle = MapDashStyleAvalonia(stroke.Dash)
        };

    private static IDashStyle? MapDashStyleAvalonia(OutlineDash dash) => dash switch
    {
        OutlineDash.Dash           => DashStyle.Dash,
        OutlineDash.Dot            => DashStyle.Dot,
        OutlineDash.DashDot        => DashStyle.DashDot,
        OutlineDash.LongDash       => new DashStyle([8.0, 3.0], 0),
        OutlineDash.LongDashDot    => new DashStyle([8.0, 3.0, 1.0, 3.0], 0),
        OutlineDash.LongDashDotDot => new DashStyle([8.0, 3.0, 1.0, 3.0, 1.0, 3.0], 0),
        OutlineDash.SystemDash     => DashStyle.Dash,
        OutlineDash.SystemDot      => DashStyle.Dot,
        OutlineDash.SystemDashDot  => DashStyle.DashDot,
        _                          => null
    };

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
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true);
            for (int index = 1; index < points.Length; index++)
                ctx.LineTo(points[index]);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
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
                    ctx.BeginFigure(point, isFilled: path.Fill.HasValue);
                else
                    ctx.LineTo(point);
            }

            if (path.Points.Count > 0)
                ctx.EndFigure(isClosed: path.IsClosed);
        }

        return geometry;
    }

    private static StreamGeometry ToPolygonGeometry(IReadOnlyList<ChartPlanPoint> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count > 0)
            {
                ctx.BeginFigure(ToPoint(points[0]), isFilled: true);
                for (int index = 1; index < points.Count; index++)
                    ctx.LineTo(ToPoint(points[index]));
                ctx.EndFigure(isClosed: true);
            }
        }
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
                ctx.BeginFigure(ToPoint(points[0]), isFilled: true);
                for (int index = 1; index < points.Count; index++)
                    ctx.LineTo(ToPoint(points[index]));
                ctx.EndFigure(isClosed: true);
            }
        }

        return geometry;
    }

    private static TextAlignment ToTextAlignment(ChartPlanTextAlignment alignment) =>
        alignment switch
        {
            ChartPlanTextAlignment.Left => TextAlignment.Left,
            ChartPlanTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Center
        };

    // ── Picture ──────────────────────────────────────────────────────────────

    private static void RenderPicture(DrawingContext dc, DrawOp.Picture pic)
    {
        if (pic.Bytes.Length == 0) return;

        Bitmap? bitmap = null;
        try
        {
            using var ms = new MemoryStream(pic.Bytes);
            bitmap = new Bitmap(ms);
        }
        catch { return; }

        var plan = PictureRenderPlanner.Plan(pic, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var destination = plan.DestinationDip;
        var dest = new Rect(destination.X, destination.Y, destination.Width, destination.Height);

        // 18A: colour effects — produce a modified bitmap via pixel manipulation.
        // BN1: ApplyColorEffectsAvalonia returns null when GDI+/libgdiplus is unavailable;
        //      in that case we keep the original uneffected bitmap so the picture isn't blank.
        IImage renderBitmap = bitmap;
        var effectPlan = plan.ColorEffects;
        if (effectPlan.HasPixelEffects)
            renderBitmap = ApplyColorEffectsAvalonia(bitmap, effectPlan) ?? (IImage)bitmap;

        IDisposable? rotScope   = null;
        IDisposable? alphaScope = null;

        if (pic.RotationDeg != 0)
        {
            double cx  = dest.Left + dest.Width  / 2;
            double cy  = dest.Top  + dest.Height / 2;
            double rad = pic.RotationDeg * Math.PI / 180.0;
            rotScope = dc.PushTransform(
                Matrix.CreateTranslation(-cx, -cy)
                * Matrix.CreateRotation(rad)
                * Matrix.CreateTranslation(cx, cy));
        }

        // Wave 26: draw outer shadow behind the picture when effects are set.
        // Route the shadow-direction/blur math through the shared renderer-neutral planner
        // (ResolvedShapeEffectRenderPlanner) so WPF + Avalonia stay in lock-step and we don't duplicate it.
        foreach (var pass in plan.OuterEffects.ShadowPasses)
        {
            var shadowBrush = new SolidColorBrush(
                Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
            var shadowDest = new Rect(dest.X + pass.OffsetX, dest.Y + pass.OffsetY, dest.Width, dest.Height);
            if (pic.HasFrameClip && pic.PictureFrameGeometry == "roundRect")
            {
                double srx = Math.Min(dest.Width, dest.Height) * 0.18;
                dc.DrawRectangle(shadowBrush, null, shadowDest, srx, srx);
            }
            else if (pic.HasFrameClip && pic.PictureFrameGeometry == "ellipse")
            {
                double scx = shadowDest.X + shadowDest.Width / 2;
                double scy = shadowDest.Y + shadowDest.Height / 2;
                dc.DrawEllipse(shadowBrush, null, new Point(scx, scy), shadowDest.Width / 2, shadowDest.Height / 2);
            }
            else
                dc.DrawRectangle(shadowBrush, null, shadowDest);
        }

        // 18A: alpha opacity
        if (plan.HasAlphaOpacity)
            alphaScope = dc.PushOpacity(plan.AlphaOpacity);

        // Wave 26: build clip geometry for non-rect frame presets.
        IDisposable? clipScope = null;
        if (pic.HasFrameClip)
        {
            Geometry clipGeom;
            if (pic.PictureFrameGeometry == "ellipse")
            {
                clipGeom = new EllipseGeometry
                {
                    Center  = new Point(dest.X + dest.Width / 2, dest.Y + dest.Height / 2),
                    RadiusX = dest.Width  / 2,
                    RadiusY = dest.Height / 2,
                };
            }
            else
            {
                // roundRect and other non-rect presets use a rounded rectangle
                double rx = Math.Min(dest.Width, dest.Height) * 0.18;
                clipGeom = new RectangleGeometry { Rect = dest, RadiusX = rx, RadiusY = rx };
            }
            clipScope = dc.PushGeometryClip(clipGeom);
        }

        // 18A: crop from the shared renderer-neutral source rectangle.
        if (plan.HasCrop)
        {
            var source = plan.SourceRectPixels;
            dc.DrawImage(
                renderBitmap,
                new Rect(source.X, source.Y, source.Width, source.Height),
                dest);
        }
        else
        {
            dc.DrawImage(renderBitmap, dest);
        }

        clipScope?.Dispose(); // pop frame clip

        alphaScope?.Dispose();

        // P3 / Wave 26: draw the picture frame outline (shaped when HasFrameClip).
        if (pic.Outline is ResolvedOutline.Visible visOutline)
        {
            var pen = MakePen(visOutline);
            if (pen is not null)
            {
                if (pic.HasFrameClip && pic.PictureFrameGeometry == "ellipse")
                {
                    double cx = dest.X + dest.Width / 2;
                    double cy = dest.Y + dest.Height / 2;
                    dc.DrawEllipse(null, pen, new Point(cx, cy), dest.Width / 2, dest.Height / 2);
                }
                else if (pic.HasFrameClip)
                {
                    double rx = Math.Min(dest.Width, dest.Height) * 0.18;
                    dc.DrawRectangle(null, pen, dest, rx, rx);
                }
                else
                    dc.DrawRectangle(null, pen, dest);
            }
        }

        if (pic.IsMedia)
            DrawPlayButtonOverlay(dc, dest);

        rotScope?.Dispose();
    }

    /// <summary>
    /// 18A: Applies grayscale, biLevel, brightness/contrast effects to an Avalonia Bitmap.
    /// Decodes pixels via System.Drawing (GDI+, available on .NET 10 Windows / Linux with libgdiplus).
    /// Falls back to returning a blank WriteableBitmap when GDI+ is unavailable so crop still works.
    /// Alpha opacity is handled via dc.PushOpacity upstream and is NOT applied here.
    /// </summary>
    private static WriteableBitmap? ApplyColorEffectsAvalonia(Bitmap src, PictureColorEffectPlan effectPlan)
    {
        int pw = src.PixelSize.Width;
        int ph = src.PixelSize.Height;
        int stride = pw * 4; // BGRA

        var wb = new WriteableBitmap(
            new PixelSize(pw, ph),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul); // BN2: pixels from System.Drawing Format32bppArgb are straight (non-premultiplied)

        // ── Decode source pixels via System.Drawing ──────────────────────────────────
        var pixels = new byte[ph * stride];
        bool pixelsLoaded = false;
        try
        {
            // Save src as PNG to memory, then load with System.Drawing to get ARGB pixels.
            byte[] rawPng;
            using (var pngMs = new MemoryStream())
            {
                src.Save(pngMs);
                rawPng = pngMs.ToArray();
            }

#pragma warning disable CA1416 // Windows-only GDI+ — entire block is in try/catch for graceful fallback
            using var sysBmp = (System.Drawing.Bitmap)System.Drawing.Image.FromStream(new MemoryStream(rawPng));
            var bmpData = sysBmp.LockBits(
                new System.Drawing.Rectangle(0, 0, pw, ph),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                // System.Drawing Format32bppArgb on little-endian = B,G,R,A in memory (same as BGRA).
                for (int row = 0; row < ph; row++)
                {
                    int srcOff = (bmpData.Stride >= 0 ? row : ph - 1 - row) * Math.Abs(bmpData.Stride);
                    Marshal.Copy(bmpData.Scan0 + srcOff, pixels, row * stride,
                        Math.Min(stride, Math.Abs(bmpData.Stride)));
                }
                pixelsLoaded = true;
            }
            finally { sysBmp.UnlockBits(bmpData); }
#pragma warning restore CA1416
        }
        catch
        {
            // BN1: GDI+ unavailable (e.g. Linux without libgdiplus) — return the original uneffected
            // source bitmap so the picture still renders rather than a blank/transparent rectangle.
            // Crop continues to work in RenderPicture because it uses the returned bitmap's bounds.
            return null; // null signals RenderPicture to draw src directly
        }

        if (!pixelsLoaded) return null; // BN1: same fallback — draw src uneffected

        PictureColorEffectPlanner.ApplyToBgra32(pixels, effectPlan);

        // ── Write processed pixels into WriteableBitmap ───────────────────────────────
        using (var buf = wb.Lock())
            Marshal.Copy(pixels, 0, buf.Address, Math.Min(pixels.Length, ph * buf.RowBytes));

        return wb;
    }

    private static void DrawPlayButtonOverlay(DrawingContext dc, Rect dest)
    {
        double cx = dest.Left + dest.Width  / 2;
        double cy = dest.Top  + dest.Height / 2;
        double r  = Math.Max(4, Math.Min(dest.Width, dest.Height) / 6);

        var circleBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0));
        dc.DrawEllipse(circleBrush, null, new Point(cx, cy), r, r);

        double tx = cx - r * 0.3;
        double ty = cy - r * 0.45;
        var triGeo = new StreamGeometry();
        using (var ctx = triGeo.Open())
        {
            ctx.BeginFigure(new Point(tx,           ty),            isFilled: true);
            ctx.LineTo(     new Point(tx + r * 0.8, cy));
            ctx.LineTo(     new Point(tx,           cy + r * 0.45));
            ctx.EndFigure(isClosed: true);
        }
        dc.DrawGeometry(Brushes.White, null, triGeo);
    }

    // ── Table ────────────────────────────────────────────────────────────────

    private static void RenderTable(DrawingContext dc, DrawOp.Table tableOp)
    {
        foreach (var cell in tableOp.Cells)
            RenderTableCell(dc, cell);
    }

    private static void RenderTableWithTransform(DrawingContext dc, DrawOp.Table tableOp)
    {
        var transform = ShapeTransformPlanner.PlanShapeTransform(
            tableOp.BoundsDip,
            tableOp.RotationDeg,
            tableOp.FlipH,
            tableOp.FlipV);
        if (!transform.IsIdentity)
        {
            using var transformScope = dc.PushTransform(ToAvaloniaMatrix(transform));
            RenderTable(dc, tableOp);
            return;
        }

        RenderTable(dc, tableOp);
    }

    private static void RenderTableCell(DrawingContext dc, TableCellOp cell)
    {
        var rect = new Rect(cell.BoundsDip.X, cell.BoundsDip.Y, cell.BoundsDip.Width, cell.BoundsDip.Height);

        var fillBrush = MakeBrush(cell.Fill, cell.BoundsDip);
        if (fillBrush is not null)
            dc.FillRectangle(fillBrush, rect);

        DrawCellBorder(dc, cell.BorderTop,
            new Point(rect.Left,  rect.Top),    new Point(rect.Right, rect.Top));
        DrawCellBorder(dc, cell.BorderBottom,
            new Point(rect.Left,  rect.Bottom),  new Point(rect.Right, rect.Bottom));
        DrawCellBorder(dc, cell.BorderLeft,
            new Point(rect.Left,  rect.Top),    new Point(rect.Left,  rect.Bottom));
        DrawCellBorder(dc, cell.BorderRight,
            new Point(rect.Right, rect.Top),    new Point(rect.Right, rect.Bottom));

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
        TableCellAnchor anchor)
    {
        if (text.VerticalType != TextVerticalType.Horizontal)
        {
            RenderText(dc, text, bounds);
            return;
        }

        var area = TextLayoutPlanner.GetTextArea(text, bounds);
        var formatted = new Dictionary<int, FormattedText>();
        var measures = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, area.Width, text.Wrap, text.AutoFitKind);
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

    // ── Chart ────────────────────────────────────────────────────────────────

    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOp)
    {
        var transform = ShapeTransformPlanner.PlanShapeTransform(
            chartOp.BoundsDip,
            chartOp.RotationDeg,
            flipH: false,
            flipV: false);
        if (!transform.IsIdentity)
        {
            using var transformScope = dc.PushTransform(ToAvaloniaMatrix(transform));
            RenderChartCore(dc, chartOp);
            return;
        }

        RenderChartCore(dc, chartOp);
    }

    private static void RenderChartCore(DrawingContext dc, DrawOp.Chart chartOp)
    {
        var bounds = chartOp.BoundsDip;
        var chart = chartOp.ChartShape;
        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            ToPlanRect(bounds),
            chartOp.SeriesColors,
            chartOp.FillPlans,
            chartOp.ChartAreaFill,
            chartOp.ChartAreaOutline,
            chartOp.PlotAreaFill,
            chartOp.PlotAreaOutline);

        var frameRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        dc.FillRectangle(scene.ChartAreaFill is { } chartFill ? ToBrush(chartFill) : Brushes.White, frameRect);
        if (scene.PlotAreaFill is { } plotFill)
            dc.FillRectangle(ToBrush(plotFill), ToRect(scene.Frame.Plot));
        if (scene.ChartAreaOutline is { } chartOutline)
            dc.DrawRectangle(ToPen(chartOutline), frameRect);
        if (scene.PlotAreaOutline is { } plotOutline)
            dc.DrawRectangle(ToPen(plotOutline), ToRect(scene.Frame.Plot));

        if (scene.Title is { } title)
            DrawChartLabel(dc, title.Text, ToRect(title.Bounds), title.IsBold, title.FontSize, ToTextAlignment(title.Alignment), textColor: title.TextColor, fontFamily: title.FontFamily, maxLineCount: title.MaxLineCount);

        if (!scene.Frame.HasPlot)
            return;

        if (scene.DrawFlatGrid && scene.GridLines.GridLines.Count > 0)
        {
            var gridPen = CreateChartGridLinePen(scene.GridLines);
            foreach (var gridLine in scene.GridLines.GridLines)
                dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));
        }

        if (scene.DrawFlatGrid && scene.MinorGridLines.GridLines.Count > 0)
        {
            var minorGridPen = CreateChartGridLinePen(scene.MinorGridLines);
            foreach (var gridLine in scene.MinorGridLines.GridLines)
                dc.DrawLine(minorGridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));
        }

        if (scene.DrawProjectedThreeDBarFrame)
            RenderProjectedThreeDBarFrame(dc, scene);

        switch (scene.GeometryKind)
        {
            case ChartSceneGeometryKind.Column:
                RenderColumnChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Surface:
                RenderSurfaceChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Bar:
                RenderBarChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Line:
                RenderLineChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Stock:
                RenderStockChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Pie:
                RenderPieChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Doughnut:
                RenderDoughnutChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Area:
                RenderAreaChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Scatter:
                RenderScatterChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Bubble:
                RenderBubbleChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Radar:
                RenderRadarChart(dc, scene);
                break;
            default:
                dc.FillRectangle(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), ToRect(scene.Frame.Plot));
                break;
        }

        if (scene.ComboLineSeries.Count > 0)
            RenderComboOverrideSeries(dc, scene);

        RenderTrendlines(dc, scene.Trendlines);
        RenderErrorBars(dc, scene.ErrorBars);

        foreach (var leaderLine in scene.DataLabelLeaderLines)
            dc.DrawLine(ToPen(leaderLine.Stroke), ToPoint(leaderLine.Start), ToPoint(leaderLine.End));

        if (scene.AxisTicks.CategoryTicks.Count > 0 || scene.AxisTicks.ValueTicks.Count > 0)
        {
            var tickPen = CreateChartAxisTickPen(scene.AxisTicks);
            foreach (var tick in scene.AxisTicks.CategoryTicks)
                dc.DrawLine(tickPen, ToPoint(tick.Start), ToPoint(tick.End));
            foreach (var tick in scene.AxisTicks.ValueTicks)
                dc.DrawLine(tickPen, ToPoint(tick.Start), ToPoint(tick.End));
        }

        foreach (var label in scene.DataLabels)
        {
            if (label.LegendKeyBounds is { } keyBounds && label.LegendKeyFill is { } keyFill)
                dc.FillRectangle(ToBrush(keyFill), ToRect(keyBounds));

            DrawChartLabel(dc, label.Text, ToRect(label.TextBounds ?? label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment),
                isItalic: label.IsItalic,
                textColor: label.TextColor,
                fontFamily: label.FontFamily,
                maxLineCount: label.WrapText ? 2 : 1);
        }

        RenderChartDataTable(dc, scene.DataTable);

        var secondaryAxisPlan = scene.SecondaryAxis;
        if (secondaryAxisPlan.Ticks.Count > 0 || secondaryAxisPlan.Labels.Count > 0)
        {
            var secondaryTickPen = CreateChartSecondaryAxisTickPen(secondaryAxisPlan);
            foreach (var tick in secondaryAxisPlan.Ticks)
                dc.DrawLine(secondaryTickPen, ToPoint(tick.Start), ToPoint(tick.End));
            foreach (var label in secondaryAxisPlan.Labels)
            {
                DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                    label.IsBold,
                    label.FontSize,
                    ToTextAlignment(label.Alignment),
                    textColor: label.TextColor);
            }
        }

        if (secondaryAxisPlan.Title is { } secondaryAxisTitle)
            DrawChartAxisTitle(dc, secondaryAxisTitle);

        foreach (var label in scene.CategoryAxisLabels)
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment),
                textColor: label.TextColor);
        }

        foreach (var label in scene.ValueAxisLabels)
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment),
                textColor: label.TextColor);
        }

        foreach (var label in scene.SurfaceSeriesAxisLabels)
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment),
                textColor: label.TextColor);
        }

        foreach (var titlePlan in scene.AxisTitles)
            DrawChartAxisTitle(dc, titlePlan);

        foreach (var item in scene.LegendItems)
        {
            var swatch = ToRect(item.SwatchBounds);
            if (item.IsLine)
            {
                double centerY = swatch.Top + swatch.Height / 2.0;
                dc.DrawLine(
                    ToPen(new ChartStrokePlan(
                        item.Fill.Color,
                        item.Fill.Alpha,
                        ChartRenderPlanner.ImportedLineSeriesStrokeThickness)),
                    new Point(swatch.Left, centerY),
                    new Point(swatch.Right, centerY));
                if (item.MarkerSymbol is { } markerSymbol)
                {
                    DrawChartMarker(
                        dc,
                        new ChartCirclePrimitive(
                            -1,
                            -1,
                            new ChartPlanPoint(
                                item.SwatchBounds.X + item.SwatchBounds.Width / 2.0,
                                item.SwatchBounds.Y + item.SwatchBounds.Height / 2.0),
                            Math.Min(item.SwatchBounds.Width, item.SwatchBounds.Height) / 2.0,
                            markerSymbol,
                            item.Fill,
                            Stroke: null));
                }
                else if (!item.IsLineOnly)
                {
                    dc.DrawRectangle(
                        ToBrush(item.Fill),
                        null,
                        new Rect(swatch.Left + swatch.Width / 2.0 - 4.0, centerY - 4.0, 8.0, 8.0));
                }
            }
            else if (item.MarkerSymbol is { } markerSymbol)
            {
                DrawChartMarker(
                    dc,
                    new ChartCirclePrimitive(
                        -1,
                        -1,
                        new ChartPlanPoint(
                            item.SwatchBounds.X + item.SwatchBounds.Width / 2.0,
                            item.SwatchBounds.Y + item.SwatchBounds.Height / 2.0),
                        Math.Min(item.SwatchBounds.Width, item.SwatchBounds.Height) / 2.0,
                        markerSymbol,
                        item.Fill,
                        Stroke: null));
            }
            else
            {
                dc.DrawRectangle(ToBrush(item.Fill), null, swatch);
            }
            DrawChartLabel(dc, item.Label.Text, ToRect(item.Label.Bounds),
                item.Label.IsBold,
                item.Label.FontSize,
                ToTextAlignment(item.Label.Alignment),
                textColor: item.Label.TextColor,
                horizontalScale: item.Label.HorizontalScale);
        }

        foreach (var trendline in scene.Trendlines)
        {
            foreach (var label in trendline.Labels)
            {
                DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                    label.IsBold,
                    label.FontSize,
                    ToTextAlignment(label.Alignment),
                    textColor: label.TextColor,
                    fontFamily: label.FontFamily,
                    horizontalScale: label.HorizontalScale);
            }
        }
    }

    private static void RenderColumnChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.Rectangles)
        {
            if (primitive.Depth is { IsThreeD: true } depth)
            {
                RenderThreeDColumn(dc, primitive, depth);
                continue;
            }

            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }
    }

    private static void RenderThreeDColumn(
        DrawingContext dc,
        ChartRectPrimitive primitive,
        ChartBarDepthPlan depth)
    {
        var rect = primitive.Bounds;
        double right = rect.Right;
        double bottom = rect.Bottom;
        var top = new[]
        {
            new ChartPlanPoint(rect.X, rect.Y),
            new ChartPlanPoint(right, rect.Y),
            new ChartPlanPoint(right + depth.OffsetX, rect.Y + depth.OffsetY),
            new ChartPlanPoint(rect.X + depth.OffsetX, rect.Y + depth.OffsetY)
        };
        var side = new[]
        {
            new ChartPlanPoint(right, rect.Y),
            new ChartPlanPoint(right + depth.OffsetX, rect.Y + depth.OffsetY),
            new ChartPlanPoint(right + depth.OffsetX, bottom + depth.OffsetY),
            new ChartPlanPoint(right, bottom)
        };

        dc.DrawGeometry(ToBrush(ShadeThreeDBarFill(primitive.Fill, 0.60)), null, ToPolygonGeometry(side));
        dc.DrawGeometry(ToBrush(ShadeThreeDBarFill(primitive.Fill, 0.75)), null, ToPolygonGeometry(top));
        dc.DrawRectangle(
            ToBrush(primitive.Fill),
            primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
            ToRect(rect));
    }

    private static ChartFillPlan ShadeThreeDBarFill(ChartFillPlan fill, double factor) =>
        new(
            new SrgbColor(
                ScaleThreeDBarChannel(fill.Color.R, factor),
                ScaleThreeDBarChannel(fill.Color.G, factor),
                ScaleThreeDBarChannel(fill.Color.B, factor)),
            fill.Alpha);

    private static byte ScaleThreeDBarChannel(byte channel, double factor) =>
        (byte)Math.Round(Math.Clamp(channel * factor, 0, 255));

    // Stock and surface specialized chart renderers.
    private static void RenderSurfaceChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Surface is not { } plan)
            return;

        var renderFacets = plan.RenderFacets.Count > 0 ? plan.RenderFacets : plan.Facets;
        foreach (var segment in plan.FrameSegments)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        foreach (var segment in plan.WireframeSegments)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        if (renderFacets.Count > 0)
        {
            foreach (var facet in renderFacets)
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

    }

    private static void RenderStockChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Stock is null)
        {
            foreach (var primitive in scene.LineSeries)
                RenderLineSeriesPrimitive(dc, primitive);
            return;
        }

        foreach (var primitive in scene.StockVolumes)
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }

        var plan = scene.Stock.Value;
        foreach (var segment in plan.HighLowLines)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        foreach (var tick in plan.OpenTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
        foreach (var tick in plan.CloseTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
    }

    // Combo-chart secondary series overlay.
    /// <summary>
    /// Renders series that carry a per-series <see cref="ChartSeries.OverrideChartType"/>
    /// (set by the IO reader for combo charts). Only Line / LineMarkers overrides
    /// are handled here; others are silently skipped.
    /// </summary>
    private static void RenderComboOverrideSeries(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.ComboLineSeries)
            RenderLineSeriesPrimitive(dc, primitive);
    }

    private static void RenderErrorBars(
        DrawingContext dc,
        IReadOnlyList<ChartErrorBarPrimitive> errorBars)
    {
        foreach (var errorBar in errorBars)
        {
            var pen = ToPen(errorBar.Stroke);
            var center = ToPoint(errorBar.Center);
            if (errorBar.MinusEnd is { } minus)
            {
                dc.DrawLine(pen, center, ToPoint(minus));
                if (!errorBar.NoEndCap)
                    DrawErrorBarCap(dc, pen, minus, errorBar.Direction);
            }
            if (errorBar.PlusEnd is { } plus)
            {
                dc.DrawLine(pen, center, ToPoint(plus));
                if (!errorBar.NoEndCap)
                    DrawErrorBarCap(dc, pen, plus, errorBar.Direction);
            }
        }
    }

    private static void RenderTrendlines(
        DrawingContext dc,
        IReadOnlyList<ChartTrendlinePrimitive> trendlines)
    {
        foreach (var trendline in trendlines)
        {
            foreach (var segment in trendline.Segments)
                dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        }
    }

    private static void DrawErrorBarCap(
        DrawingContext dc,
        Pen pen,
        ChartPlanPoint endpoint,
        ChartErrorDirection direction)
    {
        const double capHalfLength = 3.0;
        var point = ToPoint(endpoint);
        if (direction == ChartErrorDirection.Y)
            dc.DrawLine(pen, new Point(point.X - capHalfLength, point.Y), new Point(point.X + capHalfLength, point.Y));
        else
            dc.DrawLine(pen, new Point(point.X, point.Y - capHalfLength), new Point(point.X, point.Y + capHalfLength));
    }

    private static void RenderBarChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.Rectangles)
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }
    }

    private static void RenderProjectedThreeDBarFrame(DrawingContext dc, ChartScenePlan scene)
    {
        var plot = scene.Frame.Plot;
        int lineCount = scene.ValueAxisLabels.Count;
        if (lineCount < 2)
            return;

        var pen = ToPen(scene.GridLines.Stroke);
        double leftX = plot.X + 21.0;
        double leftBaseline = plot.Bottom - (ChartRenderPlanner.ImportedThreeDBarBaseLift - 8.0);
        double depthY = Math.Min(plot.Height * 0.18, 94.0);
        double rightBaseline = leftBaseline + depthY;
        double rightTop = plot.Y + depthY * 0.39;

        for (int index = 0; index < lineCount; index++)
        {
            double fraction = index / (double)(lineCount - 1);
            dc.DrawLine(
                pen,
                new Point(leftX, leftBaseline - (leftBaseline - plot.Y) * fraction),
                new Point(plot.Right, rightBaseline - (rightBaseline - rightTop) * fraction));
        }

        dc.DrawLine(pen, new Point(leftX, leftBaseline), new Point(leftX, plot.Y));
        double frontRightX = plot.Right - 49.0;
        dc.DrawLine(pen, new Point(leftX, leftBaseline), new Point(frontRightX, rightBaseline));

        int categoryCount = Math.Max(1, scene.CategoryAxisLabels.Count);
        for (int index = 0; index <= categoryCount; index++)
        {
            double fraction = index / (double)categoryCount;
            double x = leftX + (frontRightX - leftX) * fraction;
            double y = leftBaseline + depthY * fraction;
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + 5.0));
        }
    }

    private static void RenderLineChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.LineSeries)
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

    private static void RenderPieChart(DrawingContext dc, ChartScenePlan scene)
    {
        var borderPen = new Pen(Brushes.White, 0.8);
        foreach (var primitive in scene.PieSlices)
        {
            var fill = primitive.Fill!.Value;
            if (primitive.DepthFill is { } depthFill)
            {
                if (primitive.DrawDepthSidewalls)
                {
                    foreach (var interval in GetPieDepthArcIntervals(primitive))
                    {
                        dc.DrawGeometry(
                            ToBrush(ShadeImportedThreeDPieSidewall(
                                depthFill,
                                interval.Start,
                                interval.End,
                                primitive.PointIndex)),
                            null,
                            ToPieSliceDepthGeometry(primitive, interval.Start, interval.End));
                    }
                }
                else
                {
                    dc.DrawGeometry(
                        ToBrush(depthFill),
                        null,
                        ToPieSliceGeometry(primitive, primitive.DepthOffsetY));
                }
            }

            var brush = ToBrush(fill);
            var geo = ToPieSliceGeometry(primitive);
            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    private static StreamGeometry ToPieSliceGeometry(ChartPieSlicePrimitive primitive, double offsetY = 0)
    {
        var center = new ChartPlanPoint(primitive.Center.X, primitive.Center.Y + offsetY);
        var start = new ChartPlanPoint(primitive.OuterStart.X, primitive.OuterStart.Y + offsetY);
        var end = new ChartPlanPoint(primitive.OuterEnd.X, primitive.OuterEnd.Y + offsetY);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToPoint(center), isFilled: true);
            ctx.LineTo(ToPoint(start));
            ctx.ArcTo(
                ToPoint(end),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geo;
    }

    private static IEnumerable<(double Start, double End)> GetPieDepthArcIntervals(
        ChartPieSlicePrimitive primitive)
    {
        for (int turn = -1; turn <= 1; turn++)
        {
            double frontStart = turn * 2 * Math.PI;
            double frontEnd = frontStart + Math.PI;
            double start = Math.Max(primitive.StartAngle, frontStart);
            double end = Math.Min(primitive.EndAngle, frontEnd);
            if (end - start > 1e-6)
                yield return (start, end);
        }
    }

    private static StreamGeometry ToPieSliceDepthGeometry(
        ChartPieSlicePrimitive primitive,
        double startAngle,
        double endAngle)
    {
        var topStart = PointOnPieOuter(primitive, startAngle);
        var topEnd = PointOnPieOuter(primitive, endAngle);
        var bottomStart = new ChartPlanPoint(topStart.X, topStart.Y + primitive.DepthOffsetY);
        var bottomEnd = new ChartPlanPoint(topEnd.X, topEnd.Y + primitive.DepthOffsetY);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToPoint(topStart), isFilled: true);
            ctx.ArcTo(
                ToPoint(topEnd),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise);
            ctx.LineTo(ToPoint(bottomEnd));
            ctx.ArcTo(
                ToPoint(bottomStart),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                isLargeArc: false,
                SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }
        return geo;
    }

    private static ChartPlanPoint PointOnPieOuter(
        ChartPieSlicePrimitive primitive,
        double angle) =>
        new(
            primitive.Center.X + primitive.OuterRadius * Math.Cos(angle),
            primitive.Center.Y + primitive.OuterRadiusY * Math.Sin(angle));

    private static ChartFillPlan ShadeImportedThreeDPieSidewall(
        ChartFillPlan fill,
        double startAngle,
        double endAngle,
        int pointIndex)
    {
        double factor = ChartRenderPlanner.ResolveImportedThreeDPieSidewallFactor(
            pointIndex,
            startAngle,
            endAngle);
        return new ChartFillPlan(
            new SrgbColor(
                ScalePieSidewallChannel(fill.Color.R, factor),
                ScalePieSidewallChannel(fill.Color.G, factor),
                ScalePieSidewallChannel(fill.Color.B, factor)),
            fill.Alpha);
    }

    private static byte ScalePieSidewallChannel(byte channel, double factor) =>
        (byte)Math.Round(Math.Clamp(channel * factor, 0, 255));

    private static void RenderAreaChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.AreaSeries)
        {
            if (primitive.AreaPath.Fill is not { } fill)
                continue;

            if (primitive.Depth is { } depth)
            {
                var depthFill = fill.WithAlpha(depth.FillAlpha);
                dc.DrawGeometry(ToBrush(depthFill), null, ToGeometry(OffsetPath(primitive.AreaPath, depth)));
            }

            var brush = ToBrush(fill);
            var geo = ToGeometry(primitive.AreaPath);
            dc.DrawGeometry(brush, null, geo);
        }
    }

    // ── Doughnut chart ───────────────────────────────────────────────────────

    private static void RenderDoughnutChart(DrawingContext dc, ChartScenePlan scene)
    {
        var borderPen = new Pen(Brushes.White, 0.8);
        foreach (var primitive in scene.DoughnutSlices)
        {
            var brush = ToBrush(primitive.Fill!.Value);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ToPoint(primitive.OuterStart), isFilled: true);
                ctx.ArcTo(
                    ToPoint(primitive.OuterEnd),
                    new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.Clockwise);
                ctx.LineTo(ToPoint(primitive.InnerEnd));
                ctx.ArcTo(
                    ToPoint(primitive.InnerStart),
                    new Size(primitive.InnerRadius, primitive.InnerRadiusY),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.CounterClockwise);
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    // ── Scatter chart ────────────────────────────────────────────────────────

    private static void RenderScatterChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Scatter is not { } plan)
            return;
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
            if (label.LegendKeyBounds is { } keyBounds && label.LegendKeyFill is { } keyFill)
                dc.FillRectangle(ToBrush(keyFill), ToRect(keyBounds));

            DrawChartLabel(dc, label.Text, ToRect(label.TextBounds ?? label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment),
                isItalic: label.IsItalic,
                textColor: label.TextColor,
                fontFamily: label.FontFamily,
                maxLineCount: label.WrapText ? 2 : 1);
        }
    }

    // ── Bubble chart ─────────────────────────────────────────────────────────

    private static void RenderBubbleChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Bubble is not { } plan)
            return;
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

    // ── Radar chart ──────────────────────────────────────────────────────────

    private static void RenderRadarChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Radar is not { } plan)
            return;

        foreach (var ring in plan.Rings)
            dc.DrawGeometry(null, ToPen(ring.Stroke), ToGeometry(ring.Path));

        var spokePen = ToPen(plan.SpokeStroke);
        foreach (var spoke in plan.Spokes)
            dc.DrawLine(spokePen, ToPoint(spoke.Start), ToPoint(spoke.End));

        foreach (var label in plan.ValueLabels)
        {
            var labelBounds = ToRect(label.Bounds);
            if (plan.Rings.Count == 9 &&
                plan.CategoryLabels.Count == 5 &&
                plan.Series.Count == 2)
            {
                labelBounds = new Rect(
                    labelBounds.X,
                    labelBounds.Y + ImportedRadarValueLabelAvaloniaYCompensation,
                    labelBounds.Width,
                    labelBounds.Height);
            }

            DrawChartLabel(dc, label.Text, labelBounds, label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        }

        for (int labelIndex = 0; labelIndex < plan.CategoryLabels.Count; labelIndex++)
        {
            var label = plan.CategoryLabels[labelIndex];
            var labelBounds = ToRect(label.Bounds);
            if (plan.Rings.Count == 9 &&
                plan.CategoryLabels.Count == 5 &&
                plan.Series.Count == 2 &&
                labelIndex is 2 or 3)
            {
                double horizontalOffset = labelIndex == 2
                    ? AvaloniaImportedRadarAgilityLabelOffsetX
                    : AvaloniaImportedRadarStaminaLabelOffsetX;
                labelBounds = new Rect(
                    labelBounds.X + horizontalOffset,
                    labelBounds.Y + AvaloniaImportedRadarLowerLabelOffsetY,
                    labelBounds.Width,
                    labelBounds.Height);
            }
            DrawChartLabel(dc, label.Text, labelBounds, label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        }

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

    // ── Chart helpers ────────────────────────────────────────────────────────

    private static void DrawChartLabel(
        DrawingContext dc, string text, Rect rect,
        bool isBold, double fontSize, TextAlignment align,
        bool isItalic = false,
        SrgbColor? textColor = null,
        string? fontFamily = null,
        int maxLineCount = 1,
        double horizontalScale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0) return;
        var color = textColor ?? new SrgbColor(0x40, 0x40, 0x40);
        var typeface = new Typeface(fontFamily ?? "Calibri",
            isItalic ? FontStyle.Italic : FontStyle.Normal,
            isBold ? FontWeight.Bold : FontWeight.Normal,
            FontStretch.Normal);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize * (96.0 / 72.0),
            brush)
        {
            MaxTextWidth  = rect.Width,
            MaxLineCount  = maxLineCount,
            TextAlignment = align,
            Trimming      = TextTrimming.CharacterEllipsis,
        };
        var transform = horizontalScale > 0.0 && Math.Abs(horizontalScale - 1.0) > 0.0001
            ? Matrix.CreateTranslation(-rect.X, -rect.Y)
                * Matrix.CreateScale(horizontalScale, 1.0)
                * Matrix.CreateTranslation(rect.X, rect.Y)
            : Matrix.Identity;
        using var transformScope = dc.PushTransform(transform);
        dc.DrawText(ft, new Point(rect.X, rect.Y));
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void RenderChartDataTable(
        DrawingContext dc,
        ChartDataTablePrimitivePlan plan)
    {
        if (!plan.Bounds.HasPositiveArea)
            return;

        if (plan.BackgroundFill.HasValue)
            dc.FillRectangle(ToBrush(plan.BackgroundFill.Value), ToRect(plan.Bounds));

        var borderPen = ToPen(plan.BorderStroke);
        foreach (var border in plan.HorizontalBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));
        foreach (var border in plan.VerticalBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));
        foreach (var border in plan.OutlineBorders)
            dc.DrawLine(borderPen, ToPoint(border.Start), ToPoint(border.End));

        foreach (var cell in plan.Cells)
        {
            using var clipScope = dc.PushClip(ToRect(cell.CellBounds));

            if (cell.LegendKeyFill.HasValue && cell.LegendKeyBounds.HasValue)
            {
                dc.FillRectangle(
                    ToBrush(cell.LegendKeyFill.Value),
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
        }
    }

    private static void DrawChartAxisTitle(DrawingContext dc, ChartAxisTitlePlan title)
    {
        var label = title.Label;
        var rect = ToRect(label.Bounds);
        if (title.Orientation == ChartAxisTitleOrientation.Horizontal)
        {
            DrawChartLabel(dc, label.Text, rect, label.IsBold, label.FontSize, ToTextAlignment(label.Alignment), isItalic: label.IsItalic, textColor: label.TextColor, fontFamily: label.FontFamily);
            return;
        }

        double angle = title.Orientation == ChartAxisTitleOrientation.VerticalClockwise
            ? Math.PI / 2.0
            : -Math.PI / 2.0;
        double cx = rect.X + rect.Width * 0.5;
        double cy = rect.Y + rect.Height * 0.5;
        using var rotateScope = dc.PushTransform(
            Matrix.CreateTranslation(-cx, -cy)
            * Matrix.CreateRotation(angle)
            * Matrix.CreateTranslation(cx, cy));
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
            ToTextAlignment(label.Alignment),
            isItalic: label.IsItalic,
            textColor: label.TextColor,
            fontFamily: label.FontFamily);
    }

    private static void RenderText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        // Wave 18B: vertical text — rotate the text block around the shape center.
        var orientation = TextLayoutPlanner.PlanTextOrientation(text, bounds);
        if (orientation.RenderMode == TextVerticalRenderMode.StackedUpright)
        {
            RenderStackedVerticalText(dc, text, bounds);
            return;
        }

        if (orientation.IsRotated)
        {
            double rad = orientation.RotationAngleDegrees * Math.PI / 180.0;
            using var rotScope = dc.PushTransform(
                Matrix.CreateTranslation(-orientation.RotationCenterX, -orientation.RotationCenterY)
                * Matrix.CreateRotation(rad)
                * Matrix.CreateTranslation(orientation.RotationCenterX, orientation.RotationCenterY));
            RenderTextCore(dc, text, orientation.TextBounds);
            return;
        }

        RenderTextCore(dc, text, orientation.TextBounds);
    }

    private static void RenderStackedVerticalText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        var initialPlan = TextLayoutPlanner.PlanStackedVerticalText(
            text,
            bounds,
            MeasureStackedGlyphAvalonia);
        var autoFitPlan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            initialPlan.Area.Height,
            initialPlan.Paragraphs);
        var renderText = TextLayoutPlanner.ApplyAutoFitPlan(text, autoFitPlan);
        var plan = TextLayoutPlanner.PlanStackedVerticalText(
            renderText,
            bounds,
            MeasureStackedGlyphAvalonia,
            autoFitPlan);

        foreach (var glyph in plan.Glyphs)
        {
            if ((uint)glyph.ParagraphIndex >= (uint)renderText.Paragraphs.Count)
                continue;
            var paragraph = renderText.Paragraphs[glyph.ParagraphIndex];
            if ((uint)glyph.RunIndex >= (uint)paragraph.Runs.Count)
                continue;

            DrawStackedGlyphAvalonia(dc, renderText, bounds, paragraph.Runs[glyph.RunIndex], glyph);
        }
    }

    // Wave 22B: multi-column text layout helper for Avalonia.
    // Mirrors the WPF version — greedy paragraph-level assignment across N columns.
    private static void RenderTextCoreColumns(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        if (TryRenderContinuousColumnFlow(dc, text, bounds))
            return;

        var initialColumnLayout = TextLayoutPlanner.GetColumnLayout(text, bounds);
        var initialMeasured = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(
                para, initialColumnLayout.ColumnWidthDip, text.Wrap, text.AutoFitKind);
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
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(
                para, columnLayout.ColumnWidthDip, renderText.Wrap, renderText.AutoFitKind);
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
                DrawBulletPlacementAvalonia(dc, bullet);

            switch (TextLayoutPlanner.PlanParagraphRenderRoute(para, renderText))
            {
                case TextParagraphRenderRoute.Math:
                    RenderParaWithMath(dc, para, placement.X, placement.Y);
                    break;
                case TextParagraphRenderRoute.Effects:
                    RenderParaWithEffects(dc, para, placement.X, placement.Y, bounds, renderText);
                    break;
                case TextParagraphRenderRoute.Tabs:
                    RenderParaWithTabs(dc, para, placement.X, placement.Y, para.TabStops);
                    break;
                case TextParagraphRenderRoute.Baseline:
                    RenderParaWithBaseline(dc, para, placement.X, placement.Y, placement.MaxWidthDip);
                    break;
                default:
                    if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                        ft.MaxTextWidth = placement.MaxWidthDip;
                    dc.DrawText(ft, new Point(placement.X, placement.Y));
                    break;
            }
        }
    }

    private static bool TryRenderContinuousColumnFlow(
        DrawingContext dc,
        ResolvedTextLayout text,
        LayoutRect bounds)
    {
        if (text.AutoFitKind != TextAutoFitKind.None ||
            text.HasStoredFontScale ||
            text.Paragraphs.Any(para =>
                para.Runs.Count != 1 ||
                TextLayoutPlanner.PlanParagraphRenderRoute(para, text) != TextParagraphRenderRoute.Plain))
        {
            return false;
        }

        var layout = TextLayoutPlanner.GetColumnLayout(text, bounds);
        var fragments = new Dictionary<(int ParagraphIndex, int LineIndex), ResolvedParagraph>();
        var measures = new List<TextColumnLineMeasure>();

        for (int paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            var run = paragraph.Runs[0];
            var lines = SplitColumnText(paragraph, run, layout.ColumnWidthDip, text.Wrap);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var fragment = CloneParagraphWithText(paragraph, run, lines[lineIndex]);
                var formatted = BuildFormattedText(fragment, layout.ColumnWidthDip, text.Wrap);
                fragments[(paragraphIndex, lineIndex)] = fragment;
                measures.Add(new TextColumnLineMeasure(
                    paragraphIndex,
                    lineIndex,
                    formatted.Height,
                    lineIndex == 0 ? TextLayoutPlanner.PointsToDip(paragraph.SpaceBeforePt) : 0,
                    lineIndex == lines.Count - 1 ? TextLayoutPlanner.PointsToDip(paragraph.SpaceAfterPt) : 0,
                    lineIndex == 0,
                    lineIndex == lines.Count - 1));
            }
        }

        foreach (var placement in TextLayoutPlanner.PlanColumnLines(text, layout, measures))
        {
            var fragment = fragments[(placement.ParagraphIndex, placement.LineIndex)];
            var formatted = BuildFormattedText(fragment, placement.MaxWidthDip, text.Wrap);
            if (placement.IsFirstLine && fragment.IndentDip > 0 && formatted.MaxTextWidth > 0)
                formatted.MaxTextWidth = placement.MaxWidthDip;
            dc.DrawText(formatted, new Point(placement.X, placement.Y));
        }

        return true;
    }

    private static IReadOnlyList<string> SplitColumnText(
        ResolvedParagraph paragraph,
        ResolvedRun run,
        double maxWidth,
        bool wrap)
    {
        if (!wrap || maxWidth <= 0)
            return new[] { run.Text };

        var words = run.Text.Replace('\r', ' ').Replace('\n', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return new[] { string.Empty };

        var lines = new List<string>();
        string current = string.Empty;
        foreach (var word in words)
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            var measure = BuildFormattedText(
                CloneParagraphWithText(paragraph, run, candidate),
                0,
                false);
            if (current.Length > 0 && measure.Width > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    private static ResolvedParagraph CloneParagraphWithText(
        ResolvedParagraph paragraph,
        ResolvedRun run,
        string text) =>
        new()
        {
            Runs = new[]
            {
                new ResolvedRun
                {
                    Text = text,
                    FontFamily = run.FontFamily,
                    FontSizePt = run.FontSizePt,
                    BaselineOffset = run.BaselineOffset,
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
                    TextSoftEdge = run.TextSoftEdge,
                    MathLayout = run.MathLayout
                }
            },
            Align = paragraph.Align,
            RightToLeft = paragraph.RightToLeft,
            Level = paragraph.Level,
            BulletKind = paragraph.BulletKind,
            BulletChar = paragraph.BulletChar,
            BulletImage = paragraph.BulletImage,
            SpaceBeforePt = paragraph.SpaceBeforePt,
            SpaceAfterPt = paragraph.SpaceAfterPt,
            TabStops = paragraph.TabStops,
            BulletText = paragraph.BulletText,
            BulletColor = paragraph.BulletColor,
            BulletFontFamily = paragraph.BulletFontFamily,
            BulletFontSizePt = paragraph.BulletFontSizePt,
            IndentDip = paragraph.IndentDip,
            HangingDip = paragraph.HangingDip
        };

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
            var ft = BuildFormattedText(para, area.Width, text.Wrap, text.AutoFitKind);
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
            var ft = BuildFormattedText(para, area.Width, renderText.Wrap, renderText.AutoFitKind);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanBodyText(renderText, bounds, measured, autoFitPlan);
        double importedAptosBodyOriginOffsetY =
            TextLayoutPlanner.ResolveImportedAptosBodyOriginOffsetY(renderText);
        foreach (var placement in plan.Paragraphs)
        {
            var para = renderText.Paragraphs[placement.ParagraphIndex];
            var ft = formatted[placement.ParagraphIndex];
            double placementY = placement.Y - importedAptosBodyOriginOffsetY;

            if (placement.Bullet is { } bullet)
            {
                bullet = bullet with { Y = bullet.Y - importedAptosBodyOriginOffsetY };
                DrawBulletPlacementAvalonia(dc, bullet);
            }

            switch (TextLayoutPlanner.PlanParagraphRenderRoute(para, renderText))
            {
                case TextParagraphRenderRoute.Math:
                    RenderParaWithMath(dc, para, placement.X, placementY);
                    break;
                case TextParagraphRenderRoute.Effects:
                    RenderParaWithEffects(dc, para, placement.X, placementY, bounds, renderText);
                    break;
                case TextParagraphRenderRoute.Tabs:
                    RenderParaWithTabs(dc, para, placement.X, placementY, para.TabStops);
                    break;
                case TextParagraphRenderRoute.Baseline:
                    RenderParaWithBaseline(dc, para, placement.X, placementY, placement.MaxWidthDip);
                    break;
                default:
                    if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                        ft.MaxTextWidth = placement.MaxWidthDip;
                    dc.DrawText(ft, new Point(placement.X, placementY));
                    break;
            }
        }
    }

    /// <summary>
    /// Wave 19A: draws a bullet glyph or number string at the given position.
    /// </summary>
    private static void DrawBulletPlacementAvalonia(DrawingContext dc, TextBulletPlacement bullet)
    {
        if (bullet.Image is { Bytes.Length: > 0 } image)
        {
            try
            {
                using var ms = new MemoryStream(image.Bytes);
                using var bitmap = new Bitmap(ms);
                double size = Math.Max(1.0, bullet.FontSizePt * (96.0 / 72.0));
                dc.DrawImage(bitmap, new Rect(bullet.X, bullet.Y, size, size));
            }
            catch
            {
                // Keep text rendering resilient when an imported bullet image cannot be decoded.
            }

            return;
        }

        DrawBulletAvalonia(dc, bullet.Text, bullet.FontFamily, bullet.FontSizePt,
            bullet.Color, bullet.X, bullet.Y);
    }

    private static void DrawBulletAvalonia(
        DrawingContext dc,
        string bulletText,
        string fontFamily,
        double fontSizePt,
        SrgbColor color,
        double x,
        double y)
    {
        if (string.IsNullOrEmpty(bulletText)) return;
        double emPx = fontSizePt * (96.0 / 72.0);
        var typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var ft = new FormattedText(bulletText,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface, emPx, brush);
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
            (run, text) => BuildSingleRunFormattedTextAt(
                run,
                text,
                flowDirection: para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight).Width);

        double previousEndX = startX;
        foreach (var segment in plan.Segments)
        {
            var run = para.Runs[segment.RunIndex];
            var ft = BuildSingleRunFormattedTextAt(
                run,
                segment.Text,
                flowDirection: para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);

            DrawTabLeaderAvalonia(
                dc,
                run,
                segment.Leader,
                previousEndX,
                segment.X,
                startY,
                para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            dc.DrawText(ft, new Point(segment.X, startY));
            previousEndX = segment.X + ft.Width;
        }
    }

    private static void DrawTabLeaderAvalonia(
        DrawingContext dc,
        ResolvedRun run,
        TabStopLeader leader,
        double startX,
        double endX,
        double y,
        FlowDirection flowDirection)
    {
        var glyph = TextLayoutPlanner.GetTabLeaderGlyph(leader);
        double width = endX - startX;
        if (glyph == '\0' || width < 1)
            return;

        var glyphText = BuildSingleRunFormattedTextAt(
            run,
            glyph.ToString(),
            flowDirection: flowDirection);
        double glyphWidth = glyphText.Width;
        if (glyphWidth <= 0)
            return;

        int count = (int)Math.Floor(width / glyphWidth);
        if (count <= 0)
            return;

        dc.DrawText(
            BuildSingleRunFormattedTextAt(
                run,
                new string(glyph, count),
                flowDirection: flowDirection),
            new Point(startX, y));
    }

    /// <summary>
    /// Draws plain runs with authored DrawingML baseline offsets while keeping
    /// one shared line baseline. Tabs, math, and text effects retain their
    /// existing renderer-specific owners.
    /// </summary>
    internal static void RenderParaWithBaseline(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX,
        double startY,
        double maxWidth)
    {
        var formatted = para.Runs
            .Select(run => BuildSingleRunFormattedTextAt(
                run,
                run.Text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                    para.RightToLeft,
                    run.Text))))
            .ToArray();
        var widths = para.Runs
            .Select((run, index) => MeasureBaselineTextWidth(
                run,
                run.Text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                formatted[index],
                ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                    para.RightToLeft,
                    run.Text))))
            .ToArray();
        double lineAscent = formatted.Length == 0 ? 0 : formatted.Max(ft => ft.Baseline);
        double baselineY = ComputeBaselineY(startY, lineAscent);
        double totalWidth = widths.Sum();
        if (maxWidth > 0 && totalWidth > maxWidth)
        {
            RenderWrappedBaseline(dc, para, startX, startY, maxWidth);
            return;
        }
        var placements = TextLayoutPlanner.PlanRunPlacements(
            para,
            startX,
            maxWidth,
            (run, rightToLeft) => MeasureBaselineTextWidth(
                run,
                run.Text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                flowDirection: ToFlowDirection(rightToLeft)));
        foreach (var placement in placements)
        {
            var run = para.Runs[placement.RunIndex];
            var ft = formatted[placement.RunIndex];
            double offsetDip = TextLayoutPlanner.BaselineOffsetToDip(run.BaselineOffset, run.FontSizePt);
            dc.DrawText(ft, new Point(placement.X, baselineY - ft.Baseline - offsetDip));
        }
    }

    private sealed class BaselineLine
    {
        public List<(ResolvedRun Run, FormattedText Text, double Width)> Fragments { get; } = new();
        public double Width { get; set; }
        public double Ascent { get; set; }
        public double Height { get; set; }
    }

    private static void RenderWrappedBaseline(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX,
        double startY,
        double maxWidth)
    {
        var lines = BuildBaselineLines(para, maxWidth);
        double lineY = startY;
        foreach (var line in lines)
        {
            if (line.Fragments.Count == 0)
            {
                lineY += Math.Max(1, line.Height);
                continue;
            }

            double baselineY = ComputeBaselineY(lineY, line.Ascent);
            var lineParagraph = new ResolvedParagraph
            {
                Runs = line.Fragments.Select(fragment => fragment.Run).ToArray(),
                Align = para.Align,
                RightToLeft = para.RightToLeft,
            };
            var placements = TextLayoutPlanner.PlanRunPlacements(
                lineParagraph,
                startX,
                maxWidth,
                (run, rightToLeft) => MeasureBaselineTextWidth(
                    run,
                    run.Text,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    flowDirection: ToFlowDirection(rightToLeft)));
            foreach (var placement in placements)
            {
                var fragment = line.Fragments[placement.RunIndex];
                double offsetDip = TextLayoutPlanner.BaselineOffsetToDip(
                    fragment.Run.BaselineOffset,
                    fragment.Run.FontSizePt);
                dc.DrawText(
                    fragment.Text,
                    new Point(placement.X, baselineY - fragment.Text.Baseline - offsetDip));
            }
            lineY += Math.Max(1, line.Height);
        }
    }

    private static List<BaselineLine> BuildBaselineLines(
        ResolvedParagraph para,
        double maxWidth)
    {
        var lines = new List<BaselineLine> { new() };

        void NewLine() => lines.Add(new BaselineLine());

        void AddMeasured(ResolvedRun run, string text)
        {
            var formatted = BuildSingleRunFormattedTextAt(
                run,
                text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                    para.RightToLeft,
                    text)));
            double width = MeasureBaselineTextWidth(
                run,
                text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                formatted,
                ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                    para.RightToLeft,
                    text)));
            var line = lines[^1];
            if (line.Fragments.Count > 0 && line.Width + width > maxWidth)
            {
                NewLine();
                line = lines[^1];
            }
            line.Fragments.Add((run, formatted, width));
            line.Width += width;
            line.Ascent = Math.Max(line.Ascent, formatted.Baseline);
            line.Height = Math.Max(line.Height, formatted.Height);
        }

        foreach (var run in para.Runs)
        {
            for (int index = 0; index < run.Text.Length;)
            {
                char first = run.Text[index];
                if (first is '\r' or '\n')
                {
                    if (first == '\r' && index + 1 < run.Text.Length && run.Text[index + 1] == '\n')
                        index++;
                    NewLine();
                    index++;
                    continue;
                }

                bool whitespace = char.IsWhiteSpace(first);
                int end = index + 1;
                while (end < run.Text.Length && run.Text[end] is not '\r' and not '\n' &&
                       char.IsWhiteSpace(run.Text[end]) == whitespace)
                    end++;

                string token = run.Text[index..end];
                var line = lines[^1];
                var tokenText = BuildSingleRunFormattedTextAt(
                    run,
                    token,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                        para.RightToLeft,
                        token)));
                double tokenWidth = MeasureBaselineTextWidth(
                    run,
                    token,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    tokenText,
                    ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                        para.RightToLeft,
                        token)));
                if (whitespace && (line.Fragments.Count == 0 || line.Width + tokenWidth > maxWidth))
                {
                    index = end;
                    continue;
                }

                if (!whitespace && tokenWidth > maxWidth)
                {
                    foreach (char character in token)
                        AddMeasured(run, character.ToString());
                }
                else
                {
                    AddMeasured(run, token);
                }
                index = end;
            }
        }

        return lines;
    }

    /// <summary>
    /// Avalonia's FormattedText.Width trims trailing whitespace. Baseline runs
    /// are laid out token-by-token, so that trim would remove the advance of a
    /// separator between two runs. Measure a trailing-space token with a
    /// sentinel glyph and subtract the sentinel's width to retain the authored
    /// whitespace advance.
    /// </summary>
    internal static double MeasureBaselineTextWidth(
        ResolvedRun run,
        string text,
        double fontScale,
        FormattedText? formatted = null,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        formatted ??= BuildSingleRunFormattedTextAt(run, text, fontScale, flowDirection);
        if (text.Length == 0 || !char.IsWhiteSpace(text[^1]))
            return formatted.Width;

        const string sentinel = "M";
        var withSentinel = BuildSingleRunFormattedTextAt(run, text + sentinel, fontScale, flowDirection);
        var sentinelWidth = BuildSingleRunFormattedTextAt(run, sentinel, fontScale, flowDirection).Width;
        return Math.Max(formatted.Width, withSentinel.Width - sentinelWidth);
    }

    // ── Theme 27: math rendering ────────────────────────────────────────────────

    /// <summary>
    /// Renders a paragraph that contains OMML math runs using the shared
    /// <see cref="MathBoxRenderPlanner"/> and Avalonia drawing primitives.
    ///
    /// HB4: math and text runs are BASELINE-aligned, not top-aligned (mirrors
    /// the WPF SlideCanvas for parity). We measure every run's ascent (math box
    /// Ascent, or FormattedText.Baseline for plain text) to find the line's
    /// shared baseline, then draw every run's top at (baselineY - runAscent).
    /// Marked internal (not private) so FreeP.App.Rendering.Avalonia.Tests can call it directly.
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
                var ft = BuildSingleRunFormattedTextAt(
                    run,
                    run.Text,
                    flowDirection: ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                        para.RightToLeft,
                        run.Text)));
                formatted[i] = ft;
                lineAscent = Math.Max(lineAscent, ft.Baseline);
            }
        }

        double baselineY = ComputeBaselineY(startY, lineAscent);

        // Pass 2: draw each run with its top placed so its ascent lands on baselineY.
        var placements = TextLayoutPlanner.PlanRunPlacements(
            para,
            startX,
            0,
            (run, rightToLeft) => run.IsMathRun && run.MathLayout is not null
                ? run.MathLayout.Metrics.Width
                : BuildSingleRunFormattedTextAt(
                    run,
                    run.Text,
                    flowDirection: ToFlowDirection(rightToLeft)).Width);
        foreach (var placement in placements)
        {
            var run = para.Runs[placement.RunIndex];
            if (run.IsMathRun && run.MathLayout is not null)
            {
                double runY = ComputeRunTopY(baselineY, run.MathLayout.Metrics.Ascent);
                var mathOps = MathBoxRenderPlanner.Plan(
                    run.MathLayout, placement.X, runY, run.Color, run.FontFamily);
                foreach (var op in mathOps)
                    DrawMathOpAvalonia(dc, op);
            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                var ft = formatted[placement.RunIndex]!;
                double runY = ComputeRunTopY(baselineY, ft.Baseline);
                dc.DrawText(ft, new Point(placement.X, runY));
            }
        }
    }

    /// <summary>
    /// HB4 pure helper: the shared line baseline (in slide-space DIP) given the
    /// paragraph's top Y and the max ascent across all its runs (text or math).
    /// Exposed internal so tests can validate the baseline math without needing
    /// a live DrawingContext. Mirrors FreeP.App.Rendering.Wpf for parity.
    /// </summary>
    internal static double ComputeBaselineY(double startY, double lineAscent) => startY + lineAscent;

    /// <summary>
    /// HB4 pure helper: the top Y at which a run with the given ascent must be
    /// drawn so its own baseline lands exactly on <paramref name="baselineY"/>.
    /// </summary>
    internal static double ComputeRunTopY(double baselineY, double runAscent) => baselineY - runAscent;

    /// <summary>
    /// Draws a single <see cref="MathDrawOp"/> as Avalonia primitives.
    /// ALL math layout decisions come from the shared MathBoxRenderPlanner.
    /// </summary>
    private static void DrawMathOpAvalonia(DrawingContext dc, MathDrawOp op)
    {
        switch (op)
        {
            case MathDrawOp.DrawGlyph g:
            {
                var typeface = new Typeface(
                    g.FontFamily,
                    g.IsItalic ? FontStyle.Italic : FontStyle.Normal,
                    g.IsBold ? FontWeight.Bold : FontWeight.Normal, FontStretch.Normal);
                double emPx = g.FontSizePt * (96.0 / 72.0);
                var brush = new SolidColorBrush(Color.FromRgb(g.Color.R, g.Color.G, g.Color.B));
                var ft = new FormattedText(g.Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, emPx, brush);
                dc.DrawText(ft, new Point(g.X, g.Y));
                break;
            }

            case MathDrawOp.DrawHRule hr:
            {
                var pen = new Pen(
                    new SolidColorBrush(Color.FromRgb(hr.Color.R, hr.Color.G, hr.Color.B)),
                    hr.Thickness);
                dc.DrawLine(pen, new Point(hr.X, hr.Y), new Point(hr.X + hr.Width, hr.Y));
                break;
            }

            case MathDrawOp.DrawLine line:
            {
                var pen = new Pen(
                    new SolidColorBrush(Color.FromRgb(line.Color.R, line.Color.G, line.Color.B)),
                    line.Thickness);
                dc.DrawLine(pen, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                break;
            }

            case MathDrawOp.DrawBracket br:
            {
                double naturalEm = br.ScaledHeight * 0.85;
                var fontFamily = br.FontFamily.Length > 0 ? br.FontFamily : "Cambria Math";
                var typeface = new Typeface(fontFamily,
                    FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
                var brush = new SolidColorBrush(Color.FromRgb(br.Color.R, br.Color.G, br.Color.B));
                var ft = new FormattedText(br.Character,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, naturalEm, brush);
                dc.DrawText(ft, new Point(br.X, br.Y));
                break;
            }

            case MathDrawOp.DrawRadical rad:
            {
                var pen = new Pen(
                    new SolidColorBrush(Color.FromRgb(rad.Color.R, rad.Color.G, rad.Color.B)),
                    rad.OverlineThickness);

                double x0   = rad.X;
                double x1   = rad.X + rad.SignWidth * 0.25;
                double x2   = rad.X + rad.SignWidth;
                double xOvEnd = x2 + rad.OverlineWidth;
                double yTop  = rad.Y + rad.OverlineThickness / 2.0;
                double yFoot = rad.Y + rad.Height * 0.85;
                double yBase = rad.Y + rad.OverlineThickness;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x0, yTop + (yFoot - yTop) * 0.4), isFilled: false);
                    ctx.LineTo(new Point(x1, yFoot));
                    ctx.LineTo(new Point(x2, yBase));
                    ctx.LineTo(new Point(xOvEnd, yBase));
                    ctx.EndFigure(isClosed: false);
                }
                dc.DrawGeometry(null, pen, geo);
                break;
            }
        }
    }

    private static FormattedText BuildSingleRunFormattedTextAt(
        ResolvedRun run,
        string text,
        double fontSizeScale = 1.0,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        string txt = text.Length == 0 ? " " : text;
        var typeface = new Typeface(
            run.FontFamily,
            run.Italic ? FontStyle.Italic : FontStyle.Normal,
            run.Bold   ? FontWeight.Bold  : FontWeight.Normal,
            FontStretch.Normal);
        double emPx = run.FontSizePt * fontSizeScale * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        var ft = new FormattedText(txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            flowDirection,
            typeface, emPx, brush);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, txt.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, txt.Length);
        return ft;
    }

    private static FlowDirection ToFlowDirection(bool rightToLeft) =>
        rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static TextGlyphMeasure MeasureStackedGlyphAvalonia(ResolvedRun run, string text)
    {
        var ft = BuildSingleRunFormattedTextAt(run, text);
        return new TextGlyphMeasure(ft.Width, ft.Height);
    }

    private static void DrawStackedGlyphAvalonia(
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
            RenderParaWithEffects(dc, glyphParagraph, glyph.X, glyph.Y, bounds, text);
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
            BaselineOffset = run.BaselineOffset,
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

    private static FormattedText BuildFormattedText(
        ResolvedParagraph para,
        double maxWidth,
        bool wrap,
        TextAutoFitKind autoFitKind = TextAutoFitKind.None)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Runs) sb.Append(run.Text);
        string txt = sb.Length == 0 ? " " : sb.ToString();

        var firstRun = para.Runs[0];
        double fontScale = ResolvePowerPointFontScale(firstRun.FontFamily);
        var typeface = new Typeface(
            ResolvePowerPointFontFamily(firstRun.FontFamily),
            firstRun.Italic ? FontStyle.Italic : FontStyle.Normal,
            firstRun.Bold   ? FontWeight.Bold  : FontWeight.Normal,
            FontStretch.Normal);

        double emSizePx = firstRun.FontSizePt * (96.0 / 72.0) * fontScale;
        var brush = new SolidColorBrush(
            Color.FromRgb(firstRun.Color.R, firstRun.Color.G, firstRun.Color.B));

        var ft = new FormattedText(
            txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush);

        // PowerPoint's default paragraph leading is slightly tighter than Avalonia's automatic line height.
        ft.LineHeight = ResolvePowerPointLineHeight(
            firstRun.FontSizePt * (96.0 / 72.0),
            autoFitKind);

        if (wrap && maxWidth > 0)
            ft.MaxTextWidth = maxWidth;

        ft.TextAlignment = para.Align switch
        {
            TextAlign.Center                         => TextAlignment.Center,
            TextAlign.Right                          => TextAlignment.Right,
            TextAlign.Justify or TextAlign.Distributed => TextAlignment.Justify,
            _                                        => TextAlignment.Left
        };

        int pos = 0;
        foreach (var run in para.Runs)
        {
            int len = run.Text.Length;
            if (len == 0) continue;
            if (run.Bold)         ft.SetFontWeight(FontWeight.Bold, pos, len);
            if (run.Italic)       ft.SetFontStyle(FontStyle.Italic, pos, len);
            if (run.Underline)    ft.SetTextDecorations(TextDecorations.Underline, pos, len);
            if (run.Strikethrough)ft.SetTextDecorations(TextDecorations.Strikethrough, pos, len);
            ft.SetFontFamily(ResolvePowerPointFontFamily(run.FontFamily), pos, len);
            double runFontScale = ResolvePowerPointFontScale(run.FontFamily);
            ft.SetFontSize(run.FontSizePt * (96.0 / 72.0) * runFontScale, pos, len);
            ft.SetForegroundBrush(
                new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B)),
                pos, len);
            pos += len;
        }
        return ft;
    }

    // The host does not provide the Office theme's Aptos families. Arial is the
    // installed sans-serif fallback closest to the Office text silhouette.
    internal static string ResolvePowerPointFontFamily(string fontFamily) =>
        string.Equals(fontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
            ? "Arial"
            : fontFamily;

    internal static double ResolvePowerPointFontScale(string fontFamily) =>
        string.Equals(fontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
            ? 0.95
            : 1.0;

    internal static double ResolvePowerPointLineHeight(double fontSizePx) =>
        fontSizePx * PowerPointDefaultLineSpacingFactor;

    internal static double ResolvePowerPointLineHeight(
        double fontSizePx,
        TextAutoFitKind autoFitKind) =>
        fontSizePx * (autoFitKind == TextAutoFitKind.None
            ? PowerPointFixedTextLineSpacingFactor
            : PowerPointDefaultLineSpacingFactor);

    // ── Text-effects geometry helpers (Wave 16A) ──────────────────────────────

    private static IBrush MakeFillBrushForText(ResolvedFill fill)
    {
        return fill switch
        {
            ResolvedFill.Solid s =>
                new SolidColorBrush(Color.FromArgb(s.Alpha, s.Color.R, s.Color.G, s.Color.B)),
            ResolvedFill.Gradient g when g.Kind == GradientKind.Radial =>
                new RadialGradientBrush
                {
                    Center         = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops  = BuildGradientStops(g),
                },
            ResolvedFill.Gradient g =>
                new LinearGradientBrush
                {
                    StartPoint    = new RelativePoint(
                        Math.Cos(g.AngleDegrees * Math.PI / 180) >= 0 ? 0 : 1,
                        Math.Sin(g.AngleDegrees * Math.PI / 180) >= 0 ? 0 : 1,
                        RelativeUnit.Relative),
                    EndPoint      = new RelativePoint(
                        Math.Cos(g.AngleDegrees * Math.PI / 180) >= 0 ? 1 : 0,
                        Math.Sin(g.AngleDegrees * Math.PI / 180) >= 0 ? 1 : 0,
                        RelativeUnit.Relative),
                    GradientStops = BuildGradientStops(g),
                },
            _ => Brushes.Black
        };
    }

    private static FormattedText BuildSingleRunFormattedText(
        ResolvedRun run,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        string txt = run.Text.Length == 0 ? " " : run.Text;
        var typeface = new Typeface(
            run.FontFamily,
            run.Italic ? FontStyle.Italic : FontStyle.Normal,
            run.Bold   ? FontWeight.Bold  : FontWeight.Normal,
            FontStretch.Normal);
        double emPx = run.FontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        var ft = new FormattedText(txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            flowDirection,
            typeface, emPx, brush);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, txt.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, txt.Length);
        return ft;
    }

    private static void RenderParaWithEffects(
        DrawingContext dc,
        ResolvedParagraph para,
        double x, double y,
        LayoutRect shapeBounds,
        ResolvedTextLayout text)
    {
        var placements = TextLayoutPlanner.PlanRunPlacements(
            para,
            x,
            0,
            (run, rightToLeft) => BuildSingleRunFormattedText(
                run,
                ToFlowDirection(rightToLeft)).Width);
        foreach (var placement in placements)
        {
            var run = para.Runs[placement.RunIndex];
            double drawX = placement.X;

            var runFt = BuildSingleRunFormattedText(
                run,
                ToFlowDirection(placement.RightToLeft));
            double progress = shapeBounds.Width > 0 ? (drawX - shapeBounds.X) / shapeBounds.Width : 0;
            var plan = TextRunEffectRenderPlanner.Plan(
                run,
                new LayoutRect(drawX, y, runFt.Width, runFt.Height),
                progress,
                shapeBounds,
                text);
            var geo = runFt.BuildGeometry(new Point(plan.GlyphBoundsDip.X, plan.GlyphBoundsDip.Y));
            if (geo is null) continue;

            using IDisposable? warpScope = plan.WarpTransform is { HasAffineTransform: true } warp
                ? dc.PushTransform(BuildWordArtWarpMatrix(warp, plan.GlyphBoundsDip))
                : null;

            foreach (var pass in plan.Passes)
            {
                switch (pass)
                {
                    case TextRunEffectPass.Shadow shadow:
                    {
                        var shadowBrush = new SolidColorBrush(
                            Color.FromArgb(shadow.Alpha, shadow.Color.R, shadow.Color.G, shadow.Color.B));
                        using var sScope = dc.PushTransform(Matrix.CreateTranslation(shadow.OffsetX, shadow.OffsetY));
                        dc.DrawGeometry(shadowBrush, null, geo);
                        break;
                    }
                    case TextRunEffectPass.Reflection reflection:
                    {
                        using var transformScope = dc.PushTransform(BuildTextReflectionMatrix(reflection, plan.GlyphBoundsDip));
                        double reflectionScale = Math.Abs(reflection.ScaleY) < 0.001 ? 1.0 : Math.Abs(reflection.ScaleY);
                        double reflectionY = reflection.ScaleY < 0
                            ? plan.GlyphBoundsDip.Y + plan.GlyphBoundsDip.Height + reflection.OffsetY
                            : plan.GlyphBoundsDip.Y + reflection.OffsetY;
                        double reflectionEndPos = Math.Clamp(reflection.EndPos, 0.0, 1.0);
                        var reflectionStops = new GradientStops
                        {
                            new AvGradientStop(Colors.White, 0),
                            new AvGradientStop(
                                Color.FromArgb(0, 255, 255, 255),
                                Math.Max(0.001, reflectionEndPos)),
                        };
                        if (reflectionEndPos < 0.999)
                            reflectionStops.Add(new AvGradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                        var reflectionMask = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                            GradientStops = reflectionStops
                        };
                        using var maskScope = dc.PushOpacityMask(
                            reflectionMask,
                            new Rect(
                                plan.GlyphBoundsDip.X + reflection.OffsetX,
                                reflectionY,
                                plan.GlyphBoundsDip.Width,
                                plan.GlyphBoundsDip.Height * reflectionScale));
                        using var opacityScope = dc.PushOpacity(reflection.Alpha / 255.0);
                        dc.DrawGeometry(MakeFillBrushForText(reflection.FillBrush), null, geo);
                        break;
                    }
                    case TextRunEffectPass.Glow glow:
                    {
                        var glowBrush = new SolidColorBrush(
                            Color.FromArgb(glow.Alpha, glow.Color.R, glow.Color.G, glow.Color.B));
                        var glowPen = new Pen(glowBrush, glow.StrokeWidthDip);
                        dc.DrawGeometry(null, glowPen, geo);
                        break;
                    }
                    case TextRunEffectPass.SoftEdge softEdge:
                    {
                        using var opacityScope = dc.PushOpacity(softEdge.Alpha / 255.0);
                        using var transformScope = dc.PushTransform(Matrix.CreateTranslation(softEdge.OffsetX, softEdge.OffsetY));
                        dc.DrawGeometry(MakeFillBrushForText(softEdge.FillBrush), null, geo);
                        break;
                    }
                    case TextRunEffectPass.Fill fill:
                        dc.DrawGeometry(MakeFillBrushForText(fill.FillBrush), null, geo);
                        break;
                    case TextRunEffectPass.MaterialHighlight material:
                        dc.DrawGeometry(MakeFillBrushForText(material.FillBrush), null, geo);
                        break;
                    case TextRunEffectPass.Outline outline:
                        dc.DrawGeometry(null, MakePen(outline.OutlinePen), geo);
                        break;
                }
            }
        }
    }

    // ── Brush / Pen factories ─────────────────────────────────────────────────

    private static Matrix BuildWordArtWarpMatrix(
        WordArtWarpTransform warp,
        LayoutRect glyphBounds)
    {
        double cx = glyphBounds.X + glyphBounds.Width / 2.0;
        double cy = glyphBounds.Y + glyphBounds.Height / 2.0;
        return Matrix.CreateTranslation(-cx, -cy)
            * Matrix.CreateScale(1.0, warp.ScaleY)
            * Matrix.CreateRotation(warp.RotationDeg * Math.PI / 180.0)
            * Matrix.CreateTranslation(cx, cy);
    }

    private static Matrix BuildTextReflectionMatrix(
        TextRunEffectPass.Reflection reflection,
        LayoutRect glyphBounds)
    {
        double cx = glyphBounds.X + glyphBounds.Width / 2.0;
        double pivotY = glyphBounds.Y + glyphBounds.Height;
        return Matrix.CreateTranslation(-cx, -pivotY)
            * Matrix.CreateScale(1.0, reflection.ScaleY)
            * Matrix.CreateTranslation(cx + reflection.OffsetX, pivotY + reflection.OffsetY);
    }

    private static IBrush? MakeBrush(ResolvedFill fill, LayoutRect bounds, bool easeGradientStops = false) => fill switch
    {
        ResolvedFill.None      => null,
        ResolvedFill.Solid s   => new SolidColorBrush(Color.FromArgb(s.Alpha, s.Color.R, s.Color.G, s.Color.B)),
        ResolvedFill.Gradient g when g.Kind == GradientKind.Radial => MakeRadialGradientBrush(g),
        ResolvedFill.Gradient g  => MakeLinearGradientBrush(g, easeGradientStops),
        ResolvedFill.Picture  p  => MakePictureBrush(p),
        ResolvedFill.PatternFill pat => MakePatternBrush(pat),
        _                      => null
    };

    private static GradientStops BuildGradientStops(ResolvedFill.Gradient g, bool easePositions = false)
    {
        var stops = new GradientStops();
        for (int index = 0; index < g.Stops.Count; index++)
        {
            var start = g.Stops[index];
            stops.Add(new AvGradientStop(
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
                stops.Add(new AvGradientStop(
                    Color.FromArgb(alpha, color.R, color.G, color.B),
                    start.Position + (end.Position - start.Position) * fraction));
            }
        }
        return stops;
    }

    private static IBrush MakeLinearGradientBrush(ResolvedFill.Gradient g, bool easePositions = false)
    {
        // OOXML a:lin ang convention (stored as AngleDegrees = ang/60000):
        //   0°  = flows east  (left → right):   Start=(0, 0.5), End=(1, 0.5)
        //  90°  = flows south (top  → bottom):  Start=(0.5, 0), End=(0.5, 1)
        // 180°  = flows west  (right → left):   Start=(1, 0.5), End=(0, 0.5)
        // 270°  = flows north (bottom → top):   Start=(0.5, 1), End=(0.5, 0)
        // Direction vector in screen coords (x right, y down): d = (cos θ, sin θ).
        double angleRad = g.AngleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(angleRad);
        double dy = Math.Sin(angleRad);
        return new LinearGradientBrush
        {
            StartPoint    = new RelativePoint(0.5 - 0.5 * dx, 0.5 - 0.5 * dy, RelativeUnit.Relative),
            EndPoint      = new RelativePoint(0.5 + 0.5 * dx, 0.5 + 0.5 * dy, RelativeUnit.Relative),
            GradientStops = BuildGradientStops(g, easePositions)
        };
    }

    private static IBrush MakeRadialGradientBrush(ResolvedFill.Gradient g) =>
        new RadialGradientBrush
        {
            Center         = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops  = BuildGradientStops(g)
        };

    private static IBrush MakePictureBrush(ResolvedFill.Picture p)
    {
        try
        {
            using var ms = new MemoryStream(p.ImageBytes);
            var bmp = new Bitmap(ms);
            return new ImageBrush(bmp)
            {
                Stretch  = p.Tile ? Stretch.None  : Stretch.Fill,
                TileMode = p.Tile ? TileMode.Tile : TileMode.None
            };
        }
        catch { return Brushes.Transparent; }
    }

    /// <summary>
    /// Builds a tiled pattern fill by rendering a 6×6 pixel hatch tile into a
    /// <see cref="WriteableBitmap"/> and using it as a tiled <see cref="ImageBrush"/>.
    /// This avoids DrawingBrush (WPF-only) and unsafe pixel pointers.
    /// </summary>
    private static IBrush MakePatternBrush(ResolvedFill.PatternFill pat)
    {
        var fg = Color.FromRgb(pat.ForegroundColor.R, pat.ForegroundColor.G, pat.ForegroundColor.B);
        var bg = Color.FromRgb(pat.BackgroundColor.R, pat.BackgroundColor.G, pat.BackgroundColor.B);

        int tileSize = pat.Preset == "cross" ? 8 : 6;
        int S = tileSize;
        var pixels = new byte[S * S * 4]; // BGRA layout

        void FillAll(Color c)
        {
            for (int i = 0; i < S * S; i++)
            {
                int idx = i * 4;
                pixels[idx    ] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = c.A;
            }
        }

        void SetPixel(int x, int y, Color c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            int idx = (y * S + x) * 4;
            pixels[idx    ] = c.B;
            pixels[idx + 1] = c.G;
            pixels[idx + 2] = c.R;
            pixels[idx + 3] = c.A;
        }

        FillAll(bg);

        switch (pat.Preset)
        {
            case "horzStripe" or "ltHorz" or "dashHorz":
                for (int x = 0; x < S; x++) { SetPixel(x, 2, fg); SetPixel(x, 3, fg); }
                break;
            case "vertStripe" or "ltVert" or "dashVert":
                for (int y = 0; y < S; y++) { SetPixel(2, y, fg); SetPixel(3, y, fg); }
                break;
            case "pct50":
                for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                        if ((x + y) % 2 == 0) SetPixel(x, y, fg);
                break;
            case "pct0":
                FillAll(bg);
                break;
            case "pct100":
                FillAll(fg);
                break;
            case "pct25" or "pct30" or "pct5" or "pct10" or "pct20":
                for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                        if ((x * 2 + y * 3) % 4 == 0) SetPixel(x, y, fg);
                break;
            case "pct40":
                for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                        if ((x + y) % 2 == 0) SetPixel(x, y, fg);
                break;
            case "pct75" or "pct60" or "pct90":
                FillAll(fg);
                for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                        if ((x + y) % 3 == 0) SetPixel(x, y, bg);
                break;
            case "diagStripe" or "ltDnDiag" or "dnDiag":
                for (int i = 0; i < S; i++) SetPixel(i, i, fg);
                break;
            case "upDiag" or "ltUpDiag":
                for (int i = 0; i < S; i++) SetPixel(i, S - 1 - i, fg);
                break;
            case "cross":
                for (int x = 0; x < S; x++) SetPixel(x, 0, fg);
                for (int y = 0; y < S; y++) SetPixel(0, y, fg);
                break;
            case "smGrid":
                for (int x = 0; x < S; x++) SetPixel(x, 2, fg);
                for (int y = 0; y < S; y++) SetPixel(2, y, fg);
                break;
            case "diagCross" or "smConfetti" or "wave" or "trellis":
                for (int i = 0; i < S; i++) { SetPixel(i, i, fg); SetPixel(i, S - 1 - i, fg); }
                break;
            default:
                FillAll(fg);
                break;
        }

        var wb = new WriteableBitmap(
            new PixelSize(S, S),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buf = wb.Lock())
            Marshal.Copy(pixels, 0, buf.Address, pixels.Length);

        return new ImageBrush(wb)
        {
            TileMode        = TileMode.Tile,
            Stretch         = Stretch.None,
            SourceRect      = new RelativeRect(new Size(S, S), RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(new Size(S, S), RelativeUnit.Absolute),
        };
    }

    private static Pen? MakePen(ResolvedOutline outline)
    {
        if (outline is ResolvedOutline.Visible vis)
        {
            var brush = new SolidColorBrush(Color.FromArgb(vis.Alpha, vis.Color.R, vis.Color.G, vis.Color.B));
            return new Pen(brush, vis.WidthDip)
            {
                DashStyle = vis.Dash switch
                {
                    OutlineDash.Dash           => DashStyle.Dash,
                    OutlineDash.Dot            => DashStyle.Dot,
                    OutlineDash.DashDot        => DashStyle.DashDot,
                    OutlineDash.LongDash       => new DashStyle([8.0, 3.0], 0),
                    OutlineDash.LongDashDot    => new DashStyle([8.0, 3.0, 1.0, 3.0], 0),
                    OutlineDash.LongDashDotDot => new DashStyle([8.0, 3.0, 1.0, 3.0, 1.0, 3.0], 0),
                    OutlineDash.SystemDash     => DashStyle.Dash,
                    OutlineDash.SystemDot      => DashStyle.Dot,
                    OutlineDash.SystemDashDot  => DashStyle.DashDot,
                    _                          => null
                }
            };
        }

        // Wave 22B: gradient outline — build a gradient brush for the stroke.
        if (outline is ResolvedOutline.Gradient grad)
        {
            IBrush gradBrush = grad.Fill.Kind == GradientKind.Radial
                ? MakeRadialGradientBrush(grad.Fill)
                : MakeLinearGradientBrush(grad.Fill);
            return new Pen(gradBrush, grad.WidthDip)
            {
                DashStyle = grad.Dash switch
                {
                    OutlineDash.Dash           => DashStyle.Dash,
                    OutlineDash.Dot            => DashStyle.Dot,
                    OutlineDash.DashDot        => DashStyle.DashDot,
                    OutlineDash.LongDash       => new DashStyle([8.0, 3.0], 0),
                    OutlineDash.LongDashDot    => new DashStyle([8.0, 3.0, 1.0, 3.0], 0),
                    OutlineDash.LongDashDotDot => new DashStyle([8.0, 3.0, 1.0, 3.0, 1.0, 3.0], 0),
                    OutlineDash.SystemDash     => DashStyle.Dash,
                    OutlineDash.SystemDot      => DashStyle.Dot,
                    OutlineDash.SystemDashDot  => DashStyle.DashDot,
                    _                          => null
                }
            };
        }

        return null;
    }

    // ── Composition helper ───────────────────────────────────────────────────

    private void EnsureOps()
    {
        if (_cachedOps is not null) return;
        if (_presentation is null || _slide is null)
        {
            _slideWidthDip  = 0;
            _slideHeightDip = 0;
            _cachedOps      = Array.Empty<DrawOp>();
            return;
        }
        _slideWidthDip  = _presentation.SlideSizeCxEmu / 9525.0;
        _slideHeightDip = _presentation.SlideSizeCyEmu / 9525.0;
        _cachedOps      = SlideCompositor.Compose(
            _presentation,
            _slide,
            _slideIndex,
            RenderSlideBackground);
    }
}
