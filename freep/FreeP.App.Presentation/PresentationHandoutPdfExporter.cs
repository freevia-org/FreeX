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
    private static readonly PdfColor HeaderFooterText = new(0x55, 0x55, 0x55);

    private const double SlideBorderWidth = 0.5;
    private const double WritingLineWidth = 0.4;
    private const double HeaderFooterFontSize = 9;
    private const double EmuPerPoint = 12700.0;

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
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu,
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

        AppendHeaderFooterPlaceholders(ops, presentation, layout, page.PageIndex + 1);

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

    /// <summary>
    /// Draws the handout master's header/footer/date/slide-number placeholders once per handout
    /// page (they are page-level, not per-slide-slot). Text and native geometry come from
    /// <see cref="Presentation.HandoutMasterPlaceholders"/>; visibility comes from the handout
    /// master's own <c>p:hf</c> when present, else falls back to the "Notes and Handouts" tab
    /// flags that PowerPoint keeps in sync with it. Corner position falls back to the same
    /// deterministic page-margin layout PowerPoint uses for a handout master with default
    /// geometry when no native placeholder extent is available.
    /// </summary>
    private static void AppendHeaderFooterPlaceholders(
        List<PdfDrawOp> ops,
        Presentation presentation,
        PresentationHandoutLayoutPlan layout,
        int pageNumber)
    {
        var flags = presentation.HandoutHfVisibility ?? presentation.NotesHfVisibility;
        var header = PresentationNotesPagePreviewPlanner.FindPlaceholderShape(
            presentation.HandoutMasterPlaceholders, PlaceholderType.Header);
        var dateTime = PresentationNotesPagePreviewPlanner.FindPlaceholderShape(
            presentation.HandoutMasterPlaceholders, PlaceholderType.DateTime);
        var footer = PresentationNotesPagePreviewPlanner.FindPlaceholderShape(
            presentation.HandoutMasterPlaceholders, PlaceholderType.Footer);
        var slideNumber = PresentationNotesPagePreviewPlanner.FindPlaceholderShape(
            presentation.HandoutMasterPlaceholders, PlaceholderType.SlideNumber);

        AppendHeaderFooterPlaceholder(
            ops, layout, PresentationNotesPagePlaceholderKind.Header, header,
            flags?.ShowHeader ?? (header is not null), pageNumber, HandoutCorner.TopLeft);
        AppendHeaderFooterPlaceholder(
            ops, layout, PresentationNotesPagePlaceholderKind.DateTime, dateTime,
            flags?.ShowDate ?? (dateTime is not null), pageNumber, HandoutCorner.TopRight);
        AppendHeaderFooterPlaceholder(
            ops, layout, PresentationNotesPagePlaceholderKind.Footer, footer,
            flags?.ShowFooter ?? (footer is not null), pageNumber, HandoutCorner.BottomLeft);
        AppendHeaderFooterPlaceholder(
            ops, layout, PresentationNotesPagePlaceholderKind.SlideNumber, slideNumber,
            flags?.ShowSlideNum ?? (slideNumber is not null), pageNumber, HandoutCorner.BottomRight);
    }

    private enum HandoutCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private static void AppendHeaderFooterPlaceholder(
        List<PdfDrawOp> ops,
        PresentationHandoutLayoutPlan layout,
        PresentationNotesPagePlaceholderKind kind,
        SlideShape? shape,
        bool isVisible,
        int pageNumber,
        HandoutCorner corner)
    {
        if (!isVisible)
            return;

        var text = PresentationNotesPagePreviewPlanner.ResolveHeaderFooterText(kind, shape, pageNumber);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var bounds = BuildHeaderFooterBounds(layout, shape, corner);
        ops.Add(new PdfText(
            bounds.Left,
            layout.PageHeight - bounds.Top - HeaderFooterFontSize,
            HeaderFooterFontSize,
            PdfFontFace.Regular,
            HeaderFooterText,
            text));
    }

    private static LayoutRect BuildHeaderFooterBounds(
        PresentationHandoutLayoutPlan layout,
        SlideShape? nativeShape,
        HandoutCorner corner)
    {
        if (nativeShape is { ExtentCxEmu: > 0, ExtentCyEmu: > 0 })
        {
            return new LayoutRect(
                nativeShape.OffsetXEmu / EmuPerPoint,
                nativeShape.OffsetYEmu / EmuPerPoint,
                nativeShape.ExtentCxEmu / EmuPerPoint,
                nativeShape.ExtentCyEmu / EmuPerPoint);
        }

        const double height = 18;
        var margin = Math.Min(PresentationExportPlanner.DefaultHandoutMargin, layout.PageWidth / 8);
        var width = Math.Max(1, (layout.PageWidth - (margin * 2)) * 0.34);
        var top = margin / 2;
        var bottom = layout.PageHeight - (margin / 2) - height;

        return corner switch
        {
            HandoutCorner.TopLeft => new LayoutRect(margin, top, width, height),
            HandoutCorner.TopRight => new LayoutRect(layout.PageWidth - margin - width, top, width, height),
            HandoutCorner.BottomLeft => new LayoutRect(margin, bottom, width, height),
            HandoutCorner.BottomRight => new LayoutRect(layout.PageWidth - margin - width, bottom, width, height),
            _ => new LayoutRect(margin, bottom, width, height),
        };
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
