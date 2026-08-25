using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
public sealed partial class SlideCanvas : FrameworkElement
{
    private const double ImportedAptosWpfRasterScale = 0.95;
    private const double ImportedIncreasingCircleWpfRasterScaleX = 1.0;
    private const double ImportedIncreasingCircleWpfRasterScaleY = 0.94;
    private const double ImportedAptosBodyWpfLightRasterScale = 1.016;
    private const double ImportedAptosDisplayWpfRasterScaleY = 0.86;
    private const double ImportedRadarAgilityLabelOffsetX = 35.0;
    private const double ImportedRadarStaminaLabelOffsetX = -51.0;
    private const double ImportedRadarLowerLabelOffsetY = -2.0;

    // WPF has no native blur filter for glyph geometry. Keep its translated
    // shadow rings tighter while preserving shared authored offsets for Avalonia.
    private const double TextShadowBlurSpreadScale = 0.6;
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
    {
        var canvas = (SlideCanvas)d;
        canvas._canvasAutomation.ResetSelection(
            canvas.Slide,
            canvas._editingSession?.SelectedShapeIds);
        canvas.Refresh();
    }

    // ── Editing (Wave 3C / 9A) ────────────────────────────────────────────────

    private CanvasGestureHandler?      _gestureHandler;
    private InCanvasTextEditor?        _textEditor;
    private InCanvasTableCellEditor?   _tableCellEditor;   // Wave 9A
    private Canvas?                    _textOverlay;   // WPF Canvas layered above SlideCanvas for text-edit overlay
    // Standalone canvases also render slide-pane thumbnails and print surfaces. The interactive
    // main host explicitly applies its View state; secondary canvases must not inherit ruler chrome.
    private PresentationViewShowState  _viewShowState = PresentationViewShowState.Default with { ShowRulers = false };
    private PresentationViewZoomState  _viewZoomState = PresentationViewZoomState.FitToWindow;
    private PresentationViewColorModeState _viewColorModeState = PresentationViewColorModeState.Color;
    private ICanvasGestureEditingSession? _editingSession;
    private readonly PresentationCanvasAutomationSession _canvasAutomation = new();

    /// <summary>
    /// Returns the custom UIA automation peer for the slide editing surface: the canvas itself
    /// (exposing <see cref="ISelectionProvider"/>) and one virtual per-shape peer per visible
    /// shape (exposing <see cref="ISelectionItemProvider"/>). Mirrors the pattern
    /// <c>FreeX.App.UI.GridView</c> uses for its worksheet grid (src/FreeX.App.UI/GridView.cs) --
    /// a single custom peer exposing selection, plus lightweight per-item peers with no backing
    /// visual, since (like GridView) SlideCanvas paints everything directly via OnRender/
    /// DrawingContext rather than a real per-shape visual tree.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new SlideCanvasAutomationPeer(this);

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
    /// <param name="onInlineOlePayloadUpdated">
    /// Invoked with the edited bytes when a native in-place OLE server commits an inline embedded
    /// object hosted inside an open text/table-cell editor, so the shell can mark the document
    /// dirty -- the inline counterpart of the <paramref name="tryOpenOleInPlace"/> route's
    /// own <c>onPayloadUpdated</c> wiring.
    /// </param>
    /// <param name="tryActivateOleExternally">
    /// Opens a slide-level embedded object in its associated application when in-place activation
    /// is unavailable. Supply this rather than relying on the coordinator's default route: the
    /// default reports nothing back, so the shell never learns that the application saved.
    /// </param>
    public void AttachEditing(
        EditingSession editor,
        Canvas textOverlay,
        Func<SlideShape, bool>? tryOpenOleInPlace = null,
        Action<ChartPointHit>? onChartPointDoubleClick = null,
        Action<string, string>? onClipboardWriteFailed = null,
        Action<byte[]>? onInlineOlePayloadUpdated = null,
        Func<OleObjectInfo?, bool>? tryActivateOleExternally = null)
    {
        var editPointsEnabled = _gestureHandler?.EditPointsEnabled ?? true;
        // Rebuilds replace the EditingSession. Dispose the previous handler first so its
        // canvas/editor subscriptions and adorner cannot process the new document too.
        _gestureHandler?.Dispose();
        _textEditor?.Dispose();
        ActiveTextEditShapeId = null;
        _textEditor      = null;
        _tableCellEditor = null;

        // Re-point the UIA selection-notification subscription (see SlideCanvasAutomationPeer)
        // at the new EditingSession, the same way the gesture handler above is rebuilt, so the
        // canvas's automation peer never fires off a disposed/stale session's event.
        if (_editingSession is not null)
            _editingSession.SelectionChanged -= OnEditingSessionSelectionChangedForAutomation;
        _editingSession = editor;
        _canvasAutomation.ResetSelection(Slide, editor.SelectedShapeIds);
        _editingSession.SelectionChanged += OnEditingSessionSelectionChangedForAutomation;

        _gestureHandler  = new CanvasGestureHandler(
            this,
            editor,
            tryOpenOleInPlace,
            onChartPointDoubleClick,
            tryActivateOleExternally);
        _gestureHandler.EditPointsEnabled = editPointsEnabled;
        ApplyViewShowState(_viewShowState);
        _textOverlay     = textOverlay;
        _textEditor      = new InCanvasTextEditor(
            this, editor, textOverlay, onClipboardWriteFailed, onInlineOlePayloadUpdated);
        _tableCellEditor = new InCanvasTableCellEditor(
            this, editor, textOverlay, onClipboardWriteFailed, onInlineOlePayloadUpdated); // Wave 9A
    }

    /// <summary>
    /// Notifies the canvas's UIA automation peer (if one has been realized -- i.e. a screen
    /// reader or other automation client is actually listening) that the shape selection
    /// changed, so it can raise the appropriate SelectionItem/focus notifications. Mirrors
    /// GridView's NotifySelectionAutomationChanged (src/FreeX.App.UI/GridView.cs).
    /// </summary>
    private void OnEditingSessionSelectionChangedForAutomation(object? sender, EventArgs e)
    {
        var delta = _canvasAutomation.CaptureSelectionDelta(
            Slide,
            _editingSession?.SelectedShapeIds);
        if (UIElementAutomationPeer.FromElement(this) is SlideCanvasAutomationPeer peer)
            peer.NotifySelectionChanged(delta);
    }

    public PresentationViewShowState ViewShowState => _viewShowState;
    public PresentationViewZoomState ViewZoomState => _viewZoomState;
    public PresentationViewColorModeState ViewColorModeState => _viewColorModeState;

    public void ApplyViewShowState(PresentationViewShowState state)
    {
        _viewShowState = state;
        InvalidateVisual();
        if (_gestureHandler is null)
            return;

        _gestureHandler.SnapToGrid = state.ShowGridlines;
        _gestureHandler.SnapToShapes = state.ShowGuides;
    }

