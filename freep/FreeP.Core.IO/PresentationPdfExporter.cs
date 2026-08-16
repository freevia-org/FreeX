using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    public const double DefaultSlideWidthPoints = PresentationPdfScenePlanner.DefaultSlideWidthPoints;
    public const double DefaultSlideHeightPoints = PresentationPdfScenePlanner.DefaultSlideHeightPoints;
    private const double MarginPt = 54.0;
    private const double TitleSize = 32.0;
    private const double BodySize = 18.0;
    private const double BodyLeadingPt = 26.0;
    private const double ShapeTextInsetPt = 8.0;
    private const double DefaultStrokeWidthPt = 0.75;
    private const double ArrowheadMinLengthPt = 8.0;
    private const double ArrowheadLengthStrokeScale = 4.0;
    private const double ArrowheadHalfWidthRatio = 0.35;
    private const double DipToPoint = 0.75;
    private static readonly Regex InkNumberPattern = new(
        @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly XNamespace FreePInkNamespace = "https://freex.local/freep/ink/2026";

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

        return new PdfContentDocument(pages, PresentationPdfScenePlanner.BuildDocumentProperties(presentation));
    }

    /// <summary>Builds the portable draw-op page for one slide at FreeP's default 16:9 slide size.</summary>
    public static PdfContentPage BuildSlidePage(Slide slide)
        => BuildSlidePage(slide, slideWidthPoints: DefaultSlideWidthPoints, slideHeightPoints: DefaultSlideHeightPoints);

    /// <summary>Builds the portable draw-op page for one slide at the presentation's modeled slide size.</summary>
    public static PdfContentPage BuildSlidePage(Slide slide, long slideWidthEmu, long slideHeightEmu)
        => BuildSlidePage(
            slide,
            PresentationPdfScenePlanner.ResolveSlideSize(slideWidthEmu, slideHeightEmu),
            includeCommentsAndInkMarkup: false);

    /// <summary>
    /// Builds a slide page and optionally includes the persisted PowerPoint comments and InkML
    /// markup requested by the print workflow. The default remains the ordinary slide surface.
    /// </summary>
    public static PdfContentPage BuildSlidePage(
        Slide slide,
        long slideWidthEmu,
        long slideHeightEmu,
        bool includeCommentsAndInkMarkup) =>
        BuildSlidePage(
            slide,
            PresentationPdfScenePlanner.ResolveSlideSize(slideWidthEmu, slideHeightEmu),
            includeCommentsAndInkMarkup);

    /// <summary>
    /// Builds a slide page that also lays out non-title placeholder shape text (e.g. bullet/body
    /// placeholders), unlike the overloads above which -- for the plain vector-export path -- skip
    /// every placeholder shape entirely. This overload exists for callers (the raster PDF text
    /// overlay derivation) that need every visible glyph accounted for, not just freestanding
    /// shapes; the title placeholder stays excluded here too since <see cref="Slide.Title"/> already
    /// emits it separately above.
    /// </summary>
    public static PdfContentPage BuildSlidePage(
        Slide slide,
        long slideWidthEmu,
        long slideHeightEmu,
        bool includeCommentsAndInkMarkup,
        bool includePlaceholderShapeText) =>
        BuildSlidePage(
            slide,
            PresentationPdfScenePlanner.ResolveSlideSize(slideWidthEmu, slideHeightEmu),
            includeCommentsAndInkMarkup,
            includePlaceholderShapeText);

    private static PdfContentPage BuildSlidePage(
        Slide slide,
        PresentationPdfSlideSize slideSize,
        bool includeCommentsAndInkMarkup,
        bool includePlaceholderShapeText = false) =>
        BuildSlidePage(
            slide,
            slideSize.WidthPoints,
            slideSize.HeightPoints,
            includeCommentsAndInkMarkup,
            includePlaceholderShapeText);

    private static PdfContentPage BuildSlidePage(
        Slide slide,
        double slideWidthPoints,
        double slideHeightPoints,
        bool includeCommentsAndInkMarkup = false,
        bool includePlaceholderShapeText = false)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var ops = new List<PdfDrawOp>();
        // R137: link annotations for shape/text hyperlinks, plus this slide's own landing point so
        // any OTHER slide's internal hyperlink can jump here regardless of export/render order (see
        // ResolveShapeHyperlink and SlideDestinationName below).
        var linkOverlays = new List<PdfLinkOverlay>();
        var namedDestinations = new List<PdfNamedDestination>
        {
            new(SlideDestinationName(slide.Id), 0, 0),
        };

        if (TryMapFillLinearGradient(
                slide.Background,
                0,
                0,
                slideWidthPoints,
                slideHeightPoints,
                out var backgroundGradient,
                out var backgroundFallback,
                out var backgroundGradientOpacity))
            AddWithOpacity(
                ops,
                new PdfFillRectLinearGradient(
                    0,
                    0,
                    slideWidthPoints,
                    slideHeightPoints,
                    backgroundGradient,
                    backgroundFallback),
                backgroundGradientOpacity);
        else if (TryMapFill(slide.Background, out var background, out var backgroundOpacity))
            AddWithOpacity(ops, new PdfFillRect(0, 0, slideWidthPoints, slideHeightPoints, background), backgroundOpacity);

        // PDF user space has its origin at the bottom-left with y increasing upward, so we lay out from the
        // top down by starting at (height - margin) and decreasing y for each line.
        var y = slideHeightPoints - MarginPt - TitleSize;
        if (!string.IsNullOrEmpty(slide.Title))
            ops.Add(new PdfText(MarginPt, y, TitleSize, PdfFontFace.Bold, PdfColor.Black, OneLine(slide.Title)));
        y -= TitleSize * 1.4;

        // Skip placeholder shapes by default (title already rendered above via slide.Title). When
        // includePlaceholderShapeText is set, non-title placeholders (body/subtitle/etc., which do
        // carry freestanding bullet/body text) are laid out too; the title placeholder stays
        // excluded either way so slide.Title's text is never emitted twice.
        foreach (var shape in slide.Shapes.Where(s =>
            s.Placeholder is null ||
            (includePlaceholderShapeText && s.Placeholder.Type is not (PlaceholderType.Title or PlaceholderType.CenteredTitle))))
        {
            if (shape.Kind == SlideShapeKind.Ink)
            {
                if (includeCommentsAndInkMarkup)
                    AppendInkMarkup(ops, shape, slide, slideWidthPoints, slideHeightPoints);

                continue;
            }

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

                // R137: covers the shape's whole bounding box, not individual glyph runs -- this
                // exporter lays out only the shape's flattened text (see `content`/`shape.Text`
                // above), not per-run positions, so a run-level hyperlink inside a text box becomes a
                // click target over the whole shape rather than just the linked substring. Rotation is
                // not accounted for either (the annotation stays axis-aligned), matching this
                // exporter's other rotation approximations. Still a real, clickable improvement over
                // emitting nothing.
                if (ResolveShapeHyperlink(shape) is { } hyperlink &&
                    BuildLinkOverlay(hyperlink, box, slideHeightPoints) is { } overlay)
                    linkOverlays.Add(overlay);

                continue;
            }

            foreach (var line in Lines(content))
            {
                if (y < MarginPt)
                    return new PdfContentPage(slideWidthPoints, slideHeightPoints, ops, linkOverlays.Count > 0 ? linkOverlays : null, namedDestinations); // ran out of room on this slide
                ops.Add(new PdfText(MarginPt, y, BodySize, PdfFontFace.Regular, PdfColor.Black, OneLine(line)));
                y -= BodyLeadingPt;
            }
        }

        if (includeCommentsAndInkMarkup)
            AppendCommentMarkup(ops, slide, slideWidthPoints, slideHeightPoints);

        return new PdfContentPage(
            slideWidthPoints,
            slideHeightPoints,
            ops,
            linkOverlays.Count > 0 ? linkOverlays : null,
            namedDestinations);
    }

    /// <summary>
    /// The internal-hyperlink target name for <paramref name="slideId"/>'s own page (see
    /// <see cref="Slide.Id"/>): every slide registers this as a <see cref="PdfNamedDestination"/> on
    /// its own page, and a <see cref="Hyperlink.TargetSlideId"/> elsewhere in the deck resolves to it
    /// via the matching <see cref="PdfLinkOverlay.DestinationName"/>, independent of slide order.
    /// </summary>
    private static string SlideDestinationName(string slideId) => $"freep-slide-{slideId}";

    /// <summary>
    /// The hyperlink that should make <paramref name="shape"/> clickable in the exported PDF: an
    /// explicit action on the shape itself (e.g. an action button) takes priority, falling back to
    /// the first hyperlinked text run inside the shape's body (<c>a:hlinkClick</c> on <c>a:rPr</c>).
    /// </summary>
    private static Hyperlink? ResolveShapeHyperlink(SlideShape shape)
    {
        if (shape.Hyperlink is { } shapeLink)
            return shapeLink;

        if (shape.TextBody is { } textBody)
            foreach (var paragraph in textBody.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.Hyperlink is { } runLink)
                        return runLink;

        return null;
    }

    private static PdfLinkOverlay? BuildLinkOverlay(Hyperlink hyperlink, ShapeBox box, double slideHeightPoints)
    {
        var uri = hyperlink.IsExternal ? hyperlink.Url : null;
        var destinationName = !hyperlink.IsExternal && !string.IsNullOrEmpty(hyperlink.TargetSlideId)
            ? SlideDestinationName(hyperlink.TargetSlideId)
            : null;
        if (string.IsNullOrEmpty(uri) && string.IsNullOrEmpty(destinationName))
            return null;

        // box.Y is PDF-native bottom-left/y-up (the box's bottom edge); PdfLinkOverlay wants
        // top-left/y-down, so flip the same way BuildTextOverlay's raster-overlay conversion does:
        // topDownY = pageHeight - bottomUpY - height.
        return new PdfLinkOverlay(
            box.X,
            slideHeightPoints - box.Y - box.Height,
            box.Width,
            box.Height,
            uri,
            hyperlink.Tooltip,
            destinationName);
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
                case PdfFillRectLinearGradient fill:
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
                case PdfStrokeRectLinearGradient stroke:
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
                case PdfFillEllipseLinearGradient fillEllipse:
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
                case PdfStrokeEllipseLinearGradient strokeEllipse:
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
                case PdfPathLinearGradient path:
                    passOps.Add(new PdfPath(
                        OffsetContours(path.Contours, offsetX, offsetY),
                        path.FillFallbackColor is null ? null : color,
                        path.StrokeFallbackColor is null ? null : color,
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
                case PdfLineLinearGradient line:
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
                case PdfFillRectLinearGradient fill:
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
                case PdfStrokeRectLinearGradient stroke:
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
                case PdfFillEllipseLinearGradient fillEllipse:
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
                case PdfStrokeEllipseLinearGradient strokeEllipse:
                {
                    var bounds = new ShapeBounds(strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height);
                    if (!filledEllipses.Contains(bounds))
                        passOps.Add(new PdfStrokeEllipse(strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height, color, lineWidth));
                    break;
                }
                case PdfPath path:
                    passOps.Add(new PdfPath(path.Contours, null, color, lineWidth));
                    break;
                case PdfPathLinearGradient path:
                    passOps.Add(new PdfPath(path.Contours, null, color, lineWidth));
                    break;
                case PdfLine line:
                    passOps.Add(new PdfLine(line.X1, line.Y1, line.X2, line.Y2, color, lineWidth));
                    break;
                case PdfLineLinearGradient line:
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
        var width = PresentationPdfScenePlanner.EmuToPoints(shape.ExtentCxEmu);
        var height = PresentationPdfScenePlanner.EmuToPoints(shape.ExtentCyEmu);
        if (width <= 0 || height <= 0)
            return null;

        var x = PresentationPdfScenePlanner.EmuToPoints(shape.OffsetXEmu);
        var y = slideHeightPoints - PresentationPdfScenePlanner.EmuToPoints(shape.OffsetYEmu) - height;

        if (TryAppendPictureImage(ops, shape, x, y, width, height))
            return new ShapeBox(x, y, width, height);

        if (TryAppendConnectorGeometry(ops, shape, x, y, width, height, slideHeightPoints))
            return new ShapeBox(x, y, width, height);

        if (TryAppendCustomGeometry(ops, shape, x, y, width, height))
            return new ShapeBox(x, y, width, height);

        if (IsEllipseLike(shape))
        {
            if (TryMapFillLinearGradient(shape.Fill, x, y, width, height, out var fillGradient, out var fillFallback, out var fillOpacity))
                AddWithOpacity(ops, new PdfFillEllipseLinearGradient(x, y, width, height, fillGradient, fillFallback), fillOpacity);
            else if (TryMapFill(shape.Fill, out var fill, out fillOpacity))
                AddWithOpacity(ops, new PdfFillEllipse(x, y, width, height, fill), fillOpacity);

            if (TryMapOutlineLinearGradient(shape.Outline, x, y, width, height, out var strokeGradient, out var strokeFallback, out var strokeWidth, out var strokeOpacity))
                AddWithOpacity(ops, new PdfStrokeEllipseLinearGradient(x, y, width, height, strokeGradient, strokeFallback, strokeWidth), strokeOpacity);
            else if (TryMapOutline(shape.Outline, out var stroke, out strokeWidth, out strokeOpacity))
                AddWithOpacity(ops, new PdfStrokeEllipse(x, y, width, height, stroke, strokeWidth), strokeOpacity);

            return new ShapeBox(x, y, width, height);
        }

        if (TryMapFillLinearGradient(shape.Fill, x, y, width, height, out var rectFillGradient, out var rectFillFallback, out var rectFillOpacity))
            AddWithOpacity(ops, new PdfFillRectLinearGradient(x, y, width, height, rectFillGradient, rectFillFallback), rectFillOpacity);
        else if (TryMapFill(shape.Fill, out var rectFill, out rectFillOpacity))
            AddWithOpacity(ops, new PdfFillRect(x, y, width, height, rectFill), rectFillOpacity);

        if (TryMapOutlineLinearGradient(shape.Outline, x, y, width, height, out var rectStrokeGradient, out var rectStrokeFallback, out var rectStrokeWidth, out var rectStrokeOpacity))
            AddWithOpacity(ops, new PdfStrokeRectLinearGradient(x, y, width, height, rectStrokeGradient, rectStrokeFallback, rectStrokeWidth), rectStrokeOpacity);
        else if (TryMapOutline(shape.Outline, out var rectStroke, out rectStrokeWidth, out rectStrokeOpacity))
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
        pictureFrameGeometry?.Trim() switch
        {
            string geometry when geometry.Equals("ellipse", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Ellipse,
            string geometry when geometry.Equals("roundRect", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.RoundedRectangle,
            string geometry when geometry.Equals("triangle", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Triangle,
            string geometry when geometry.Equals("diamond", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Diamond,
            string geometry when geometry.Equals("parallelogram", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Parallelogram,
            string geometry when geometry.Equals("hexagon", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Hexagon,
            string geometry when geometry.Equals("chevron", StringComparison.OrdinalIgnoreCase) => PdfImageClipKind.Chevron,
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

        var hasGradientStroke = TryMapOutlineLinearGradient(shape.Outline, x, y, width, height, out var strokeGradient, out var strokeFallback, out var strokeWidth, out var strokeOpacity);
        PdfColor stroke = default;
        var hasSolidStroke = !hasGradientStroke && TryMapOutline(shape.Outline, out stroke, out strokeWidth, out strokeOpacity);
        if (!hasGradientStroke && !hasSolidStroke)
            return true;

        var markerColor = hasGradientStroke ? strokeFallback : stroke;

        if (shape.AutoShapeKind == DrawingShapeKind.ElbowConnector
            && shape.ElbowRoute is { Count: >= 2 } route)
        {
            for (var i = 1; i < route.Count; i++)
            {
                var start = ToPdfPoint(route[i - 1], slideHeightPoints);
                var routeEnd = ToPdfPoint(route[i], slideHeightPoints);
                AddWithOpacity(
                    ops,
                    hasGradientStroke
                        ? new PdfLineLinearGradient(start.X, start.Y, routeEnd.X, routeEnd.Y, strokeGradient, strokeFallback, strokeWidth)
                        : new PdfLine(start.X, start.Y, routeEnd.X, routeEnd.Y, stroke, strokeWidth),
                    strokeOpacity);
            }

            if (TryGetLineEnds(shape.Outline, out var beginLineEnd, out var endLineEnd))
            {
                var first = ToPdfPoint(route[0], slideHeightPoints);
                var second = ToPdfPoint(route[1], slideHeightPoints);
                var penultimate = ToPdfPoint(route[^2], slideHeightPoints);
                var last = ToPdfPoint(route[^1], slideHeightPoints);
                AppendLineEndMarker(ops, beginLineEnd, first.X, first.Y, second.X, second.Y, markerColor, strokeWidth, strokeOpacity);
                AppendLineEndMarker(ops, endLineEnd, last.X, last.Y, penultimate.X, penultimate.Y, markerColor, strokeWidth, strokeOpacity);
            }

            return true;
        }

        var (x1, y1, x2, y2) = GetLineEndpoints(shape, x, y, width, height);
        AddWithOpacity(
            ops,
            hasGradientStroke
                ? new PdfLineLinearGradient(x1, y1, x2, y2, strokeGradient, strokeFallback, strokeWidth)
                : new PdfLine(x1, y1, x2, y2, stroke, strokeWidth),
            strokeOpacity);
        if (TryGetLineEnds(shape.Outline, out var begin, out var lineEnd))
        {
            AppendLineEndMarker(ops, begin, x1, y1, x2, y2, markerColor, strokeWidth, strokeOpacity);
            AppendLineEndMarker(ops, lineEnd, x2, y2, x1, y1, markerColor, strokeWidth, strokeOpacity);
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

        var fillStyle = MapFillStyle(shape.Fill, x, y, width, height);
        var strokeStyle = MapOutlineStyle(shape.Outline, x, y, width, height);

        foreach (var path in shape.CustomGeometry)
        {
            var contours = BuildCustomPathContours(path, x, y, width, height);
            if (contours.Count == 0)
                continue;

            var fill = path.Fill ? fillStyle : null;
            var stroke = path.Stroke ? strokeStyle : null;
            if (fill is null && stroke is null)
                continue;

            if (fill?.Gradient is null && stroke?.Gradient is null)
            {
                if (fill is not null && stroke is not null && Math.Abs(fill.Opacity - stroke.Opacity) < 0.0001)
                {
                    AddWithOpacity(ops, new PdfPath(contours, fill.FallbackColor, stroke.FallbackColor, stroke.StrokeWidth), fill.Opacity);
                    continue;
                }

                if (fill is not null)
                    AddWithOpacity(ops, new PdfPath(contours, fill.FallbackColor, null, stroke?.StrokeWidth ?? DefaultStrokeWidthPt), fill.Opacity);
                if (stroke is not null)
                    AddWithOpacity(ops, new PdfPath(contours, null, stroke.FallbackColor, stroke.StrokeWidth), stroke.Opacity);
                continue;
            }

            if (fill is not null && stroke is not null && Math.Abs(fill.Opacity - stroke.Opacity) < 0.0001)
            {
                AddWithOpacity(
                    ops,
                    new PdfPathLinearGradient(
                        contours,
                        fill.Gradient,
                        fill.FallbackColor,
                        stroke.Gradient,
                        stroke.FallbackColor,
                        stroke.StrokeWidth),
                    fill.Opacity);
                continue;
            }

            if (fill is not null)
                AddWithOpacity(
                    ops,
                    new PdfPathLinearGradient(contours, fill.Gradient, fill.FallbackColor, null, null, stroke?.StrokeWidth ?? DefaultStrokeWidthPt),
                    fill.Opacity);
            if (stroke is not null)
                AddWithOpacity(
                    ops,
                    new PdfPathLinearGradient(contours, null, null, stroke.Gradient, stroke.FallbackColor, stroke.StrokeWidth),
                    stroke.Opacity);
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
        PresentationPdfScenePlanner.ToPdfPoint(point, slideHeightPoints);

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

    private static PdfPaintStyle? MapFillStyle(
        ShapeFill? fill,
        double x,
        double y,
        double width,
        double height)
    {
        if (TryMapFillLinearGradient(fill, x, y, width, height, out var gradient, out var fallbackColor, out var opacity))
            return new PdfPaintStyle(gradient, fallbackColor, StrokeWidth: 0, opacity);
        if (TryMapFill(fill, out fallbackColor, out opacity))
            return new PdfPaintStyle(Gradient: null, fallbackColor, StrokeWidth: 0, opacity);

        return null;
    }

    private static PdfPaintStyle? MapOutlineStyle(
        ShapeOutline? outline,
        double x,
        double y,
        double width,
        double height)
    {
        if (TryMapOutlineLinearGradient(outline, x, y, width, height, out var gradient, out var fallbackColor, out var strokeWidth, out var opacity))
            return new PdfPaintStyle(gradient, fallbackColor, strokeWidth, opacity);
        if (TryMapOutline(outline, out fallbackColor, out strokeWidth, out opacity))
            return new PdfPaintStyle(Gradient: null, fallbackColor, strokeWidth, opacity);

        return null;
    }

    private static bool TryMapFillLinearGradient(
        ShapeFill? fill,
        double x,
        double y,
        double width,
        double height,
        out PdfLinearGradient gradient,
        out PdfColor fallbackColor,
        out double opacity)
    {
        if (fill is ShapeFill.Gradient source &&
            TryMapLinearGradient(source, x, y, width, height, out gradient, out fallbackColor, out opacity))
            return true;

        gradient = default!;
        fallbackColor = default;
        opacity = 1.0;
        return false;
    }

    private static bool TryMapOutlineLinearGradient(
        ShapeOutline? outline,
        double x,
        double y,
        double width,
        double height,
        out PdfLinearGradient gradient,
        out PdfColor fallbackColor,
        out double widthPt,
        out double opacity)
    {
        if (outline is ShapeOutline.GradientVisible source &&
            TryMapLinearGradient(source.Gradient, x, y, width, height, out gradient, out fallbackColor, out opacity))
        {
            widthPt = Math.Max(0.1, source.WidthPt);
            return true;
        }

        gradient = default!;
        fallbackColor = default;
        widthPt = 0;
        opacity = 1.0;
        return false;
    }

    private static bool TryMapLinearGradient(
        ShapeFill.Gradient source,
        double x,
        double y,
        double width,
        double height,
        out PdfLinearGradient gradient,
        out PdfColor fallbackColor,
        out double opacity)
    {
        fallbackColor = ToPdfColor(source.StartColor);
        opacity = ToPdfOpacity(source.StartColor);
        gradient = default!;
        if (source.Kind != GradientKind.Linear || width <= 0 || height <= 0)
            return false;

        var stops = source.Stops
            .OrderBy(stop => stop.Position)
            .Select(stop => new PdfGradientStop(stop.Position, ToPdfColor(stop.Color)))
            .ToArray();
        if (stops.Length < 2)
            return false;

        var (startX, startY, endX, endY) = LinearGradientAxis(x, y, width, height, source.AngleDegrees);
        gradient = new PdfLinearGradient(startX, startY, endX, endY, stops);
        return true;
    }

    private static (double StartX, double StartY, double EndX, double EndY) LinearGradientAxis(
        double x,
        double y,
        double width,
        double height,
        double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = -Math.Sin(radians);
        var halfLength = (Math.Abs(width * dx) + Math.Abs(height * dy)) / 2.0;
        if (halfLength < 0.001)
            halfLength = Math.Sqrt((width * width) + (height * height)) / 2.0;

        var centerX = x + width / 2.0;
        var centerY = y + height / 2.0;
        return (
            centerX - dx * halfLength,
            centerY - dy * halfLength,
            centerX + dx * halfLength,
            centerY + dy * halfLength);
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

    private static void AppendCommentMarkup(
        List<PdfDrawOp> ops,
        Slide slide,
        double slideWidthPoints,
        double slideHeightPoints)
    {
        foreach (var comment in slide.Comments)
        {
            var anchorX = Math.Clamp(
                PresentationPdfScenePlanner.EmuToPoints(comment.Xemu),
                4,
                Math.Max(4, slideWidthPoints - 4));
            var anchorTop = Math.Clamp(
                PresentationPdfScenePlanner.EmuToPoints(comment.Yemu),
                4,
                Math.Max(4, slideHeightPoints - 4));
            var markerY = slideHeightPoints - anchorTop - 3;
            var cardWidth = Math.Min(180, Math.Max(64, slideWidthPoints - 8));
            var cardHeight = 28.0;
            var cardX = Math.Clamp(anchorX, 4, Math.Max(4, slideWidthPoints - cardWidth - 4));
            var cardY = Math.Max(4, markerY - cardHeight - 7);
            var author = TrimMarkupText(
                string.IsNullOrWhiteSpace(comment.Initials) ? comment.Author : comment.Initials,
                24);
            var body = TrimMarkupText(comment.Text, 82);

            ops.Add(new PdfFillRect(cardX, cardY, cardWidth, cardHeight, new PdfColor(0xFF, 0xF2, 0xCC)));
            ops.Add(new PdfStrokeRect(cardX, cardY, cardWidth, cardHeight, new PdfColor(0xBF, 0x90, 0x00), 0.5));
            ops.Add(new PdfFillEllipse(anchorX - 3, markerY - 3, 6, 6, new PdfColor(0xC0, 0x00, 0x00)));

            if (author.Length > 0)
                ops.Add(new PdfText(cardX + 4, cardY + cardHeight - 10, 8, PdfFontFace.Bold, PdfColor.Black, author));
            if (body.Length > 0)
                ops.Add(new PdfText(cardX + 4, cardY + 5, 8, PdfFontFace.Regular, PdfColor.Black, body));
        }
    }

    private static string TrimMarkupText(string? text, int maxLength)
    {
        var value = OneLine(text ?? string.Empty).Trim();
        if (value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static void AppendInkMarkup(
        List<PdfDrawOp> ops,
        SlideShape shape,
        Slide slide,
        double slideWidthPoints,
        double slideHeightPoints)
    {
        foreach (var stroke in ReadInkStrokes(shape, slide))
        {
            var lines = new List<PdfDrawOp>(Math.Max(0, stroke.Points.Count - 1));
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var start = stroke.Points[index - 1];
                var end = stroke.Points[index];
                var x1 = start.X * DipToPoint;
                var x2 = end.X * DipToPoint;
                var y1 = slideHeightPoints - (start.Y * DipToPoint);
                var y2 = slideHeightPoints - (end.Y * DipToPoint);
                if ((x1 < -slideWidthPoints || x1 > slideWidthPoints * 2)
                    && (x2 < -slideWidthPoints || x2 > slideWidthPoints * 2))
                    continue;

                lines.Add(new PdfLine(x1, y1, x2, y2, stroke.Color, stroke.WidthDip * DipToPoint));
            }

            if (lines.Count == 0)
                continue;

            if (stroke.Opacity < 0.999)
                ops.Add(new PdfOpacityGroup(stroke.Opacity, lines));
            else
                ops.AddRange(lines);
        }
    }

    private static IReadOnlyList<InkStroke> ReadInkStrokes(SlideShape shape, Slide slide)
    {
        if (shape.PreservedObject is not { ObjectKind: PreservedObjectKind.Ink } info)
            return Array.Empty<InkStroke>();

        var bytes = info.Parts
            .Where(part => info.PartContentTypes.TryGetValue(part.Key, out var contentType)
                && contentType.Contains("inkml", StringComparison.OrdinalIgnoreCase))
            .Select(part => part.Value)
            .Concat(info.Parts
                .Where(part => part.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(part => part.Value))
            .FirstOrDefault(part => part.AsSpan().IndexOf("<ink"u8) >= 0);
        if (bytes is not { Length: > 0 })
            return Array.Empty<InkStroke>();

        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return Array.Empty<InkStroke>();
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "ink", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<InkStroke>();

        var traceFormat = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "traceFormat");
        var channels = traceFormat?.Elements()
            .Where(element => element.Name.LocalName == "channel")
            .Select(element => (
                Name: GetInkAttribute(element, "name") ?? string.Empty,
                Units: GetInkAttribute(element, "units") ?? string.Empty))
            .ToArray() ?? [];
        var xIndex = Array.FindIndex(channels, channel => channel.Name.Equals("X", StringComparison.OrdinalIgnoreCase));
        var yIndex = Array.FindIndex(channels, channel => channel.Name.Equals("Y", StringComparison.OrdinalIgnoreCase));
        if (xIndex < 0 || yIndex < 0 || channels.Length < 2)
            return Array.Empty<InkStroke>();

        var brushes = ReadInkBrushes(root);
        var frameWidthDip = PresentationPdfScenePlanner.EmuToPoints(shape.ExtentCxEmu) / DipToPoint;
        var frameHeightDip = PresentationPdfScenePlanner.EmuToPoints(shape.ExtentCyEmu) / DipToPoint;
        var frameLeftDip = PresentationPdfScenePlanner.EmuToPoints(shape.OffsetXEmu) / DipToPoint;
        var frameTopDip = PresentationPdfScenePlanner.EmuToPoints(shape.OffsetYEmu) / DipToPoint;
        var isFreePAbsolute = string.Equals(
            GetInkAttribute(root, "format", FreePInkNamespace),
            "freep-slideshow-ink",
            StringComparison.OrdinalIgnoreCase);
        var result = new List<InkStroke>();

        foreach (var trace in root.Descendants().Where(element => element.Name.LocalName == "trace"))
        {
            var values = InkNumberPattern.Matches(trace.Value)
                .Select(match => ParseInkDouble(match.Value, double.NaN))
                .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                .ToArray();
            if (values.Length < channels.Length)
                continue;

            var points = new List<(double X, double Y)>();
            for (var index = 0; index + channels.Length <= values.Length; index += channels.Length)
            {
                points.Add((
                    ConvertInkValue(values[index + xIndex], channels[xIndex].Units),
                    ConvertInkValue(values[index + yIndex], channels[yIndex].Units)));
            }

            if (points.Count == 0)
                continue;

            if (!isFreePAbsolute && points.All(point =>
                    point.X >= -1 && point.Y >= -1
                    && point.X <= frameWidthDip + 1 && point.Y <= frameHeightDip + 1))
            {
                points = points
                    .Select(point => (point.X + frameLeftDip, point.Y + frameTopDip))
                    .ToList();
            }

            var brushId = GetInkAttribute(trace, "brushRef")?.TrimStart('#');
            var brush = brushId is not null && brushes.TryGetValue(brushId, out var parsedBrush)
                ? parsedBrush
                : new InkBrush(PdfColor.Black, 1.5, 1.0);
            var color = TryParseInkColor(GetInkAttribute(trace, "color", FreePInkNamespace), out var traceColor)
                ? traceColor
                : brush.Color;
            var width = ParseOptionalInkDouble(GetInkAttribute(trace, "thicknessDip", FreePInkNamespace))
                ?? brush.WidthDip;
            var opacity = ParseOptionalInkDouble(GetInkAttribute(trace, "opacity", FreePInkNamespace))
                ?? brush.Opacity;
            result.Add(new InkStroke(points, color, Math.Max(0.1, width), Math.Clamp(opacity, 0, 1)));
        }

        return result;
    }

    private static Dictionary<string, InkBrush> ReadInkBrushes(XElement root)
    {
        var result = new Dictionary<string, InkBrush>(StringComparer.OrdinalIgnoreCase);
        foreach (var brushElement in root.Descendants().Where(element => element.Name.LocalName == "brush"))
        {
            var id = GetInkAttribute(brushElement, "id", XNamespace.Xml);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var color = PdfColor.Black;
            var width = 1.5;
            var opacity = 1.0;
            foreach (var property in brushElement.Elements().Where(element => element.Name.LocalName == "brushProperty"))
            {
                switch (GetInkAttribute(property, "name")?.ToLowerInvariant())
                {
                    case "color":
                        TryParseInkColor(GetInkAttribute(property, "value"), out color);
                        break;
                    case "width":
                        width = ConvertInkValue(ParseInkDouble(GetInkAttribute(property, "value"), 1.5), GetInkAttribute(property, "units"));
                        break;
                    case "transparency":
                        var transparency = ParseInkDouble(GetInkAttribute(property, "value"), 0);
                        opacity = 1 - Math.Clamp(transparency > 1 ? transparency / 255 : transparency, 0, 1);
                        break;
                }
            }

            result[id.TrimStart('#')] = new InkBrush(color, Math.Max(0.1, width), opacity);
        }

        return result;
    }

    private static string? GetInkAttribute(XElement element, string localName, XNamespace? namespaceName = null) =>
        (namespaceName is null
            ? element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            : element.Attribute(namespaceName + localName))?.Value;

    private static double ConvertInkValue(double value, string? units) =>
        units?.Trim().ToLowerInvariant() switch
        {
            "cm" => value * 96 / 2.54,
            "mm" => value * 96 / 25.4,
            "in" or "inch" or "inches" => value * 96,
            "pt" or "point" or "points" => value * 96 / 72,
            "m" => value * 96 / 0.0254,
            "um" => value * 96 / 25400,
            "nm" => value * 96 / 25400000,
            _ => value,
        };

    private static double ParseInkDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double? ParseOptionalInkDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool TryParseInkColor(string? value, out PdfColor color)
    {
        color = PdfColor.Black;
        if (!RgbColorTextCodec.TryParse(
                value,
                RgbColorTextProfile.FlexibleInk,
                out var rgb))
            return false;

        color = new PdfColor(rgb.R, rgb.G, rgb.B);
        return true;
    }

    // The portable text op draws a single line; flatten tabs so spacing is at least visible.
    private static string OneLine(string text) => text.Replace("\t", "    ");

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static PdfColor ToPdfColor(ThemeAwareColor color)
    {
        var resolved = color.Resolved;
        return ToPdfColor(resolved);
    }

    private static double ToPdfOpacity(ThemeAwareColor color) => color.Alpha / 255.0;

    private static PdfColor ToPdfColor(SrgbColor color) => new(color.R, color.G, color.B);

    private sealed record PdfPaintStyle(
        PdfLinearGradient? Gradient,
        PdfColor FallbackColor,
        double StrokeWidth,
        double Opacity);

    private sealed record ShapeBox(double X, double Y, double Width, double Height);
    private sealed record InkBrush(PdfColor Color, double WidthDip, double Opacity);
    private sealed record InkStroke(
        IReadOnlyList<(double X, double Y)> Points,
        PdfColor Color,
        double WidthDip,
        double Opacity);
    private readonly record struct ShapeBounds(double X, double Y, double Width, double Height);
}
