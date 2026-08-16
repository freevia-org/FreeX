using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public delegate byte[] PresentationRasterPdfWriter(PdfRasterDocument document);
public delegate byte[] PresentationSlideImageRendererWithPrintMarkup(
    Presentation presentation,
    int slideIndex,
    int widthPx,
    int heightPx,
    bool includeCommentsAndInkMarkup);

public sealed record PresentationRasterPdfExportRequest(
    PresentationSlideRangeRequest? SlideRange = null,
    int WidthPx = PresentationRasterPdfExporter.DefaultWidthPx,
    int? HeightPx = null,
    // R137: mirrors PresentationPrintRequest.PrintHiddenSlides. Notes/Handout PDF export already
    // excludes hidden slides by default (PresentationExportPlanner.BuildPrintPlan's
    // presentation-aware overload). File > Export as PDF and the FullPageSlides print/native-print
    // routes go through this raster request instead, so the same default -- and the same live
    // "Print hidden slides" backstage toggle -- must be honoured here too, or a PDF exported to hide
    // slides from a client silently ships them (see docs/parity findings, R137).
    bool PrintHiddenSlides = false);

public sealed record PresentationRasterPdfRenderPlan(
    PresentationSlideRangePlan SlideRange,
    double PageWidthPoints,
    double PageHeightPoints,
    int WidthPx,
    int HeightPx,
    IReadOnlyList<PdfRasterPage> Pages);

/// <summary>
/// Shared raster-backed slide PDF export for FreeP. Hosts supply only slide rasterization and a
/// PDF backend; range policy and the neutral raster document stay shared.
/// </summary>
public static class PresentationRasterPdfExporter
{
    public const int DefaultWidthPx = PresentationPdfScenePlanner.DefaultRasterWidthPx;

