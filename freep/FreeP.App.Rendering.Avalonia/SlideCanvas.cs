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

        double scale   = Math.Min(renderW / _slideWidthDip, renderH / _slideHeightDip);
        double offsetX = (renderW - _slideWidthDip * scale) / 2;
        double offsetY = (renderH - _slideHeightDip * scale) / 2;

        // Expose the slide→screen transform so the editing layer can use it.
        CurrentTransform = new SlideTransformCore(scale, offsetX, offsetY, _slideWidthDip, _slideHeightDip);

        var matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        using var _ = context.PushTransform(matrix);

        foreach (var op in _cachedOps)
            RenderOp(context, op);
    }

    private void RenderOp(DrawingContext dc, DrawOp op)
    {
        switch (op)
        {
            case DrawOp.Background bg:  RenderBackground(dc, bg);  break;
            case DrawOp.Shape shape:    RenderShape(dc, shape);    break;
            case DrawOp.Picture pic:    RenderPicture(dc, pic);    break;
            case DrawOp.Table table:    RenderTable(dc, table);    break;
            case DrawOp.Chart chartOp:  RenderChart(dc, chartOp);  break;
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
            transformScope = dc.PushTransform(BuildShapeMatrix(shape));

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
        var fx = shape.Effects!;
        if (shape.Geometry.Contours.Count == 0) return;

        if (fx.HasOuterShadow)
        {
            double rad = fx.OuterShadowDirDeg * Math.PI / 180.0;
            double dx  = Math.Cos(rad) * fx.OuterShadowDistDip;
            double dy  = Math.Sin(rad) * fx.OuterShadowDistDip;

            byte a = fx.OuterShadowAlpha;
            var shadowBrush = new SolidColorBrush(
                Color.FromArgb(a, fx.OuterShadowColor.R, fx.OuterShadowColor.G, fx.OuterShadowColor.B));
            var shadowGeo = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (shadowGeo is null) return;

            using var shadowScope = dc.PushTransform(Matrix.CreateTranslation(dx, dy));

            if (fx.OuterShadowBlurDip > 1.0)
            {
                int passes = Math.Min(4, (int)Math.Ceiling(fx.OuterShadowBlurDip / 2));
                for (int i = passes; i >= 1; i--)
                {
                    double spread  = fx.OuterShadowBlurDip * i / passes;
                    byte passAlpha = (byte)(a / (passes + 1));
                    var passBrush  = new SolidColorBrush(
                        Color.FromArgb(passAlpha, fx.OuterShadowColor.R, fx.OuterShadowColor.G, fx.OuterShadowColor.B));
                    for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        using var spreadScope = dc.PushTransform(Matrix.CreateTranslation(ox * spread, oy * spread));
                        dc.DrawGeometry(passBrush, null, shadowGeo);
                    }
                }
                dc.DrawGeometry(shadowBrush, null, shadowGeo);
            }
            else
            {
                dc.DrawGeometry(shadowBrush, null, shadowGeo);
            }
        }

        if (fx.HasGlow)
        {
            var glowGeo = AvaloniaSlideGeometryFactory.ToGeometry(shape.Geometry);
            if (glowGeo is null) return;
            int passes = Math.Min(5, (int)Math.Ceiling(fx.GlowRadiusDip / 2));
            for (int i = passes; i >= 1; i--)
            {
                double r       = fx.GlowRadiusDip * i / passes;
                byte passAlpha = (byte)(fx.GlowAlpha / (passes + 1));
                var glowBrush  = new SolidColorBrush(
                    Color.FromArgb(passAlpha, fx.GlowColor.R, fx.GlowColor.G, fx.GlowColor.B));
                var glowPen    = new Pen(glowBrush, r * 2);
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

    private static Matrix BuildShapeMatrix(DrawOp.Shape shape)
    {
        var b  = shape.BoundsDip;
        double cx = b.X + b.Width  / 2;
        double cy = b.Y + b.Height / 2;

        var m = Matrix.Identity;
        if (shape.FlipH) m = m * new Matrix(-1, 0, 0, 1, cx * 2, 0);
        if (shape.FlipV) m = m * new Matrix(1, 0, 0, -1, 0, cy * 2);
        if (shape.RotationDeg != 0)
        {
            double rad = shape.RotationDeg * Math.PI / 180.0;
            m = m
                * Matrix.CreateTranslation(-cx, -cy)
                * Matrix.CreateRotation(rad)
                * Matrix.CreateTranslation(cx, cy);
        }
        return m;
    }

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
        if (pic.Grayscale || pic.BiLevelThreshold.HasValue || pic.Brightness.HasValue || pic.Contrast.HasValue)
            renderBitmap = ApplyColorEffectsAvalonia(bitmap, pic) ?? (IImage)bitmap;

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
    private static WriteableBitmap? ApplyColorEffectsAvalonia(Bitmap src, DrawOp.Picture pic)
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

        // ── Apply effects ────────────────────────────────────────────────────────────
        bool doGray    = pic.Grayscale;
        bool doBiLevel = pic.BiLevelThreshold.HasValue;
        double biThresh = doBiLevel ? pic.BiLevelThreshold!.Value : 0;
        bool doLum      = pic.Brightness.HasValue || pic.Contrast.HasValue;
        double bright   = pic.Brightness ?? 0;
        double contrast = pic.Contrast  ?? 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i]     / 255.0;
            double g = pixels[i + 1] / 255.0;
            double r = pixels[i + 2] / 255.0;

            if (doGray)
            {
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = g = b = lum;
            }

            if (doLum)
            {
                r = Math.Clamp(r + bright, 0, 1);
                g = Math.Clamp(g + bright, 0, 1);
                b = Math.Clamp(b + bright, 0, 1);

                if (contrast > 0)
                {
                    double den = Math.Max(1.0 - contrast, 0.001);
                    r = Math.Clamp((r - 0.5) / den + 0.5, 0, 1);
                    g = Math.Clamp((g - 0.5) / den + 0.5, 0, 1);
                    b = Math.Clamp((b - 0.5) / den + 0.5, 0, 1);
                }
                else if (contrast < 0)
                {
                    r = Math.Clamp((r - 0.5) * (1 + contrast) + 0.5, 0, 1);
                    g = Math.Clamp((g - 0.5) * (1 + contrast) + 0.5, 0, 1);
                    b = Math.Clamp((b - 0.5) * (1 + contrast) + 0.5, 0, 1);
                }
            }

            if (doBiLevel)
            {
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double bw  = lum >= biThresh ? 1.0 : 0.0;
                r = g = b = bw;
            }

            pixels[i]     = (byte)(b * 255);
            pixels[i + 1] = (byte)(g * 255);
            pixels[i + 2] = (byte)(r * 255);
            // pixels[i+3] = alpha — preserved unchanged
        }

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
        double insetLeft   = text.InsetLeftDip;
        double insetTop    = text.InsetTopDip;
        double insetRight  = text.InsetRightDip;
        double insetBottom = text.InsetBottomDip;
        double textAreaW   = Math.Max(0, bounds.Width  - insetLeft - insetRight);
        double textAreaH   = Math.Max(0, bounds.Height - insetTop  - insetBottom);

        var formatted = new List<(FormattedText ft, double spaceAfter)>();
        double totalH = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, textAreaW, text.Wrap);
            formatted.Add((ft, para.SpaceAfterPt * (96.0 / 72.0)));
            totalH += ft.Height + para.SpaceBeforePt * (96.0 / 72.0) + para.SpaceAfterPt * (96.0 / 72.0);
        }

        double startY = anchor switch
        {
            TableCellAnchor.Middle => bounds.Y + insetTop + Math.Max(0, (textAreaH - totalH) / 2),
            TableCellAnchor.Bottom => bounds.Y + insetTop + Math.Max(0, textAreaH - totalH),
            _                      => bounds.Y + insetTop
        };

        double curY = startY;
        int paraIdx = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) { paraIdx++; continue; }
            var (ft, spaceAfterDip) = formatted[paraIdx];
            curY += para.SpaceBeforePt * (96.0 / 72.0);
            dc.DrawText(ft, new Point(bounds.X + insetLeft, curY));
            curY += ft.Height + spaceAfterDip;
            paraIdx++;
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

        const double margin       = 8.0;
        const double titleH       = 18.0;
        const double legendH      = 14.0;
        const double axisLabelW   = 40.0;
        const double catLabelH    = 16.0;
        const double barCatLabelW = 44.0;
        const double gridlinePad  = 2.0;

        double titleAreaH = chart.Title is not null ? titleH + margin : 0;
        bool   hasLegend  = chart.Legend.HasValue;
        bool   isPie      = chart.ChartType is ChartType.Pie or ChartType.Doughnut;
        bool   isBar      = chart.ChartType is ChartType.BarClustered
                                            or ChartType.BarStacked
                                            or ChartType.BarStacked100;
        bool   isScatterLike = chart.ChartType is ChartType.Scatter or ChartType.Bubble;
        bool   isRadar    = chart.ChartType == ChartType.Radar;

        if (chart.Title is not null)
            DrawChartLabel(dc, chart.Title,
                new Rect(bounds.X + margin, bounds.Y + margin, bounds.Width - 2 * margin, titleH),
                isBold: true, fontSize: 9.0, align: TextAlignment.Center);

        double legendAreaW = 0, legendAreaH = 0;
        bool   legendRight = chart.Legend is LegendPosition.Right or LegendPosition.Left;
        if (hasLegend)
        {
            if (legendRight) legendAreaW = Math.Min(90, bounds.Width * 0.20);
            else             legendAreaH = legendH + margin;
        }

        double plotLeft   = bounds.X + margin + (isPie || isRadar || isScatterLike ? 0 : (isBar ? barCatLabelW : axisLabelW));
        double plotTop    = bounds.Y + margin + titleAreaH;
        double plotRight  = bounds.X + bounds.Width  - margin - legendAreaW;
        double plotBottom = bounds.Y + bounds.Height - margin - legendAreaH
                                     - (isPie || isRadar || isScatterLike ? 0 : (isBar ? axisLabelW : catLabelH));
        double plotW      = plotRight  - plotLeft;
        double plotH      = plotBottom - plotTop;
        if (plotW <= 0 || plotH <= 0) return;

        if (!isPie && !isRadar && !isScatterLike && chart.ValueAxis.HasMajorGridlines)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
            var (minG, maxG, muG) = ComputeNiceAxisRange(chart);
            double stepsG = (maxG - minG) / muG;
            if (isBar)
            {
                for (int gi = 0; gi <= (int)Math.Round(stepsG); gi++)
                {
                    double gx = plotLeft + plotW * gi / stepsG;
                    dc.DrawLine(gridPen, new Point(gx, plotTop), new Point(gx, plotTop + plotH));
                }
            }
            else
            {
                for (int gi = 0; gi <= (int)Math.Round(stepsG); gi++)
                {
                    double gy = plotTop + plotH - plotH * gi / stepsG;
                    dc.DrawLine(gridPen, new Point(plotLeft, gy), new Point(plotLeft + plotW, gy));
                }
            }
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

        if (!isPie && !isRadar && !isScatterLike && chart.Categories.Count > 0)
        {
            if (isBar)
            {
                int catN = chart.Categories.Count;
                double catStep = plotH / Math.Max(1, catN);
                for (int ci = 0; ci < catN; ci++)
                {
                    int renderRow = catN - 1 - ci;
                    double ly = plotTop + renderRow * catStep;
                    DrawChartLabel(dc, chart.Categories[ci],
                        new Rect(bounds.X + margin, ly, barCatLabelW - 4, catStep),
                        false, 6.5, TextAlignment.Right);
                }
            }
            else
            {
                double catStep = plotW / Math.Max(1, chart.Categories.Count);
                for (int ci = 0; ci < chart.Categories.Count; ci++)
                {
                    double lx = plotLeft + ci * catStep;
                    DrawChartLabel(dc, chart.Categories[ci],
                        new Rect(lx, plotTop + plotH + 2, catStep, catLabelH),
                        false, 7.0, TextAlignment.Center);
                }
            }
        }

        if (!isPie && !isRadar && !isScatterLike)
        {
            var (minV, maxV, mu) = ComputeNiceAxisRange(chart);
            double stepsV = (maxV - minV) / mu;
            if (isBar)
            {
                for (int ti = 0; ti <= (int)Math.Round(stepsV); ti++)
                {
                    double val = minV + mu * ti;
                    double vx  = plotLeft + plotW * ti / stepsV;
                    DrawChartLabel(dc, FormatAxisValue(val),
                        new Rect(vx - axisLabelW / 2, plotTop + plotH + 2, axisLabelW, catLabelH),
                        false, 6.5, TextAlignment.Center);
                }
            }
            else
            {
                for (int ti = 0; ti <= (int)Math.Round(stepsV); ti++)
                {
                    double val = minV + mu * ti;
                    double vy  = plotTop + plotH - plotH * ti / stepsV;
                    DrawChartLabel(dc, FormatAxisValue(val),
                        new Rect(bounds.X + margin, vy - 6, axisLabelW - gridlinePad, 12),
                        false, 6.5, TextAlignment.Right);
                }
            }
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
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;
        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;
        bool stacked    = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        const double gapRatio = 1.5;
        double catW    = plotW / catCount;
        double clusterW = catW / (1.0 + gapRatio);
        double halfGap  = (catW - clusterW) / 2.0;
        int serCount    = Math.Max(1, chart.Series.Count);
        double serW     = stacked ? clusterW : clusterW / serCount;

        for (int ci = 0; ci < catCount; ci++)
        {
            double catLeft  = plotX + ci * catW + halfGap;
            double stackedY = plotY + plotH;
            for (int si = 0; si < chart.Series.Count; si++)
            {
                double? rawVal = ci < chart.Series[si].Values.Count ? chart.Series[si].Values[ci] : null;
                if (rawVal is null) continue;
                double val  = rawVal.Value;
                var color   = GetSeriesColor(chart, si, ci, seriesColors);
                var brush   = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                double drawW = Math.Max(1, stacked ? serW : serW - 1);
                double barX  = stacked ? catLeft : catLeft + si * serW;
                if (stacked)
                {
                    double h = Math.Max(0.5, Math.Abs(val / range) * plotH);
                    dc.FillRectangle(brush, new Rect(barX, stackedY - h, drawW, h));
                    stackedY -= h;
                }
                else
                {
                    double barH = Math.Max(0.5, Math.Abs((val - minVal) / range * plotH));
                    double barY = plotY + plotH - (val - minVal) / range * plotH;
                    dc.FillRectangle(brush, new Rect(barX, barY, drawW, barH));
                }
            }
        }
    }

    private static void RenderBarChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;
        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;
        bool stacked    = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        const double gapRatio = 1.5;
        double catH    = plotH / catCount;
        double clusterH = catH / (1.0 + gapRatio);
        double halfGap  = (catH - clusterH) / 2.0;
        int serCount    = Math.Max(1, chart.Series.Count);
        double serH     = stacked ? clusterH : clusterH / serCount;

        for (int ci = 0; ci < catCount; ci++)
        {
            int    renderRow = catCount - 1 - ci;
            double catTop    = plotY + renderRow * catH + halfGap;
            double stackedX  = plotX;
            for (int si = 0; si < chart.Series.Count; si++)
            {
                double? rawVal = ci < chart.Series[si].Values.Count ? chart.Series[si].Values[ci] : null;
                if (rawVal is null) continue;
                double val     = rawVal.Value;
                double barW    = Math.Max(0.5, Math.Abs((val - minVal) / range * plotW));
                int    renderSer = stacked ? si : (serCount - 1 - si);
                double barY    = stacked ? catTop : catTop + renderSer * serH;
                double barX    = stacked ? stackedX : plotX;
                var color      = GetSeriesColor(chart, si, ci, seriesColors);
                var brush      = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                double drawH   = Math.Max(1, stacked ? serH : serH - 1);
                dc.FillRectangle(brush, new Rect(barX, barY, barW, drawH));
                if (stacked) stackedX += barW;
            }
        }
    }

    private static void RenderLineChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH, bool withMarkers)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;
        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;
        double stepX = plotW / Math.Max(1, catCount - 1);

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var pen    = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 1.5);
            Point? prev = null;
            for (int ci = 0; ci < catCount; ci++)
            {
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) { prev = null; continue; }
                double px = plotX + ci * stepX;
                double py = plotY + plotH - (rawVal.Value - minVal) / range * plotH;
                var pt = new Point(px, py);
                if (prev.HasValue) dc.DrawLine(pen, prev.Value, pt);
                if (withMarkers)
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), null, pt, 3, 3);
                prev = pt;
            }
        }
    }

    private static void RenderPieChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;
        var values = chart.Series[0].Values.Where(v => v.HasValue && v.Value > 0).Select(v => v!.Value).ToList();
        if (values.Count == 0) return;
        double total = values.Sum();
        if (total <= 0) return;

        double cx = plotX + plotW / 2;
        double cy = plotY + plotH / 2;
        double r  = Math.Min(plotW, plotH) / 2 * 0.85;
        double startAngle = -Math.PI / 2;

        var borderPen = new Pen(Brushes.White, 0.8);
        for (int i = 0; i < values.Count; i++)
        {
            double sweepAngle = values[i] / total * 2 * Math.PI;
            double endAngle   = startAngle + sweepAngle;
            SrgbColor sc = i < seriesColors.Count ? seriesColors[i] : new SrgbColor(0x4F, 0x81, 0xBD);
            var brush = new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B));
            bool largeArc = sweepAngle > Math.PI;
            var startPt = new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle));
            var endPt   = new Point(cx + r * Math.Cos(endAngle),   cy + r * Math.Sin(endAngle));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx, cy), isFilled: true);
                ctx.LineTo(startPt);
                ctx.ArcTo(endPt, new Size(r, r), 0, largeArc, SweepDirection.Clockwise);
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, borderPen, geo);
            startAngle = endAngle;
        }
    }

    private static void RenderAreaChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;
        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;
        double stepX = plotW / Math.Max(1, catCount - 1);
        double baseY = plotY + plotH;

        for (int si = chart.Series.Count - 1; si >= 0; si--)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var brush  = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B));
            if (series.Values.Count == 0) continue;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(plotX, baseY), isFilled: true);
                for (int ci = 0; ci < catCount; ci++)
                {
                    double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                    double  val    = rawVal ?? 0;
                    double  px     = plotX + ci * stepX;
                    double  py     = plotY + plotH - (val - minVal) / range * plotH;
                    ctx.LineTo(new Point(px, py));
                }
                ctx.LineTo(new Point(plotX + plotW, baseY));
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(brush, null, geo);
        }
    }

    // ── Doughnut chart ───────────────────────────────────────────────────────

    private static void RenderDoughnutChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;

        double cx  = plotX + plotW / 2;
        double cy  = plotY + plotH / 2;
        double rOut = Math.Min(plotW, plotH) / 2 * 0.85;
        double rIn  = rOut * Math.Clamp(chart.DoughnutHolePercent, 0, 90) / 100.0;

        var borderPen = new Pen(Brushes.White, 0.8);
        int serCount  = chart.Series.Count;
        double ringGap = serCount > 1 ? rOut * 0.04 : 0;
        double ringW   = serCount > 1 ? (rOut - rIn - (serCount - 1) * ringGap) / serCount : (rOut - rIn);

        for (int si = 0; si < serCount; si++)
        {
            var series = chart.Series[si];
            var values = series.Values.Where(v => v.HasValue && v.Value > 0).Select(v => v!.Value).ToList();
            if (values.Count == 0) continue;
            double total = values.Sum();
            if (total <= 0) continue;

            double outerR = rOut - si * (ringW + ringGap);
            double innerR = Math.Max(0, outerR - ringW);
            double startAngle = -Math.PI / 2;

            for (int pi = 0; pi < values.Count; pi++)
            {
                double sweepAngle = values[pi] / total * 2 * Math.PI;
                double endAngle   = startAngle + sweepAngle;
                SrgbColor sc = pi < seriesColors.Count ? seriesColors[pi] : GetSeriesColor(chart, pi, 0, seriesColors);
                var brush = new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B));
                bool largeArc = sweepAngle > Math.PI;

                var outerStart = new Point(cx + outerR * Math.Cos(startAngle), cy + outerR * Math.Sin(startAngle));
                var outerEnd   = new Point(cx + outerR * Math.Cos(endAngle),   cy + outerR * Math.Sin(endAngle));
                var innerEnd   = new Point(cx + innerR * Math.Cos(endAngle),   cy + innerR * Math.Sin(endAngle));
                var innerStart = new Point(cx + innerR * Math.Cos(startAngle), cy + innerR * Math.Sin(startAngle));

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(outerStart, isFilled: true);
                    ctx.ArcTo(outerEnd, new Size(outerR, outerR), 0, largeArc, SweepDirection.Clockwise);
                    ctx.LineTo(innerEnd);
                    ctx.ArcTo(innerStart, new Size(innerR, innerR), 0, largeArc, SweepDirection.CounterClockwise);
                    ctx.EndFigure(isClosed: true);
                }
                dc.DrawGeometry(brush, borderPen, geo);
                startAngle = endAngle;
            }
        }
    }

    // ── Scatter chart ────────────────────────────────────────────────────────

    private static void RenderScatterChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;
        var (xMin, xMax, xUnit) = ComputeNiceScatterAxisRange(chart, useX: true);
        var (yMin, yMax, yUnit) = ComputeNiceAxisRange(chart);
        double xRange = xMax - xMin; double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0) return;

        bool drawLines   = chart.ScatterStyle is ScatterStyle.Line or ScatterStyle.LineMarker
                                               or ScatterStyle.Smooth or ScatterStyle.SmoothMarker;
        bool drawMarkers = chart.ScatterStyle is ScatterStyle.Marker or ScatterStyle.LineMarker
                                               or ScatterStyle.SmoothMarker;
        if (!drawLines && !drawMarkers) drawMarkers = true;

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
        double xSteps = xRange / xUnit, ySteps = yRange / yUnit;
        for (int gi = 0; gi <= (int)Math.Round(xSteps); gi++)
        {
            double gx = plotX + plotW * gi / xSteps;
            dc.DrawLine(gridPen, new Point(gx, plotY), new Point(gx, plotY + plotH));
        }
        for (int gi = 0; gi <= (int)Math.Round(ySteps); gi++)
        {
            double gy = plotY + plotH - plotH * gi / ySteps;
            dc.DrawLine(gridPen, new Point(plotX, gy), new Point(plotX + plotW, gy));
        }

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var pen    = drawLines ? new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 1.5) : null;
            int ptCount = Math.Max(series.XValues.Count, series.Values.Count);
            Point? prev = null;
            for (int pi = 0; pi < ptCount; pi++)
            {
                double? xv = pi < series.XValues.Count ? series.XValues[pi] : null;
                double? yv = pi < series.Values.Count  ? series.Values[pi]  : null;
                if (!xv.HasValue || !yv.HasValue) { prev = null; continue; }
                double px = plotX + (xv.Value - xMin) / xRange * plotW;
                double py = plotY + plotH - (yv.Value - yMin) / yRange * plotH;
                var pt = new Point(px, py);
                if (drawLines && pen is not null && prev.HasValue) dc.DrawLine(pen, prev.Value, pt);
                if (drawMarkers) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), null, pt, 3.5, 3.5);
                prev = pt;
            }
        }

        for (int ti = 0; ti <= (int)Math.Round(xSteps); ti++)
        {
            double val = xMin + xUnit * ti;
            double vx  = plotX + plotW * ti / xSteps;
            DrawChartLabel(dc, FormatAxisValue(val), new Rect(vx - 20, plotY + plotH + 2, 40, 12), false, 6.5, TextAlignment.Center);
        }
        for (int ti = 0; ti <= (int)Math.Round(ySteps); ti++)
        {
            double val = yMin + yUnit * ti;
            double vy  = plotY + plotH - plotH * ti / ySteps;
            DrawChartLabel(dc, FormatAxisValue(val), new Rect(plotX - 38, vy - 6, 36, 12), false, 6.5, TextAlignment.Right);
        }
    }

    // ── Bubble chart ─────────────────────────────────────────────────────────

    private static void RenderBubbleChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;
        var (xMin, xMax, xUnit) = ComputeNiceScatterAxisRange(chart, useX: true);
        var (yMin, yMax, yUnit) = ComputeNiceAxisRange(chart);
        double xRange = xMax - xMin; double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0) return;

        double maxBubble = 0;
        foreach (var s in chart.Series) foreach (var bv in s.BubbleSizes) if (bv.HasValue) maxBubble = Math.Max(maxBubble, bv.Value);
        if (maxBubble <= 0) maxBubble = 1;
        double maxBubbleRadius = Math.Min(plotW, plotH) / 8.0;

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
        double xSteps = xRange / xUnit, ySteps = yRange / yUnit;
        for (int gi = 0; gi <= (int)Math.Round(xSteps); gi++)
        {
            double gx = plotX + plotW * gi / xSteps;
            dc.DrawLine(gridPen, new Point(gx, plotY), new Point(gx, plotY + plotH));
        }
        for (int gi = 0; gi <= (int)Math.Round(ySteps); gi++)
        {
            double gy = plotY + plotH - plotH * gi / ySteps;
            dc.DrawLine(gridPen, new Point(plotX, gy), new Point(plotX + plotW, gy));
        }

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var brush  = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B));
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 0.8);
            int ptCount = Math.Max(series.XValues.Count, series.Values.Count);
            for (int pi = 0; pi < ptCount; pi++)
            {
                double? xv = pi < series.XValues.Count    ? series.XValues[pi]    : null;
                double? yv = pi < series.Values.Count      ? series.Values[pi]     : null;
                double? bv = pi < series.BubbleSizes.Count ? series.BubbleSizes[pi]: null;
                if (!xv.HasValue || !yv.HasValue) continue;
                double px = plotX + (xv.Value - xMin) / xRange * plotW;
                double py = plotY + plotH - (yv.Value - yMin) / yRange * plotH;
                double r  = bv.HasValue ? Math.Sqrt(bv.Value / maxBubble) * maxBubbleRadius : maxBubbleRadius * 0.3;
                r = Math.Max(2, r);
                dc.DrawEllipse(brush, outlinePen, new Point(px, py), r, r);
            }
        }

        for (int ti = 0; ti <= (int)Math.Round(xSteps); ti++)
        {
            double val = xMin + xUnit * ti;
            double vx  = plotX + plotW * ti / xSteps;
            DrawChartLabel(dc, FormatAxisValue(val), new Rect(vx - 20, plotY + plotH + 2, 40, 12), false, 6.5, TextAlignment.Center);
        }
        for (int ti = 0; ti <= (int)Math.Round(ySteps); ti++)
        {
            double val = yMin + yUnit * ti;
            double vy  = plotY + plotH - plotH * ti / ySteps;
            DrawChartLabel(dc, FormatAxisValue(val), new Rect(plotX - 38, vy - 6, 36, 12), false, 6.5, TextAlignment.Right);
        }
    }

    // ── Radar chart ──────────────────────────────────────────────────────────

    private static void RenderRadarChart(
        DrawingContext dc, ChartShape chart, IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;
        int catCount = Math.Max(3, chart.Categories.Count > 0 ? chart.Categories.Count
            : (chart.Series[0].Values.Count > 0 ? chart.Series[0].Values.Count : 3));
        double cx = plotX + plotW / 2;
        double cy = plotY + plotH / 2;
        double r  = Math.Min(plotW, plotH) / 2 * 0.75;

        double dataMax = 0;
        foreach (var s in chart.Series) foreach (var v in s.Values) if (v.HasValue) dataMax = Math.Max(dataMax, Math.Abs(v.Value));
        if (dataMax <= 0) dataMax = 1;

        // Gridline rings
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5);
        for (int ring = 1; ring <= 4; ring++)
        {
            double ringR = r * ring / 4;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int ci = 0; ci < catCount; ci++)
                {
                    double angle = -Math.PI / 2 + 2 * Math.PI * ci / catCount;
                    var pt = new Point(cx + ringR * Math.Cos(angle), cy + ringR * Math.Sin(angle));
                    if (ci == 0) ctx.BeginFigure(pt, isFilled: false);
                    else         ctx.LineTo(pt);
                }
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(null, gridPen, geo);
        }

        // Spokes
        var spokePen = new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)), 0.5);
        for (int ci = 0; ci < catCount; ci++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * ci / catCount;
            dc.DrawLine(spokePen, new Point(cx, cy),
                new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
        }

        // Category labels
        for (int ci = 0; ci < chart.Categories.Count && ci < catCount; ci++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * ci / catCount;
            double lx = cx + (r + 6) * Math.Cos(angle);
            double ly = cy + (r + 6) * Math.Sin(angle);
            DrawChartLabel(dc, chart.Categories[ci], new Rect(lx - 20, ly - 6, 40, 12), false, 6.5, TextAlignment.Center);
        }

        bool withMarkers = chart.RadarStyle == RadarStyle.Marker;
        bool filled      = chart.RadarStyle == RadarStyle.Filled;

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var pen    = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 1.5);
            IBrush? fillBrush = filled ? new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)) : null;

            var polyGeo = new StreamGeometry();
            using (var ctx = polyGeo.Open())
            {
                for (int ci = 0; ci < catCount; ci++)
                {
                    double? v = ci < series.Values.Count ? series.Values[ci] : null;
                    double  frac = Math.Clamp((v ?? 0) / dataMax, 0, 1);
                    double  angle = -Math.PI / 2 + 2 * Math.PI * ci / catCount;
                    var pt = new Point(cx + r * frac * Math.Cos(angle), cy + r * frac * Math.Sin(angle));
                    if (ci == 0) ctx.BeginFigure(pt, isFilled: filled);
                    else         ctx.LineTo(pt);
                }
                ctx.EndFigure(isClosed: true);
            }
            dc.DrawGeometry(fillBrush, pen, polyGeo);

            if (withMarkers)
            {
                var markerBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                for (int ci = 0; ci < catCount; ci++)
                {
                    double? v = ci < series.Values.Count ? series.Values[ci] : null;
                    double  frac = Math.Clamp((v ?? 0) / dataMax, 0, 1);
                    double  angle = -Math.PI / 2 + 2 * Math.PI * ci / catCount;
                    dc.DrawEllipse(markerBrush, null,
                        new Point(cx + r * frac * Math.Cos(angle), cy + r * frac * Math.Sin(angle)), 3, 3);
                }
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

    internal static (double min, double max, double majorUnit) ComputeNiceAxisRange(ChartShape chart)
    {
        double dataMin = 0, dataMax = 0;
        foreach (var series in chart.Series)
            foreach (var v in series.Values)
                if (v.HasValue) { dataMin = Math.Min(dataMin, v.Value); dataMax = Math.Max(dataMax, v.Value); }

        double min = chart.ValueAxis.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.ValueAxis.Max ?? dataMax;
        if (max <= min) max = min + 1;

        double range   = max - min;
        double rawUnit = range / 4.0;
        double mag     = Math.Pow(10, Math.Floor(Math.Log10(rawUnit)));
        double norm    = rawUnit / mag;
        double niceMult = norm switch { < 1.5 => 1.0, < 2.25 => 2.0, < 3.75 => 2.5, < 7.5 => 5.0, _ => 10.0 };
        double mu      = niceMult * mag;
        double niceMax = Math.Ceiling(max / mu) * mu;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / mu) * mu;
        if (Math.Abs(niceMax - max) < mu * 1e-9) niceMax += mu;
        return (niceMin, niceMax, mu);
    }

    internal static (double min, double max, double majorUnit) ComputeNiceScatterAxisRange(
        ChartShape chart, bool useX)
    {
        double dataMin = 0, dataMax = 0;
        foreach (var series in chart.Series)
        {
            var list = useX ? series.XValues : series.Values;
            foreach (var v in list)
                if (v.HasValue) { dataMin = Math.Min(dataMin, v.Value); dataMax = Math.Max(dataMax, v.Value); }
        }
        double min = dataMin >= 0 ? 0 : dataMin;
        double max = dataMax;
        if (max <= min) max = min + 1;
        double range = max - min;
        double rawUnit = range / 4.0;
        if (rawUnit <= 0) rawUnit = 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawUnit)));
        double norm = rawUnit / magnitude;
        double niceMult = norm switch { < 1.5 => 1.0, < 2.25 => 2.0, < 3.75 => 2.5, < 7.5 => 5.0, _ => 10.0 };
        double mu = niceMult * magnitude;
        double niceMax = Math.Ceiling(max / mu) * mu;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / mu) * mu;
        if (Math.Abs(niceMax - max) < mu * 1e-9) niceMax += mu;
        return (niceMin, niceMax, mu);
    }

    private static string FormatAxisValue(double v) =>
        Math.Abs(v) >= 1000
            ? $"{v / 1000:G4}K"
            : v == Math.Floor(v)
                ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);

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

    private static void RenderTextCore(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        double insetLeft   = text.InsetLeftDip;
        double insetTop    = text.InsetTopDip;
        double insetRight  = text.InsetRightDip;
        double insetBottom = text.InsetBottomDip;
        double textAreaW   = Math.Max(0, bounds.Width  - insetLeft - insetRight);
        double textAreaH   = Math.Max(0, bounds.Height - insetTop  - insetBottom);

        var formatted = new List<(FormattedText ft, double spaceAfter)>();
        double totalH = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, textAreaW, text.Wrap);
            formatted.Add((ft, para.SpaceAfterPt * (96.0 / 72.0)));
            totalH += ft.Height + para.SpaceBeforePt * (96.0 / 72.0) + para.SpaceAfterPt * (96.0 / 72.0);
        }

        double startY = text.Anchor switch
        {
            VerticalAnchor.Middle => bounds.Y + insetTop + Math.Max(0, (textAreaH - totalH) / 2),
            VerticalAnchor.Bottom => bounds.Y + insetTop + Math.Max(0, textAreaH - totalH),
            _                     => bounds.Y + insetTop
        };

        double curY   = startY;
        double textX  = bounds.X + insetLeft;

        // Wave 19A: line-spacing scale from normAutofit lnSpcReduction
        double lnSpcScale = 1.0 - text.LnSpcReduction;

        int paraIdx = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) { paraIdx++; continue; }
            var (ft, spaceAfterDip) = formatted[paraIdx];
            curY += para.SpaceBeforePt * (96.0 / 72.0) * lnSpcScale;

            // Wave 19A: draw bullet (char or number) to the left of paragraph text.
            double paraTextX = textX + para.IndentDip;
            if (!string.IsNullOrEmpty(para.BulletText))
            {
                double bulletX = paraTextX - para.HangingDip;
                DrawBulletAvalonia(dc, para.BulletText, para.BulletFontFamily, para.BulletFontSizePt,
                    para.BulletColor, bulletX, curY);
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
                RenderParaWithEffects(dc, para, paraTextX, curY, bounds, text.WarpPreset);
            }
            else if (hasTabs)
            {
                RenderParaWithTabs(dc, para, paraTextX, curY, para.TabStops);
            }
            else
            {
                // Adjust MaxTextWidth to account for indent
                if (para.IndentDip > 0 && ft.MaxTextWidth > 0)
                    ft.MaxTextWidth = Math.Max(1, textAreaW - para.IndentDip);
                dc.DrawText(ft, new Point(paraTextX, curY));
            }

            curY += ft.Height * lnSpcScale + spaceAfterDip * lnSpcScale;
            paraIdx++;
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

                // BO1: compute alignment offset based on the NEXT segment's width.
                double alignOffset = 0;
                TabStopAlignment align = matchedStop?.Alignment ?? TabStopAlignment.Left;
                if (align != TabStopAlignment.Left && seg.Length > 0)
                {
                    var nextFt = BuildSingleRunFormattedTextAt(run, seg);
                    double segW = nextFt.Width;
                    alignOffset = align switch
                    {
                        TabStopAlignment.Right   => -segW,                // segment ends at stop
                        TabStopAlignment.Center  => -segW / 2.0,          // segment centred on stop
                        TabStopAlignment.Decimal =>                        // decimal pt at stop
                            -(seg.Contains('.')
                                ? BuildSingleRunFormattedTextAt(run, seg[..(seg.IndexOf('.') + 1)]).Width
                                : segW),
                        _ => 0
                    };
                }

                curX = startX + stopDip + alignOffset;
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

    private static Func<double, double, double>? BuildWarpYFunc(string? preset, LayoutRect shapeBounds)
    {
        if (string.IsNullOrWhiteSpace(preset)) return null;
        double ampFrac = 0.35;
        return preset.ToLowerInvariant() switch
        {
            "textarchup"   or "textcirclecurve"      => (t, sh) => -sh * ampFrac * 4 * t * (1 - t),
            "textarchdown" or "textarchdownpour"      => (t, sh) =>  sh * ampFrac * 4 * t * (1 - t),
            "textcircle"                              => (t, sh) => -sh * ampFrac * Math.Sin(t * Math.PI),
            "textwaveup"   or "textwave1" or "textwaves" => (t, sh) => -sh * 0.15 * Math.Sin(t * 2 * Math.PI),
            "textwave2"                               => (t, sh) => -sh * 0.10 * Math.Sin(t * 4 * Math.PI),
            "texttriangle" or "texttrianglepour"      => (t, sh) =>  sh * ampFrac * (0.5 - t),
            "textinversetriangle"                     => (t, sh) => -sh * ampFrac * (0.5 - t),
            "textslantup"                             => (t, sh) => -sh * 0.3 * t,
            "textslantdown"                           => (t, sh) =>  sh * 0.3 * t,
            "textcantop"   or "textcan"               => (t, sh) => -sh * ampFrac * Math.Sin(t * Math.PI),
            _                                         => null
        };
    }

    private static void RenderParaWithEffects(
        DrawingContext dc,
        ResolvedParagraph para,
        double x, double y,
        LayoutRect shapeBounds,
        string? warpPreset)
    {
        Func<double, double, double>? warpYOffset = BuildWarpYFunc(warpPreset, shapeBounds);

        int pos = 0;
        foreach (var run in para.Runs)
        {
            bool hasEffects = run.TextFill is not null || run.TextOutline is not null || run.TextShadow is not null;

            double runOffX = ComputeRunOffsetX(para, pos);
            double drawX = x + runOffX;
            double drawY = y;

            // BA2 fix: plain runs are no longer drawn by an outer DrawText pass, so draw them here
            // at their flat baseline (no warp, solid-color fill, no outline).
            if (!hasEffects && warpYOffset is null)
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

            if (warpYOffset is not null)
                drawY += warpYOffset(shapeBounds.Width > 0 ? (drawX - shapeBounds.X) / shapeBounds.Width : 0,
                                     shapeBounds.Height);

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
        double angleRad = g.AngleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);
        return new LinearGradientBrush
        {
            StartPoint    = new RelativePoint(cos >= 0 ? 0 : 1, sin >= 0 ? 0 : 1, RelativeUnit.Relative),
            EndPoint      = new RelativePoint(cos >= 0 ? 1 : 0, sin >= 0 ? 1 : 0, RelativeUnit.Relative),
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
        if (outline is not ResolvedOutline.Visible vis) return null;
        var brush = new SolidColorBrush(Color.FromRgb(vis.Color.R, vis.Color.G, vis.Color.B));
        var pen   = new Pen(brush, vis.WidthDip)
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
                _                          => null   // null = solid (Avalonia default)
            }
        };
        return pen;
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
