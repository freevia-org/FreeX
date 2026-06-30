using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
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
/// The control is viewer-only — interactive editing adorners are deferred to Wave 14C.
/// The slide is scaled uniformly to fit the control's available size (letterboxed).
/// </summary>
public sealed class SlideCanvas : Control
{
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

    private Presentation? _presentation;
    private Slide? _slide;
    private int _slideIndex;

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

    public int SlideIndex
    {
        get => _slideIndex;
        set { SetAndRaise(SlideIndexProperty, ref _slideIndex, value); Refresh(); }
    }

    // ── Current slide→screen transform (updated on every render pass) ───────────

    /// <summary>
    /// The current slide→screen transform.  Updated every time <see cref="Render"/> runs.
    /// The gesture handler and adorner layer read this to map between coordinate spaces.
    /// </summary>
    public SlideTransformCore CurrentTransform { get; private set; } = SlideTransformCore.Identity;

    // ── Cached draw ops ──────────────────────────────────────────────────────

    private IReadOnlyList<DrawOp>? _cachedOps;
    private double _slideWidthDip;
    private double _slideHeightDip;

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
        InvalidateVisual();
    }

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
        CurrentTransform = SlideTransformCore.Compute(renderW, renderH, _slideWidthDip, _slideHeightDip);

        var matrix = Matrix.CreateScale(CurrentTransform.Scale, CurrentTransform.Scale)
            * Matrix.CreateTranslation(CurrentTransform.OffsetX, CurrentTransform.OffsetY);
        using var _ = context.PushTransform(matrix);

        foreach (var op in _cachedOps)
            RenderOp(context, op);
    }

    private void RenderOp(DrawingContext dc, DrawOp op)
    {
        switch (op)
        {
            case DrawOp.Background bg:
                RenderBackground(dc, bg);
                break;
            case DrawOp.Shape shape:
                // DA1: skip shapes that the slideshow has not yet revealed (entrance animation).
                if (shape.ShapeId != 0 && SuppressedShapeIds.Contains(shape.ShapeId)) break;
                RenderShape(dc, shape);
                break;
            case DrawOp.Picture pic:
                if (pic.ShapeId != 0 && SuppressedShapeIds.Contains(pic.ShapeId)) break;
                RenderPicture(dc, pic);
                break;
            case DrawOp.Table table:
                if (table.ShapeId != 0 && SuppressedShapeIds.Contains(table.ShapeId)) break;
                RenderTable(dc, table);
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
        var brush = MakeBrush(bg.Fill, bg.BoundsDip);
        if (brush is null) return;
        dc.FillRectangle(brush,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null) return;

        var bounds = shape.BoundsDip;
        bool hasTransform = shape.RotationDeg != 0 || shape.FlipH || shape.FlipV;

        IDisposable? transformScope = null;
        if (hasTransform)
            transformScope = dc.PushTransform(ToAvaloniaMatrix(ShapeTransformPlanner.PlanShapeTransform(shape)));

        if (shape.Effects is not null)
            RenderShapeEffects(dc, shape);

        if (shape.Geometry.Contours.Count > 0)
        {
            var geometry  = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (geometry is not null)
            {
                var fillBrush = MakeBrush(shape.Fill, bounds);
                var pen       = MakePen(shape.Outline);
                dc.DrawGeometry(fillBrush, pen, geometry);
            }
        }

        if (shape.Effects is not null)
            RenderShapeBevel(dc, shape);

        if (shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        transformScope?.Dispose();
    }

    private static void RenderShapeEffects(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0) return;
        var plan = ShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects);

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
            var cBrush     = new SolidColorBrush(Color.FromArgb(200, cColor.R, cColor.G, cColor.B));
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
        double bw = Math.Min(bevelW, w / 3);
        double bh = Math.Min(bevelH, h / 3);

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

        var dest = new Rect(pic.DestDip.X, pic.DestDip.Y, pic.DestDip.Width, pic.DestDip.Height);

        // 18A: colour effects — produce a modified bitmap via pixel manipulation.
        // BN1: ApplyColorEffectsAvalonia returns null when GDI+/libgdiplus is unavailable;
        //      in that case we keep the original uneffected bitmap so the picture isn't blank.
        IImage renderBitmap = bitmap;
        var effectPlan = PictureColorEffectPlanner.Plan(pic);
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

        // 18A: alpha opacity
        if (pic.AlphaModPct.HasValue && pic.AlphaModPct.Value < 1.0)
            alphaScope = dc.PushOpacity(Math.Max(0, Math.Min(1, pic.AlphaModPct.Value)));

        // 18A: crop — expand the dest rect to show only the cropped sub-region of the image.
        // We do this by scaling the dest rect outward so that the uncropped image region maps to
        // exactly dest. No clip is needed; the shape bounds act as a natural clip.
        if (pic.HasCrop)
        {
            // The visible fraction in each dimension
            double visW = 1.0 - pic.CropLeft - pic.CropRight;
            double visH = 1.0 - pic.CropTop  - pic.CropBottom;
            if (visW > 0 && visH > 0)
            {
                // Full image rendered into expanded rect so that the visible portion fills dest
                double fullW = dest.Width  / visW;
                double fullH = dest.Height / visH;
                double offX  = dest.X - pic.CropLeft  * fullW;
                double offY  = dest.Y - pic.CropTop   * fullH;
                var expandedDest = new Rect(offX, offY, fullW, fullH);

                // Clip to dest so the cropped-off margins aren't visible
                using var clip = dc.PushClip(dest);
                dc.DrawImage(renderBitmap, expandedDest);
            }
        }
        else
        {
            dc.DrawImage(renderBitmap, dest);
        }

        alphaScope?.Dispose();

        if (pic.Outline is ResolvedOutline.Visible visOutline)
        {
            var pen = MakePen(visOutline);
            if (pen is not null)
                dc.DrawRectangle(null, pen, dest);
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

    // ── Chart ────────────────────────────────────────────────────────────────

    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOp)
    {
        var bounds = chartOp.BoundsDip;
        var chart  = chartOp.ChartShape;

        dc.FillRectangle(Brushes.White,
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var framePen = new Pen(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF)), 0.5);
        dc.DrawRectangle(null, framePen,
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));

        var frame = ChartRenderPlanner.BuildFramePlan(chart, ToPlanRect(bounds));
        bool hasLegend = frame.HasLegend;
        bool legendRight = frame.LegendRight;
        bool isPie = frame.IsPie;
        bool isBar = frame.IsBar;
        bool isScatterLike = frame.IsScatterLike;
        bool isRadar = frame.IsRadar;
        double legendAreaW = frame.LegendAreaWidth;
        double legendAreaH = frame.LegendAreaHeight;
        double margin = ChartRenderPlanner.Margin;
        double legendH = ChartRenderPlanner.LegendHeight;

        if (chart.Title is not null)
            DrawChartLabel(dc, chart.Title,
                ToRect(frame.TitleBounds!.Value),
                isBold: true, fontSize: 9.0, align: TextAlignment.Center);

        if (!frame.HasPlot) return;

        var plot = frame.Plot;
        double plotLeft = plot.X;
        double plotTop = plot.Y;
        double plotW = plot.Width;
        double plotH = plot.Height;

        var gridLinePlans = ChartRenderPlanner.BuildMajorGridLinePlans(chart, frame);
        if (gridLinePlans.Count > 0)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
            foreach (var gridLine in gridLinePlans)
                dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));
        }

        switch (chart.ChartType)
        {
            case ChartType.ColumnClustered:
            case ChartType.ColumnStacked:
            case ChartType.ColumnStacked100:
                RenderColumnChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.BarClustered:
            case ChartType.BarStacked:
            case ChartType.BarStacked100:
                RenderBarChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Line:
            case ChartType.LineMarkers:
                RenderLineChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH,
                    withMarkers: chart.ChartType == ChartType.LineMarkers);
                break;
            case ChartType.Pie:
                RenderPieChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Doughnut:
                RenderDoughnutChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Area:
            case ChartType.AreaStacked:
                RenderAreaChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Scatter:
                RenderScatterChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Bubble:
                RenderBubbleChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            case ChartType.Radar:
                RenderRadarChart(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
                break;
            default:
                dc.FillRectangle(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    new Rect(plotLeft, plotTop, plotW, plotH));
                break;
        }

        // ── Combo chart: render secondary-group series with their OverrideChartType ──
        bool hasOverrideSeries = chart.Series.Any(s => s.OverrideChartType.HasValue);
        if (hasOverrideSeries && !isPie && !isBar && !isRadar && !isScatterLike)
        {
            RenderComboOverrideSeries(dc, chart, chartOp.SeriesColors, plotLeft, plotTop, plotW, plotH);
        }

        // ── Data labels ────────────────────────────────────────────────────────
        foreach (var label in ChartRenderPlanner.BuildDataLabelPlans(chart, plot))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment));
        }

        // ── Secondary value axis (right side) ──────────────────────────────────
        if (chart.SecondaryValueAxis is not null && !isPie && !isRadar && !isScatterLike && !isBar)
        {
            foreach (var label in ChartRenderPlanner.BuildSecondaryValueAxisLabelPlans(
                chart,
                plot,
                bounds.X + bounds.Width - legendAreaW - margin))
            {
                DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                    label.IsBold,
                    label.FontSize,
                    ToTextAlignment(label.Alignment));
            }
        }

        foreach (var label in ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment));
        }

        foreach (var label in ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame))
        {
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds),
                label.IsBold,
                label.FontSize,
                ToTextAlignment(label.Alignment));
        }

        if (hasLegend && chart.Series.Count > 0)
        {
            double lx, ly, lw;
            if (legendRight)
            {
                lx = bounds.X + bounds.Width - legendAreaW - margin / 2;
                ly = plotTop;
                lw = legendAreaW - margin / 2;
            }
            else
            {
                lx = plotLeft;
                ly = bounds.Y + bounds.Height - legendAreaH - margin / 2;
                lw = plotW;
            }
            double itemH = legendH;

            if (isPie)
            {
                int catItems = chart.Categories.Count > 0
                    ? chart.Categories.Count : chart.Series[0].Values.Count;
                int maxItems = (int)Math.Max(1, legendRight ? plotH / itemH : lw / 80);
                int toShow   = Math.Min(catItems, maxItems);
                for (int ci = 0; ci < toShow; ci++)
                {
                    var sc = ci < chartOp.SeriesColors.Count ? chartOp.SeriesColors[ci] : new SrgbColor(0x4F, 0x81, 0xBD);
                    string lbl = ci < chart.Categories.Count ? chart.Categories[ci] : $"Point {ci + 1}";
                    if (legendRight)
                    {
                        double iy = ly + ci * itemH;
                        dc.FillRectangle(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B)), new Rect(lx, iy + 3, 8, 8));
                        DrawChartLabel(dc, lbl, new Rect(lx + 10, iy, lw - 10, itemH), false, 7.0, TextAlignment.Left);
                    }
                    else
                    {
                        double ix = lx + ci * 80.0;
                        dc.FillRectangle(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B)), new Rect(ix, ly + 3, 8, 8));
                        DrawChartLabel(dc, lbl, new Rect(ix + 10, ly, 70, itemH), false, 7.0, TextAlignment.Left);
                    }
                }
            }
            else
            {
                int maxItems = (int)Math.Max(1, legendRight ? plotH / itemH : lw / 80);
                int toShow   = Math.Min(chart.Series.Count, maxItems);
                for (int si = 0; si < toShow; si++)
                {
                    var sc = si < chartOp.SeriesColors.Count ? chartOp.SeriesColors[si] : new SrgbColor(0x4F, 0x81, 0xBD);
                    if (legendRight)
                    {
                        double iy = ly + si * itemH;
                        dc.FillRectangle(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B)), new Rect(lx, iy + 3, 8, 8));
                        DrawChartLabel(dc, chart.Series[si].Name, new Rect(lx + 10, iy, lw - 10, itemH), false, 7.0, TextAlignment.Left);
                    }
                    else
                    {
                        double ix = lx + si * 80.0;
                        dc.FillRectangle(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B)), new Rect(ix, ly + 3, 8, 8));
                        DrawChartLabel(dc, chart.Series[si].Name, new Rect(ix + 10, ly, 70, itemH), false, 7.0, TextAlignment.Left);
                    }
                }
            }
        }
    }

    private static void RenderColumnChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildColumnPrimitives(chart, plot))
        {
            var color = GetSeriesColor(chart, primitive.SeriesIndex, primitive.CategoryIndex, seriesColors);
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            dc.FillRectangle(brush, ToRect(primitive.Bounds));
        }
    }

    // ── Combo-chart secondary series overlay ─────────────────────────────────
    /// <summary>
    /// Renders series that carry a per-series <see cref="ChartSeries.OverrideChartType"/>
    /// (set by the IO reader for combo charts). Only Line / LineMarkers overrides
    /// are handled here; others are silently skipped.
    /// </summary>
    private static void RenderComboOverrideSeries(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildComboOverrideLineSeriesPrimitives(chart, plot))
            RenderLineSeriesPrimitive(dc, chart, seriesColors, primitive);
    }

    private static void RenderBarChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildBarPrimitives(chart, plot))
        {
            var color = GetSeriesColor(chart, primitive.SeriesIndex, primitive.CategoryIndex, seriesColors);
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            dc.FillRectangle(brush, ToRect(primitive.Bounds));
        }
    }

    private static void RenderLineChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH, bool withMarkers)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildLineSeriesPrimitives(chart, plot, withMarkers))
            RenderLineSeriesPrimitive(dc, chart, seriesColors, primitive);
    }

    private static void RenderLineSeriesPrimitive(
        DrawingContext dc,
        ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartLineSeriesPrimitive primitive)
    {
        var color = GetSeriesColor(chart, primitive.SeriesIndex, 0, seriesColors);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var pen = new Pen(brush, 1.5);

        Point? previous = null;
        foreach (var plannedPoint in primitive.Points)
        {
            if (!plannedPoint.HasValue)
            {
                previous = null;
                continue;
            }

            var point = ToPoint(plannedPoint.Value);
            if (previous.HasValue)
                dc.DrawLine(pen, previous.Value, point);

            if (primitive.WithMarkers)
                dc.DrawEllipse(brush, null, point, 3, 3);

            previous = point;
        }
    }

    private static void RenderPieChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var borderPen = new Pen(Brushes.White, 0.8);
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildPieSlicePrimitives(chart, plot))
        {
            SrgbColor sc = primitive.PointIndex < seriesColors.Count
                ? seriesColors[primitive.PointIndex]
                : new SrgbColor(0x4F, 0x81, 0xBD);
            var brush = new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B));
            var startPt = ToPoint(primitive.OuterStart);
            var endPt = ToPoint(primitive.OuterEnd);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ToPoint(primitive.Center), isFilled: true);
                ctx.LineTo(startPt);
                ctx.ArcTo(
                    endPt,
                    new Size(primitive.OuterRadius, primitive.OuterRadius),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.Clockwise);
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    private static void RenderAreaChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildAreaSeriesPrimitives(chart, plot, seriesColors))
        {
            if (primitive.AreaPath.Fill is not { } fill)
                continue;

            var brush = new SolidColorBrush(Color.FromArgb(
                fill.Alpha,
                fill.Color.R,
                fill.Color.G,
                fill.Color.B));
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int pointIndex = 0; pointIndex < primitive.AreaPath.Points.Count; pointIndex++)
                {
                    var point = ToPoint(primitive.AreaPath.Points[pointIndex]);
                    if (pointIndex == 0)
                        ctx.BeginFigure(point, isFilled: true);
                    else
                        ctx.LineTo(point);
                }
                ctx.EndFigure(isClosed: primitive.AreaPath.IsClosed);
            }
            dc.DrawGeometry(brush, null, geo);
        }
    }

    // ── Doughnut chart ───────────────────────────────────────────────────────

    private static void RenderDoughnutChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var borderPen = new Pen(Brushes.White, 0.8);
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        foreach (var primitive in ChartRenderPlanner.BuildDoughnutSlicePrimitives(chart, plot))
        {
            SrgbColor sc = primitive.PointIndex < seriesColors.Count
                ? seriesColors[primitive.PointIndex]
                : GetSeriesColor(chart, primitive.PointIndex, 0, seriesColors);
            var brush = new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ToPoint(primitive.OuterStart), isFilled: true);
                ctx.ArcTo(
                    ToPoint(primitive.OuterEnd),
                    new Size(primitive.OuterRadius, primitive.OuterRadius),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.Clockwise);
                ctx.LineTo(ToPoint(primitive.InnerEnd));
                ctx.ArcTo(
                    ToPoint(primitive.InnerStart),
                    new Size(primitive.InnerRadius, primitive.InnerRadius),
                    0,
                    primitive.IsLargeArc,
                    SweepDirection.CounterClockwise);
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, borderPen, geo);
        }
    }

    // ── Scatter chart ────────────────────────────────────────────────────────

    private static void RenderScatterChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);

        foreach (var gridLine in plan.GridLines)
            dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));

        foreach (var primitive in plan.Series)
            RenderScatterSeriesPrimitive(dc, chart, seriesColors, primitive);

        foreach (var label in plan.XAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        foreach (var label in plan.YAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
    }

    private static void RenderScatterSeriesPrimitive(
        DrawingContext dc,
        ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        ChartScatterSeriesPrimitive primitive)
    {
        var color = GetSeriesColor(chart, primitive.SeriesIndex, 0, seriesColors);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var pen = primitive.DrawLines ? new Pen(brush, 1.5) : null;
        var markerBrush = primitive.DrawMarkers ? brush : null;

        Point? previous = null;
        foreach (var plannedPoint in primitive.Points)
        {
            if (!plannedPoint.HasValue)
            {
                previous = null;
                continue;
            }

            var point = ToPoint(plannedPoint.Value);
            if (primitive.DrawLines && pen is not null && previous.HasValue)
                dc.DrawLine(pen, previous.Value, point);
            if (primitive.DrawMarkers && markerBrush is not null)
                dc.DrawEllipse(markerBrush, null, point, 3.5, 3.5);

            previous = point;
        }
    }

    // ── Bubble chart ─────────────────────────────────────────────────────────

    private static void RenderBubbleChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);

        foreach (var gridLine in plan.GridLines)
            dc.DrawLine(gridPen, ToPoint(gridLine.Start), ToPoint(gridLine.End));

        foreach (var primitive in plan.Bubbles)
        {
            var color = GetSeriesColor(chart, primitive.SeriesIndex, 0, seriesColors);
            var brush  = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B));
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 0.8);
            dc.DrawEllipse(brush, outlinePen, ToPoint(primitive.Center), primitive.Radius, primitive.Radius);
        }

        foreach (var label in plan.XAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
        foreach (var label in plan.YAxisLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));
    }

    // ── Radar chart ──────────────────────────────────────────────────────────

    private static void RenderRadarChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        var plot = new ChartPlanRect(plotX, plotY, plotW, plotH);
        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot);

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
        foreach (var ring in plan.Rings)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int pointIndex = 0; pointIndex < ring.Points.Count; pointIndex++)
                {
                    var point = ToPoint(ring.Points[pointIndex]);
                    if (pointIndex == 0)
                        ctx.BeginFigure(point, isFilled: false);
                    else
                        ctx.LineTo(point);
                }
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(null, gridPen, geo);
        }

        var spokePen = new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)), 0.5);
        foreach (var spoke in plan.Spokes)
            dc.DrawLine(spokePen, ToPoint(spoke.Start), ToPoint(spoke.End));

        foreach (var label in plan.CategoryLabels)
            DrawChartLabel(dc, label.Text, ToRect(label.Bounds), label.IsBold, label.FontSize, ToTextAlignment(label.Alignment));

        foreach (var primitive in plan.Series)
        {
            var color = GetSeriesColor(chart, primitive.SeriesIndex, 0, seriesColors);
            var pen    = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 1.5);
            IBrush? fillBrush = primitive.IsFilled
                ? new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B))
                : null;

            var polyGeo = new StreamGeometry();
            using (var ctx = polyGeo.Open())
            {
                for (int pointIndex = 0; pointIndex < primitive.Points.Count; pointIndex++)
                {
                    var point = ToPoint(primitive.Points[pointIndex]);
                    if (pointIndex == 0)
                        ctx.BeginFigure(point, isFilled: primitive.IsFilled);
                    else
                        ctx.LineTo(point);
                }
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(fillBrush, pen, polyGeo);

            if (primitive.WithMarkers)
            {
                var markerBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                foreach (var point in primitive.Points)
                    dc.DrawEllipse(markerBrush, null, ToPoint(point), 3, 3);
            }
        }
    }

    // ── Chart helpers ────────────────────────────────────────────────────────

    private static SrgbColor GetSeriesColor(ChartShape chart, int si, int ci, IReadOnlyList<SrgbColor> colors)
    {
        if (si < colors.Count) return colors[si];
        var fallbacks = new SrgbColor[]
        {
            new(0x4F,0x81,0xBD), new(0xC0,0x50,0x4D), new(0x9B,0xBB,0x59),
            new(0x80,0x64,0xA2), new(0x4B,0xAC,0xC6), new(0xF7,0x96,0x46)
        };
        return fallbacks[si % fallbacks.Length];
    }

    internal static (double min, double max, double majorUnit) ComputeNiceAxisRange(ChartShape chart) =>
        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

    /// <summary>
    /// Computes the nice axis range for the SECONDARY value axis using ONLY series that have
    /// OnSecondaryAxis == true.  Returns (0,1,1) when there are no secondary-axis series
    /// (avoids divide-by-zero).  CB1 fix.
    /// </summary>
    internal static (double min, double max, double majorUnit) ComputeNiceSecondaryAxisRange(ChartShape chart) =>
        ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

    internal static (double min, double max, double majorUnit) ComputeNiceScatterAxisRange(
        ChartShape chart, bool useX) =>
        ChartRenderPlanner.ComputeScatterAxisRange(chart, useX);

    private static string FormatAxisValue(double v) =>
        ChartRenderPlanner.FormatAxisValue(v);

    private static void DrawChartLabel(
        DrawingContext dc, string text, Rect rect,
        bool isBold, double fontSize, TextAlignment align)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0) return;
        var typeface = new Typeface("Calibri",
            FontStyle.Normal,
            isBold ? FontWeight.Bold : FontWeight.Normal,
            FontStretch.Normal);
        var brush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize * (96.0 / 72.0),
            brush)
        {
            MaxTextWidth  = rect.Width,
            MaxLineCount  = 1,
            TextAlignment = align,
            Trimming      = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, new Point(rect.X, rect.Y));
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void RenderText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        // Wave 18B: vertical text — rotate the text block around the shape center.
        bool isVertical = text.VerticalType is TextVerticalType.Vertical
                                            or TextVerticalType.EastAsianVertical
                                            or TextVerticalType.WordArtVertical
                                            or TextVerticalType.WordArtVerticalRtl;
        bool isVert270  = text.VerticalType == TextVerticalType.Vertical270;

        if (isVertical || isVert270)
        {
            double cx = bounds.X + bounds.Width  * 0.5;
            double cy = bounds.Y + bounds.Height * 0.5;
            double rad = isVert270 ? -Math.PI / 2.0 : Math.PI / 2.0;
            using var rotScope = dc.PushTransform(
                Matrix.CreateTranslation(-cx, -cy)
                * Matrix.CreateRotation(rad)
                * Matrix.CreateTranslation(cx, cy));
            var rotatedBounds = new LayoutRect(
                bounds.X + (bounds.Width - bounds.Height) * 0.5,
                bounds.Y + (bounds.Height - bounds.Width) * 0.5,
                bounds.Height,
                bounds.Width);
            RenderTextCore(dc, text, rotatedBounds);
            return;
        }

        RenderTextCore(dc, text, bounds);
    }

    // Wave 22B: multi-column text layout helper for Avalonia.
    // Mirrors the WPF version — greedy paragraph-level assignment across N columns.
    private static void RenderTextCoreColumns(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        var columnLayout = TextLayoutPlanner.GetColumnLayout(text, bounds);
        var formatted = new Dictionary<int, FormattedText>();
        var measured = new List<TextParagraphMeasure>();

        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, columnLayout.ColumnWidthDip, text.Wrap);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt,
                columnLayout.LineSpacingScale));
        }

        var plan = TextLayoutPlanner.PlanColumns(text, columnLayout, measured);
        foreach (var placement in plan.Paragraphs)
        {
            var para = text.Paragraphs[placement.ParagraphIndex];
            var ft = formatted[placement.ParagraphIndex];
            if (!string.IsNullOrEmpty(para.BulletText))
                DrawBulletAvalonia(dc, para.BulletText, para.BulletFontFamily, para.BulletFontSizePt,
                    para.BulletColor, placement.X - para.HangingDip, placement.Y);
            bool hasEffects = ParaHasTextEffects(para) || text.WarpPreset is not null;
            bool hasTabs    = para.Runs.Any(r => r.Text.Contains('\t'));
            if (hasEffects)
                RenderParaWithEffects(dc, para, placement.X, placement.Y, bounds, text.WarpPreset);
            else if (hasTabs)
                RenderParaWithTabs(dc, para, placement.X, placement.Y, para.TabStops);
            else
            {
                if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                    ft.MaxTextWidth = placement.MaxWidthDip;
                dc.DrawText(ft, new Point(placement.X, placement.Y));
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
        var formatted = new Dictionary<int, FormattedText>();
        var measured = new List<TextParagraphMeasure>();
        for (int i = 0; i < text.Paragraphs.Count; i++)
        {
            var para = text.Paragraphs[i];
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, area.Width, text.Wrap);
            formatted[i] = ft;
            measured.Add(TextLayoutPlanner.CreateParagraphMeasure(
                i,
                ft.Height,
                para.SpaceBeforePt,
                para.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanBodyText(text, bounds, measured);
        foreach (var placement in plan.Paragraphs)
        {
            var para = text.Paragraphs[placement.ParagraphIndex];
            var ft = formatted[placement.ParagraphIndex];

            // Wave 19A: draw bullet (char or number) to the left of paragraph text.
            if (!string.IsNullOrEmpty(para.BulletText))
            {
                double bulletX = placement.X - para.HangingDip;
                DrawBulletAvalonia(dc, para.BulletText, para.BulletFontFamily, para.BulletFontSizePt,
                    para.BulletColor, bulletX, placement.Y);
            }

            // Wave 16A: use geometry-based rendering when any run has text effects or warp is active.
            // BA2 fix: when effects/warp are active, skip the flat DrawText base pass entirely and
            // let RenderParaWithEffects draw ALL runs (plain ones at their flat baseline, effect/warp
            // ones with the appropriate transforms). This prevents each effect/warp run being drawn
            // twice (flat ghost from DrawText + warped/overlaid copy from RenderParaWithEffects).
            bool hasEffects = ParaHasTextEffects(para) || text.WarpPreset is not null;

            // Wave 18B: render run-by-run whenever any run contains a tab character so that
            // BO2 fix: paragraphs with NO explicit tab stops (relying on default ~96 DIP interval)
            // also go through RenderParaWithTabs instead of plain DrawText (which ignores \t).
            bool hasTabs = para.Runs.Any(r => r.Text.Contains('\t'));

            if (hasEffects)
            {
                RenderParaWithEffects(dc, para, placement.X, placement.Y, bounds, text.WarpPreset);
            }
            else if (hasTabs)
            {
                RenderParaWithTabs(dc, para, placement.X, placement.Y, para.TabStops);
            }
            else
            {
                // Adjust MaxTextWidth to account for indent
                if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                    ft.MaxTextWidth = placement.MaxWidthDip;
                dc.DrawText(ft, new Point(placement.X, placement.Y));
            }
        }
    }

    /// <summary>
    /// Wave 19A: draws a bullet glyph or number string at the given position.
    /// </summary>
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
        const double DefaultTabDip = 96.0;

        // BO1: Flatten all runs into a sequence of (text, run, isTab) tokens.
        // Each entry is a text segment + the run it belongs to; isTab=true means a tab
        // character precedes this segment (alignment must be applied before drawing).
        var tokens = new System.Collections.Generic.List<(string text, ResolvedRun run, bool isTab)>();
        foreach (var run in para.Runs)
        {
            if (run.Text.Length == 0) continue;
            var segs = run.Text.Split('\t');
            for (int si = 0; si < segs.Length; si++)
                tokens.Add((segs[si], run, si > 0));
        }

        double curX = startX;

        for (int ti = 0; ti < tokens.Count; ti++)
        {
            var (seg, run, isTab) = tokens[ti];

            // Advance to the next tab stop before drawing this segment.
            if (isTab)
            {
                double relX = curX - startX;

                // Find the matching tab stop.
                double stopDip = DefaultTabDip;
                ResolvedTabStop? matchedStop = null;
                bool found = false;
                foreach (var ts in tabStops)
                {
                    if (ts.PositionDip > relX + 0.5)
                    {
                        stopDip     = ts.PositionDip;
                        matchedStop = ts;
                        found       = true;
                        break;
                    }
                }
                if (!found)
                    stopDip = Math.Floor(relX / DefaultTabDip + 1.0) * DefaultTabDip;

                // BQ1+BQ2: compute alignment offset by scanning the segment width ACROSS all
                // following tokens up to the next tab (run-agnostic, mirrors FreeW EmitLinePaged).
                // The aligned segment may span multiple runs (e.g. tab in run1, text in run2).
                double alignOffset = 0;
                TabStopAlignment align = matchedStop?.Alignment ?? TabStopAlignment.Left;
                if (align != TabStopAlignment.Left)
                {
                    // Forward-scan: collect the combined text and total width of consecutive
                    // non-tab tokens starting from the CURRENT token (seg, which may be "") and
                    // continuing into following tokens until we hit another tab token or end.
                    var sbCombined = new System.Text.StringBuilder();
                    double segW = 0;

                    // Include the current (same-run) segment first.
                    if (seg.Length > 0)
                    {
                        sbCombined.Append(seg);
                        segW += BuildSingleRunFormattedTextAt(run, seg).Width;
                    }
                    // Then scan following tokens until the next isTab==true or end.
                    for (int fwd = ti + 1; fwd < tokens.Count; fwd++)
                    {
                        var (fwdSeg, fwdRun, fwdIsTab) = tokens[fwd];
                        if (fwdIsTab) break;           // next tab boundary — stop here
                        if (fwdSeg.Length > 0)
                        {
                            sbCombined.Append(fwdSeg);
                            segW += BuildSingleRunFormattedTextAt(fwdRun, fwdSeg).Width;
                        }
                    }

                    if (segW > 0)
                    {
                        string combinedText = sbCombined.ToString();
                        alignOffset = align switch
                        {
                            TabStopAlignment.Right   => -segW,            // segment ends at stop
                            TabStopAlignment.Center  => -segW / 2.0,      // segment centred on stop
                            TabStopAlignment.Decimal =>                    // decimal pt at stop
                                -(combinedText.Contains('.')
                                    ? BuildSingleRunFormattedTextAt(run,
                                          combinedText[..(combinedText.IndexOf('.') + 1)]).Width
                                    : segW),
                            _ => 0
                        };
                    }
                }

                // BQ2: clamp — never start the aligned segment before the current pen position
                // (prevents overlap when segment width > gap from prior text to stop).
                double prevCurX = curX;
                curX = Math.Max(prevCurX, startX + stopDip + alignOffset);
            }

            // Draw the segment.
            if (seg.Length > 0)
            {
                var segFt = BuildSingleRunFormattedTextAt(run, seg);
                dc.DrawText(segFt, new Point(curX, startY));
                curX += segFt.Width;
            }
        }
    }

    private static FormattedText BuildSingleRunFormattedTextAt(ResolvedRun run, string text)
    {
        string txt = text.Length == 0 ? " " : text;
        var typeface = new Typeface(
            run.FontFamily,
            run.Italic ? FontStyle.Italic : FontStyle.Normal,
            run.Bold   ? FontWeight.Bold  : FontWeight.Normal,
            FontStretch.Normal);
        double emPx = run.FontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        var ft = new FormattedText(txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface, emPx, brush);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, txt.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, txt.Length);
        return ft;
    }

    private static FormattedText BuildFormattedText(ResolvedParagraph para, double maxWidth, bool wrap)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Runs) sb.Append(run.Text);
        string txt = sb.Length == 0 ? " " : sb.ToString();

        var firstRun = para.Runs[0];
        var typeface = new Typeface(
            firstRun.FontFamily,
            firstRun.Italic ? FontStyle.Italic : FontStyle.Normal,
            firstRun.Bold   ? FontWeight.Bold  : FontWeight.Normal,
            FontStretch.Normal);

        double emSizePx = firstRun.FontSizePt * (96.0 / 72.0);
        var brush = new SolidColorBrush(
            Color.FromRgb(firstRun.Color.R, firstRun.Color.G, firstRun.Color.B));

        var ft = new FormattedText(
            txt,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush);

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
            ft.SetFontFamily(run.FontFamily, pos, len);
            ft.SetFontSize(run.FontSizePt * (96.0 / 72.0), pos, len);
            ft.SetForegroundBrush(
                new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B)),
                pos, len);
            pos += len;
        }
        return ft;
    }

    // ── Text-effects geometry helpers (Wave 16A) ──────────────────────────────

    private static bool ParaHasTextEffects(ResolvedParagraph para) =>
        para.Runs.Any(r => r.TextFill is not null || r.TextOutline is not null || r.TextShadow is not null);

    private static IBrush MakeFillBrushForText(ResolvedFill fill)
    {
        return fill switch
        {
            ResolvedFill.Solid s =>
                new SolidColorBrush(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B)),
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

    private static FormattedText BuildSingleRunFormattedText(ResolvedRun run)
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
            FlowDirection.LeftToRight,
            typeface, emPx, brush);
        if (run.Underline)     ft.SetTextDecorations(TextDecorations.Underline, 0, txt.Length);
        if (run.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough, 0, txt.Length);
        return ft;
    }

    private static double ComputeRunOffsetX(ResolvedParagraph para, int targetPos)
    {
        double accX = 0;
        int p = 0;
        foreach (var run in para.Runs)
        {
            if (p == targetPos) break;
            var prev = BuildSingleRunFormattedText(run);
            accX += prev.Width;
            p += run.Text.Length;
        }
        return accX;
    }

    private static void RenderParaWithEffects(
        DrawingContext dc,
        ResolvedParagraph para,
        double x, double y,
        LayoutRect shapeBounds,
        string? warpPreset)
    {
        bool hasWarp = WordArtWarpPlanner.ComputeYOffset(warpPreset, 0, shapeBounds).HasValue;

        int pos = 0;
        foreach (var run in para.Runs)
        {
            bool hasEffects = run.TextFill is not null || run.TextOutline is not null || run.TextShadow is not null;

            double runOffX = ComputeRunOffsetX(para, pos);
            double drawX = x + runOffX;
            double drawY = y;

            // BA2 fix: plain runs are no longer drawn by an outer DrawText pass, so draw them here
            // at their flat baseline (no warp, solid-color fill, no outline).
            if (!hasEffects && !hasWarp)
            {
                var plainFt  = BuildSingleRunFormattedText(run);
                var plainGeo = plainFt.BuildGeometry(new Point(drawX, drawY));
                if (plainGeo is not null)
                {
                    IBrush plainBrush = new SolidColorBrush(
                        Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
                    dc.DrawGeometry(plainBrush, null, plainGeo);
                }
                pos += run.Text.Length;
                continue;
            }

            if (hasWarp)
            {
                double t = shapeBounds.Width > 0 ? (drawX - shapeBounds.X) / shapeBounds.Width : 0;
                drawY += WordArtWarpPlanner.ComputeYOffset(warpPreset, t, shapeBounds) ?? 0;
            }

            var runFt = BuildSingleRunFormattedText(run);
            var geo   = runFt.BuildGeometry(new Point(drawX, drawY));
            if (geo is null) { pos += run.Text.Length; continue; }

            // 1. Shadow
            if (run.TextShadow is { } ts)
            {
                double rad = ts.DirDeg * Math.PI / 180.0;
                double dx  = Math.Cos(rad) * ts.DistDip;
                double dy  = Math.Sin(rad) * ts.DistDip;
                var shadowBrush = new SolidColorBrush(
                    Color.FromArgb(ts.Alpha, ts.Color.R, ts.Color.G, ts.Color.B));
                if (ts.BlurDip > 0.5)
                {
                    int passes = Math.Min(3, (int)Math.Ceiling(ts.BlurDip / 1.5));
                    for (int pi = 1; pi <= passes; pi++)
                    {
                        double spread = ts.BlurDip * pi / passes;
                        byte passAlpha = (byte)(ts.Alpha / (passes + 1));
                        var passBrush = new SolidColorBrush(
                            Color.FromArgb(passAlpha, ts.Color.R, ts.Color.G, ts.Color.B));
                        for (int ox = -1; ox <= 1; ox++)
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            using var s2 = dc.PushTransform(
                                Matrix.CreateTranslation(dx + ox * spread, dy + oy * spread));
                            dc.DrawGeometry(passBrush, null, geo);
                        }
                    }
                }
                using var sScope = dc.PushTransform(Matrix.CreateTranslation(dx, dy));
                dc.DrawGeometry(shadowBrush, null, geo);
            }

            // 2. Fill
            IBrush fillBrush = run.TextFill is not null
                ? MakeFillBrushForText(run.TextFill)
                : new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));

            // 3. Outline
            Pen? outlinePen = run.TextOutline is not null ? MakePen(run.TextOutline) : null;

            dc.DrawGeometry(fillBrush, outlinePen, geo);

            pos += run.Text.Length;
        }
    }

    // ── Brush / Pen factories ─────────────────────────────────────────────────

    private static IBrush? MakeBrush(ResolvedFill fill, LayoutRect bounds) => fill switch
    {
        ResolvedFill.None      => null,
        ResolvedFill.Solid s   => new SolidColorBrush(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B)),
        ResolvedFill.Gradient g when g.Kind == GradientKind.Radial => MakeRadialGradientBrush(g),
        ResolvedFill.Gradient g  => MakeLinearGradientBrush(g),
        ResolvedFill.Picture  p  => MakePictureBrush(p),
        ResolvedFill.PatternFill pat => MakePatternBrush(pat),
        _                      => null
    };

    private static GradientStops BuildGradientStops(ResolvedFill.Gradient g)
    {
        var stops = new GradientStops();
        foreach (var s in g.Stops)
            stops.Add(new AvGradientStop(
                Color.FromRgb(s.Color.R, s.Color.G, s.Color.B),
                s.Position));
        return stops;
    }

    private static IBrush MakeLinearGradientBrush(ResolvedFill.Gradient g)
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
            GradientStops = BuildGradientStops(g)
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

        const int S = 6;
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
            case "pct25" or "pct30" or "pct5" or "pct10" or "pct20":
                for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                        if ((x * 2 + y * 3) % 4 == 0) SetPixel(x, y, fg);
                break;
            case "pct75" or "pct60" or "pct40" or "pct90":
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
            case "cross" or "smGrid":
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
            var brush = new SolidColorBrush(Color.FromRgb(vis.Color.R, vis.Color.G, vis.Color.B));
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
        _cachedOps      = SlideCompositor.Compose(_presentation, _slide, _slideIndex);
    }
}