    private static readonly byte[] BlankWhitePng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59,
        0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationRasterPdfExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationRasterPdfWriter writePdf,
        PresentationSlideImageRendererWithPrintMarkup? renderSlideWithMarkup = null) =>
        writePdf(BuildDocument(presentation, request, renderSlideToPng, renderSlideWithMarkup));

    public static PdfRasterDocument BuildDocument(
        Presentation presentation,
        PresentationRasterPdfExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationSlideImageRendererWithPrintMarkup? renderSlideWithMarkup = null)
    {
        var plan = BuildRenderPlan(presentation, request, renderSlideToPng, renderSlideWithMarkup);
        return new PdfRasterDocument(plan.Pages, BuildDocumentProperties(presentation));
    }

    public static PresentationRasterPdfRenderPlan BuildRenderPlan(
        Presentation presentation,
        PresentationRasterPdfExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationSlideImageRendererWithPrintMarkup? renderSlideWithMarkup = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);
        request ??= new PresentationRasterPdfExportRequest();

        var rasterSize = PresentationPdfScenePlanner.ResolveRasterSize(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu,
            request.WidthPx,
            request.HeightPx);
        var pageWidth = rasterSize.SlideSize.WidthPoints;
        var pageHeight = rasterSize.SlideSize.HeightPoints;
        var widthPx = rasterSize.WidthPx;
        var heightPx = rasterSize.HeightPx;
        // R137: presentation-aware so hidden slides are excluded by default (matching Notes/Handout
        // via PresentationExportPlanner.BuildPrintPlan) unless the caller's PrintHiddenSlides flag --
        // threaded from the live "Print hidden slides" backstage toggle for the print routes, and
        // left at its default false for File > Export as PDF, which has no such option -- opts in.
        var range = PresentationExportPlanner.BuildSlideRangePlan(
            request.SlideRange,
            presentation,
            request.PrintHiddenSlides);

        var pages = new List<PdfRasterPage>(Math.Max(1, range.SlideNumbers.Count));
        foreach (var slideNumber in range.SlideNumbers)
        {
            var slideIndex = slideNumber - 1;
            var imageBytes = renderSlideWithMarkup is null
                ? renderSlideToPng(presentation, slideIndex, widthPx, heightPx)
                : renderSlideWithMarkup(presentation, slideIndex, widthPx, heightPx, true);
            if (imageBytes.Length == 0)
                throw new InvalidOperationException($"Slide PDF renderer returned no bytes for slide {slideNumber}.");

            var vectorPage = BuildSlideVectorPage(presentation, presentation.Slides[slideIndex]);
            var textOverlays = CollectTextOverlays(vectorPage, pageHeight);
            // R137: the vector builder's LinkOverlays are already in the same top-left, y-down page
            // space PdfRasterPage.LinkOverlays wants (see PdfLinkOverlay's doc comment), so no
            // per-shape coordinate transform is needed here -- unlike the text-overlay conversion
            // above, which has to walk PDF-native draw ops. Internal (slide-to-slide) hyperlinks stay
            // in this list too even though the raster backends only act on external Uri overlays
            // today (PdfRasterDocument has no cross-page named-destination table yet); each writer
            // silently skips the DestinationName-only entries, so carrying them through is harmless
            // and forward-compatible rather than filtering them out here.
            var linkOverlays = vectorPage.LinkOverlays;
            pages.Add(new PdfRasterPage(
                pageWidth,
                pageHeight,
                imageBytes,
                textOverlays.Count > 0 ? textOverlays : null,
                linkOverlays is { Count: > 0 } ? linkOverlays : null));
        }

        if (pages.Count == 0)
            pages.Add(new PdfRasterPage(pageWidth, pageHeight, BlankWhitePng));

        return new PresentationRasterPdfRenderPlan(range, pageWidth, pageHeight, widthPx, heightPx, pages);
    }

    private static PdfDocumentProperties? BuildDocumentProperties(Presentation presentation)
        => PresentationPdfScenePlanner.BuildDocumentProperties(presentation);

    // R132: the raster PDF page is a plain bitmap with no text layer at all, so nothing exported here
    // was searchable, selectable, or readable to a screen reader -- the same defect class fixed for
    // FreeW's Windows PDF export. FreeP's slide model already has a shared, tested vector-text builder
    // (FreeP.Core.IO.PresentationPdfExporter.BuildSlidePage) that lays out the title and each shape's
    // text at the right position/size/style; rather than re-deriving that geometry here, this converts
    // its PdfText draw ops (PDF-native bottom-left, y-up baseline space) into invisible PdfTextOverlay
    // entries (top-left, y-down space -- see PdfRasterPage.TextOverlays) drawn over the raster image.
    // Both raster PDF backends (WpfRasterPdfWriter for FreeP.App.Host, SkiaRasterPdfWriter for
    // FreeP.App.Avalonia) already/now honor PdfRasterPage.TextOverlays, so this one shared conversion
    // fixes both hosts instead of re-deriving text placement per platform.
    //
    // R132-FOLLOWUP: the first pass here only ever scanned top-level ops and asked the vector builder
    // to skip every placeholder shape. That missed two dominant real-world cases: (1) ordinary bullet
    // text lives in a BODY placeholder (PptxPackageReader maps p:ph type="body"/unspecified to
    // PlaceholderType.Body), so a typical "Title and Content" slide produced only a title overlay; and
    // (2) AppendShapeOps wraps a shape's whole op list in a PdfRotationGroup whenever RotationDeg != 0,
    // so rotated shapes' PdfText children were never visited by a flat top-level scan. Both are fixed
    // below: BuildSlidePage is asked to include non-title placeholder text, and the walk recurses into
    // group ops, carrying the enclosing rotation (if any) so the overlay lands on the rotated glyphs.
    // R137: split out of the former BuildSlideTextOverlays so the same vector page (and its
    // LinkOverlays, populated by FreeP.Core.IO.PresentationPdfExporter.BuildSlidePage for shape and
    // text-run hyperlinks) can also feed PdfRasterPage.LinkOverlays -- one shared derivation instead
    // of building the vector page twice per slide.
    private static PdfContentPage BuildSlideVectorPage(Presentation presentation, Slide slide) =>
        PresentationPdfExporter.BuildSlidePage(
            slide,
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu,
            includeCommentsAndInkMarkup: false,
            includePlaceholderShapeText: true);

    private static IReadOnlyList<PdfTextOverlay> CollectTextOverlays(
        PdfContentPage vectorPage,
        double pageHeightPoints)
    {
        var overlays = new List<PdfTextOverlay>();
        CollectTextOverlays(vectorPage.Ops, pageHeightPoints, activeRotation: null, overlays);
        return overlays;
    }

    private static void CollectTextOverlays(
        IReadOnlyList<PdfDrawOp> ops,
        double pageHeightPoints,
        PdfRotationGroup? activeRotation,
        List<PdfTextOverlay> overlays)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case PdfText text when !string.IsNullOrEmpty(text.Text):
                    overlays.Add(BuildTextOverlay(text, pageHeightPoints, activeRotation));
                    break;

                case PdfRotationGroup rotationGroup:
                    // Every shape that has one nests at most a single rotation group (see
                    // AppendShapeOps), so an already-active rotation (from an outer group) is kept
                    // rather than overwritten if one is somehow already active.
                    CollectTextOverlays(rotationGroup.Ops, pageHeightPoints, activeRotation ?? rotationGroup, overlays);
                    break;

                case PdfOpacityGroup opacityGroup:
                    CollectTextOverlays(opacityGroup.Ops, pageHeightPoints, activeRotation, overlays);
                    break;

                case PdfClipGroup clipGroup:
                    CollectTextOverlays(clipGroup.Ops, pageHeightPoints, activeRotation, overlays);
                    break;
            }
        }
    }

    private static PdfTextOverlay BuildTextOverlay(
        PdfText text,
        double pageHeightPoints,
        PdfRotationGroup? activeRotation)
    {
        var baselineX = text.X;
        var baselineY = text.Y;
        var rotationDegrees = 0.0;

        if (activeRotation is { } rotation && Math.Abs(rotation.RotationDegrees) > 0.001)
        {
            // Mirrors PortablePdfWriter.AppendRotationTransform's matrix (a=cos,b=sin,c=-sin,d=cos
            // with theta = -RotationDegrees in radians) so the overlay's anchor point ends up at the
            // same PDF-native position the rotated glyphs are actually painted at. FlipH/FlipV are
            // not modeled here (text runs don't currently combine rotation with a flip).
            var thetaRad = -rotation.RotationDegrees * Math.PI / 180.0;
            var cos = Math.Cos(thetaRad);
            var sin = Math.Sin(thetaRad);
            var dx = baselineX - rotation.CenterX;
            var dy = baselineY - rotation.CenterY;
            baselineX = rotation.CenterX + (cos * dx) + (-sin * dy);
            baselineY = rotation.CenterY + (sin * dx) + (cos * dy);
            // The raster writers rotate the overlay glyph run about its own (already-repositioned)
            // top-down anchor point by RotationDegrees directly in their y-down canvas/graphics
            // space, which is the same positive-is-Office-clockwise convention PdfRotationGroup
            // documents -- no extra sign flip needed here.
            rotationDegrees = rotation.RotationDegrees;
        }

        // The vector builder places text.Y as the baseline in PDF-native bottom-left/y-up space,
        // approximating the baseline as (top-of-line + FontSize). WpfRasterPdfWriter's overlay
        // consumer makes the same approximation in the other direction (overlay.Y + FontSize as the
        // XGraphics top-down baseline), so flipping with -FontSize here keeps the two consistent:
        // topDownY = pageHeight - baselineY - FontSize.
        var overlayY = pageHeightPoints - baselineY - text.FontSize;
        return new PdfTextOverlay(
            X: baselineX,
            Y: overlayY,
            FontSize: text.FontSize,
            FontFamily: text.FontFamily ?? "Calibri",
            Bold: text.Face is PdfFontFace.Bold or PdfFontFace.BoldItalic,
            Italic: text.Face is PdfFontFace.Italic or PdfFontFace.BoldItalic,
            Color: text.Color,
            RotationDegrees: rotationDegrees,
            Text: text.Text);
    }
}
