using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationNotesPagePdfExportRequest(
    PresentationPrintRequest? PrintRequest = null,
    double? PageWidth = null,
    double? PageHeight = null);

public sealed record PresentationNotesPagePdfRenderPlan(
    PresentationPrintPlan PrintPlan,
    IReadOnlyList<PresentationNotesPagePreviewPlan> PreviewPlans,
    IReadOnlyList<PdfContentPage> Pages);

/// <summary>
/// Shared notes-page PDF rendering for FreeP. Hosts stay responsible for native picker/print
/// surfaces; notes-page geometry, slide thumbnail placement, and speaker-note text output stay
/// in the common presentation layer so WPF and Avalonia cannot drift.
/// </summary>
public static class PresentationNotesPagePdfExporter
{
    private static readonly PdfColor PageBackground = new(0xFF, 0xFF, 0xFF);
    private static readonly PdfColor SlideBorder = new(0x80, 0x80, 0x80);
    private static readonly PdfColor NotesBorder = new(0xB8, 0xB8, 0xB8);
    private static readonly PdfColor NotesText = new(0x20, 0x20, 0x20);
    private static readonly PdfColor PlaceholderText = new(0x78, 0x78, 0x78);
    private static readonly PdfColor HeaderFooterText = new(0x55, 0x55, 0x55);

