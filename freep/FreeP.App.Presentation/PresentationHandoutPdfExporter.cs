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
        return new PdfContentDocument(renderPlan.Pages, BuildDocumentProperties(presentation));
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
            presentation.Slides.Count,
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

            var slidePage = PresentationPdfExporter.BuildSlidePage(presentation.Slides[slot.SlideIndex]);
            ops.AddRange(MapSlideOps(slidePage, slot.SlideBounds, layout.PageHeight));
            ops.Add(ToPdfStrokeRect(slot.SlideBounds, layout.PageHeight, SlideBorder, SlideBorderWidth));
            foreach (var line in slot.BlankLineBounds)
                ops.Add(ToPdfLine(line, layout.PageHeight, WritingLine, WritingLineWidth));
        }

        return new PdfContentPage(layout.PageWidth, layout.PageHeight, ops);
    }

    private static IEnumerable<PdfDrawOp> MapSlideOps(
        PdfContentPage slidePage,
        LayoutRect destination,
        double pageHeight)
    {
        var scale = Math.Min(
            destination.Width / slidePage.WidthPoints,
            destination.Height / slidePage.HeightPoints);
        if (scale <= 0)
            yield break;

        var contentWidth = slidePage.WidthPoints * scale;
        var contentHeight = slidePage.HeightPoints * scale;
        var fitted = new LayoutRect(
            destination.X + ((destination.Width - contentWidth) / 2),
            destination.Y + ((destination.Height - contentHeight) / 2),
            contentWidth,
            contentHeight);

        foreach (var op in slidePage.Ops)
        {
            switch (op)
            {
                case PdfFillRect fill:
                    yield return MapRect(fill.X, fill.Y, fill.Width, fill.Height, fill.Color, null);
                    break;
                case PdfStrokeRect stroke:
                    yield return MapRect(stroke.X, stroke.Y, stroke.Width, stroke.Height, stroke.Color, stroke.LineWidth);
                    break;
                case PdfFillEllipse fill:
                    yield return MapEllipse(fill.X, fill.Y, fill.Width, fill.Height, fill.Color, null);
                    break;
                case PdfStrokeEllipse stroke:
                    yield return MapEllipse(stroke.X, stroke.Y, stroke.Width, stroke.Height, stroke.Color, stroke.LineWidth);
                    break;
                case PdfText text:
                    yield return new PdfText(
                        MapX(text.X),
                        MapY(text.Y),
                        text.FontSize * scale,
                        text.Face,
                        text.Color,
                        text.Text);
                    break;
                case PdfLine line:
                    yield return new PdfLine(
                        MapX(line.X1),
                        MapY(line.Y1),
                        MapX(line.X2),
                        MapY(line.Y2),
                        line.Color,
                        line.LineWidth * scale);
                    break;
            }
        }

        PdfDrawOp MapRect(double x, double y, double width, double height, PdfColor color, double? lineWidth)
        {
            var mapped = new LayoutRect(
                MapX(x),
                MapTopFromPdfBottom(y + height),
                width * scale,
                height * scale);
            var pdfY = pageHeight - mapped.Bottom;
            return lineWidth is null
                ? new PdfFillRect(mapped.X, pdfY, mapped.Width, mapped.Height, color)
                : new PdfStrokeRect(mapped.X, pdfY, mapped.Width, mapped.Height, color, lineWidth.Value * scale);
        }

        PdfDrawOp MapEllipse(double x, double y, double width, double height, PdfColor color, double? lineWidth)
        {
            var mapped = new LayoutRect(
                MapX(x),
                MapTopFromPdfBottom(y + height),
                width * scale,
                height * scale);
            var pdfY = pageHeight - mapped.Bottom;
            return lineWidth is null
                ? new PdfFillEllipse(mapped.X, pdfY, mapped.Width, mapped.Height, color)
                : new PdfStrokeEllipse(mapped.X, pdfY, mapped.Width, mapped.Height, color, lineWidth.Value * scale);
        }

        double MapX(double x) => fitted.X + (x * scale);

        double MapY(double y) => pageHeight - MapTopFromPdfBottom(y);

        double MapTopFromPdfBottom(double y) => fitted.Y + ((slidePage.HeightPoints - y) * scale);
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

    private static PdfDocumentProperties? BuildDocumentProperties(Presentation presentation)
    {
        var p = presentation.Properties;
        return new PdfDocumentProperties(
            Title: NullIfBlank(p.Title),
            Author: NullIfBlank(p.Author),
            Subject: NullIfBlank(p.Subject),
            Keywords: NullIfBlank(p.Keywords),
            Creator: "FreeP");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
