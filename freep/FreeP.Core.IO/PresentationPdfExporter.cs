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

        if (TryMapFill(slide.Background, out var background))
            ops.Add(new PdfFillRect(0, 0, slideWidthPoints, slideHeightPoints, background));

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
            var hasText = !string.IsNullOrEmpty(shape.Text);
            var content = hasText ? shape.Text : $"[{shape.Kind}]";

            if (shapeBox is { } box)
            {
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
            if (TryMapFill(shape.Fill, out var fill))
                ops.Add(new PdfFillEllipse(x, y, width, height, fill));

            if (TryMapOutline(shape.Outline, out var stroke, out var strokeWidth))
                ops.Add(new PdfStrokeEllipse(x, y, width, height, stroke, strokeWidth));

            return new ShapeBox(x, y, width, height);
        }

        if (TryMapFill(shape.Fill, out var rectFill))
            ops.Add(new PdfFillRect(x, y, width, height, rectFill));

        if (TryMapOutline(shape.Outline, out var rectStroke, out var rectStrokeWidth))
            ops.Add(new PdfStrokeRect(x, y, width, height, rectStroke, rectStrokeWidth));

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
            MapPictureOpacity(shape)));
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

        if (!TryMapOutline(shape.Outline, out var stroke, out var strokeWidth))
            return true;

        if (shape.AutoShapeKind == DrawingShapeKind.ElbowConnector
            && shape.ElbowRoute is { Count: >= 2 } route)
        {
            for (var i = 1; i < route.Count; i++)
            {
                var start = ToPdfPoint(route[i - 1], slideHeightPoints);
                var routeEnd = ToPdfPoint(route[i], slideHeightPoints);
                ops.Add(new PdfLine(start.X, start.Y, routeEnd.X, routeEnd.Y, stroke, strokeWidth));
            }

            if (TryGetLineEnds(shape.Outline, out var beginLineEnd, out var endLineEnd))
            {
                var first = ToPdfPoint(route[0], slideHeightPoints);
                var second = ToPdfPoint(route[1], slideHeightPoints);
                var penultimate = ToPdfPoint(route[^2], slideHeightPoints);
                var last = ToPdfPoint(route[^1], slideHeightPoints);
                AppendLineEndMarker(ops, beginLineEnd, first.X, first.Y, second.X, second.Y, stroke, strokeWidth);
                AppendLineEndMarker(ops, endLineEnd, last.X, last.Y, penultimate.X, penultimate.Y, stroke, strokeWidth);
            }

            return true;
        }

        var (x1, y1, x2, y2) = GetLineEndpoints(shape, x, y, width, height);
        ops.Add(new PdfLine(x1, y1, x2, y2, stroke, strokeWidth));
        if (TryGetLineEnds(shape.Outline, out var begin, out var lineEnd))
        {
            AppendLineEndMarker(ops, begin, x1, y1, x2, y2, stroke, strokeWidth);
            AppendLineEndMarker(ops, lineEnd, x2, y2, x1, y1, stroke, strokeWidth);
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

        var hasFill = TryMapFill(shape.Fill, out var fill);
        var hasStroke = TryMapOutline(shape.Outline, out var stroke, out var strokeWidth);

        foreach (var path in shape.CustomGeometry)
        {
            var contours = BuildCustomPathContours(path, x, y, width, height);
            if (contours.Count == 0)
                continue;

            var fillColor = path.Fill && hasFill ? fill : (PdfColor?)null;
            var strokeColor = path.Stroke && hasStroke ? stroke : (PdfColor?)null;
            if (fillColor is null && strokeColor is null)
                continue;

            ops.Add(new PdfPath(contours, fillColor, strokeColor, strokeWidth));
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
                    var endSource = GetArcEnd(currentSource, segment);
                    var end = Map(endSource.X, endSource.Y);
                    segments.Add(PdfPathSegment.LineTo(end));
                    current = end;
                    currentSource = endSource;
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
        double strokeWidth)
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

        ops.Add(new PdfFilledTriangle(
            tipX,
            tipY,
            baseX + px * halfWidth,
            baseY + py * halfWidth,
            baseX - px * halfWidth,
            baseY - py * halfWidth,
            color));
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

    private static bool TryMapFill(ShapeFill? fill, out PdfColor color)
    {
        switch (fill)
        {
            case ShapeFill.Solid solid:
                color = ToPdfColor(solid.Color);
                return true;
            case ShapeFill.Gradient gradient:
                color = ToPdfColor(gradient.StartColor);
                return true;
            case ShapeFill.Pattern pattern:
                color = ToPdfColor(pattern.BackgroundColor);
                return true;
            default:
                color = default;
                return false;
        }
    }

    private static bool TryMapOutline(ShapeOutline? outline, out PdfColor color, out double widthPt)
    {
        switch (outline)
        {
            case ShapeOutline.Visible visible:
                color = ToPdfColor(visible.Color);
                widthPt = Math.Max(0.1, visible.WidthPt);
                return true;
            case ShapeOutline.GradientVisible gradient:
                color = ToPdfColor(gradient.Gradient.StartColor);
                widthPt = Math.Max(0.1, gradient.WidthPt);
                return true;
            case null:
                color = PdfColor.Black;
                widthPt = DefaultStrokeWidthPt;
                return true;
            default:
                color = default;
                widthPt = 0;
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
        return new PdfColor(resolved.R, resolved.G, resolved.B);
    }

    private sealed record ShapeBox(double X, double Y, double Width, double Height);
}
