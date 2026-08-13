using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationHandoutPdfExportRequest(
    PresentationPrintRequest? PrintRequest = null,
    double PageWidth = PresentationExportPlanner.DefaultPrintPageWidth,
    double PageHeight = PresentationExportPlanner.DefaultPrintPageHeight);

public sealed record PresentationHandoutPdfRenderPlan(
    PresentationHandoutLayoutPlan LayoutPlan,
    IReadOnlyList<PdfContentPage> Pages);

/// <summary>
/// Shared handout PDF rendering for FreeP. Hosts provide only the presentation model; layout and
/// portable draw-op generation stay in the app-agnostic presentation layer.
/// </summary>
public static class PresentationHandoutPdfExporter
{
    private static readonly PdfColor PageBackground = new(0xFF, 0xFF, 0xFF);
    private static readonly PdfColor SlideBorder = new(0x80, 0x80, 0x80);
    private static readonly PdfColor WritingLine = new(0xB0, 0xB0, 0xB0);

    private const double SlideBorderWidth = 0.5;
    private const double WritingLineWidth = 0.4;

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationHandoutPdfExportRequest? request = null) =>
        PortablePdfWriter.WriteToBytes(BuildDocument(presentation, request), "FreeP handout PDF");

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationHandoutPdfExportRequest? request,
        PresentationPdfContentWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer(BuildDocument(presentation, request));
    }

    public static void Export(
        Presentation presentation,
        Stream stream,
        PresentationHandoutPdfExportRequest? request = null) =>
        PortablePdfWriter.Write(BuildDocument(presentation, request), stream, "FreeP handout PDF");

    public static PdfContentDocument BuildDocument(
        Presentation presentation,
        PresentationHandoutPdfExportRequest? request = null)
    {
        var renderPlan = BuildRenderPlan(presentation, request);
        return new PdfContentDocument(
            renderPlan.Pages,
            PresentationPdfScenePlanner.BuildDocumentProperties(presentation));
    }

    public static PresentationHandoutPdfRenderPlan BuildRenderPlan(
        Presentation presentation,
        PresentationHandoutPdfExportRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        request ??= new PresentationHandoutPdfExportRequest();

        var pageWidth = Math.Max(1, request.PageWidth);
        var pageHeight = Math.Max(1, request.PageHeight);
        var layout = PresentationExportPlanner.BuildHandoutLayoutPlan(
            request.PrintRequest,
            presentation,
            PresentationPdfExporter.DefaultSlideWidthPoints,
            PresentationPdfExporter.DefaultSlideHeightPoints,
            pageWidth,
            pageHeight);

        var pages = layout.Pages.Count == 0
            ? [new PdfContentPage(pageWidth, pageHeight, [new PdfFillRect(0, 0, pageWidth, pageHeight, PageBackground)])]
            : layout.Pages.Select(page => BuildHandoutPage(presentation, layout, page)).ToArray();

        return new PresentationHandoutPdfRenderPlan(layout, pages);
    }

    private static PdfContentPage BuildHandoutPage(
        Presentation presentation,
        PresentationHandoutLayoutPlan layout,
        PresentationHandoutPagePlan page)
    {
        var ops = new List<PdfDrawOp>
        {
            new PdfFillRect(0, 0, layout.PageWidth, layout.PageHeight, PageBackground),
        };

        foreach (var slot in page.Slots)
        {
            if (slot.SlideIndex < 0 || slot.SlideIndex >= presentation.Slides.Count)
                continue;

            var slidePage = PresentationPdfExporter.BuildSlidePage(
                presentation.Slides[slot.SlideIndex],
                presentation.SlideSizeCxEmu,
                presentation.SlideSizeCyEmu,
                layout.PrintPlan.Options.IncludeCommentsAndInkMarkup);
            ops.AddRange(PdfContentPagePlacement.MapOps(
                slidePage,
                slot.SlideBounds.X,
                slot.SlideBounds.Y,
                slot.SlideBounds.Width,
                slot.SlideBounds.Height,
                layout.PageHeight));
            if (layout.PrintPlan.Options.FrameSlides)
                ops.Add(ToPdfStrokeRect(slot.SlideBounds, layout.PageHeight, SlideBorder, SlideBorderWidth));
            foreach (var line in slot.BlankLineBounds)
                ops.Add(ToPdfLine(line, layout.PageHeight, WritingLine, WritingLineWidth));
        }

        return new PdfContentPage(layout.PageWidth, layout.PageHeight, ops);
    }

    private static PdfStrokeRect ToPdfStrokeRect(
        LayoutRect rect,
        double pageHeight,
        PdfColor color,
        double lineWidth) =>
        new(rect.X, pageHeight - rect.Bottom, rect.Width, rect.Height, color, lineWidth);

    private static PdfLine ToPdfLine(
        LayoutRect line,
        double pageHeight,
        PdfColor color,
        double lineWidth) =>
        new(line.X, pageHeight - line.Y, line.Right, pageHeight - line.Y, color, lineWidth);

}