    private const double SlideBorderWidth = 0.5;
    private const double NotesBorderWidth = 0.5;
    internal const double NotesFontSize = 12;
    private const double PlaceholderFontSize = 12;
    private const double HeaderFooterFontSize = 9;
    internal const double NotesInset = 10;
    private const double NotesLeading = 16;

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest? request = null) =>
        PortablePdfWriter.WriteToBytes(BuildDocument(presentation, request), "FreeP notes page PDF");

    public static void Export(
        Presentation presentation,
        Stream stream,
        PresentationNotesPagePdfExportRequest? request = null) =>
        PortablePdfWriter.Write(BuildDocument(presentation, request), stream, "FreeP notes page PDF");

    public static PdfContentDocument BuildDocument(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest? request = null)
    {
        var renderPlan = BuildRenderPlan(presentation, request);
        return new PdfContentDocument(renderPlan.Pages, BuildDocumentProperties(presentation));
    }

    public static PresentationNotesPagePdfRenderPlan BuildRenderPlan(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        request ??= new PresentationNotesPagePdfExportRequest();

        var pageWidth = PresentationNotesPagePreviewPlanner.ResolveNotesPageWidthPoints(presentation, request.PageWidth);
        var pageHeight = PresentationNotesPagePreviewPlanner.ResolveNotesPageHeightPoints(presentation, request.PageHeight);
        var notesRequest = (request.PrintRequest ?? new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages)) with
        {
            Layout = PresentationPrintLayoutKind.NotesPages,
            HandoutSlidesPerPage = null,
        };
        var printPlan = PresentationExportPlanner.BuildPrintPlan(notesRequest, presentation.Slides.Count);

        if (printPlan.SlideRange.SlideNumbers.Count == 0)
        {
            var emptyPlan = PresentationNotesPagePreviewPlanner.Build(presentation, 0, pageWidth, pageHeight);
            return new PresentationNotesPagePdfRenderPlan(
                printPlan,
                [emptyPlan],
                BuildNotesPages(presentation, emptyPlan).ToArray());
        }

        var previewPlans = printPlan.SlideRange.SlideNumbers
            .Select(slideNumber => PresentationNotesPagePreviewPlanner.Build(
                presentation,
                slideNumber - 1,
                pageWidth,
                pageHeight))
            .ToArray();
        var pages = previewPlans.SelectMany(plan => BuildNotesPages(presentation, plan)).ToArray();
        return new PresentationNotesPagePdfRenderPlan(printPlan, previewPlans, pages);
    }

    internal static int CountRenderedPages(PresentationNotesPagePreviewPlan plan)
    {
        var top = plan.PageBounds.Height - plan.NotesBounds.Top - NotesInset - NotesFontSize;
        var bottom = plan.PageBounds.Height - plan.NotesBounds.Bottom + NotesInset;
        if (top < bottom || plan.NoteLines.Count == 0)
            return 1;

        var linesPerPage = 0;
        for (var y = top; y >= bottom; y -= NotesLeading)
            linesPerPage++;

        return Math.Max(1, (int)Math.Ceiling(plan.NoteLines.Count / (double)Math.Max(1, linesPerPage)));
    }

    /// <summary>
    /// Builds one notes page for <paramref name="plan"/>'s slide, plus as many continuation pages
    /// as needed when the speaker notes overflow the notes box. PowerPoint's own notes-page export
    /// continues overflowing notes onto additional pages rather than silently dropping them, so
    /// each continuation page repeats the slide thumbnail and notes-box border for context and
    /// resumes the note text where the previous page left off.
    /// </summary>
    private static IEnumerable<PdfContentPage> BuildNotesPages(
        Presentation presentation,
        PresentationNotesPagePreviewPlan plan)
    {
        var remainingLines = plan.NoteLines;
        var isFirstPage = true;

        do
        {
            var ops = new List<PdfDrawOp>
            {
                new PdfFillRect(
                    0,
                    0,
                    plan.PageBounds.Width,
                    plan.PageBounds.Height,
                    PageBackground),
            };

            if (plan.SlideIndex is { } slideIndex && slideIndex >= 0 && slideIndex < presentation.Slides.Count)
            {
                var slidePage = PresentationPdfExporter.BuildSlidePage(
                    presentation.Slides[slideIndex],
                    presentation.SlideSizeCxEmu,
                    presentation.SlideSizeCyEmu);
                ops.AddRange(MapSlideOps(slidePage, plan.SlideBounds, plan.PageBounds.Height));
            }

            ops.Add(ToPdfStrokeRect(plan.SlideBounds, plan.PageBounds.Height, SlideBorder, SlideBorderWidth));
            ops.Add(ToPdfStrokeRect(plan.NotesBounds, plan.PageBounds.Height, NotesBorder, NotesBorderWidth));
            AppendHeaderFooterPlaceholders(ops, plan);

            var showPlaceholder = isFirstPage &&
                remainingLines.Count == 0 &&
                plan.NotesPlaceholder.ShouldShowPlaceholder;
            remainingLines = AppendNotesText(ops, plan, remainingLines, showPlaceholder);

            yield return new PdfContentPage(plan.PageBounds.Width, plan.PageBounds.Height, ops);
            isFirstPage = false;
        }
        while (remainingLines.Count > 0);
    }

    /// <summary>
    /// Draws as many lines from <paramref name="lines"/> as fit in the notes box and returns the
    /// lines that did not fit, so the caller can continue them onto a following page instead of
    /// dropping them.
    /// </summary>
    private static IReadOnlyList<string> AppendNotesText(
        List<PdfDrawOp> ops,
        PresentationNotesPagePreviewPlan plan,
        IReadOnlyList<string> lines,
        bool showPlaceholder)
    {
        var top = plan.PageBounds.Height - plan.NotesBounds.Top - NotesInset - NotesFontSize;
        var bottom = plan.PageBounds.Height - plan.NotesBounds.Bottom + NotesInset;
        if (top < bottom)
            return [];

        if (showPlaceholder)
        {
            ops.Add(new PdfText(
                plan.NotesPlaceholder.Bounds.Left + NotesInset,
                top,
                PlaceholderFontSize,
                PdfFontFace.Regular,
                PlaceholderText,
                plan.NotesPlaceholder.PlaceholderText));
            return [];
        }

        var y = top;
        for (var index = 0; index < lines.Count; index++)
        {
            if (y < bottom)
                return lines.Skip(index).ToArray();

            var line = lines[index];
            ops.Add(new PdfText(
                plan.NotesBounds.Left + NotesInset,
                y,
                NotesFontSize,
                PdfFontFace.Regular,
                NotesText,
                string.IsNullOrWhiteSpace(line) ? " " : line));
            y -= NotesLeading;
        }

        return [];
    }

    private static void AppendHeaderFooterPlaceholders(
        List<PdfDrawOp> ops,
        PresentationNotesPagePreviewPlan plan)
    {
        foreach (var placeholder in plan.HeaderFooterPlaceholders)
        {
            if (!placeholder.IsVisible || string.IsNullOrWhiteSpace(placeholder.Text))
                continue;

            ops.Add(new PdfText(
                placeholder.Bounds.Left,
                plan.PageBounds.Height - placeholder.Bounds.Top - HeaderFooterFontSize,
                HeaderFooterFontSize,
                PdfFontFace.Regular,
                HeaderFooterText,
                placeholder.Text));
        }
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