    private static void RenderRulers(
        DrawingContext dc,
        SlideTransformCore transform,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0)
            return;

        const double thickness = PresentationRulerTickPlanner.RulerThickness;
        var surface = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)));
        var lineBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)));
        var pen = new Pen(lineBrush, 1);
        pen.Freeze();
        dc.DrawRectangle(surface, pen, new Rect(0, 0, width, thickness));
        dc.DrawRectangle(surface, pen, new Rect(0, 0, thickness, height));

        foreach (var tick in PresentationRulerTickPlanner.BuildHorizontal(transform))
        {
            dc.DrawLine(pen, new Point(tick.Offset, thickness), new Point(tick.Offset, thickness - tick.Length));
            if (tick.Label is not null)
                dc.DrawText(CreateRulerLabel(tick.Label), new Point(tick.Offset + 2, 0));
        }

        foreach (var tick in PresentationRulerTickPlanner.BuildVertical(transform))
            dc.DrawLine(pen, new Point(thickness, tick.Offset), new Point(thickness - tick.Length, tick.Offset));
    }

    private static FormattedText CreateRulerLabel(string text) => new(
        text,
        System.Globalization.CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        8,
        FreezeBrush(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))),
        numberSubstitution: null,
        textFormattingMode: TextFormattingMode.Display,
        pixelsPerDip: 1.0);

    public void ApplyViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        InvalidateVisual();
    }

    /// <summary>
    /// Applies the non-persistent View &gt; Color/Grayscale treatment. Live presentation
    /// editing remains backed by the original slide model; only this canvas is filtered.
    /// </summary>
    public void ApplyViewColorModeState(PresentationViewColorModeState state)
    {
        _viewColorModeState = state;
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

    // ── Layout: maintain slide aspect ratio ────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureOps();
        if (_slideWidthDip <= 0 || _slideHeightDip <= 0)
            return base.MeasureOverride(availableSize);

        var fitted = SlideCanvasGeometryPlanner.FitAspectRatio(
            _slideWidthDip,
            _slideHeightDip,
            availableSize.Width,
            availableSize.Height);
        return new Size(fitted.Width, fitted.Height);
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_viewColorModeState.Mode != PresentationViewColorMode.Color)
        {
            RenderColorTransformedCanvas(dc, ActualWidth, ActualHeight);
            return;
        }

        RenderToDrawingContext(dc, ActualWidth, ActualHeight, preserveAspectRatio: true, renderViewAids: true);
        if (_viewShowState.ShowRulers)
            RenderRulers(dc, CurrentTransform.Core, ActualWidth, ActualHeight);
    }

    private void RenderColorTransformedCanvas(DrawingContext destination, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;

        var visual = new DrawingVisual();
        using (var source = visual.RenderOpen())
        {
            RenderToDrawingContext(source, width, height, preserveAspectRatio: true, renderViewAids: true);
            if (_viewShowState.ShowRulers)
                RenderRulers(source, CurrentTransform.Core, width, height);
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var plan = _viewColorModeState.Mode == PresentationViewColorMode.BlackAndWhite
            ? new PictureColorEffectPlan(Grayscale: false, BiLevelThreshold: 0.5, Brightness: null, Contrast: null)
            : new PictureColorEffectPlan(Grayscale: true, BiLevelThreshold: null, Brightness: null, Contrast: null);
        destination.DrawImage(ApplyColorEffectsWpf(bitmap, plan), new Rect(0, 0, width, height));
    }

    private static void RenderViewAids(
        DrawingContext dc,
        SlideTransformCore transform,
        PresentationViewShowState state)
    {
        var plan = PresentationViewAidPlanner.Build(transform, state);
        if (plan.Gridlines.Count == 0 && plan.Guides.Count == 0)
            return;

        var gridPen = new Pen(
            FreezeBrush(new SolidColorBrush(Color.FromArgb(0x54, 0x79, 0x79, 0x79))),
            1)
        {
            DashStyle = DashStyles.Dot,
        };
        gridPen.Freeze();
        foreach (var line in plan.Gridlines)
            dc.DrawLine(gridPen, new Point(line.StartX, line.StartY), new Point(line.EndX, line.EndY));

        var guidePen = new Pen(
            FreezeBrush(new SolidColorBrush(Color.FromArgb(0xA8, 0x6D, 0x9E, 0xEB))),
            1);
        guidePen.Freeze();
        foreach (var line in plan.Guides)
            dc.DrawLine(guidePen, new Point(line.StartX, line.StartY), new Point(line.EndX, line.EndY));
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
        bool preserveAspectRatio,
        bool renderViewAids = false)
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
            if (renderViewAids)
                RenderViewAids(dc, CurrentTransform.Core, _viewShowState);
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

        foreach (var command in SlideRenderExecutionPlanner.Plan(
                     _cachedOps,
                     _liveTransformPreviewOps,
                     SuppressedShapeIds,
                     ActiveTextEditShapeId))
        {
            RenderCommand(dc, command);
        }

        if (RenderPrintMarkup && Presentation is not null && Slide is not null)
            RenderPrintCommentCallouts(dc, Presentation, Slide);

        dc.Pop();
    }

    private static void RenderPrintCommentCallouts(DrawingContext dc, Presentation presentation, Slide slide)
    {
        foreach (var callout in SlidePrintMarkupPlanner.BuildCommentCallouts(presentation, slide))
        {
            var visual = callout.Visual;
            var fill = FreezeBrush(new SolidColorBrush(Color.FromRgb(
                visual.FillColor.R,
                visual.FillColor.G,
                visual.FillColor.B)));
            var border = new Pen(FreezeBrush(new SolidColorBrush(Color.FromRgb(
                visual.BorderColor.R,
                visual.BorderColor.G,
                visual.BorderColor.B))), visual.BorderThickness);
            var marker = FreezeBrush(new SolidColorBrush(Color.FromRgb(
                visual.MarkerColor.R,
                visual.MarkerColor.G,
                visual.MarkerColor.B)));
            var card = new Rect(
                visual.CardBounds.X,
                visual.CardBounds.Y,
                visual.CardBounds.Width,
                visual.CardBounds.Height);
            dc.DrawRectangle(fill, border, card);
            dc.DrawEllipse(
                marker,
                null,
                new Point(visual.AnchorCenter.X, visual.AnchorCenter.Y),
                visual.MarkerRadius,
                visual.MarkerRadius);
            DrawChartLabel(dc, visual.Author.Text, ToWpfRect(visual.Author.Bounds),
                visual.Author.IsBold, visual.Author.FontSize, TextAlignment.Left);
            DrawChartLabel(dc, visual.Body.Text, ToWpfRect(visual.Body.Bounds),
                visual.Body.IsBold, visual.Body.FontSize, TextAlignment.Left);
        }
    }

    private static Rect ToWpfRect(LayoutRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private SlideTransform ComputeViewTransform(
        double renderW,
        double renderH,
        double slideWidthDip,
        double slideHeightDip)
    {
        var rulerInset = _viewShowState.ShowRulers
            ? PresentationRulerTickPlanner.RulerThickness
            : 0;
        var plan = PresentationViewZoomPlanner.PlanStageTransform(
            Math.Max(0, renderW - rulerInset),
            Math.Max(0, renderH - rulerInset),
            slideWidthDip,
            slideHeightDip,
            _viewZoomState);
        return new SlideTransform(
            plan.Scale,
            plan.OffsetX + rulerInset,
            plan.OffsetY + rulerInset,
            plan.SlideWidthDip,
            plan.SlideHeightDip);
    }

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

        var autoFitPlan = ResolveShapeAutoFitPlan(shape);
        var bounds = autoFitPlan.Bounds;
        var materialPlan = ShapeMaterialRenderPlanner.Plan(shape);
        var shapeGeometry = GetShapeRenderGeometry(shape, materialPlan);
        bool hasTransform = !autoFitPlan.RenderTransform.IsIdentity;
        bool hasTextTransform = !autoFitPlan.TextRenderTransform.IsIdentity;
        bool hasAutoFitGeometryScale = !autoFitPlan.GeometryTransform.IsIdentity;

        if (hasTransform)
        {
            dc.PushTransform(ToWpfTransform(autoFitPlan.RenderTransform));
        }

        if (hasAutoFitGeometryScale)
        {
            dc.PushTransform(ToWpfTransform(autoFitPlan.GeometryTransform));
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

        if (hasAutoFitGeometryScale)
            dc.Pop();

        if (hasTransform)
            dc.Pop();

        // Draw text overlay. Text gets its own transform (rotation only, never flipH/flipV) so
        // that flipping a shape mirrors its outline/fill but keeps the text upright, matching
        // PowerPoint -- see ShapeTransformPlanner.PlanShapeTextRenderTransform.
        if (!suppressText && shape.Text is not null)
        {
            if (hasTextTransform)
                dc.PushTransform(ToWpfTransform(autoFitPlan.TextRenderTransform));

            RenderText(
                dc,
                shape.Text,
                bounds,
                shape.SmartArtRole == SmartArtSemanticRole.FollowNode,
                shape.UseImportedIncreasingCircleTextRaster);

            if (hasTextTransform)
                dc.Pop();
        }
    }

    private static ShapeAutoFitRenderPlan ResolveShapeAutoFitPlan(DrawOp.Shape shape)
        => ShapeAutoFitRenderPlanner.PlanRender(
            shape,
            request => BuildFormattedText(
                request.Paragraph,
                request.MaximumWidthDip,
                request.Wrap,
                useIdealMetrics: false).Height);

    private static void RenderShapeEffects(DrawingContext dc, DrawOp.Shape shape, Geometry shapeGeometry)
    {
        if (shape.Geometry.Contours.Count == 0) return;
        if (shape.Text is not null && shape.Fill is ResolvedFill.None) return;
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);

        if (plan.ShadowPasses.Count > 0)
        {
            foreach (var pass in plan.ShadowPasses)
            {
                var shadowBrush = new SolidColorBrush(
                    Color.FromArgb(pass.Alpha, pass.Color.R, pass.Color.G, pass.Color.B));
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

        // Reflection: mirror the shape's own fill+outline below itself, faded via an opacity
        // mask, exactly like the DrawOp.Picture reflection block in RenderPicture below —
        // just painting shapeGeometry instead of the decoded bitmap.
        if (plan.HasReflection)
        {
            var reflectionFillBrush = MakeBrush(shape.Fill, shape.BoundsDip);
            var reflectionPen = MakePen(shape.Outline);
            if (reflectionFillBrush is not null || reflectionPen is not null)
            {
                double reflectionCenterX = shape.BoundsDip.X + shape.BoundsDip.Width / 2;
                foreach (var pass in plan.ReflectionPasses)
                {
                    var reflectionMask = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                    };
                    reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                        Color.FromArgb(plan.ReflectionAlpha, 255, 255, 255), 0));
                    reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                        Color.FromArgb(0, 255, 255, 255), plan.ReflectionEndPos));
                    if (plan.ReflectionNeedsTerminalTransparentStop)
                        reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                            Color.FromArgb(0, 255, 255, 255), 1));

                    dc.PushTransform(new ScaleTransform(
                        1, plan.ReflectionScaleY, reflectionCenterX, plan.ReflectionPivotYDip));
                    dc.PushTransform(new TranslateTransform(pass.OffsetXDip, pass.OffsetYDip));
                    dc.PushOpacityMask(reflectionMask);
                    dc.PushOpacity(pass.Opacity);
                    dc.DrawGeometry(reflectionFillBrush, reflectionPen, shapeGeometry);
                    dc.Pop();
                    dc.Pop();
                    dc.Pop();
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

        // A shape may declare only a:bevelB with no a:bevelT (independently settable via
        // 3-D Format's Bottom bevel). Fall back to BevelBottom so that case still paints
        // a bevel overlay instead of silently rendering flat.
        var activeBevel = fx.BevelTop ?? fx.BevelBottom;
        if (hasBevel && activeBevel is not null)
        {
            var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, activeBevel, fx.LightDirDeg);
            DrawBevelOverlay(dc, shapeGeometry, bounds, highlight, shade,
                activeBevel.WidthDip, activeBevel.HeightDip, activeBevel.PresetName);
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
        catch (Exception ex)
        {
            // Skip undecodable images rather than crashing the renderer, but report the loss through
            // the ambient diagnostics sink (see SlideImageRenderDiagnostics) so an export command that
            // installed a collector can surface it instead of the slide looking silently incomplete.
            FreeP.App.Compositor.SlideImageRenderDiagnostics.ReportUndecodableImage(pic.ShapeId, ex.Message);
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

        var pictureTransform = ShapeTransformPlanner.PlanPictureTransform(pic);
        if (!pictureTransform.IsIdentity)
            dc.PushTransform(ToWpfTransform(pictureTransform));

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
                dc.DrawRoundedRectangle(
                    shadowBrush,
                    null,
                    shadowDest,
                    plan.FrameCornerRadiusDip,
                    plan.FrameCornerRadiusDip);
            }
            else if (pic.HasFrameClip && pic.PictureFrameGeometry == "ellipse")
                dc.DrawEllipse(shadowBrush, null, new System.Windows.Point(shadowDest.X + shadowDest.Width / 2, shadowDest.Y + shadowDest.Height / 2), shadowDest.Width / 2, shadowDest.Height / 2);
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
                var reflectionMask = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0.5, 0),
                    EndPoint = new System.Windows.Point(0.5, 1),
                };
                reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                    Color.FromArgb(plan.ReflectionAlpha, 255, 255, 255), 0));
                reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                    Color.FromArgb(0, 255, 255, 255),
                    plan.ReflectionEndPos));
                if (plan.ReflectionNeedsTerminalTransparentStop)
                    reflectionMask.GradientStops.Add(new System.Windows.Media.GradientStop(
                        Color.FromArgb(0, 255, 255, 255), 1));
                dc.PushTransform(new ScaleTransform(
                    1,
                    plan.ReflectionScaleY,
                    dest.Left + dest.Width / 2,
                    plan.ReflectionPivotY));
                dc.PushOpacityMask(reflectionMask);
                dc.PushOpacity(blurPass.Opacity);
                dc.DrawImage(bitmap, reflectionDest);
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }
        }

        // 18A: apply alpha opacity layer if needed
        bool hasAlpha = plan.HasAlphaOpacity;
        if (hasAlpha)
            dc.PushOpacity(plan.AlphaOpacity);

        // Wave 26: clip to frame geometry when a non-rect preset is specified.
        bool hasFrameClip = pic.HasFrameClip;
        if (hasFrameClip)
        {
            Geometry clipGeom = pic.PictureFrameGeometry switch
            {
                "ellipse" => new EllipseGeometry(new System.Windows.Point(dest.X + dest.Width / 2, dest.Y + dest.Height / 2), dest.Width / 2, dest.Height / 2),
                _         => new RectangleGeometry(
                    dest,
                    plan.FrameCornerRadiusDip,
                    plan.FrameCornerRadiusDip), // roundRect + others
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
                    dc.DrawRoundedRectangle(
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

        // Draw play button overlay for media shapes (already in scaled coords since a transform is pushed).
        if (plan.MediaPlayGlyph is { } playGlyph)
            DrawPlayButtonOverlay(dc, playGlyph);

        if (!pictureTransform.IsIdentity) dc.Pop();
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

    private static void DrawPlayButtonOverlay(
        DrawingContext dc,
        PictureMediaPlayGlyphPlan glyph)
    {
        var circleBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
        circleBrush.Freeze();
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
                isFilled: true,
                isClosed: true);
            ctx.LineTo(
                new Point(glyph.TriangleDip[1].X, glyph.TriangleDip[1].Y),
                true,
                false);
            ctx.LineTo(
                new Point(glyph.TriangleDip[2].X, glyph.TriangleDip[2].Y),
                true,
                false);
        }
        triGeo.Freeze();
        dc.DrawGeometry(Brushes.White, null, triGeo);
    }

    // ── Table ──────────────────────────────────────────────────────────────────

    private static void RenderTableWithTransform(DrawingContext dc, DrawOp.Table tableOp)
    {
        var transform = ShapeTransformPlanner.PlanShapeTransform(
            tableOp.BoundsDip,
            tableOp.RotationDeg,
            tableOp.FlipH,
            tableOp.FlipV);

        if (!transform.IsIdentity)
            dc.PushTransform(ToWpfTransform(transform));

        foreach (var cell in tableOp.Cells)
            RenderTableCellGeometry(dc, cell);

        if (!transform.IsIdentity)
            dc.Pop();

        // Text overlay. Text gets its own transform (rotation only, never flipH/flipV) so
        // that flipping a table mirrors its cell fills/borders but keeps the cell text
        // upright, matching PowerPoint and the shape-render fix -- see
        // ShapeTransformPlanner.PlanShapeTextRenderTransform.
        var textTransform = ShapeTransformPlanner.PlanShapeTransform(
            tableOp.BoundsDip,
            tableOp.RotationDeg,
            flipH: false,
            flipV: false);
        if (!textTransform.IsIdentity)
            dc.PushTransform(ToWpfTransform(textTransform));

        foreach (var cell in tableOp.Cells)
        {
            if (cell.Text is not null)
            {
                // Flipping the table swaps cell positions (a flipped column layout), which
                // the geometry pass above achieves by drawing the cell's own bounds under the
                // full flip+rotate transform. The text pass instead pre-mirrors the cell's
                // position (so its box still lands where the flipped cell now sits) and then
                // relies on the rotation-only pushed transform above, so the glyphs themselves
                // are never mirrored.
                var flippedBounds = ShapeTransformPlanner.FlipTableCellBounds(
                    cell.BoundsDip, tableOp.BoundsDip, tableOp.FlipH, tableOp.FlipV);
                RenderTableCellText(dc, cell.Text, flippedBounds, cell.Anchor);
            }
        }

        if (!textTransform.IsIdentity)
            dc.Pop();
    }

    private static void RenderTableCellGeometry(DrawingContext dc, TableCellOp cell)
    {
        var rect = new Rect(cell.BoundsDip.X, cell.BoundsDip.Y, cell.BoundsDip.Width, cell.BoundsDip.Height);

        // Fill
        var fillBrush = MakeBrush(cell.Fill, cell.BoundsDip);
        if (fillBrush is not null)
            dc.DrawRectangle(fillBrush, null, rect);

        // Per-side borders draw as single-pixel lines along each edge to avoid overlap issues.
        var borderSink = new TableCellBorderRenderSink(dc);
        TableCellBorderRenderSequence.Dispatch(cell, ref borderSink);
    }

    private readonly struct TableCellBorderRenderSink(DrawingContext drawingContext) :
        ITableCellBorderRenderSink
    {
        public void Render(ResolvedOutline outline, LayoutPoint start, LayoutPoint end) =>
            DrawCellBorder(
                drawingContext,
                outline,
                new Point(start.X, start.Y),
                new Point(end.X, end.Y));
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
        if (!TextParagraphNativeRenderDispatcher.TryRenderTableCell(
                text,
                bounds,
                anchor,
                (paragraph, width, wrap) =>
                {
                    var formatted = BuildFormattedText(paragraph, width, wrap);
                    return new TextNativeMeasurement<FormattedText>(
                        formatted,
                        formatted.Height,
                        formatted.WidthIncludingTrailingWhitespace);
                },
                (formatted, placement) =>
                    dc.DrawText(formatted, new Point(placement.X, placement.Y))))
            RenderText(dc, text, bounds);
    }

    // ── Chart ──────────────────────────────────────────────────────────────────

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

    private static void RenderText(
        DrawingContext dc,
        ResolvedTextLayout text,
        LayoutRect bounds,
        bool useNativeBulletMarkerFallback = false,
        bool useImportedIncreasingCircleTextRaster = false)
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
            RenderTextCore(
                dc,
                text,
                orientation.TextBounds,
                useNativeBulletMarkerFallback,
                useImportedIncreasingCircleTextRaster);
            dc.Pop();
            return;
        }

        RenderTextCore(
            dc,
            text,
            orientation.TextBounds,
            useNativeBulletMarkerFallback,
            useImportedIncreasingCircleTextRaster);
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

        TextParagraphNativeRenderDispatcher.RenderStacked(
            renderText,
            plan,
            (layout, run, glyph) => DrawStackedGlyphWpf(dc, layout, bounds, run, glyph));
    }

    // Wave 22B: multi-column text layout helper.
    // Greedy paragraph-level assignment: fill column 1 top-to-bottom, then column 2, etc.
    private static void RenderTextCoreColumns(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        if (TryRenderContinuousColumnFlow(dc, text, bounds))
            return;

        var plan = TextLayoutPlanner.PlanMeasuredColumns<FormattedText>(
            text,
            bounds,
            request =>
            {
                var formattedText = BuildFormattedText(
                    request.Paragraph,
                    request.MaxWidthDip,
                    request.Text.Wrap,
                    request.UseIdealMetrics);
                return new TextNativeMeasurement<FormattedText>(
                    formattedText,
                    formattedText.Height,
                    formattedText.WidthIncludingTrailingWhitespace);
            });
        RenderMeasuredParagraphsWpf(dc, plan, bounds, applyImportedAptosRasterPolicy: false);
    }

    private static bool TryRenderContinuousColumnFlow(
        DrawingContext dc,
        ResolvedTextLayout text,
        LayoutRect bounds)
    {
        const double importedAptosFallbackScale = 0.93;
        var plan = TextLayoutPlanner.PlanMeasuredContinuousColumnFlow<FormattedText>(
            text,
            bounds,
            request =>
            {
                // Imported column breakpoints align with WPF display metrics, so the host
                // supplies display-mode measurements while the shared planner owns the flow.
                var formattedText = BuildFormattedText(
                    request.Paragraph,
                    request.MaxWidthDip,
                    request.Wrap,
                    useIdealMetrics: false);
                return new TextNativeMeasurement<FormattedText>(
                    formattedText,
                    formattedText.Height,
                    formattedText.WidthIncludingTrailingWhitespace);
            },
            paragraph => string.Equals(
                paragraph.Runs[0].FontFamily,
                "Aptos",
                StringComparison.OrdinalIgnoreCase)
                    ? importedAptosFallbackScale
                    : 1.0);
        if (!plan.IsApplicable)
            return false;

        foreach (var line in plan.Lines)
        {
            var placement = line.Placement;
            var formatted = line.Artifact;
            if (placement.IsFirstLine && line.Paragraph.IndentDip > 0 && formatted.MaxTextWidth > 0)
                formatted.MaxTextWidth = placement.MaxWidthDip;
            if (line.HorizontalScale < 1.0)
                dc.PushTransform(new ScaleTransform(line.HorizontalScale, 1.0, placement.X, placement.Y));
            dc.DrawText(formatted, new Point(placement.X, placement.Y));
            if (line.HorizontalScale < 1.0)
                dc.Pop();
        }

        return true;
    }

    private static void RenderTextCore(
        DrawingContext dc,
        ResolvedTextLayout text,
        LayoutRect bounds,
        bool useNativeBulletMarkerFallback = false,
        bool useImportedIncreasingCircleText = false)
    {
        // Wave 22B: multi-column layout
        if (text.ColumnCount > 1)
        {
            RenderTextCoreColumns(dc, text, bounds);
            return;
        }

        var plan = TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>(
            text,
            bounds,
            request =>
            {
                var formattedText = BuildFormattedText(
                    request.Paragraph,
                    request.MaxWidthDip,
                    request.Text.Wrap,
                    request.UseIdealMetrics);
                return new TextNativeMeasurement<FormattedText>(
                    formattedText,
                    formattedText.Height,
                    formattedText.WidthIncludingTrailingWhitespace);
            });
        RenderMeasuredParagraphsWpf(
            dc,
            plan,
            bounds,
            applyImportedAptosRasterPolicy: true,
            useNativeBulletMarkerFallback,
            useImportedIncreasingCircleText);
    }

    private static void RenderMeasuredParagraphsWpf(
        DrawingContext dc,
        TextMeasuredBlockLayoutPlan<FormattedText> plan,
        LayoutRect bounds,
        bool applyImportedAptosRasterPolicy,
        bool useNativeBulletMarkerFallback = false,
        bool useImportedIncreasingCircleText = false)
    {
        var renderText = plan.RenderText;
        bool useImportedAptosRasterScale =
            applyImportedAptosRasterPolicy && UsesImportedAptosFont(renderText);
        bool useImportedAptosBodyRasterScale =
            applyImportedAptosRasterPolicy && UsesImportedAptosBodyFont(renderText);
        TextParagraphNativeRenderDispatcher.Render(
            plan,
            new(
                bullet => DrawBulletPlacementWpf(dc, bullet, useNativeBulletMarkerFallback),
                (paragraph, placement) =>
                    RenderParaWithMath(dc, paragraph, placement.X, placement.Y),
                (paragraph, placement) => RenderParaWithEffects(
                    dc,
                    paragraph,
                    placement.X,
                    placement.Y,
                    placement.MaxWidthDip,
                    renderText.Wrap,
                    renderText,
                    bounds),
                (paragraph, placement) => RenderParaWithTabs(
                    dc,
                    paragraph,
                    placement.X,
                    placement.Y,
                    paragraph.TabStops),
                (paragraph, placement) => RenderParaWithBaseline(
                    dc,
                    paragraph,
                    placement.X,
                    placement.Y,
                    placement.MaxWidthDip),
                (paragraph, formatted, placement) => DrawPlainParagraphWpf(
                    dc,
                    paragraph,
                    formatted,
                    placement,
                    bounds,
                    useImportedAptosRasterScale,
                    useImportedAptosBodyRasterScale,
                    useImportedIncreasingCircleText)));
    }

    private static void DrawPlainParagraphWpf(
        DrawingContext dc,
        ResolvedParagraph paragraph,
        FormattedText formatted,
        TextParagraphPlacement placement,
        LayoutRect bounds,
        bool useImportedAptosRasterScale,
        bool useImportedAptosBodyRasterScale,
        bool useImportedIncreasingCircleText)
    {
        if (paragraph.IndentDip > 0 && formatted.MaxTextWidth > 0)
            formatted.MaxTextWidth = placement.MaxWidthDip;
        bool useImportedAptosDisplayRasterScale = UsesImportedAptosDisplayFont(paragraph);
        if (useImportedAptosBodyRasterScale)
            formatted.SetFontWeight(FontWeights.Light, 0, formatted.Text.Length);
        if (useImportedAptosRasterScale)
        {
            double scaleX = useImportedIncreasingCircleText
                ? ImportedIncreasingCircleWpfRasterScaleX
                : useImportedAptosBodyRasterScale
                ? ImportedAptosBodyWpfLightRasterScale
                : ImportedAptosWpfRasterScale;
            double centerX = paragraph.Align == TextAlign.Center
                ? bounds.X + bounds.Width * 0.5
                : placement.X;
            double scaleY = useImportedIncreasingCircleText
                ? ImportedIncreasingCircleWpfRasterScaleY
                : useImportedAptosDisplayRasterScale
                    ? ImportedAptosDisplayWpfRasterScaleY
                    : 1.0;
            double pivotY = useImportedIncreasingCircleText
                ? placement.Y
                : useImportedAptosDisplayRasterScale
                ? placement.Y + formatted.Height
                : placement.Y;
            dc.PushTransform(new ScaleTransform(scaleX, scaleY, centerX, pivotY));
        }

        dc.DrawText(formatted, new Point(placement.X, placement.Y));
        if (useImportedAptosRasterScale)
            dc.Pop();
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
    private static void DrawBulletPlacementWpf(
        DrawingContext dc,
        TextBulletPlacement bullet,
        bool useNativeBulletMarkerFallback)
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
            bullet.Color, bullet.X, bullet.Y, useNativeBulletMarkerFallback);
    }

    private static void DrawBulletWpf(
        DrawingContext dc,
        string bulletText,
        string fontFamily,
        double fontSizePt,
        SrgbColor color,
        double x,
        double y,
        bool useNativeBulletMarkerFallback)
    {
        if (string.IsNullOrEmpty(bulletText)) return;
        // WPF can draw the imported Aptos paragraph text through its fallback chain, but
        // the standalone bullet glyph is dropped when the unavailable Office face is used
        // directly. Keep the bullet host policy aligned with Avalonia's Arial fallback.
        var typeface = new Typeface(new FontFamily(
                useNativeBulletMarkerFallback
                    ? ResolvePowerPointFontFamily(fontFamily)
                    : fontFamily),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double emPx = fontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        if (brush.CanFreeze) brush.Freeze();

        // WPF's fallback chain can measure the Office bullet family but drops the
        // standalone U+2022 glyph during DrawingContext rasterization. PowerPoint
        // renders this marker as a filled disc, so preserve that semantic shape on
        // this host while leaving other marker strings on the text path.
        if (useNativeBulletMarkerFallback && bulletText == "\u2022")
        {
            double radius = emPx * 0.12;
            dc.DrawEllipse(
                brush,
                null,
                new Point(x + emPx * 0.175, y + emPx * 0.57),
                radius,
                radius);
            return;
        }

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

    private static string ResolvePowerPointFontFamily(string fontFamily) =>
        string.Equals(fontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fontFamily, "Aptos Display", StringComparison.OrdinalIgnoreCase)
            ? "Arial"
            : fontFamily;

    /// <summary>
    /// Renders a paragraph run-by-run, expanding tab characters to the next tab stop position.
    /// Default tab interval is 96 DIP (1 inch at 96 DPI) when tab stops are exhausted.
    /// </summary>
    private static void RenderParaWithTabs(
        DrawingContext dc, ResolvedParagraph para,
        double startX, double startY,
        IReadOnlyList<ResolvedTabStop> tabStops) =>
        // Owns TextLayoutPlanner.PlanTabStops, TextLayoutPlanner.PlanTabLeaderFill(...),
        // and the former DrawTabLeaderWpf flow.
        TextNativeRenderSequence.RenderTabs(
            para,
            startX,
            startY,
            tabStops,
            (run, text, rightToLeft) => BuildNativeTextArtifact(
                run, text, fontScale: 1.0, rightToLeft),
            (artifact, x, y) => dc.DrawText(artifact, new Point(x, y)));

    /// <summary>
    /// Draws plain runs with authored DrawingML baseline offsets while keeping
    /// one shared line baseline. Tabs, math, and text effects retain their
    /// existing renderer-specific owners.
    /// </summary>
    internal static void RenderParaWithBaseline(
        DrawingContext dc, ResolvedParagraph para,
        double startX, double startY, double maxWidth) =>
        // Owns TextLayoutPlanner.PlanBaselineLines and PlanInlineBaselineLine.
        TextNativeRenderSequence.RenderBaseline(
            para,
            startX,
            startY,
            maxWidth,
            BuildNativeTextArtifact,
            (artifact, x, y) => dc.DrawText(artifact, new Point(x, y)));

    // ── Theme 27: math rendering ────────────────────────────────────────────────

    /// <summary>
    /// Renders a paragraph that contains one or more OMML math runs by calling
    /// <see cref="MathBoxRenderPlanner.Plan"/> for each math run and drawing the
    /// resulting renderer-neutral ops as WPF primitives.
    /// Non-math runs in the same paragraph are drawn with plain FormattedText.
    /// Inline baseline geometry comes from
    /// <see cref="TextLayoutPlanner.PlanInlineBaselineLine"/>; WPF retains native
    /// measurement, glyph construction, brushes, and draw calls.
    /// Marked internal (not private) so FreeP.App.Host.Tests can call it directly.
    /// </summary>
    internal static void RenderParaWithMath(
        DrawingContext dc,
        ResolvedParagraph para,
        double startX, double startY)
    {
        // TextNativeRenderSequence owns TextLayoutPlanner.PlanInlineBaselineLine,
        // new TextInlineRunMeasure(metrics.Width, metrics.Ascent, metrics.Height), and
        // MathBoxRenderPlanner.Plan(...); WPF retains BuildSingleRunFormattedTextAt and DrawMathOpWpf.
        TextNativeRenderSequence.RenderMath(
            para,
            startX,
            startY,
            (run, text, rightToLeft) => BuildInlineNativeTextArtifact(
                run, text, fontScale: 1.0, rightToLeft),
            (artifact, x, y) => dc.DrawText(artifact, new Point(x, y)),
            operation => DrawMathOpWpf(dc, operation));
    }

    private static TextNativeArtifact<FormattedText> BuildNativeTextArtifact(
        ResolvedRun run,
        string text,
        double fontScale,
        bool rightToLeft)
    {
        var formatted = BuildSingleRunFormattedTextAt(
            run,
            text,
            fontScale,
            ToFlowDirection(rightToLeft));
        return new(
            formatted,
            formatted.WidthIncludingTrailingWhitespace,
            formatted.Baseline,
            formatted.Height);
    }

    private static TextNativeArtifact<FormattedText> BuildInlineNativeTextArtifact(
        ResolvedRun run,
        string text,
        double fontScale,
        bool rightToLeft)
    {
        var formatted = BuildSingleRunFormattedTextAt(
            run,
            text,
            fontScale,
            ToFlowDirection(rightToLeft));
        return new(formatted, formatted.Width, formatted.Baseline, formatted.Height);
    }

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
        var glyphRun = run.WithText(glyph.Text);
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
                    var endpoints = GradientFillRenderPlanner.PlanLinearEndpoints(
                        g.AngleDegrees,
                        GradientEndpointProfile.CenteredDirection);
                    var lb = new LinearGradientBrush(BuildGradientStops(g),
                        new Point(endpoints.Start.X, endpoints.Start.Y),
                        new Point(endpoints.End.X, endpoints.End.Y))
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
        foreach (var stop in GradientFillRenderPlanner.ExpandStops(g, easePositions))
        {
            stops.Add(new System.Windows.Media.GradientStop(
                Color.FromArgb(stop.Alpha, stop.Color.R, stop.Color.G, stop.Color.B),
                stop.Position));
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
        var endpoints = GradientFillRenderPlanner.PlanLinearEndpoints(g.AngleDegrees);
        var startPoint = new Point(endpoints.Start.X, endpoints.Start.Y);
        var endPoint = new Point(endpoints.End.X, endpoints.End.Y);

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
        var fg = Color.FromRgb(pat.ForegroundColor.R, pat.ForegroundColor.G, pat.ForegroundColor.B);
        var bg = Color.FromRgb(pat.BackgroundColor.R, pat.BackgroundColor.G, pat.BackgroundColor.B);
        var plan = PatternFillRenderPlanner.Plan(pat.Preset, PatternFillRendererProfile.WpfVector);
        return plan switch
        {
            PatternFillRenderPlan.Solid solid =>
                new SolidColorBrush(ResolvePatternColor(solid.Color, fg, bg)),
            PatternFillRenderPlan.VectorTile tile => BuildPatternTileBrush(tile, fg, bg),
            _ => new SolidColorBrush(fg)
        };
    }

    private static DrawingBrush BuildPatternTileBrush(
        PatternFillRenderPlan.VectorTile tile,
        Color foreground,
        Color background)
    {
        var drawings = new DrawingGroup();
        foreach (var primitive in tile.Primitives)
        {
            var color = ResolvePatternColor(primitive.Color, foreground, background);
            switch (primitive)
            {
                case PatternFillVectorPrimitive.Rectangle rectangle:
                    drawings.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(color),
                        null,
                        new RectangleGeometry(new Rect(
                            rectangle.X,
                            rectangle.Y,
                            rectangle.Width,
                            rectangle.Height))));
                    break;
                case PatternFillVectorPrimitive.Ellipse ellipse:
                    drawings.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(color),
                        null,
                        new EllipseGeometry(
                            new Point(ellipse.CenterX, ellipse.CenterY),
                            ellipse.RadiusX,
                            ellipse.RadiusY)));
                    break;
                case PatternFillVectorPrimitive.LinePath path:
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        foreach (var segment in path.Segments)
                        {
                            context.BeginFigure(
                                new Point(segment.StartX, segment.StartY),
                                isFilled: false,
                                isClosed: false);
                            context.LineTo(
                                new Point(segment.EndX, segment.EndY),
                                isStroked: true,
                                isSmoothJoin: false);
                        }
                    }
                    drawings.Children.Add(new GeometryDrawing(
                        null,
                        new Pen(new SolidColorBrush(color), path.StrokeWidth),
                        geometry));
                    break;
            }
        }

        return new DrawingBrush(drawings)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile.Width, tile.Height),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static Color ResolvePatternColor(
        PatternFillColorRole role,
        Color foreground,
        Color background) =>
        role == PatternFillColorRole.Foreground ? foreground : background;

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

    private static Geometry ToGeometry(ChartLinePathFigurePrimitive figure)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(figure.Start), isFilled: false, isClosed: false);
            foreach (var segment in figure.Segments)
            {
                switch (segment.Kind)
                {
                    case ChartLinePathSegmentKind.CubicBezier:
                        ctx.BezierTo(
                            ToPoint(segment.Control1),
                            ToPoint(segment.Control2),
                            ToPoint(segment.End),
                            isStroked: true,
                            isSmoothJoin: true);
                        break;

                    default:
                        ctx.LineTo(
                            ToPoint(segment.End),
                            isStroked: true,
                            isSmoothJoin: true);
                        break;
                }
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

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

    private static void DrawChartMarker(DrawingContext dc, ChartMarkerRenderPlan marker) =>
        ChartMarkerRenderPrimitiveDispatcher.Dispatch(
            marker.Primitives,
            new ChartMarkerRenderPrimitiveSink(dc));

    private sealed class ChartMarkerRenderPrimitiveSink(DrawingContext dc) :
        IChartMarkerRenderPrimitiveSink
    {
        public void Render(ChartMarkerRenderPrimitive.Ellipse ellipse) =>
            dc.DrawEllipse(
                ellipse.Fill is { } fill ? ToBrush(fill) : null,
                ellipse.Stroke is { } stroke ? ToPen(stroke) : null,
                ToPoint(ellipse.Center),
                ellipse.RadiusX,
                ellipse.RadiusY);

        public void Render(ChartMarkerRenderPrimitive.Rectangle rectangle) =>
            dc.DrawRectangle(
                rectangle.Fill is { } fill ? ToBrush(fill) : null,
                rectangle.Stroke is { } stroke ? ToPen(stroke) : null,
                ToRect(rectangle.Bounds));

        public void Render(ChartMarkerRenderPrimitive.Path path) =>
            dc.DrawGeometry(
                path.Geometry.Fill is { } fill ? ToBrush(fill) : null,
                path.Stroke is { } stroke ? ToPen(stroke) : null,
                ToMarkerGeometry(path.Geometry));

        public void Render(ChartMarkerRenderPrimitive.Line line) =>
            dc.DrawLine(ToPen(line.Stroke), ToPoint(line.Start), ToPoint(line.End));
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
                    ctx.BeginFigure(point, path.Fill.HasValue, path.IsClosed);
                else
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: false);
            }
        }

        if (geometry.CanFreeze) geometry.Freeze();
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
        try
        {
            _cachedOps = SlideCompositor.Compose(
                presentation,
                slide,
                slideIndex < 0 ? 0 : slideIndex,
                RenderSlideBackground);
        }
        catch (Exception)
        {
            // Composition runs from OnRender, where an escaping exception is fatal in WPF. Worse, the
            // failure repeats: _cachedOps stays null, so the next paint recomposes and throws again —
            // one malformed shape on the active slide would crash the app in a loop with no way back
            // to the deck. Cache an empty result so the slide degrades to blank for this render pass
            // instead, and the rest of the app stays usable.
            _cachedOps = Array.Empty<DrawOp>();
        }
    }

    // ── Accessibility: UI Automation (R134) ────────────────────────────────────
    //
    // Mirrors FreeX.App.UI.GridView's automation pattern (src/FreeX.App.UI/GridView.cs): a
    // single custom peer for the surface itself (here, the slide canvas) exposing
    // ISelectionProvider, plus one lightweight per-item peer per selectable element (here, per
    // shape) exposing ISelectionItemProvider. Like GridView's cells, shapes have no backing WPF
    // visual -- SlideCanvas paints everything directly via OnRender/DrawingContext -- so the
    // shape peers are purely virtual UIA nodes: GetBoundingRectangleCore projects the shape's
    // EMU bounds through the canvas's live CurrentTransform instead of reading a real element's
    // layout rect.
    //
    // PresentationCanvasAutomationSession owns the virtual tree, identity, roles, selection
    // snapshots, deltas, and focus intent. This peer only translates that contract to WPF UIA
    // providers/events and maps presentation coordinates to screen coordinates.
    private sealed class SlideCanvasAutomationPeer :
        FrameworkElementAutomationPeer,
        ISelectionProvider
    {
        private readonly PresentationCanvasAutomationPeerCoordinator<SlideShapeAutomationPeer>
            _coordinator;

        public SlideCanvasAutomationPeer(SlideCanvas owner) : base(owner)
        {
            _coordinator = new(
                owner._canvasAutomation,
                () => owner.Presentation,
                () => owner.Slide,
                () => owner._editingSession?.SelectedShapeIds,
                shapeId => new SlideShapeAutomationPeer(this, shapeId));
        }

        private SlideCanvas OwnerCanvas => (SlideCanvas)Owner;

        internal PresentationCanvasAutomationPeerCoordinator<SlideShapeAutomationPeer>
            Coordinator => _coordinator;

        public bool CanSelectMultiple => _coordinator.CanSelectMultiple;

        public bool IsSelectionRequired => _coordinator.IsSelectionRequired;

        public IRawElementProviderSimple[] GetSelection()
        {
            return
            [
                .. _coordinator.GetSelection().Select(ProviderFromPeer)
            ];
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface switch
            {
                PatternInterface.Selection => this,
                _ => base.GetPattern(patternInterface)
            };

        protected override List<AutomationPeer> GetChildrenCore() =>
            _coordinator.SynchronizeChildren().Cast<AutomationPeer>().ToList();

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            ToNativeRole(_coordinator.CanvasDescriptor.Role);

        protected override string GetClassNameCore() =>
            _coordinator.CanvasDescriptor.ClassName;

        protected override string GetNameCore() =>
            _coordinator.CanvasDescriptor.Name;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;

        internal static AutomationControlType ToNativeRole(PresentationCanvasAutomationRole role) =>
            PresentationCanvasAutomationRoleMapper.Map(
                role,
                AutomationControlType.Pane,
                AutomationControlType.Image,
                AutomationControlType.DataGrid,
                AutomationControlType.Custom);

        /// <summary>
        /// Projects a shape's EMU bounds through the canvas's live slide→screen
        /// <see cref="CurrentTransform"/> (the same transform <see cref="RenderToDrawingContext"/>
        /// paints with) and then to actual screen coordinates, mirroring
        /// GridViewAutomationPeer.GetCellBoundingRectangle. Falls back to the un-screen-mapped
        /// rectangle if the canvas is not currently connected to a visual tree/presentation
        /// source (e.g. queried before the window is shown), matching GridView's own fallback.
        /// </summary>
        internal Rect GetShapeBoundingRectangle(uint shapeId)
        {
            if (!_coordinator.TryProjectLocalBounds(
                    shapeId,
                    OwnerCanvas.CurrentTransform.Core,
                    out var localBounds))
                return Rect.Empty;

            var topLeft = new Point(localBounds.Left, localBounds.Top);
            var bottomRight = new Point(localBounds.Right, localBounds.Bottom);

            try
            {
                var screenTopLeft = OwnerCanvas.PointToScreen(topLeft);
                var screenBottomRight = OwnerCanvas.PointToScreen(bottomRight);
                return new Rect(screenTopLeft, screenBottomRight);
            }
            catch (InvalidOperationException)
            {
                return new Rect(topLeft, bottomRight);
            }
        }

        /// <summary>
        /// Raises UIA selection-changed notifications by diffing the newly-selected shape ids
        /// against the last-notified set: newly-selected shapes get an IsSelected property
        /// change plus a SelectionItemPatternOnElementSelected event (the last one added also
        /// gets AutomationFocusChanged, matching GridViewAutomationPeer's active-cell focus
        /// semantics), and deselected shapes get an IsSelected property change to false. Called
        /// from <see cref="OnEditingSessionSelectionChangedForAutomation"/> whenever
        /// EditingSession.SelectionChanged fires.
        /// </summary>
        internal void NotifySelectionChanged(PresentationCanvasAutomationSelectionDelta delta)
        {
            foreach (var change in _coordinator.GetSelectionChanges(delta))
            {
                change.Peer.RaisePropertyChangedEvent(
                    SelectionItemPatternIdentifiers.IsSelectedProperty,
                    change.WasSelected,
                    change.IsSelected);
                if (change.IsSelected)
                    change.Peer.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
            }

            if (delta.FocusIntent == PresentationCanvasAutomationFocusIntent.None)
                return;

            var focusChange = _coordinator.GetFocusChange(delta);
            focusChange.PreviousPeer?.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
            focusChange.CurrentPeer?.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        }
    }

    private sealed class SlideShapeAutomationPeer(SlideCanvasAutomationPeer parent, uint shapeId) :
        AutomationPeer,
        ISelectionItemProvider
    {
        private readonly PresentationCanvasAutomationShapePeerAdapter<
            SlideShapeAutomationPeer,
            AutomationControlType,
            Rect> _adapter = new(
                parent.Coordinator,
                shapeId,
                SlideCanvasAutomationPeer.ToNativeRole,
                AutomationControlType.Custom,
                parent.GetShapeBoundingRectangle);

        public bool IsSelected => _adapter.IsSelected;

        public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(parent);

        public void Select() => _adapter.Select();

        public void AddToSelection() => _adapter.AddToSelection();

        public void RemoveFromSelection() => _adapter.RemoveFromSelection();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface switch
            {
                PatternInterface.SelectionItem => this,
                _ => null
            };

        protected override string GetNameCore() => _adapter.Name;

        protected override AutomationControlType GetAutomationControlTypeCore() => _adapter.Role;

        protected override string GetClassNameCore() => _adapter.ClassName;

        protected override string GetAutomationIdCore() => _adapter.AutomationId;

        protected override Rect GetBoundingRectangleCore() => _adapter.Bounds;

        protected override List<AutomationPeer> GetChildrenCore() => [];

        protected override Point GetClickablePointCore()
        {
            var bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty
                ? new Point(double.NaN, double.NaN)
                : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override string GetHelpTextCore() => _adapter.HelpText;

        protected override string GetItemStatusCore() => string.Empty;

        protected override string GetItemTypeCore() => string.Empty;

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override string GetLocalizedControlTypeCore() => _adapter.LocalizedControlType;

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        protected override bool HasKeyboardFocusCore() => _adapter.HasKeyboardFocus;

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => true;

        protected override bool IsOffscreenCore() => GetBoundingRectangleCore().IsEmpty;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => false;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override void SetFocusCore()
        {
        }
    }
}
