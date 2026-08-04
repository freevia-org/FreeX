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
    int? HeightPx = null);

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
        var range = PresentationExportPlanner.BuildSlideRangePlan(request.SlideRange, presentation.Slides.Count);

        var pages = new List<PdfRasterPage>(Math.Max(1, range.SlideNumbers.Count));
        foreach (var slideNumber in range.SlideNumbers)
        {
            var slideIndex = slideNumber - 1;
            var imageBytes = renderSlideWithMarkup is null
                ? renderSlideToPng(presentation, slideIndex, widthPx, heightPx)
                : renderSlideWithMarkup(presentation, slideIndex, widthPx, heightPx, true);
            if (imageBytes.Length == 0)
                throw new InvalidOperationException($"Slide PDF renderer returned no bytes for slide {slideNumber}.");

            pages.Add(new PdfRasterPage(pageWidth, pageHeight, imageBytes));
        }

        if (pages.Count == 0)
            pages.Add(new PdfRasterPage(pageWidth, pageHeight, BlankWhitePng));

        return new PresentationRasterPdfRenderPlan(range, pageWidth, pageHeight, widthPx, heightPx, pages);
    }

    private static PdfDocumentProperties? BuildDocumentProperties(Presentation presentation)
        => PresentationPdfScenePlanner.BuildDocumentProperties(presentation);
}
