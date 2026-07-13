using System.IO;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Exports a <see cref="Presentation"/> to a real PDF — one page per slide — through the shared, portable
/// (no-WPF) <see cref="PortablePdfWriter"/> tier that FreeX and FreeW also use. The exporter emits
/// selectable vector text plus shared vector geometry for slide backgrounds, basic shapes, and
/// connector/line strokes rather than relying on a renderer-specific raster canvas. As the slide
/// model gains more visual primitives, this builder can grow richer draw ops without changing the
/// emitter or the calling code.
/// </summary>
public static class PresentationPdfExporter
{
    // Widescreen 16:9 slide (PowerPoint default 13.333in x 7.5in) in PDF points (1/72 inch).
    public const double DefaultSlideWidthPoints = 960.0;
    public const double DefaultSlideHeightPoints = 540.0;
    private const double MarginPt = 54.0;
    private const double TitleSize = 32.0;
    private const double BodySize = 18.0;
    private const double BodyLeadingPt = 26.0;
    private const double ShapeTextInsetPt = 8.0;
    private const double DefaultStrokeWidthPt = 0.75;
    private const double ArrowheadMinLengthPt = 8.0;
    private const double ArrowheadLengthStrokeScale = 4.0;
    private const double ArrowheadHalfWidthRatio = 0.35;
    private const double EmuPerPoint = 12700.0;
    private const double DipToPoint = 0.75;

    /// <summary>Renders the presentation to PDF bytes in memory.</summary>
    public static byte[] ExportToBytes(Presentation presentation) =>
        PortablePdfWriter.WriteToBytes(BuildDocument(presentation), "FreeP portable PDF");

    /// <summary>Renders the presentation and writes the PDF to <paramref name="stream"/> (not disposed).</summary>
    public static void Export(Presentation presentation, Stream stream) =>
        PortablePdfWriter.Write(BuildDocument(presentation), stream, "FreeP portable PDF");

    /// <summary>Builds the app-agnostic content document (one page per slide) handed to the PDF emitter.</summary>
    public static PdfContentDocument BuildDocument(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var pages = new List<PdfContentPage>(Math.Max(presentation.Slides.Count, 1));
        if (presentation.Slides.Count == 0)
            pages.Add(BuildSlidePage(
                new Slide(),
                presentation.SlideSizeCxEmu,
                presentation.SlideSizeCyEmu)); // a valid PDF always has at least one page
        else
            foreach (var slide in presentation.Slides)
                pages.Add(BuildSlidePage(slide, presentation.SlideSizeCxEmu, presentation.SlideSizeCyEmu));

        var p = presentation.Properties;
        var properties = new PdfDocumentProperties(
            Title: NullIfBlank(p.Title),
            Author: NullIfBlank(p.Author),
            Subject: NullIfBlank(p.Subject),
            Keywords: NullIfBlank(p.Keywords),
            Creator: "FreeP");

        return new PdfContentDocument(pages, properties);
    }

    /// <summary>Builds the portable draw-op page for one slide at FreeP's default 16:9 slide size.</summary>
    public static PdfContentPage BuildSlidePage(Slide slide)
        => BuildSlidePage(slide, slideWidthPoints: DefaultSlideWidthPoints, slideHeightPoints: DefaultSlideHeightPoints);

    /// <summary>Builds the portable draw-op page for one slide at the presentation's modeled slide size.</summary>
    public static PdfContentPage BuildSlidePage(Slide slide, long slideWidthEmu, long slideHeightEmu)
        => BuildSlidePage(
            slide,
            slideWidthPoints: slideWidthEmu > 0 ? EmuToPoints(slideWidthEmu) : DefaultSlideWidthPoints,
            slideHeightPoints: slideHeightEmu > 0 ? EmuToPoints(slideHeightEmu) : DefaultSlideHeightPoints);

    private static PdfContentPage BuildSlidePage(
        Slide slide,
        double slideWidthPoints,
        double slideHeightPoints)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var ops = new List<PdfDrawOp>();

        if (TryMapFill(slide.Background, out var background, out var backgroundOpacity))
            AddWithOpacity(ops, new PdfFillRect(0, 0, slideWidthPoints, slideHeightPoints, background), backgroundOpacity);

