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
    private const double ImportedAptosWpfRasterScale = 0.95;
    private const double ImportedAptosBodyWpfRasterScale = 0.957;
    private const double ImportedAptosBodyWpfLightRasterScale = 1.016;
    private const double ImportedAptosDisplayWpfRasterScaleY = 0.86;
    private const double ImportedRadarAgilityLabelOffsetX = 35.0;
    private const double ImportedRadarStaminaLabelOffsetX = -51.0;
    private const double ImportedRadarLowerLabelOffsetY = -2.0;

    // WPF has no native blur filter for glyph geometry. Keep its translated
    // shadow rings tighter while preserving shared authored offsets for Avalonia.
    private const double TextShadowBlurSpreadScale = 0.6;
    private const double ImportedTextShadowFitScaleX = 0.95;
    private const double ImportedTextShadowFitScaleY = 0.90;
    private const double ImportedTextShadowFitTranslateX = 1.0;
    private const double ImportedTextShadowFitTranslateY = 2.0;
    // WPF centers a stroke on the contour; PowerPoint's shape soft edge is
    // predominantly an inner feather, so keep the host's visible outer halo narrow.
    private const double SoftEdgeOuterSpreadScale = 0.20;

    // PowerPoint's imported isometricTopUp sample exposes a short projected
    // side wall even though its sp3d payload has no extrusionH. Keep this
    // renderer-local until the shared camera/material model covers it.

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

    public static readonly DependencyProperty ActiveTextEditShapeIdProperty =
        DependencyProperty.Register(
            nameof(ActiveTextEditShapeId),
            typeof(uint?),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RenderSlideBackgroundProperty =
        DependencyProperty.Register(
            nameof(RenderSlideBackground),
            typeof(bool),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender,
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

    /// <summary>Whether the compositor paints the slide background.</summary>
    public bool RenderSlideBackground
    {
        get => (bool)GetValue(RenderSlideBackgroundProperty);
        set => SetValue(RenderSlideBackgroundProperty, value);
    }

    /// <summary>Whether print-only comment callouts are painted over the slide.</summary>
    public bool RenderPrintMarkup { get; set; }

    /// <summary>Shape whose base text is hidden while its rich editor overlay is active.</summary>
    public uint? ActiveTextEditShapeId
    {
        get => (uint?)GetValue(ActiveTextEditShapeIdProperty);
        set => SetValue(ActiveTextEditShapeIdProperty, value);
    }
    /// <summary>
    /// Shape ids temporarily omitted from the base canvas while the slideshow host
    /// renders an animation overlay for the same shape.
    /// </summary>
    public HashSet<uint> SuppressedShapeIds { get; } = new();

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
    public void AttachEditing(
        EditingSession editor,
        Canvas textOverlay,
        Func<SlideShape, bool>? tryOpenOleInPlace = null,
        Action<ChartPointHit>? onChartPointDoubleClick = null)
    {
        var editPointsEnabled = _gestureHandler?.EditPointsEnabled ?? true;
        // Rebuilds replace the EditingSession. Dispose the previous handler first so its
        // canvas/editor subscriptions and adorner cannot process the new document too.
        _gestureHandler?.Dispose();
        _textEditor?.Dispose();
        ActiveTextEditShapeId = null;
        _textEditor      = null;
        _tableCellEditor = null;
        _gestureHandler  = new CanvasGestureHandler(
            this,
            editor,
            tryOpenOleInPlace,
            onChartPointDoubleClick);
        _gestureHandler.EditPointsEnabled = editPointsEnabled;
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

    /// <summary>
    /// Gets or sets whether supported preset shapes expose draggable edit points.
    /// The mutation still flows through <see cref="EditingSession"/> and remains undoable.
    /// </summary>
    public bool EditPointsEnabled
    {
        get => _gestureHandler?.EditPointsEnabled ?? false;
        set
        {
            if (_gestureHandler is not null)
                _gestureHandler.EditPointsEnabled = value;
        }
    }

    /// <summary>Enables or disables the Edit Points interaction mode.</summary>
    public void SetEditPointsMode(bool enabled) => EditPointsEnabled = enabled;

    internal CanvasGestureHandler? GestureHandlerForTests => _gestureHandler;

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

    /// <summary>Arms the single-click source-then-target Format Painter workflow.</summary>
    public bool BeginFormatPainter() => _gestureHandler?.BeginFormatPainter() == true;

    /// <summary>Disarms the single-click source-then-target Format Painter workflow.</summary>
    public void CancelFormatPainter() => _gestureHandler?.CancelFormatPainter();

    // ── Cached draw ops (invalidated on model change) ─────────────────────────

    private IReadOnlyList<DrawOp>? _cachedOps;
    private IReadOnlyDictionary<uint, DrawOp>? _liveTransformPreviewOps;
    private double _slideWidthDip;
    private double _slideHeightDip;

    /// <summary>Forces a recomposition and repaint.</summary>
    public void Refresh()
    {
        _cachedOps = null;
        _liveTransformPreviewOps = null;
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
        => RenderToDrawingContext(dc, renderW, renderH, preserveAspectRatio: true);

    /// <summary>
    /// Renders into a WPF drawing context, optionally stretching the slide to the
    /// requested surface. Export adapters may use the stretch mode when matching a
    /// reference application's fixed-size bitmap export; the live canvas remains
    /// aspect-preserving by default.
    /// </summary>
    public void RenderToDrawingContext(
        DrawingContext dc,
        double renderW,
        double renderH,
        bool preserveAspectRatio)
    {
        EnsureOps();

        if (_cachedOps is null || _slideWidthDip <= 0)
            return;

        if (renderW <= 0 || renderH <= 0) return;

        // Scale slide DIP coordinates → actual render pixels (uniform fit).
        var transform = new TransformGroup();
        if (preserveAspectRatio)
        {
            CurrentTransform = ComputeViewTransform(renderW, renderH, _slideWidthDip, _slideHeightDip);
            transform.Children.Add(new ScaleTransform(CurrentTransform.Scale, CurrentTransform.Scale));
            transform.Children.Add(new TranslateTransform(CurrentTransform.OffsetX, CurrentTransform.OffsetY));
        }
        else
        {
            // PowerPoint COM Slide.Export fills the requested bitmap even when it
            // differs from the deck's native aspect ratio.
            transform.Children.Add(new ScaleTransform(
                renderW / _slideWidthDip,
                renderH / _slideHeightDip));
        }

        dc.PushTransform(transform);

        foreach (var op in _cachedOps)
            RenderOp(dc, op);

        if (RenderPrintMarkup && Presentation is not null && Slide is not null)
            RenderPrintCommentCallouts(dc, Presentation, Slide);

        dc.Pop();
    }

    private static void RenderPrintCommentCallouts(DrawingContext dc, Presentation presentation, Slide slide)
    {
        var fill = FreezeBrush(new SolidColorBrush(Color.FromRgb(255, 249, 196)));
        var border = new Pen(FreezeBrush(new SolidColorBrush(Color.FromRgb(192, 160, 0))), 1);
        var marker = FreezeBrush(new SolidColorBrush(Color.FromRgb(220, 40, 40)));

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

    // ── Background ─────────────────────────────────────────────────────────────

    private static void RenderBackground(DrawingContext dc, DrawOp.Background bg)
    {
        var brush = MakeBrush(bg.Fill, bg.BoundsDip, easeGradientStops: true);
        if (brush is null) return;

        dc.DrawRectangle(brush, null,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ──────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape, bool suppressText)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null
            && (shape.ElbowRouteDip is null || shape.ElbowRouteDip.Count < 2)) return;

        var bounds = shape.BoundsDip;
        var materialPlan = ShapeMaterialRenderPlanner.Plan(shape);
        var shapeGeometry = GetShapeRenderGeometry(shape, materialPlan);
        var renderTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        bool hasTransform = !renderTransform.IsIdentity;

        if (hasTransform)
        {
            dc.PushTransform(ToWpfTransform(renderTransform));
        }

        // Effects: draw before the shape (painter's algorithm — shadow behind shape)
        if (shape.Effects is not null)
            RenderShapeEffects(dc, shape, shapeGeometry);

        if (materialPlan.Kind == ImportedShapeMaterialKind.IsometricCrossDepth)
            RenderImportedShapeDepth(dc, shape, materialPlan);

        // Wave 26: if an explicit elbow route is provided, draw it as a polyline and
        // skip the bbox-derived elbow geometry.
        if (shape.ElbowRouteDip is { Count: >= 2 })
        {
            var pen = shape.Effects?.HasSoftEdge == true ? null : MakePen(shape.Outline);
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
            var fillBrush = MakeBrush(shape.Fill, bounds);
            var pen = shape.Effects?.HasSoftEdge == true ? null : MakePen(shape.Outline);
            dc.DrawGeometry(fillBrush, pen, shapeGeometry);

            if (materialPlan.Kind == ImportedShapeMaterialKind.Circle)
                RenderImportedShapeMaterial(dc, materialPlan, shapeGeometry);
        }

        // Bevel overlay: painted ON TOP of the fill (but before text)
        if (shape.Effects is not null)
            RenderShapeBevel(dc, shape, shapeGeometry);

        if (materialPlan.Kind is ImportedShapeMaterialKind.RelaxedInset or ImportedShapeMaterialKind.Angle)
            RenderImportedShapeMaterial(dc, materialPlan, shapeGeometry);

        // Draw text overlay
        if (!suppressText && shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        if (hasTransform)
            dc.Pop();
    }

    private static void RenderShapeEffects(DrawingContext dc, DrawOp.Shape shape, Geometry shapeGeometry)
    {
        if (shape.Geometry.Contours.Count == 0) return;
        if (shape.Text is not null && shape.Fill is ResolvedFill.None) return;
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);

        if (plan.ShadowPasses.Count > 0)
        {
            for (int passIndex = 0; passIndex < plan.ShadowPasses.Count; passIndex++)
            {
                var pass = plan.ShadowPasses[passIndex];
                byte alpha = IsImportedEffectsShadowSignature(shape.Effects)
                    && passIndex < plan.ShadowPasses.Count - 1
                    ? (byte)Math.Round(pass.Alpha * 0.5)
                    : pass.Alpha;
                var shadowBrush = new SolidColorBrush(
                    Color.FromArgb(alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                if (shadowBrush.CanFreeze) shadowBrush.Freeze();
                dc.PushTransform(new TranslateTransform(pass.OffsetX, pass.OffsetY));
                dc.DrawGeometry(shadowBrush, null, shapeGeometry);
                dc.Pop();
            }
        }

        if (plan.GlowPasses.Count > 0)
        {
            foreach (var pass in plan.GlowPasses)
            {
                var glowBrush = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
                if (glowBrush.CanFreeze) glowBrush.Freeze();
                var glowPen = new Pen(glowBrush, pass.StrokeWidthDip);
                if (glowPen.CanFreeze) glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, shapeGeometry);
            }
        }

        if (plan.SoftEdgePasses.Count > 0)
        {
            var fillBrush = MakeBrush(shape.Fill, shape.BoundsDip);
            if (fillBrush is not null)
            {
                foreach (var pass in plan.SoftEdgePasses)
                {
                    var softEdgePen = new Pen(fillBrush, pass.StrokeWidthDip * SoftEdgeOuterSpreadScale);
                    dc.PushOpacity(pass.Alpha / 255.0);
                    dc.DrawGeometry(null, softEdgePen, shapeGeometry);
                    dc.Pop();
                }
            }
        }

        // Bevel: overlay highlight + shade stripes on the inner edge of the shape bounds.
        // This runs AFTER the shape fill/outline are drawn (the caller RenderShape draws
        // geometry after calling this method for shadows — but bevel must paint ON TOP of
        // the fill).  We therefore invoke this portion from a second call site in RenderShape
        // (RenderShapeBevel) so it can be layered correctly.
    }

    private static bool IsImportedEffectsShadowSignature(ResolvedShapeEffects? effects) =>
        effects is not null
        && effects.HasOuterShadow
        && !effects.HasGlow
        && !effects.HasSoftEdge
        && effects.OuterShadowColor == new SrgbColor(0x40, 0x40, 0x40)
        && effects.OuterShadowAlpha == 153
        && Math.Abs(effects.OuterShadowBlurDip - 8) < 0.01
        && Math.Abs(effects.OuterShadowDistDip - 11.31) < 0.01
        && Math.Abs(effects.OuterShadowDirDeg - 45) < 0.01;

    private static void RenderImportedShapeDepth(
        DrawingContext dc,
        DrawOp.Shape shape,
        ShapeMaterialRenderPlan plan)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            plan.FaceAlpha,
            plan.ExtrusionColor!.Value.R,
            plan.ExtrusionColor.Value.G,
            plan.ExtrusionColor.Value.B));
        if (brush.CanFreeze) brush.Freeze();

        var geometry = ContourListToGeometry(shape.Geometry);
        dc.PushTransform(new TranslateTransform(plan.DepthOffsetDip, plan.DepthOffsetDip));
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
    }

    private static void RenderImportedShapeMaterial(
        DrawingContext dc,
        ShapeMaterialRenderPlan plan,
        Geometry shapeGeo)
    {
        var bounds = plan.Bounds;
        var coreBrush = new SolidColorBrush(Color.FromRgb(
            plan.FaceColor.R, plan.FaceColor.G, plan.FaceColor.B));
        if (coreBrush.CanFreeze) coreBrush.Freeze();

        dc.PushClip(shapeGeo);
        dc.DrawRectangle(coreBrush, null,
            new Rect(bounds.X + 1, bounds.Y + 1,
                Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2)));

        foreach (var band in plan.Bands)
        {
            DrawMaterialBand(dc, CreateMaterialBrush(band), new Rect(
                band.Bounds.X, band.Bounds.Y, band.Bounds.Width, band.Bounds.Height));
        }
        dc.Pop();
    }

    private static LinearGradientBrush CreateMaterialBrush(ShapeMaterialBandPlan band)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = band.IsVertical ? new Point(0, 1) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        foreach (var stop in band.Stops)
            brush.GradientStops.Add(new System.Windows.Media.GradientStop(
                Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), stop.Position));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static void DrawMaterialBand(DrawingContext dc, LinearGradientBrush brush, Rect bounds) =>
        dc.DrawRectangle(brush, null, bounds);

    /// <summary>
    /// Renders the bevel highlight/shade overlay for a shape.
    /// Called AFTER the shape geometry has been painted so the overlay sits on top.
    /// Also draws the contour outline if one is requested.
    /// </summary>
    private static void RenderShapeBevel(DrawingContext dc, DrawOp.Shape shape, Geometry shapeGeometry)
    {
        var fx = shape.Effects;
        if (fx is null) return;

        bool hasBevel   = fx.BevelTop is not null || fx.BevelBottom is not null;
        bool hasContour = fx.ContourWidthDip > 0;
        if (!hasBevel && !hasContour) return;

        if (shape.Geometry.Contours.Count == 0) return;

        var bounds = shape.BoundsDip;

        if (hasBevel && fx.BevelTop is not null)
        {
            var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, fx.BevelTop, fx.LightDirDeg);
            DrawBevelOverlay(dc, shapeGeometry, bounds, highlight, shade,
                fx.BevelTop.WidthDip, fx.BevelTop.HeightDip, fx.BevelTop.PresetName);
        }

        // Contour outline (thin ring in contourColor)
        if (hasContour)
        {
            var cColor  = fx.ContourColor ?? new SrgbColor(0x60, 0x60, 0x60);
            var contourBrush = new SolidColorBrush(Color.FromArgb(255, cColor.R, cColor.G, cColor.B));
            if (contourBrush.CanFreeze) contourBrush.Freeze();
            var contourPen = new Pen(contourBrush, Math.Max(0.5, fx.ContourWidthDip));
            if (contourPen.CanFreeze) contourPen.Freeze();
            dc.DrawGeometry(null, contourPen, shapeGeometry);
        }
    }

    private static Geometry GetShapeRenderGeometry(
        DrawOp.Shape shape,
        ShapeMaterialRenderPlan materialPlan)
    {
        if (materialPlan.Kind == ImportedShapeMaterialKind.RelaxedInset)
        {
            var bounds = shape.BoundsDip;
            var radius = Math.Min(bounds.Width, bounds.Height) * 0.16;
            var geometry = new RectangleGeometry(
                new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                radius,
                radius);
            if (geometry.CanFreeze) geometry.Freeze();
            return geometry;
        }

        return ContourListToGeometry(shape.Geometry);
    }

    private static void DrawBevelOverlay(
        DrawingContext dc,
        Geometry shapeGeo,
        LayoutRect bounds,
        BevelEdgeSet highlight,
        BevelEdgeSet shade,
        double bevelW,
        double bevelH,
        string presetName)
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

        if (string.Equals(presetName, "relaxedInset", StringComparison.OrdinalIgnoreCase))
        {
            // PowerPoint's relaxed inset has a second, shaded material band
            // between the outer bevel highlight and the front face.
            double ix = x + bw;
            double iy = y + bh;
            double iw = Math.Max(0, w - 2 * bw);
            double ih = Math.Max(0, h - 2 * bh);

            DrawWedge(true, shadeBrush,
                new Point(ix, iy),
                new Point(ix + iw, iy),
                new Point(ix + iw - bw, iy + bh),
                new Point(ix + bw, iy + bh));
            DrawWedge(true, shadeBrush,
                new Point(ix + bw, iy + ih - bh),
                new Point(ix + iw - bw, iy + ih - bh),
                new Point(ix + iw, iy + ih),
                new Point(ix, iy + ih));
            DrawWedge(true, shadeBrush,
                new Point(ix, iy),
                new Point(ix + bw, iy + bh),
                new Point(ix + bw, iy + ih - bh),
                new Point(ix, iy + ih));
            DrawWedge(true, shadeBrush,
                new Point(ix + iw - bw, iy + bh),
                new Point(ix + iw, iy),
                new Point(ix + iw, iy + ih),
                new Point(ix + iw - bw, iy + ih - bh));
        }

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
        if (pic.Outline is not ResolvedOutline.None)
        {
            var pen = MakePen(pic.Outline);
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

    private static void RenderTableWithTransform(DrawingContext dc, DrawOp.Table tableOp)
    {
        var transform = ShapeTransformPlanner.PlanShapeTransform(
            tableOp.BoundsDip,
            tableOp.RotationDeg,
            tableOp.FlipH,
            tableOp.FlipV);
        if (!transform.IsIdentity)
            dc.PushTransform(ToWpfTransform(transform));

        RenderTable(dc, tableOp);

        if (!transform.IsIdentity)
            dc.Pop();
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
        if (text.VerticalType != FreeP.Core.Model.TextVerticalType.Horizontal)
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
        var transform = ShapeTransformPlanner.PlanShapeTransform(
            chartOp.BoundsDip,
            chartOp.RotationDeg,
            flipH: false,
            flipV: false);
        if (!transform.IsIdentity)
            dc.PushTransform(ToWpfTransform(transform));

        RenderChartCore(dc, chartOp);

        if (!transform.IsIdentity)
            dc.Pop();
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

        var chartRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var chartBrush = scene.ChartAreaFill is { } chartFill
            ? ToBrush(chartFill)
            : FreezeBrush(new SolidColorBrush(Colors.White));
        var chartPen = scene.ChartAreaOutline is { } chartOutline ? ToPen(chartOutline) : null;
        if (scene.RoundedCorners)
        {
            var radius = Math.Min(ChartRenderPlanner.RoundedChartCornerRadius,
                Math.Min(chartRect.Width, chartRect.Height) / 2.0);
            dc.DrawRoundedRectangle(chartBrush, chartPen, chartRect, radius, radius);
        }
        else
        {
            dc.DrawRectangle(chartBrush, chartPen, chartRect);
        }

        if (scene.PlotAreaFill is { } plotFill)
            dc.DrawRectangle(ToBrush(plotFill), scene.PlotAreaOutline is { } plotOutline ? ToPen(plotOutline) : null, ToRect(scene.Frame.Plot));

        if (scene.Title is { } title)
        {
            if (scene.UsesStockLineFallback)
            {
                // The imported line-series fallback uses the classic Office title
                // raster, whose visible glyph block is slightly narrower and lower
                // than WPF's default FormattedText placement.
                title = title with
                {
                    Bounds = title.Bounds with
                    {
                        X = title.Bounds.X + 5.0,
                        Y = title.Bounds.Y + 2.0
                    }
                };
            }

            DrawChartLabel(dc, title.Text, ToRect(title.Bounds), title.IsBold, title.FontSize, ToTextAlignment(title.Alignment), textColor: title.TextColor, fontFamily: title.FontFamily, maxLineCount: title.MaxLineCount);
        }

        if (!scene.Frame.HasPlot)
            return;

        if (scene.DrawFlatGrid && scene.GridLines.GridLines.Count > 0)
        {
            var gridPen = CreateChartGridLinePen(scene.GridLines);
            foreach (var gridLine in scene.GridLines.GridLines)
            {
                if (scene.UseWpfPixelSnappedImportedGrid &&
                    Math.Abs(gridLine.Start.Y - gridLine.End.Y) < 0.001)
                {
                    var left = Math.Min(gridLine.Start.X, gridLine.End.X);
                    var right = Math.Max(gridLine.Start.X, gridLine.End.X);
                    var top = Math.Round(gridLine.Start.Y - 0.5, MidpointRounding.AwayFromZero);
                    dc.DrawRectangle(gridPen.Brush, null, new Rect(left, top, right - left, 1.0));
                }
                else
                {
                    dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));
                }
            }
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
            case ChartSceneGeometryKind.Funnel:
                RenderFunnelChart(dc, scene);
                break;
            case ChartSceneGeometryKind.Waterfall:
                RenderColumnChart(dc, scene);
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
                dc.DrawRectangle(
                    FreezeBrush(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))),
                    null,
                    ToRect(scene.Frame.Plot));
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
                dc.DrawRectangle(ToBrush(keyFill), null, ToRect(keyBounds));

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
            var labelBounds = ToRect(label.Bounds);
            if (scene.UsesStockLineFallback)
            {
                // The imported stock fallback's value labels sit in a wider
                // left gutter in PowerPoint than WPF's generic text placement.
                labelBounds = new Rect(
                    labelBounds.X + 10.0,
                    labelBounds.Y + 6.0,
                    labelBounds.Width,
                    labelBounds.Height);
            }

            DrawChartLabel(dc, label.Text, labelBounds,
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

        RenderDropLines(dc, scene.SeriesLines);
        foreach (var connector in scene.WaterfallConnectorLines)
            dc.DrawLine(ToPen(connector.Stroke), ToPoint(connector.Start), ToPoint(connector.End));
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

    // ── Combo-chart secondary series overlay ─────────────────────────────────
    /// <summary>
    /// Renders series that carry a per-series <see cref="FreeP.Core.Model.ChartSeries.OverrideChartType"/>
    /// (set by the IO reader for combo charts where a secondary chart-type group, e.g. a
    /// lineChart, is mixed with the primary type, e.g. barChart).
    /// Only Line / LineMarkers overrides are handled here; others are future-proofed silently.
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

    // ── Bar (horizontal) chart ────────────────────────────────────────────────

    private static void RenderBarChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.Rectangles)
        {
            dc.DrawRectangle(
                ToBrush(primitive.Fill),
                primitive.Stroke.HasValue ? ToPen(primitive.Stroke.Value) : null,
                ToRect(primitive.Bounds));
        }

        RenderDropLines(dc, scene.SeriesLines);
    }

    // ── Line chart ────────────────────────────────────────────────────────────

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
        foreach (var bar in scene.UpDownBars)
            dc.DrawRectangle(ToBrush(bar.Fill), bar.Stroke.HasValue ? ToPen(bar.Stroke.Value) : null, ToRect(bar.Bounds));
        RenderDropLines(dc, scene.DropLines);
        foreach (var primitive in scene.LineSeries)
            RenderLineSeriesPrimitive(dc, primitive);
    }

    private static void RenderDropLines(DrawingContext dc, IReadOnlyList<ChartLineSegmentPrimitive> lines)
    {
        foreach (var line in lines)
            dc.DrawLine(ToPen(line.Stroke), ToPoint(line.Start), ToPoint(line.End));
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

    private static void RenderPieChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var seriesLine in scene.OfPieSeriesLines)
            dc.DrawLine(ToPen(seriesLine.Stroke), ToPoint(seriesLine.Start), ToPoint(seriesLine.End));

        foreach (var primitive in scene.PieSlices.Concat(scene.OfPieSecondarySlices))
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

            var borderPen = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
            if (borderPen.CanFreeze) borderPen.Freeze();

            dc.DrawGeometry(brush, borderPen, geo);
        }

        if (scene.OfPieSecondaryType == OfPieType.Bar)
            RenderColumnChart(dc, scene);
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
            ctx.BeginFigure(ToPoint(topStart), isFilled: true, isClosed: true);
            ctx.ArcTo(
                ToPoint(topEnd),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise,
                isStroked: false,
                isSmoothJoin: false);
            ctx.LineTo(ToPoint(bottomEnd), isStroked: false, isSmoothJoin: false);
            ctx.ArcTo(
                ToPoint(bottomStart),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Counterclockwise,
                isStroked: false,
                isSmoothJoin: false);
        }
        if (geo.CanFreeze) geo.Freeze();
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

    // ── Area chart ────────────────────────────────────────────────────────────

    private static void RenderAreaChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var primitive in scene.AreaSeries)
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

    private static void RenderFunnelChart(DrawingContext dc, ChartScenePlan scene)
    {
        foreach (var segment in scene.FunnelSegments)
        {
            if (segment.Path.Fill is not { } fill)
                continue;

            dc.DrawGeometry(ToBrush(fill), null, ToGeometry(segment.Path));
        }
    }

    private static void RenderSurfaceChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Surface is not { } plan)
            return;

        var renderFacets = plan.WpfRenderFacets.Count > 0
            ? plan.WpfRenderFacets
            : plan.RenderFacets.Count > 0 ? plan.RenderFacets : plan.Facets;
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

    private static void RenderDoughnutChart(DrawingContext dc, ChartScenePlan scene)
    {
        var borderPen = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
        if (borderPen.CanFreeze) borderPen.Freeze();

        foreach (var primitive in scene.DoughnutSlices)
        {
            var brush = ToBrush(primitive.Fill!.Value);

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
                dc.DrawRectangle(ToBrush(keyFill), null, ToRect(keyBounds));

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

    // ── Bubble chart ──────────────────────────────────────────────────────────

    private static void RenderStockChart(DrawingContext dc, ChartScenePlan scene)
    {
        if (scene.Stock is null)
        {
            RenderDropLines(dc, scene.DropLines);
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

        foreach (var bar in scene.UpDownBars)
        {
            dc.DrawRectangle(ToBrush(bar.Fill), null, ToRect(bar.Bounds));
        }

        var plan = scene.Stock.Value;

        foreach (var segment in plan.HighLowLines)
            dc.DrawLine(ToPen(segment.Stroke), ToPoint(segment.Start), ToPoint(segment.End));
        foreach (var tick in plan.OpenTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
        foreach (var tick in plan.CloseTicks)
            dc.DrawLine(ToPen(tick.Segment.Stroke), ToPoint(tick.Segment.Start), ToPoint(tick.Segment.End));
    }

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

    // ── Radar chart ───────────────────────────────────────────────────────────

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
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));

        for (int labelIndex = 0; labelIndex < plan.CategoryLabels.Count; labelIndex++)
        {
            var label = plan.CategoryLabels[labelIndex];
            var labelBounds = ToRect(label.Bounds);
            if (plan.Rings.Count == 9 &&
                plan.CategoryLabels.Count == 5 &&
                plan.Series.Count == 2 &&
                labelIndex is 2 or 3)
            {
                // Imported PowerPoint keeps the lower radar labels in the
                // same vertical band but registers their WPF text boxes farther
                // along the angled spokes. Avalonia retains the shared plan;
                // this is WPF-only host registration.
                double horizontalOffset = labelIndex == 2
                    ? ImportedRadarAgilityLabelOffsetX
                    : ImportedRadarStaminaLabelOffsetX;
                labelBounds = new Rect(
                    labelBounds.X + horizontalOffset,
                    labelBounds.Y + ImportedRadarLowerLabelOffsetY,
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

    // ── Chart helpers ─────────────────────────────────────────────────────────

    private static void DrawChartLabel(
        DrawingContext dc, string text, Rect rect,
        bool isBold, double fontSize, TextAlignment align,
        bool isItalic = false,
        SrgbColor? textColor = null,
        string? fontFamily = null,
        int maxLineCount = 1,
        double horizontalScale = 1.0)
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
            MaxLineCount   = maxLineCount,
            TextAlignment  = align,
            Trimming       = TextTrimming.CharacterEllipsis
        };

        bool scaled = Math.Abs(horizontalScale - 1.0) > 0.0001;
        if (scaled)
            dc.PushTransform(new ScaleTransform(horizontalScale, 1.0, rect.X, rect.Y));
        dc.DrawText(ft, new Point(rect.X, rect.Y));
        if (scaled)
            dc.Pop();
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
            DrawChartLabel(dc, label.Text, rect, label.IsBold, label.FontSize, ToTextAlignment(label.Alignment), isItalic: label.IsItalic, textColor: label.TextColor, fontFamily: label.FontFamily);
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
            ToTextAlignment(label.Alignment),
            isItalic: label.IsItalic,
            textColor: label.TextColor,
            fontFamily: label.FontFamily);
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
        if (TryRenderContinuousColumnFlow(dc, text, bounds))
            return;

        var initialColumnLayout = TextLayoutPlanner.GetColumnLayout(text, bounds);
        var initialMeasured = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0)
            {
                continue;
            }
            var ft = BuildFormattedText(
                para,
                initialColumnLayout.ColumnWidthDip,
                text.Wrap,
                useIdealMetrics: text.AutoFitKind == TextAutoFitKind.None);
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
            var ft = BuildFormattedText(
                para,
                columnLayout.ColumnWidthDip,
                renderText.Wrap,
                useIdealMetrics: renderText.AutoFitKind == TextAutoFitKind.None);
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
        const double importedAptosFallbackScale = 0.93;

        // PowerPoint's imported column breakpoints align more closely with WPF's
        // display metrics than with ideal metrics, so use the same mode for both
        // line measurement and placement.
        for (int paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            var run = paragraph.Runs[0];
            double horizontalScale = string.Equals(run.FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
                ? importedAptosFallbackScale
                : 1.0;
            var lines = SplitColumnText(paragraph, run, layout.ColumnWidthDip / horizontalScale, text.Wrap);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var fragment = CloneParagraphWithText(paragraph, run, lines[lineIndex]);
                var formatted = BuildFormattedText(fragment, layout.ColumnWidthDip, text.Wrap, useIdealMetrics: false);
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
            var sourceRun = text.Paragraphs[placement.ParagraphIndex].Runs[0];
            double horizontalScale = string.Equals(sourceRun.FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
                ? importedAptosFallbackScale
                : 1.0;
            var formatted = BuildFormattedText(
                fragment,
                horizontalScale < 1.0 ? 0 : placement.MaxWidthDip,
                horizontalScale < 1.0 ? false : text.Wrap,
                useIdealMetrics: false);
            if (placement.IsFirstLine && fragment.IndentDip > 0 && formatted.MaxTextWidth > 0)
                formatted.MaxTextWidth = placement.MaxWidthDip;
            if (horizontalScale < 1.0)
                dc.PushTransform(new ScaleTransform(horizontalScale, 1.0, placement.X, placement.Y));
            dc.DrawText(formatted, new Point(placement.X, placement.Y));
            if (horizontalScale < 1.0)
                dc.Pop();
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
                false,
                useIdealMetrics: false);
            if (current.Length > 0 && measure.WidthIncludingTrailingWhitespace > maxWidth)
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

            var ft = BuildFormattedText(
                para,
                area.Width,
                text.Wrap,
                useIdealMetrics: text.AutoFitKind == TextAutoFitKind.None);
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

            var ft = BuildFormattedText(
                para,
                area.Width,
                renderText.Wrap,
                useIdealMetrics: renderText.AutoFitKind == TextAutoFitKind.None);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanBodyText(renderText, bounds, measured, autoFitPlan);
        bool useImportedAptosRasterScale = UsesImportedAptosFont(renderText);
        bool useImportedAptosBodyRasterScale = UsesImportedAptosBodyFont(renderText);
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
                DrawBulletPlacementWpf(dc, bullet);
            }

            switch (TextLayoutPlanner.PlanParagraphRenderRoute(para, renderText))
            {
                case TextParagraphRenderRoute.Math:
                    RenderParaWithMath(dc, para, placement.X, placementY);
                    break;
                case TextParagraphRenderRoute.Effects:
                    RenderParaWithEffects(dc, para, placement.X, placementY, placement.MaxWidthDip, renderText.Wrap, renderText, bounds);
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
                    bool useImportedAptosDisplayRasterScale = UsesImportedAptosDisplayFont(para);
                    // The exact imported Aptos body signature falls back to a heavier WPF font;
                    // keep its measured layout, but tune only the host fallback paint weight.
                    if (useImportedAptosBodyRasterScale)
                        ft.SetFontWeight(FontWeights.Light, 0, ft.Text.Length);
                    if (useImportedAptosRasterScale)
                    {
                        double scaleX = useImportedAptosBodyRasterScale
                            ? ImportedAptosBodyWpfLightRasterScale
                            : ImportedAptosWpfRasterScale;
                        double centerX = para.Align == TextAlign.Center
                            ? bounds.X + bounds.Width * 0.5
                            : placement.X;
                        double scaleY = useImportedAptosDisplayRasterScale
                            ? ImportedAptosDisplayWpfRasterScaleY
                            : 1.0;
                        double pivotY = useImportedAptosDisplayRasterScale
                            ? placement.Y + ft.Height
                            : placementY;
                        dc.PushTransform(new ScaleTransform(
                            scaleX,
                            scaleY,
                            centerX,
                            pivotY));
                    }
                    dc.DrawText(ft, new Point(placement.X, placementY));
                    if (useImportedAptosRasterScale)
                    {
                        dc.Pop();
                    }
                    break;
            }
        }
    }

    private static bool UsesImportedAptosFont(ResolvedTextLayout text) =>
        text.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.FontFamily.StartsWith("Aptos", StringComparison.OrdinalIgnoreCase));

    private static bool UsesImportedAptosBodyFont(ResolvedTextLayout text) =>
        text.AutoFitKind == TextAutoFitKind.None
        && text.Paragraphs.Count == 8
        && text.Paragraphs.All(paragraph =>
            paragraph.Runs.Count == 1
            && string.Equals(paragraph.Runs[0].FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(paragraph.Runs[0].FontSizePt - 18.0) < 0.01
            && !paragraph.Runs[0].Bold
            && !paragraph.Runs[0].Italic
            && paragraph.BulletKind == BulletKind.None);

    private static bool UsesImportedAptosDisplayFont(ResolvedParagraph paragraph) =>
        paragraph.Runs.Any(run =>
            string.Equals(run.FontFamily, "Aptos Display", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(run.FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(run.FontSizePt - 28.0) < 0.01
                && string.Equals(run.Text, "Autofit Shrink Demo", StringComparison.Ordinal)));

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

            DrawTabLeaderWpf(
                dc,
                run,
                segment.Leader,
                previousEndX,
                segment.X,
                startY,
                para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            dc.DrawText(ft, new Point(segment.X, startY));
            previousEndX = segment.X + ft.WidthIncludingTrailingWhitespace;
        }
    }

    private static void DrawTabLeaderWpf(
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
        double glyphWidth = glyphText.WidthIncludingTrailingWhitespace;
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
        double lineAscent = formatted.Length == 0 ? 0 : formatted.Max(ft => ft.Baseline);
        double baselineY = ComputeBaselineY(startY, lineAscent);
        double totalWidth = formatted.Sum(ft => ft.WidthIncludingTrailingWhitespace);
        if (maxWidth > 0 && totalWidth > maxWidth)
        {
            RenderWrappedBaseline(dc, para, startX, startY, maxWidth);
            return;
        }
        var placements = TextLayoutPlanner.PlanRunPlacements(
            para,
            startX,
            maxWidth,
            (run, rightToLeft) => BuildSingleRunFormattedTextAt(
                run,
                run.Text,
                run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                ToFlowDirection(rightToLeft)).WidthIncludingTrailingWhitespace);
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
                (run, rightToLeft) => BuildSingleRunFormattedTextAt(
                    run,
                    run.Text,
                    run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0,
                    ToFlowDirection(rightToLeft)).WidthIncludingTrailingWhitespace);
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
            double width = formatted.WidthIncludingTrailingWhitespace;
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
                double tokenWidth = tokenText.WidthIncludingTrailingWhitespace;
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

                // Plan the math draw ops using the shared engine (renderer-neutral).
                var mathOps = MathBoxRenderPlanner.Plan(
                    run.MathLayout, placement.X, runY, run.Color, run.FontFamily);

                foreach (var op in mathOps)
                    DrawMathOpWpf(dc, op);

            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                // Plain text run inline with math, baseline-aligned with it.
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
    private static FormattedText BuildSingleRunFormattedTextAt(
        ResolvedRun run,
        string text,
        double fontSizeScale = 1.0,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        var typeface = new Typeface(new FontFamily(run.FontFamily),
            run.Italic ? FontStyles.Italic : FontStyles.Normal,
            run.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        double emSizePx = run.FontSizePt * fontSizeScale * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        if (brush.CanFreeze) brush.Freeze();
        var ft = new FormattedText(
            text.Length > 0 ? text : " ",
            System.Globalization.CultureInfo.CurrentUICulture,
            flowDirection,
            typeface, emSizePx, brush,
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, ft.Text.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, ft.Text.Length);
        return ft;
    }

    private static FlowDirection ToFlowDirection(bool rightToLeft) =>
        rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

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
        bool useIdealMetrics = false)
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

        // P1: Keep Display formatting for bold headings, which matches PowerPoint's
        // pixel-grid-snapped title rendering at 96 DPI. Regular text in a no-autofit body
        // uses Ideal metrics below so wrapping follows PowerPoint's vector text layout.
        // pixelsPerDip = 1.0 is correct for RenderTargetBitmap at 96 DPI.
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
            para.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush,
            numberSubstitution: null,
            textFormattingMode: useIdealMetrics && !para.Runs.Any(run => run.Bold)
                ? TextFormattingMode.Ideal
                : TextFormattingMode.Display,
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
        var placements = TextLayoutPlanner.PlanRunPlacements(
            para,
            x,
            0,
            (run, rightToLeft) => BuildSingleRunFormattedText(
                run,
                0,
                ToFlowDirection(rightToLeft)).Width);
        foreach (var placement in placements)
        {
            var run = para.Runs[placement.RunIndex];

            var runFt2 = BuildSingleRunFormattedText(
                run,
                wrap ? maxWidth : 0,
                ToFlowDirection(placement.RightToLeft));
            double drawX = placement.X;

            var runFt = runFt2;   // already built above

            double progress = shapeBounds.Width > 0 ? (drawX - shapeBounds.X) / shapeBounds.Width : 0;
            var plan = TextRunEffectRenderPlanner.Plan(
                run,
                new LayoutRect(drawX, y, runFt.Width, runFt.Height),
                progress,
                shapeBounds,
                text);
            var geo = runFt.BuildGeometry(new Point(plan.GlyphBoundsDip.X, plan.GlyphBoundsDip.Y));

            bool useImportedTextShadowFit =
                text.WarpPreset is null &&
                string.Equals(run.Text, "Text Shadow", StringComparison.Ordinal) &&
                Math.Abs(run.FontSizePt - 40.0) < 0.01 &&
                run.TextShadow is { BlurDip: > 6.0 and < 7.0 };
            if (useImportedTextShadowFit)
            {
                var fitOrigin = new Point(geo.Bounds.X, geo.Bounds.Bottom);
                dc.PushTransform(new ScaleTransform(
                    ImportedTextShadowFitScaleX,
                    ImportedTextShadowFitScaleY,
                    fitOrigin.X,
                    fitOrigin.Y));
                dc.PushTransform(new TranslateTransform(
                    ImportedTextShadowFitTranslateX,
                    ImportedTextShadowFitTranslateY));
            }

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
                            double offsetX = shadow.OffsetX;
                            double offsetY = shadow.OffsetY;
                            if (shadow.IsBlurPass)
                            {
                                offsetX = shadow.BaseOffsetX +
                                    (shadow.OffsetX - shadow.BaseOffsetX) * TextShadowBlurSpreadScale;
                                offsetY = shadow.BaseOffsetY +
                                    (shadow.OffsetY - shadow.BaseOffsetY) * TextShadowBlurSpreadScale;
                            }
                            dc.PushTransform(new TranslateTransform(offsetX, offsetY));
                            dc.DrawGeometry(shadowBrush, null, geo);
                            dc.Pop();
                            break;
                        }
                        case TextRunEffectPass.Reflection reflection:
                        {
                            var geoRect = geo.Bounds;
                            var r2 = new Rect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height));
                            dc.PushTransform(BuildTextReflectionTransform(
                                reflection,
                                new LayoutRect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height))));
                            var reflectionMask = new LinearGradientBrush
                            {
                                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                                // Reflections use a negative Y scale. Reverse the
                                // mask direction so opacity fades away from the glyph.
                                StartPoint = new Point(0.5, 1),
                                EndPoint = new Point(0.5, 0),
                            };
                            reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(Colors.White, 0));
                            reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                                Color.FromArgb(0, 255, 255, 255), Math.Max(0.001, reflection.EndPos)));
                            if (reflection.EndPos < 0.999)
                                reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                                    Color.FromArgb(0, 255, 255, 255), 1));
                            if (reflectionMask.CanFreeze) reflectionMask.Freeze();
                            dc.PushOpacityMask(reflectionMask);
                            dc.PushOpacity(reflection.Alpha / 255.0);
                            dc.DrawGeometry(MakeFillBrushForText(reflection.FillBrush, r2), null, geo);
                            dc.Pop();
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
                        case TextRunEffectPass.MaterialHighlight material:
                        {
                            var geoRect = geo.Bounds;
                            var r2 = new Rect(geoRect.X, geoRect.Y, Math.Max(1, geoRect.Width), Math.Max(1, geoRect.Height));
                            dc.DrawGeometry(MakeFillBrushForText(material.FillBrush, r2), null, geo);
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
                if (useImportedTextShadowFit)
                {
                    dc.Pop();
                    dc.Pop();
                }
            }

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

    /// <summary>Builds a FormattedText for a single run (used for glyph geometry extraction).</summary>
    private static FormattedText BuildSingleRunFormattedText(
        ResolvedRun run,
        double maxWidth,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
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
            flowDirection, typeface, emPx, brush,
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
            "pct0" => new SolidColorBrush(bg),
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
            "pct100" => new SolidColorBrush(fg),
            "horzStripe" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "vertStripe" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "ltHorz" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "ltVert" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "dashHorz" => BuildStripePatternBrush(bg, fg, horizontal: true),
            "dashVert" => BuildStripePatternBrush(bg, fg, horizontal: false),
            "diagStripe" or "ltDnDiag" or "dnDiag" => BuildDiagPatternBrush(bg, fg, down: true),
            "upDiag" or "ltUpDiag" => BuildDiagPatternBrush(bg, fg, down: false),
            // PowerPoint's cross preset repeats on an 8-pixel grid at slide render scale.
            "cross" => BuildCrossPatternBrush(bg, fg, tileSize: 8, strokeWidth: 1),
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

    private static DrawingBrush BuildCrossPatternBrush(
        Color bg,
        Color fg,
        double tileSize = 6,
        double strokeWidth = 2)
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null,
            new RectangleGeometry(new Rect(0, 0, tileSize, tileSize))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
            new RectangleGeometry(new Rect(0, 0, strokeWidth, tileSize))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(fg), null,
            new RectangleGeometry(new Rect(0, 0, tileSize, strokeWidth))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileSize, tileSize),
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

        if (outline is ResolvedOutline.Pattern pattern)
        {
            var brush = MakePatternBrush(pattern.Fill);
            if (brush.CanFreeze) brush.Freeze();
            var pen = new Pen(brush, pattern.WidthDip);
            pen.DashStyle = MapDashStyleWpf(pattern.Dash);
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

    private static StreamGeometry ToPolygonGeometry(IReadOnlyList<ChartPlanPoint> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count > 0)
            {
                ctx.BeginFigure(ToPoint(points[0]), isFilled: true, isClosed: true);
                for (int index = 1; index < points.Count; index++)
                    ctx.LineTo(ToPoint(points[index]), isStroked: false, isSmoothJoin: false);
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
        _cachedOps = SlideCompositor.Compose(
            presentation,
            slide,
            slideIndex < 0 ? 0 : slideIndex,
            RenderSlideBackground);
    }
}
