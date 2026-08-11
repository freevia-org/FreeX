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
public sealed partial class SlideCanvas : Control
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

    /// <summary>Whether print-only comment callouts are painted over the slide.</summary>
    public bool RenderPrintMarkup { get; set; }

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

        if (_cachedOps is null || _slideWidthDip <= 0) return;

        double renderW = Bounds.Width;
        double renderH = Bounds.Height;
        if (renderW <= 0 || renderH <= 0) return;

        // Expose the slide→screen transform so the editing layer can use it.
        CurrentTransform = ComputeViewTransform(renderW, renderH, _slideWidthDip, _slideHeightDip);

        var matrix = Matrix.CreateScale(CurrentTransform.Scale, CurrentTransform.Scale)
            * Matrix.CreateTranslation(CurrentTransform.OffsetX, CurrentTransform.OffsetY);
        using var _ = context.PushTransform(matrix);

        foreach (var command in SlideRenderExecutionPlanner.Plan(
                     _cachedOps,
                     _liveTransformPreviewOps,
                     SuppressedShapeIds,
                     ActiveTextEditShapeId))
        {
            RenderCommand(context, command);
        }

        if (RenderPrintMarkup && _presentation is not null && _slide is not null)
            RenderPrintCommentCallouts(context, _presentation, _slide);
    }

    private static void RenderPrintCommentCallouts(DrawingContext dc, Presentation presentation, Slide slide)
    {
        var fill = new SolidColorBrush(Color.FromRgb(255, 249, 196));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(192, 160, 0)), 1);
        var marker = new SolidColorBrush(Color.FromRgb(220, 40, 40));

        foreach (var callout in SlidePrintMarkupPlanner.BuildCommentCallouts(presentation, slide))
        {
            var card = new Rect(callout.CardX, callout.CardY, callout.CardWidth, callout.CardHeight);
            dc.DrawRectangle(fill, border, card);
            dc.DrawEllipse(marker, null, new Point(callout.AnchorX, callout.AnchorY), 3, 3);
            DrawChartLabel(dc, callout.Author, new Rect(card.X + 6, card.Y + 3, card.Width - 12, 9),
                isBold: true, fontSize: 8, align: TextAlignment.Left);
            DrawChartLabel(dc, callout.Body, new Rect(card.X + 6, card.Y + 13, card.Width - 12, 11),
                isBold: false, fontSize: 7, align: TextAlignment.Left);
        }
    }

    private SlideTransformCore ComputeViewTransform(
        double renderW,
        double renderH,
        double slideWidthDip,
        double slideHeightDip) =>
        PresentationViewZoomPlanner.PlanStageTransform(
            renderW,
            renderH,
            slideWidthDip,
            slideHeightDip,
            _viewZoomState);

    private static void RenderCommand(DrawingContext dc, SlideRenderExecutionCommand command)
    {
        switch (command.Operation)
        {
            case DrawOp.Background bg:
                RenderBackground(dc, bg);
                break;
            case DrawOp.Shape shape:
                RenderShape(dc, shape, command.SuppressShapeText);
                break;
            case DrawOp.Picture pic:
                RenderPicture(dc, pic);
                break;
            case DrawOp.Table table:
                RenderTableWithTransform(dc, table);
                break;
            case DrawOp.Chart chartOp:
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

        var sourceBounds = shape.BoundsDip;
        var bounds = ResolveShapeAutoFitBounds(shape);
        bool grewForShapeAutoFit = bounds.Height > sourceBounds.Height + 0.5;
        var renderTransform = grewForShapeAutoFit
            ? ShapeAffineTransform.Identity
            : ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        bool hasTransform = !renderTransform.IsIdentity;

        IDisposable? transformScope = null;
        if (hasTransform)
            transformScope = dc.PushTransform(ToAvaloniaMatrix(renderTransform));

        IDisposable? autoFitGeometryScope = null;
        if (grewForShapeAutoFit && sourceBounds.Height > 0.001)
        {
            double scaleY = bounds.Height / sourceBounds.Height;
            autoFitGeometryScope = dc.PushTransform(new Matrix(
                1, 0, 0, scaleY, 0, bounds.Y - scaleY * sourceBounds.Y));
        }

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

        autoFitGeometryScope?.Dispose();

        if (!suppressText && shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        transformScope?.Dispose();
    }

    private static LayoutRect ResolveShapeAutoFitBounds(DrawOp.Shape shape)
        => ShapeAutoFitRenderPlanner.Plan(
            shape,
            request => BuildFormattedText(
                request.Paragraph,
                request.MaximumWidthDip,
                request.Wrap,
                request.AutoFitKind).Height);

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

    private static void DrawChartMarker(DrawingContext dc, ChartMarkerRenderPlan marker)
    {
        foreach (var primitive in marker.Primitives)
        {
            switch (primitive)
            {
                case ChartMarkerRenderPrimitive.Ellipse ellipse:
                    dc.DrawEllipse(
                        ellipse.Fill is { } ellipseFill ? ToBrush(ellipseFill) : null,
                        ellipse.Stroke is { } ellipseStroke ? ToPen(ellipseStroke) : null,
                        ToPoint(ellipse.Center),
                        ellipse.RadiusX,
                        ellipse.RadiusY);
                    break;
                case ChartMarkerRenderPrimitive.Rectangle rectangle:
                    dc.DrawRectangle(
                        rectangle.Fill is { } rectangleFill ? ToBrush(rectangleFill) : null,
                        rectangle.Stroke is { } rectangleStroke ? ToPen(rectangleStroke) : null,
                        ToRect(rectangle.Bounds));
                    break;
                case ChartMarkerRenderPrimitive.Path path:
                    dc.DrawGeometry(
                        path.Geometry.Fill is { } pathFill ? ToBrush(pathFill) : null,
                        path.Stroke is { } pathStroke ? ToPen(pathStroke) : null,
                        ToMarkerGeometry(path.Geometry));
                    break;
                case ChartMarkerRenderPrimitive.Line line:
                    dc.DrawLine(ToPen(line.Stroke), ToPoint(line.Start), ToPoint(line.End));
                    break;
            }
        }
    }

    private static StreamGeometry ToMarkerGeometry(ChartPathPrimitive path)
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
        catch (Exception ex)
        {
            // Skip undecodable images rather than crashing the renderer, but report the loss through
            // the ambient diagnostics sink (see SlideImageRenderDiagnostics) so an export command that
            // installed a collector can surface it instead of the slide looking silently incomplete.
            SlideImageRenderDiagnostics.ReportUndecodableImage(pic.ShapeId, ex.Message);
            return;
        }

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

        IDisposable? pictureTransformScope = null;
        IDisposable? alphaScope = null;

        var pictureTransform = ShapeTransformPlanner.PlanPictureTransform(pic);
        if (!pictureTransform.IsIdentity)
            pictureTransformScope = dc.PushTransform(ToAvaloniaMatrix(pictureTransform));

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
                dc.DrawRectangle(
                    shadowBrush,
                    null,
                    shadowDest,
                    plan.FrameCornerRadiusDip,
                    plan.FrameCornerRadiusDip);
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

        if (plan.HasReflection)
        {
            foreach (var blurPass in plan.ReflectionBlurPasses)
            {
                var reflectionDest = new Rect(
                    dest.X + blurPass.OffsetXDip,
                    dest.Y + blurPass.OffsetYDip,
                    dest.Width,
                    dest.Height);
                var reflectionStops = new GradientStops
                {
                    new AvGradientStop(
                        Color.FromArgb(plan.ReflectionAlpha, 255, 255, 255), 0),
                    new AvGradientStop(
                        Color.FromArgb(0, 255, 255, 255),
                        plan.ReflectionEndPos),
                };
                if (plan.ReflectionNeedsTerminalTransparentStop)
                    reflectionStops.Add(new AvGradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                var reflectionMask = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                    GradientStops = reflectionStops,
                };
                using var transformScope = dc.PushTransform(
                    Matrix.CreateTranslation(-(dest.Left + dest.Width / 2), -plan.ReflectionPivotY)
                    * Matrix.CreateScale(1, plan.ReflectionScaleY)
                    * Matrix.CreateTranslation(dest.Left + dest.Width / 2, plan.ReflectionPivotY));
                using var maskScope = dc.PushOpacityMask(
                    reflectionMask,
                    new Rect(reflectionDest.Left, reflectionDest.Bottom + plan.ReflectionDistDip,
                        reflectionDest.Width, reflectionDest.Height * Math.Abs(plan.ReflectionScaleY)));
                using var opacityScope = dc.PushOpacity(blurPass.Opacity);
                dc.DrawImage(renderBitmap, reflectionDest);
            }
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
                clipGeom = new RectangleGeometry
                {
                    Rect = dest,
                    RadiusX = plan.FrameCornerRadiusDip,
                    RadiusY = plan.FrameCornerRadiusDip,
                };
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
        if (pic.Outline is not ResolvedOutline.None)
        {
            var pen = MakePen(pic.Outline);
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
                    dc.DrawRectangle(
                        null,
                        pen,
                        dest,
                        plan.FrameCornerRadiusDip,
                        plan.FrameCornerRadiusDip);
                }
                else
                    dc.DrawRectangle(null, pen, dest);
            }
        }

        if (plan.MediaPlayGlyph is { } playGlyph)
            DrawPlayButtonOverlay(dc, playGlyph);

        pictureTransformScope?.Dispose();
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

    private static void DrawPlayButtonOverlay(
        DrawingContext dc,
        PictureMediaPlayGlyphPlan glyph)
    {
        var circleBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0));
        dc.DrawEllipse(
            circleBrush,
            null,
            new Point(glyph.CenterDip.X, glyph.CenterDip.Y),
            glyph.RadiusDip,
            glyph.RadiusDip);

        var triGeo = new StreamGeometry();
        using (var ctx = triGeo.Open())
        {
            ctx.BeginFigure(
                new Point(glyph.TriangleDip[0].X, glyph.TriangleDip[0].Y),
                isFilled: true);
            ctx.LineTo(new Point(glyph.TriangleDip[1].X, glyph.TriangleDip[1].Y));
            ctx.LineTo(new Point(glyph.TriangleDip[2].X, glyph.TriangleDip[2].Y));
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
        DrawCellBorder(dc, cell.BorderDiagonalDown,
            new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom));
        DrawCellBorder(dc, cell.BorderDiagonalUp,
            new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Top));

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
            var lines = TextLayoutPlanner.SplitColumnText(
                run.Text,
                layout.ColumnWidthDip,
                text.Wrap,
                candidate => BuildFormattedText(
                    TextLayoutPlanner.CloneParagraphWithText(paragraph, run, candidate),
                    0,
                    false).Width);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var fragment = TextLayoutPlanner.CloneParagraphWithText(paragraph, run, lines[lineIndex]);
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
                para.SpaceAfterPt,
                paragraphLineSpacingScale: TextLayoutPlanner.ResolveParagraphLineSpacingScale(para, ft.Height)));
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
                para.SpaceAfterPt,
                paragraphLineSpacingScale: TextLayoutPlanner.ResolveParagraphLineSpacingScale(para, ft.Height)));
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
        double totalWidth = widths.Sum();
        if (maxWidth > 0 && totalWidth > maxWidth)
        {
            RenderWrappedBaseline(dc, para, startX, startY, maxWidth);
            return;
        }

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            para,
            startX,
            startY,
            maxWidth,
            (runIndex, run, rightToLeft) => new TextInlineRunMeasure(
                MeasureBaselineTextWidth(
                    run,
                    run.Text,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    flowDirection: ToFlowDirection(rightToLeft)),
                formatted[runIndex].Baseline,
                formatted[runIndex].Height));
        foreach (var placement in line.Runs)
        {
            var run = para.Runs[placement.RunIndex];
            var ft = formatted[placement.RunIndex];
            double offsetDip = TextLayoutPlanner.BaselineOffsetToDip(run.BaselineOffset, run.FontSizePt);
            dc.DrawText(ft, new Point(placement.X, placement.Y - offsetDip));
        }
    }

    private static void RenderWrappedBaseline(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX,
        double startY,
        double maxWidth)
    {
        var lines = TextLayoutPlanner.PlanBaselineLines(
            para,
            startX,
            startY,
            maxWidth,
            (run, text, rightToLeft) =>
            {
                var flowDirection = ToFlowDirection(rightToLeft);
                double fontScale = run.BaselineOffset.HasValue
                    ? TextLayoutPlanner.BaselineRunFontScale
                    : 1.0;
                var formatted = BuildSingleRunFormattedTextAt(
                    run,
                    text,
                    fontScale,
                    flowDirection);
                return new TextBaselineFragmentMeasure(
                    MeasureBaselineTextWidth(
                        run,
                        text,
                        fontScale,
                        formatted,
                        flowDirection),
                    formatted.Baseline,
                    formatted.Height);
            });
        foreach (var line in lines)
        {
            foreach (var fragment in line.Fragments)
            {
                var run = para.Runs[fragment.RunIndex];
                var formatted = BuildSingleRunFormattedTextAt(
                    run,
                    fragment.Text,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    ToFlowDirection(fragment.RightToLeft));
                dc.DrawText(formatted, new Point(fragment.X, fragment.Y));
            }
        }
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
    /// Inline baseline geometry comes from
    /// <see cref="TextLayoutPlanner.PlanInlineBaselineLine"/>; Avalonia retains
    /// native measurement, glyph construction, brushes, and draw calls.
    /// Marked internal (not private) so FreeP.App.Rendering.Avalonia.Tests can call it directly.
    /// </summary>
    internal static void RenderParaWithMath(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX, double startY)
    {
        var formatted = new FormattedText?[para.Runs.Count];
        for (int i = 0; i < para.Runs.Count; i++)
        {
            var run = para.Runs[i];
            if (!run.IsMathRun && !string.IsNullOrEmpty(run.Text))
            {
                formatted[i] = BuildSingleRunFormattedTextAt(
                    run,
                    run.Text,
                    flowDirection: ToFlowDirection(TextLayoutPlanner.ResolveRunRightToLeft(
                        para.RightToLeft,
                        run.Text)));
            }
        }

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            para,
            startX,
            startY,
            0,
            (runIndex, run, rightToLeft) =>
            {
                if (run.IsMathRun && run.MathLayout is not null)
                {
                    var metrics = run.MathLayout.Metrics;
                    return new TextInlineRunMeasure(metrics.Width, metrics.Ascent, metrics.Height);
                }

                double width = BuildSingleRunFormattedTextAt(
                    run,
                    run.Text,
                    flowDirection: ToFlowDirection(rightToLeft)).Width;
                var text = formatted[runIndex];
                return new TextInlineRunMeasure(
                    width,
                    text?.Baseline ?? 0,
                    text?.Height ?? 0);
            });
        foreach (var placement in line.Runs)
        {
            var run = para.Runs[placement.RunIndex];
            if (run.IsMathRun && run.MathLayout is not null)
            {
                var mathOps = MathBoxRenderPlanner.Plan(
                    run.MathLayout, placement.X, placement.Y, run.Color, run.FontFamily);
                foreach (var op in mathOps)
                    DrawMathOpAvalonia(dc, op);
            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                var ft = formatted[placement.RunIndex]!;
                dc.DrawText(ft, new Point(placement.X, placement.Y));
            }
        }
    }

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
        var glyphRun = run.WithText(glyph.Text);
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
            ResolvedFill.Gradient g => MakeLinearGradientBrushForText(g),
            _ => Brushes.Black
        };
    }

    private static LinearGradientBrush MakeLinearGradientBrushForText(ResolvedFill.Gradient gradient)
    {
        var endpoints = GradientFillRenderPlanner.PlanLinearEndpoints(
            gradient.AngleDegrees,
            GradientEndpointProfile.AvaloniaTextCorners);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(
                endpoints.Start.X,
                endpoints.Start.Y,
                RelativeUnit.Relative),
            EndPoint = new RelativePoint(
                endpoints.End.X,
                endpoints.End.Y,
                RelativeUnit.Relative),
            GradientStops = BuildGradientStops(gradient),
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
        foreach (var stop in GradientFillRenderPlanner.ExpandStops(g, easePositions))
        {
            stops.Add(new AvGradientStop(
                Color.FromArgb(stop.Alpha, stop.Color.R, stop.Color.G, stop.Color.B),
                stop.Position));
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
        var endpoints = GradientFillRenderPlanner.PlanLinearEndpoints(g.AngleDegrees);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(
                endpoints.Start.X,
                endpoints.Start.Y,
                RelativeUnit.Relative),
            EndPoint = new RelativePoint(
                endpoints.End.X,
                endpoints.End.Y,
                RelativeUnit.Relative),
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
        var tile = (PatternFillRenderPlan.PixelTile)PatternFillRenderPlanner.Plan(
            pat.Preset,
            PatternFillRendererProfile.AvaloniaPixel);
        var pixels = new byte[tile.Width * tile.Height * 4];
        for (int index = 0; index < tile.Pixels.Count; index++)
        {
            var color = tile.Pixels[index] == PatternFillColorRole.Foreground ? fg : bg;
            int offset = index * 4;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        var wb = new WriteableBitmap(
            new PixelSize(tile.Width, tile.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buf = wb.Lock())
            Marshal.Copy(pixels, 0, buf.Address, pixels.Length);

        return new ImageBrush(wb)
        {
            TileMode        = TileMode.Tile,
            Stretch         = Stretch.None,
            SourceRect      = new RelativeRect(new Size(tile.Width, tile.Height), RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(new Size(tile.Width, tile.Height), RelativeUnit.Absolute),
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

        if (outline is ResolvedOutline.Pattern pattern)
        {
            IBrush patternBrush = MakePatternBrush(pattern.Fill);
            return new Pen(patternBrush, pattern.WidthDip)
            {
                DashStyle = pattern.Dash switch
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
        try
        {
            _cachedOps = SlideCompositor.Compose(
                _presentation,
                _slide,
                _slideIndex,
                RenderSlideBackground);
        }
        catch (Exception)
        {
            // Composition runs from Render, where an escaping exception is fatal. Worse, the failure
            // repeats: _cachedOps stays null, so the next frame recomposes and throws again — one
            // malformed shape on the active slide would crash the app in a loop with no way back to
            // the deck. Cache an empty result so the slide degrades to blank instead.
            _cachedOps = Array.Empty<DrawOp>();
        }
    }
}