        // PDF user space has its origin at the bottom-left with y increasing upward, so we lay out from the
        // top down by starting at (height - margin) and decreasing y for each line.
        var y = slideHeightPoints - MarginPt - TitleSize;
        if (!string.IsNullOrEmpty(slide.Title))
            ops.Add(new PdfText(MarginPt, y, TitleSize, PdfFontFace.Bold, PdfColor.Black, OneLine(slide.Title)));
        y -= TitleSize * 1.4;

        // Skip placeholder shapes (title already rendered above; body placeholders have no freestanding text).
        foreach (var shape in slide.Shapes.Where(s => s.Placeholder is null))
        {
            var shapeOps = new List<PdfDrawOp>();
            var shapeBox = TryAppendShapeGeometry(shapeOps, shape, slideHeightPoints);
            var geometryOpsCount = shapeOps.Count;
            var hasText = !string.IsNullOrEmpty(shape.Text);
            var content = hasText ? shape.Text : $"[{shape.Kind}]";

            if (shapeBox is { } box)
            {
                AppendShapeEffectOps(
                    ops,
                    shape,
                    shapeOps.Take(geometryOpsCount).ToArray(),
                    box);

                if (hasText || (!IsConnectorLike(shape) && !IsPictureLike(shape)))
                    AppendShapeText(shapeOps, box, content);

                AppendShapeOps(ops, shapeOps, box, shape.RotationDeg);
                continue;
            }

            foreach (var line in Lines(content))
            {
                if (y < MarginPt)
                    return new PdfContentPage(slideWidthPoints, slideHeightPoints, ops); // ran out of room on this slide
                ops.Add(new PdfText(MarginPt, y, BodySize, PdfFontFace.Regular, PdfColor.Black, OneLine(line)));
                y -= BodyLeadingPt;
            }
        }

