using FreeP.App.Compositor;
using System.Windows;
using System.Windows.Controls;
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

        EnsureOps();

        if (_cachedOps is null || _cachedOps.Count == 0 || _slideWidthDip <= 0)
            return;

        double renderW = ActualWidth;
        double renderH = ActualHeight;
        if (renderW <= 0 || renderH <= 0) return;

        // Scale slide DIP coordinates → actual render pixels (uniform fit).
        double scale = Math.Min(renderW / _slideWidthDip, renderH / _slideHeightDip);
        double offsetX = (renderW - _slideWidthDip * scale) / 2;
        double offsetY = (renderH - _slideHeightDip * scale) / 2;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));

        dc.PushTransform(transform);

        foreach (var op in _cachedOps)
            RenderOp(dc, op);

        dc.Pop();
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
        }
    }

    // ── Background ─────────────────────────────────────────────────────────────

    private static void RenderBackground(DrawingContext dc, DrawOp.Background bg)
    {
        var brush = MakeBrush(bg.Fill, bg.BoundsDip);
        if (brush is null) return;

        dc.DrawRectangle(brush, null,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ──────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null) return;

        var bounds = shape.BoundsDip;
        bool hasTransform = shape.RotationDeg != 0 || shape.FlipH || shape.FlipV;

        if (hasTransform)
        {
            dc.PushTransform(BuildShapeTransform(shape));
        }

        // Draw geometry
        if (shape.Geometry.Contours.Count > 0)
        {
            var geometry = ContourListToGeometry(shape.Geometry);
            var fillBrush = MakeBrush(shape.Fill, bounds);
            var pen = MakePen(shape.Outline);
            dc.DrawGeometry(fillBrush, pen, geometry);
        }

        // Draw text overlay
        if (shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        if (hasTransform)
            dc.Pop();
    }

    private static Transform BuildShapeTransform(DrawOp.Shape shape)
    {
        var bounds = shape.BoundsDip;
        double cx = bounds.X + bounds.Width / 2;
        double cy = bounds.Y + bounds.Height / 2;

        var group = new TransformGroup();

        if (shape.FlipH)
            group.Children.Add(new ScaleTransform(-1, 1, cx, cy));

        if (shape.FlipV)
            group.Children.Add(new ScaleTransform(1, -1, cx, cy));

        if (shape.RotationDeg != 0)
            group.Children.Add(new RotateTransform(shape.RotationDeg, cx, cy));

        return group;
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

        var dest = new Rect(pic.DestDip.X, pic.DestDip.Y, pic.DestDip.Width, pic.DestDip.Height);

        bool hasRotation = pic.RotationDeg != 0;
        if (hasRotation)
        {
            double cx = dest.Left + dest.Width / 2;
            double cy = dest.Top + dest.Height / 2;
            dc.PushTransform(new RotateTransform(pic.RotationDeg, cx, cy));
        }

        dc.DrawImage(bitmap, dest);

        if (hasRotation) dc.Pop();
    }

    // ── Text ────────────────────────────────────────────────────────────────────

    private static void RenderText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        double insetLeft = text.InsetLeftDip;
        double insetTop = text.InsetTopDip;
        double insetRight = text.InsetRightDip;
        double insetBottom = text.InsetBottomDip;

        double textAreaW = Math.Max(0, bounds.Width - insetLeft - insetRight);
        double textAreaH = Math.Max(0, bounds.Height - insetTop - insetBottom);

        // Measure all paragraphs to determine total height for vertical anchoring.
        var formatted = new List<(FormattedText ft, double spaceAfter)>();
        double totalH = 0;

        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) continue;

            var ft = BuildFormattedText(para, textAreaW, text.Wrap);
            formatted.Add((ft, para.SpaceAfterPt * (96.0 / 72.0)));
            totalH += ft.Height + para.SpaceBeforePt * (96.0 / 72.0) + para.SpaceAfterPt * (96.0 / 72.0);
        }

        // Determine starting Y based on vertical anchor.
        double startY = text.Anchor switch
        {
            VerticalAnchor.Middle => bounds.Y + insetTop + Math.Max(0, (textAreaH - totalH) / 2),
            VerticalAnchor.Bottom => bounds.Y + insetTop + Math.Max(0, textAreaH - totalH),
            _ => bounds.Y + insetTop  // Top (default)
        };

        double curY = startY;

        int paraIdx = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) { paraIdx++; continue; }

            var (ft, spaceAfterDip) = formatted[paraIdx];
            double spaceBefore = para.SpaceBeforePt * (96.0 / 72.0);
            curY += spaceBefore;

            // Horizontal alignment (FormattedText handles Left/Center/Right/Justify).
            double textX = bounds.X + insetLeft;

            dc.DrawText(ft, new Point(textX, curY));

            curY += ft.Height + spaceAfterDip;
            paraIdx++;
        }
    }

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

        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush,
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

    // ── WPF primitive helpers ──────────────────────────────────────────────────

    private static Brush? MakeBrush(ResolvedFill fill, LayoutRect bounds) => fill switch
    {
        ResolvedFill.None => null,
        ResolvedFill.Solid s => FreezeBrush(
            new SolidColorBrush(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B))),
        ResolvedFill.Gradient g => MakeGradientBrush(g, bounds),
        _ => null
    };

    private static Brush MakeGradientBrush(ResolvedFill.Gradient g, LayoutRect bounds)
    {
        // Angle: 0 = left→right, 90 = top→bottom.
        double angleRad = g.AngleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);

        // Map angle to WPF start/end points (GradientBrush uses 0,0..1,1 relative space).
        // 0° → left to right; 90° → top to bottom
        var startPoint = new Point(
            cos >= 0 ? 0 : 1,
            sin >= 0 ? 0 : 1);
        var endPoint = new Point(
            cos >= 0 ? 1 : 0,
            sin >= 0 ? 1 : 0);

        var brush = new LinearGradientBrush(
            Color.FromRgb(g.StartColor.R, g.StartColor.G, g.StartColor.B),
            Color.FromRgb(g.EndColor.R, g.EndColor.G, g.EndColor.B),
            startPoint,
            endPoint);

        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Pen? MakePen(ResolvedOutline outline)
    {
        if (outline is not ResolvedOutline.Visible vis) return null;

        var brush = new SolidColorBrush(Color.FromRgb(vis.Color.R, vis.Color.G, vis.Color.B));
        if (brush.CanFreeze) brush.Freeze();

        var pen = new Pen(brush, vis.WidthDip);

        var dashStyle = vis.Dash switch
        {
            OutlineDash.Dash => DashStyles.Dash,
            OutlineDash.Dot => DashStyles.Dot,
            OutlineDash.DashDot => DashStyles.DashDot,
            OutlineDash.LongDash => new DashStyle(new[] { 8.0, 3.0 }, 0),
            OutlineDash.LongDashDot => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0 }, 0),
            OutlineDash.LongDashDotDot => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0, 1.0, 3.0 }, 0),
            OutlineDash.SystemDash => DashStyles.Dash,
            OutlineDash.SystemDot => DashStyles.Dot,
            OutlineDash.SystemDashDot => DashStyles.DashDot,
            _ => DashStyles.Solid
        };

        pen.DashStyle = dashStyle;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

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
        _cachedOps = SlideCompositor.Compose(presentation, slide);
    }
}