        return new PdfContentPage(slideWidthPoints, slideHeightPoints, ops);
    }

    private static void AppendShapeEffectOps(
        List<PdfDrawOp> ops,
        SlideShape shape,
        IReadOnlyList<PdfDrawOp> geometryOps,
        ShapeBox box)
    {
        if (geometryOps.Count == 0 || IsPictureLike(shape))
            return;

        var plan = ShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects);
        if (plan.ShadowPasses.Count == 0 && plan.GlowPasses.Count == 0)
            return;

        foreach (var shadow in plan.ShadowPasses)
        {
            var shadowOps = CreateShadowPassOps(geometryOps, shadow);
            AppendEffectPass(ops, shadowOps, shadow.Alpha, box, shape.RotationDeg);
        }

        foreach (var glow in plan.GlowPasses)
        {
            var glowOps = CreateGlowPassOps(geometryOps, glow);
            AppendEffectPass(ops, glowOps, glow.Alpha, box, shape.RotationDeg);
        }
    }

    private static void AppendEffectPass(
        List<PdfDrawOp> ops,
        IReadOnlyList<PdfDrawOp> passOps,
        byte alpha,
        ShapeBox box,
        double rotationDegrees)
    {
        if (passOps.Count == 0 || alpha == 0)
            return;

        AppendShapeOps(
            ops,
            [new PdfOpacityGroup(alpha / 255.0, passOps)],
            box,
            rotationDegrees);
    }

    private static IReadOnlyList<PdfDrawOp> CreateShadowPassOps(
        IReadOnlyList<PdfDrawOp> geometryOps,
        ShapeShadowPass shadow)
    {
        var color = ToPdfColor(shadow.Color);
        var offsetX = shadow.OffsetX * DipToPoint;
        var offsetY = -shadow.OffsetY * DipToPoint;
        var passOps = new List<PdfDrawOp>(geometryOps.Count);

        foreach (var op in geometryOps)
        {
            switch (op)
            {
                case PdfFillRect fill:
                    passOps.Add(new PdfFillRect(
                        fill.X + offsetX,
                        fill.Y + offsetY,
                        fill.Width,
                        fill.Height,
                        color));
                    break;
                case PdfStrokeRect stroke:
                    passOps.Add(new PdfStrokeRect(
                        stroke.X + offsetX,
                        stroke.Y + offsetY,
                        stroke.Width,
                        stroke.Height,
                        color,
                        stroke.LineWidth));
                    break;
                case PdfFillEllipse fillEllipse:
                    passOps.Add(new PdfFillEllipse(
                        fillEllipse.X + offsetX,
                        fillEllipse.Y + offsetY,
                        fillEllipse.Width,
                        fillEllipse.Height,
                        color));
                    break;
                case PdfStrokeEllipse strokeEllipse:
                    passOps.Add(new PdfStrokeEllipse(
                        strokeEllipse.X + offsetX,
                        strokeEllipse.Y + offsetY,
                        strokeEllipse.Width,
                        strokeEllipse.Height,
                        color,
                        strokeEllipse.LineWidth));
                    break;
                case PdfPath path:
                    passOps.Add(new PdfPath(
                        OffsetContours(path.Contours, offsetX, offsetY),
                        path.FillColor is null ? null : color,
                        path.StrokeColor is null ? null : color,
                        path.StrokeWidth));
                    break;
                case PdfLine line:
                    passOps.Add(new PdfLine(
                        line.X1 + offsetX,
                        line.Y1 + offsetY,
                        line.X2 + offsetX,
                        line.Y2 + offsetY,
                        color,
                        line.LineWidth));
                    break;
                case PdfFilledTriangle triangle:
                    passOps.Add(new PdfFilledTriangle(
                        triangle.X1 + offsetX,
                        triangle.Y1 + offsetY,
                        triangle.X2 + offsetX,
                        triangle.Y2 + offsetY,
                        triangle.X3 + offsetX,
                        triangle.Y3 + offsetY,
                        color));
                    break;
            }
        }

        return passOps;
    }

    private static IReadOnlyList<PdfDrawOp> CreateGlowPassOps(
        IReadOnlyList<PdfDrawOp> geometryOps,
        ShapeGlowPass glow)
    {
        var color = ToPdfColor(glow.Color);
        var lineWidth = Math.Max(0.1, glow.StrokeWidthDip * DipToPoint);
        var passOps = new List<PdfDrawOp>(geometryOps.Count);
        var filledRects = new HashSet<ShapeBounds>();
        var filledEllipses = new HashSet<ShapeBounds>();

        foreach (var op in geometryOps)
        {
            switch (op)
            {
                case PdfFillRect fill:
                {
                    var bounds = new ShapeBounds(fill.X, fill.Y, fill.Width, fill.Height);
                    filledRects.Add(bounds);
                    passOps.Add(new PdfStrokeRect(fill.X, fill.Y, fill.Width, fill.Height, color, lineWidth));
                    break;
                }
                case PdfStrokeRect stroke:
                {
                    var bounds = new ShapeBounds(stroke.X, stroke.Y, stroke.Width, stroke.Height);
                    if (!filledRects.Contains(bounds))
                        passOps.Add(new PdfStrokeRect(stroke.X, stroke.Y, stroke.Width, stroke.Height, color, lineWidth));
                    break;
                }
                case PdfFillEllipse fillEllipse:
                {
                    var bounds = new ShapeBounds(fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height);
                    filledEllipses.Add(bounds);
                    passOps.Add(new PdfStrokeEllipse(fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, color, lineWidth));
                    break;
                }
                case PdfStrokeEllipse strokeEllipse:
                {
                    var bounds = new ShapeBounds(strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height);
                    if (!filledEllipses.Contains(bounds))
                        passOps.Add(new PdfStrokeEllipse(strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height, color, lineWidth));
                    break;
                }
                case PdfPath path:
                    passOps.Add(new PdfPath(path.Contours, null, color, lineWidth));
                    break;
                case PdfLine line:
                    passOps.Add(new PdfLine(line.X1, line.Y1, line.X2, line.Y2, color, lineWidth));
                    break;
            }
        }

        return passOps;
    }

    private static IReadOnlyList<PdfPathContour> OffsetContours(
        IReadOnlyList<PdfPathContour> contours,
        double offsetX,
        double offsetY)
    {
        var translated = new List<PdfPathContour>(contours.Count);
        foreach (var contour in contours)
        {
            var segments = new List<PdfPathSegment>(contour.Segments.Count);
            foreach (var segment in contour.Segments)
            {
                segments.Add(segment.Kind switch
                {
                    PdfPathSegmentKind.Line => PdfPathSegment.LineTo(OffsetPoint(segment.End, offsetX, offsetY)),
                    PdfPathSegmentKind.CubicBezier => PdfPathSegment.BezierTo(
                        OffsetPoint(segment.Control1, offsetX, offsetY),
                        OffsetPoint(segment.Control2, offsetX, offsetY),
                        OffsetPoint(segment.End, offsetX, offsetY)),
                    _ => segment,
                });
            }

            translated.Add(new PdfPathContour(
                OffsetPoint(contour.Start, offsetX, offsetY),
                segments,
                contour.Closed));
        }

        return translated;
    }

    private static PdfPathPoint OffsetPoint(PdfPathPoint point, double offsetX, double offsetY) =>
        new(point.X + offsetX, point.Y + offsetY);

    private static void AppendShapeOps(
        List<PdfDrawOp> ops,
        IReadOnlyList<PdfDrawOp> shapeOps,
        ShapeBox box,
        double rotationDegrees)
    {
        if (shapeOps.Count == 0)
            return;

        if (shapeOps.Any(op => op is PdfImage) || Math.Abs(rotationDegrees) <= 0.001)
        {
            ops.AddRange(shapeOps);
            return;
        }

        ops.Add(new PdfRotationGroup(
            box.X + box.Width / 2.0,
            box.Y + box.Height / 2.0,
            rotationDegrees,
            shapeOps));
    }

    private static ShapeBox? TryAppendShapeGeometry(List<PdfDrawOp> ops, SlideShape shape, double slideHeightPoints)
    {
        var width = EmuToPoints(shape.ExtentCxEmu);
        var height = EmuToPoints(shape.ExtentCyEmu);
        if (width <= 0 || height <= 0)
            return null;

        var x = EmuToPoints(shape.OffsetXEmu);
        var y = slideHeightPoints - EmuToPoints(shape.OffsetYEmu) - height;

        if (TryAppendPictureImage(ops, shape, x, y, width, height))
            return new ShapeBox(x, y, width, height);

        if (TryAppendConnectorGeometry(ops, shape, x, y, width, height, slideHeightPoints))
            return new ShapeBox(x, y, width, height);

        if (TryAppendCustomGeometry(ops, shape, x, y, width, height))
            return new ShapeBox(x, y, width, height);

        if (IsEllipseLike(shape))
        {
            if (TryMapFill(shape.Fill, out var fill, out var fillOpacity))
                AddWithOpacity(ops, new PdfFillEllipse(x, y, width, height, fill), fillOpacity);

            if (TryMapOutline(shape.Outline, out var stroke, out var strokeWidth, out var strokeOpacity))
                AddWithOpacity(ops, new PdfStrokeEllipse(x, y, width, height, stroke, strokeWidth), strokeOpacity);

            return new ShapeBox(x, y, width, height);
        }

        if (TryMapFill(shape.Fill, out var rectFill, out var rectFillOpacity))
            AddWithOpacity(ops, new PdfFillRect(x, y, width, height, rectFill), rectFillOpacity);

        if (TryMapOutline(shape.Outline, out var rectStroke, out var rectStrokeWidth, out var rectStrokeOpacity))
            AddWithOpacity(ops, new PdfStrokeRect(x, y, width, height, rectStroke, rectStrokeWidth), rectStrokeOpacity);

        return new ShapeBox(x, y, width, height);
    }

    private static bool TryAppendPictureImage(
        List<PdfDrawOp> ops,
        SlideShape shape,
        double x,
        double y,
        double width,
        double height)
    {
        if (!IsPictureLike(shape) || shape.Picture is not { Bytes.Length: > 0 } picture)
            return false;
        if (!IsSupportedImageContentType(picture.ContentType))
            return false;

        ops.Add(new PdfImage(
            x,
            y,
            width,
            height,
            picture.Bytes,
            picture.ContentType,
            shape.RotationDeg,
            MapPictureFrameClip(shape.PictureFrameGeometry),
            MapPictureOpacity(shape),
            MapPictureSourceCrop(shape),
            MapPictureColorEffects(shape)));
        return true;
    }

    private static PdfImageClipKind MapPictureFrameClip(string? pictureFrameGeometry) =>
        pictureFrameGeometry switch
        {
            "ellipse" => PdfImageClipKind.Ellipse,
            "roundRect" => PdfImageClipKind.RoundedRectangle,
            _ => PdfImageClipKind.None,
        };

    private static double MapPictureOpacity(SlideShape shape) =>
        shape.Kind == SlideShapeKind.Picture && shape.PictureFormat?.AlphaModPct is { } opacity
            ? Math.Clamp(opacity, 0.0, 1.0)
            : 1.0;

    private static PdfImageSourceCrop MapPictureSourceCrop(SlideShape shape)
    {
        var format = shape.PictureFormat;
        return format is { HasCrop: true }
            ? new PdfImageSourceCrop(
                format.CropLeft,
                format.CropTop,
                format.CropRight,
                format.CropBottom)
            : default;
    }

    private static PdfImageColorEffects MapPictureColorEffects(SlideShape shape)
    {
        var format = shape.PictureFormat;
        return format is { HasColorEffect: true }
            ? new PdfImageColorEffects(
                format.Grayscale,
                format.BiLevelThreshold,
                format.Brightness,
                format.Contrast)
            : default;
    }

    private static bool TryAppendConnectorGeometry(
        List<PdfDrawOp> ops,
        SlideShape shape,
        double x,
        double y,
        double width,
        double height,
        double slideHeightPoints)
    {
        if (!IsConnectorLike(shape))
            return false;

        if (!TryMapOutline(shape.Outline, out var stroke, out var strokeWidth, out var strokeOpacity))
            return true;

        if (shape.AutoShapeKind == DrawingShapeKind.ElbowConnector
            && shape.ElbowRoute is { Count: >= 2 } route)
        {
            for (var i = 1; i < route.Count; i++)
            {
                var start = ToPdfPoint(route[i - 1], slideHeightPoints);
                var routeEnd = ToPdfPoint(route[i], slideHeightPoints);
                AddWithOpacity(ops, new PdfLine(start.X, start.Y, routeEnd.X, routeEnd.Y, stroke, strokeWidth), strokeOpacity);
            }

            if (TryGetLineEnds(shape.Outline, out var beginLineEnd, out var endLineEnd))
            {
                var first = ToPdfPoint(route[0], slideHeightPoints);
                var second = ToPdfPoint(route[1], slideHeightPoints);
                var penultimate = ToPdfPoint(route[^2], slideHeightPoints);
                var last = ToPdfPoint(route[^1], slideHeightPoints);
                AppendLineEndMarker(ops, beginLineEnd, first.X, first.Y, second.X, second.Y, stroke, strokeWidth, strokeOpacity);
                AppendLineEndMarker(ops, endLineEnd, last.X, last.Y, penultimate.X, penultimate.Y, stroke, strokeWidth, strokeOpacity);
            }

            return true;
        }

        var (x1, y1, x2, y2) = GetLineEndpoints(shape, x, y, width, height);
        AddWithOpacity(ops, new PdfLine(x1, y1, x2, y2, stroke, strokeWidth), strokeOpacity);
        if (TryGetLineEnds(shape.Outline, out var begin, out var lineEnd))
        {
            AppendLineEndMarker(ops, begin, x1, y1, x2, y2, stroke, strokeWidth, strokeOpacity);
            AppendLineEndMarker(ops, lineEnd, x2, y2, x1, y1, stroke, strokeWidth, strokeOpacity);
        }

        return true;
    }

    private static bool TryAppendCustomGeometry(
        List<PdfDrawOp> ops,
        SlideShape shape,
        double x,
        double y,
        double width,
        double height)
    {
        if (shape.Kind != SlideShapeKind.AutoShape || shape.CustomGeometry.Count == 0)
            return false;

        var hasFill = TryMapFill(shape.Fill, out var fill, out var fillOpacity);
        var hasStroke = TryMapOutline(shape.Outline, out var stroke, out var strokeWidth, out var strokeOpacity);

        foreach (var path in shape.CustomGeometry)
        {
            var contours = BuildCustomPathContours(path, x, y, width, height);
            if (contours.Count == 0)
                continue;

            var fillColor = path.Fill && hasFill ? fill : (PdfColor?)null;
            var strokeColor = path.Stroke && hasStroke ? stroke : (PdfColor?)null;
            if (fillColor is null && strokeColor is null)
                continue;

            if (fillColor is not null && strokeColor is not null && Math.Abs(fillOpacity - strokeOpacity) < 0.0001)
            {
                AddWithOpacity(ops, new PdfPath(contours, fillColor, strokeColor, strokeWidth), fillOpacity);
                continue;
            }

            if (fillColor is not null)
                AddWithOpacity(ops, new PdfPath(contours, fillColor, null, strokeWidth), fillOpacity);
            if (strokeColor is not null)
                AddWithOpacity(ops, new PdfPath(contours, null, strokeColor, strokeWidth), strokeOpacity);
        }

        return true;
    }

    private static IReadOnlyList<PdfPathContour> BuildCustomPathContours(
        CustomGeometryPath path,
        double x,
        double y,
        double width,
        double height)
    {
        var pathWidth = path.PathW > 0 ? path.PathW : Math.Max(1, width);
        var pathHeight = path.PathH > 0 ? path.PathH : Math.Max(1, height);
        var scaleX = width / pathWidth;
        var scaleY = height / pathHeight;
        var contours = new List<PdfPathContour>();
        var segments = new List<PdfPathSegment>();
        PdfPathPoint current = new(x, y + height);
        PdfPathPoint? start = null;
        (double X, double Y) currentSource = (0, 0);

        PdfPathPoint Map(double pointX, double pointY) =>
            new(x + (pointX * scaleX), y + height - (pointY * scaleY));

        void Flush(bool closed)
        {
            if (start is not { } contourStart)
                return;

            contours.Add(new PdfPathContour(contourStart, segments.ToArray(), closed));
            segments.Clear();
            start = null;
        }

        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case CustomSegmentKind.MoveTo:
                    if (start is not null && segments.Count > 0)
                        Flush(closed: false);
                    current = Map(segment.X, segment.Y);
                    currentSource = (segment.X, segment.Y);
                    start = current;
                    break;
                case CustomSegmentKind.LineTo:
                {
                    EnsureStarted();
                    var end = Map(segment.X, segment.Y);
                    segments.Add(PdfPathSegment.LineTo(end));
                    current = end;
                    currentSource = (segment.X, segment.Y);
                    break;
                }
                case CustomSegmentKind.CubicBezTo:
                {
                    EnsureStarted();
                    var control1 = Map(segment.X, segment.Y);
                    var control2 = Map(segment.X1, segment.Y1);
                    var end = Map(segment.X2, segment.Y2);
                    segments.Add(PdfPathSegment.BezierTo(control1, control2, end));
                    current = end;
                    currentSource = (segment.X2, segment.Y2);
                    break;
                }
                case CustomSegmentKind.QuadBezTo:
                {
                    EnsureStarted();
                    var control = Map(segment.X, segment.Y);
                    var end = Map(segment.X1, segment.Y1);
                    var control1 = new PdfPathPoint(
                        current.X + (2.0 / 3.0 * (control.X - current.X)),
                        current.Y + (2.0 / 3.0 * (control.Y - current.Y)));
                    var control2 = new PdfPathPoint(
                        end.X + (2.0 / 3.0 * (control.X - end.X)),
                        end.Y + (2.0 / 3.0 * (control.Y - end.Y)));
                    segments.Add(PdfPathSegment.BezierTo(control1, control2, end));
                    current = end;
                    currentSource = (segment.X1, segment.Y1);
                    break;
                }
                case CustomSegmentKind.ArcTo:
                {
                    EnsureStarted();
                    var arcSegments = BuildArcBezierSegments(currentSource, segment, Map, scaleX, scaleY);
                    if (arcSegments.Count == 0)
                    {
                        var endSource = GetArcEnd(currentSource, segment);
                        var end = Map(endSource.X, endSource.Y);
                        segments.Add(PdfPathSegment.LineTo(end));
                        current = end;
                        currentSource = endSource;
                        break;
                    }

                    segments.AddRange(arcSegments);
                    current = arcSegments[^1].End;
                    currentSource = GetArcEnd(currentSource, segment);
                    break;
                }
                case CustomSegmentKind.Close:
                    Flush(closed: true);
                    break;
            }
        }

        if (start is not null && segments.Count > 0)
            Flush(closed: false);

        return contours;

        void EnsureStarted()
        {
            if (start is null)
                start = current;
        }
    }

    private static IReadOnlyList<PdfPathSegment> BuildArcBezierSegments(
        (double X, double Y) currentSource,
        CustomSegment segment,
        Func<double, double, PdfPathPoint> map,
        double scaleX,
        double scaleY)
    {
        if (Math.Abs(segment.WR) < 0.001 ||
            Math.Abs(segment.HR) < 0.001 ||
            Math.Abs(segment.SwAng) < 0.001)
            return Array.Empty<PdfPathSegment>();

        var radiusX = segment.WR;
        var radiusY = segment.HR;
        var startAngle = segment.StAng * Math.PI / 180.0;
        var sweepAngle = segment.SwAng * Math.PI / 180.0;
        var centerX = currentSource.X - radiusX * Math.Cos(startAngle);
        var centerY = currentSource.Y - radiusY * Math.Sin(startAngle);
        var segmentCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2.0)));
        var angleStep = sweepAngle / segmentCount;
        var result = new List<PdfPathSegment>(segmentCount);

        for (var index = 0; index < segmentCount; index++)
        {
            var a0 = startAngle + angleStep * index;
            var a1 = a0 + angleStep;
            var p0 = PointAt(a0);
            var p3 = PointAt(a1);
            var k = 4.0 / 3.0 * Math.Tan((a1 - a0) / 4.0);
            var d0 = DerivativeAt(a0);
            var d1 = DerivativeAt(a1);
            var c1 = new PdfPathPoint(p0.X + k * d0.X, p0.Y + k * d0.Y);
            var c2 = new PdfPathPoint(p3.X - k * d1.X, p3.Y - k * d1.Y);

            result.Add(PdfPathSegment.BezierTo(c1, c2, p3));
        }

        return result;

        PdfPathPoint PointAt(double angle) =>
            map(
                centerX + radiusX * Math.Cos(angle),
                centerY + radiusY * Math.Sin(angle));

        PdfPathPoint DerivativeAt(double angle) =>
            new(
                -radiusX * scaleX * Math.Sin(angle),
                -radiusY * scaleY * Math.Cos(angle));
    }

    private static (double X, double Y) GetArcEnd((double X, double Y) currentSource, CustomSegment segment)
    {
        var startAngle = segment.StAng * Math.PI / 180.0;
        var sweepAngle = segment.SwAng * Math.PI / 180.0;
        var endAngle = startAngle + sweepAngle;
        var centerX = currentSource.X - (segment.WR * Math.Cos(startAngle));
        var centerY = currentSource.Y - (segment.HR * Math.Sin(startAngle));
        return (
            centerX + (segment.WR * Math.Cos(endAngle)),
            centerY + (segment.HR * Math.Sin(endAngle)));
    }

    private static bool TryGetLineEnds(ShapeOutline? outline, out ShapeLineEnd? begin, out ShapeLineEnd? end)
    {
        switch (outline)
        {
            case ShapeOutline.Visible visible:
                begin = visible.BeginLineEnd;
                end = visible.EndLineEnd;
                return begin is not null || end is not null;
            case ShapeOutline.GradientVisible gradient:
                begin = gradient.BeginLineEnd;
                end = gradient.EndLineEnd;
                return begin is not null || end is not null;
            default:
                begin = null;
                end = null;
                return false;
        }
    }

    private static void AppendLineEndMarker(
        List<PdfDrawOp> ops,
        ShapeLineEnd? lineEnd,
        double tipX,
        double tipY,
        double adjacentX,
        double adjacentY,
        PdfColor color,
        double strokeWidth,
        double opacity)
    {
        if (lineEnd?.Kind != ShapeLineEndKind.Triangle)
            return;

        var dx = tipX - adjacentX;
        var dy = tipY - adjacentY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 0.001)
            return;

        var ux = dx / distance;
        var uy = dy / distance;
        var length = Math.Max(ArrowheadMinLengthPt, strokeWidth * ArrowheadLengthStrokeScale);
        var halfWidth = length * ArrowheadHalfWidthRatio;
        var baseX = tipX - ux * length;
        var baseY = tipY - uy * length;
        var px = -uy;
        var py = ux;

        AddWithOpacity(
            ops,
            new PdfFilledTriangle(
                tipX,
                tipY,
                baseX + px * halfWidth,
                baseY + py * halfWidth,
                baseX - px * halfWidth,
                baseY - py * halfWidth,
                color),
            opacity);
    }

    private static bool IsConnectorLike(SlideShape shape) =>
        shape.Kind == SlideShapeKind.Connector
        || shape.AutoShapeKind is DrawingShapeKind.Line
            or DrawingShapeKind.ElbowConnector
            or DrawingShapeKind.CurvedConnector;

    private static bool IsPictureLike(SlideShape shape) =>
        shape.Kind is SlideShapeKind.Picture
            or SlideShapeKind.Media
            or SlideShapeKind.Ole
            or SlideShapeKind.PreservedObject
        || shape.Picture is not null;

    private static bool IsEllipseLike(SlideShape shape) =>
        shape.AutoShapeKind == DrawingShapeKind.Ellipse;

    private static bool IsSupportedImageContentType(string? contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim();
        return normalized is not null &&
               (normalized.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpg", StringComparison.OrdinalIgnoreCase));
    }

    private static (double X1, double Y1, double X2, double Y2) GetLineEndpoints(
        SlideShape shape,
        double x,
        double y,
        double width,
        double height)
    {
        var x1 = shape.FlipH ? x + width : x;
        var x2 = shape.FlipH ? x : x + width;
        var y1 = shape.FlipV ? y : y + height;
        var y2 = shape.FlipV ? y + height : y;
        return (x1, y1, x2, y2);
    }

    private static (double X, double Y) ToPdfPoint((long X, long Y) point, double slideHeightPoints) =>
        (EmuToPoints(point.X), slideHeightPoints - EmuToPoints(point.Y));

    private static void AppendShapeText(List<PdfDrawOp> ops, ShapeBox box, string content)
    {
        var y = box.Y + box.Height - ShapeTextInsetPt - BodySize;
        foreach (var line in Lines(content))
        {
            if (y < box.Y + ShapeTextInsetPt)
                return;

            ops.Add(new PdfText(
                box.X + ShapeTextInsetPt,
                y,
                BodySize,
                PdfFontFace.Regular,
                PdfColor.Black,
                OneLine(line)));
            y -= BodyLeadingPt;
        }
    }

    private static void AddWithOpacity(List<PdfDrawOp> ops, PdfDrawOp op, double opacity)
    {
        if (opacity >= 0.999)
        {
            ops.Add(op);
            return;
        }

        if (opacity > 0.0)
            ops.Add(new PdfOpacityGroup(opacity, [op]));
    }

    private static bool TryMapFill(ShapeFill? fill, out PdfColor color, out double opacity)
    {
        switch (fill)
        {
            case ShapeFill.Solid solid:
                color = ToPdfColor(solid.Color);
                opacity = ToPdfOpacity(solid.Color);
                return true;
            case ShapeFill.Gradient gradient:
                color = ToPdfColor(gradient.StartColor);
                opacity = ToPdfOpacity(gradient.StartColor);
                return true;
            case ShapeFill.Pattern pattern:
                color = ToPdfColor(pattern.BackgroundColor);
                opacity = ToPdfOpacity(pattern.BackgroundColor);
                return true;
            default:
                color = default;
                opacity = 1.0;
                return false;
        }
    }

    private static bool TryMapOutline(ShapeOutline? outline, out PdfColor color, out double widthPt, out double opacity)
    {
        switch (outline)
        {
            case ShapeOutline.Visible visible:
                color = ToPdfColor(visible.Color);
                widthPt = Math.Max(0.1, visible.WidthPt);
                opacity = ToPdfOpacity(visible.Color);
                return true;
            case ShapeOutline.GradientVisible gradient:
                color = ToPdfColor(gradient.Gradient.StartColor);
                widthPt = Math.Max(0.1, gradient.WidthPt);
                opacity = ToPdfOpacity(gradient.Gradient.StartColor);
                return true;
            case null:
                color = PdfColor.Black;
                widthPt = DefaultStrokeWidthPt;
                opacity = 1.0;
                return true;
            default:
                color = default;
                widthPt = 0;
                opacity = 1.0;
                return false;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // The portable text op draws a single line; flatten tabs so spacing is at least visible.
    private static string OneLine(string text) => text.Replace("\t", "    ");

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static double EmuToPoints(long emu) => emu / EmuPerPoint;

    private static PdfColor ToPdfColor(ThemeAwareColor color)
    {
        var resolved = color.Resolved;
        return ToPdfColor(resolved);
    }

    private static double ToPdfOpacity(ThemeAwareColor color) => color.Alpha / 255.0;

    private static PdfColor ToPdfColor(SrgbColor color) => new(color.R, color.G, color.B);

    private sealed record ShapeBox(double X, double Y, double Width, double Height);
    private readonly record struct ShapeBounds(double X, double Y, double Width, double Height);
}
