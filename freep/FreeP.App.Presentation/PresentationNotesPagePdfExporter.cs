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

/// <summary>Host-supplied writer for a laid-out vector PDF content document.</summary>
public delegate byte[] PresentationPdfContentWriter(PdfContentDocument document);

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
    // PowerPoint's default notes-master bodyPr uses 45720 EMU (3.6 pt) insets.
    // Its 12 pt body text advances at roughly 15 pt, rather than the wider host fallback.
    internal const double NotesInset = 3.6;
    internal const double NotesLeading = 15;
    private const double EmptyNativeFirstPageTextLeft = -3.6;
    private const double EmptyNativeFirstPageTextTop = -4.32;
    private const double EmptyNativeContinuationTextLeft = 57.624;
    private const double EmptyNativeContinuationTextTop = 53.3;
    private const double AverageGlyphWidthPerFontSize = 0.55;

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest? request = null) =>
        PortablePdfWriter.WriteToBytes(BuildDocument(presentation, request), "FreeP notes page PDF");

    public static byte[] ExportToBytes(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest? request,
        PresentationPdfContentWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer(BuildDocument(presentation, request));
    }

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
        return new PdfContentDocument(
            renderPlan.Pages,
            PresentationPdfScenePlanner.BuildDocumentProperties(presentation));
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
        var printPlan = PresentationExportPlanner.BuildPrintPlan(notesRequest, presentation);

        if (printPlan.SlideRange.SlideNumbers.Count == 0)
        {
            var emptyPlan = PresentationNotesPagePreviewPlanner.Build(presentation, 0, pageWidth, pageHeight);
            return new PresentationNotesPagePdfRenderPlan(
                printPlan,
                [emptyPlan],
                BuildNotesPages(
                    presentation,
                    emptyPlan,
                    printPlan.Options.IncludeCommentsAndInkMarkup).ToArray());
        }

        var previewPlans = printPlan.SlideRange.SlideNumbers
            .Select(slideNumber => PresentationNotesPagePreviewPlanner.Build(
                presentation,
                slideNumber - 1,
                pageWidth,
                pageHeight))
            .ToArray();
        var pages = previewPlans
            .SelectMany(plan => BuildNotesPages(
                presentation,
                plan,
                printPlan.Options.IncludeCommentsAndInkMarkup))
            .ToArray();
        return new PresentationNotesPagePdfRenderPlan(printPlan, previewPlans, pages);
    }

    internal static int CountRenderedPages(PresentationNotesPagePreviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Math.Max(1, plan.RenderedPageCount);
    }

    /// <summary>
    /// Builds one notes page for <paramref name="plan"/>'s slide, plus as many continuation pages
    /// as needed when the speaker notes overflow the notes box. Native notes masters without
    /// renderable placeholder shapes follow PowerPoint's text-only continuation surface.
    /// </summary>
    private static IEnumerable<PdfContentPage> BuildNotesPages(
        Presentation presentation,
        PresentationNotesPagePreviewPlan plan,
        bool includeCommentsAndInkMarkup)
    {
        foreach (var renderedPage in plan.RenderPages)
        {
            var usesEmptyNativeNotesMaster = plan.UsesEmptyNativeNotesMaster;
            var ops = new List<PdfDrawOp>
            {
                new PdfFillRect(
                    0,
                    0,
                    plan.PageBounds.Width,
                    plan.PageBounds.Height,
                    PageBackground),
            };

            if (!usesEmptyNativeNotesMaster &&
                plan.SlideIndex is { } slideIndex && slideIndex >= 0 && slideIndex < presentation.Slides.Count)
            {
                var slidePage = PresentationPdfExporter.BuildSlidePage(
                    presentation.Slides[slideIndex],
                    presentation.SlideSizeCxEmu,
                    presentation.SlideSizeCyEmu,
                    includeCommentsAndInkMarkup);
                ops.AddRange(PdfContentPagePlacement.MapOps(
                    slidePage,
                    plan.SlideBounds.X,
                    plan.SlideBounds.Y,
                    plan.SlideBounds.Width,
                    plan.SlideBounds.Height,
                    plan.PageBounds.Height));
            }

            if (!usesEmptyNativeNotesMaster)
            {
                ops.Add(ToPdfStrokeRect(plan.SlideBounds, plan.PageBounds.Height, SlideBorder, SlideBorderWidth));
                ops.Add(ToPdfStrokeRect(plan.NotesBounds, plan.PageBounds.Height, NotesBorder, NotesBorderWidth));
                AppendHeaderFooterPlaceholders(ops, plan);
            }

            var textPlan = usesEmptyNativeNotesMaster
                ? plan with
                {
                    NotesBounds = new LayoutRect(
                        renderedPage.IsContinuation
                            ? EmptyNativeContinuationTextLeft
                            : EmptyNativeFirstPageTextLeft,
                        renderedPage.IsContinuation
                            ? EmptyNativeContinuationTextTop
                            : EmptyNativeFirstPageTextTop,
                        plan.PageBounds.Width,
                        plan.PageBounds.Height)
                }
                : plan;

            var pageLines = plan.NoteLines
                .Skip(renderedPage.FirstNoteLineIndex)
                .Take(renderedPage.NoteLineCount)
                .ToArray();
            var styledPageLines = plan.StyledNoteLines
                .Skip(renderedPage.FirstNoteLineIndex)
                .Take(renderedPage.NoteLineCount)
                .ToArray();
            AppendNotesText(ops, textPlan, pageLines, styledPageLines, renderedPage.ShowsPlaceholder);

            yield return new PdfContentPage(plan.PageBounds.Width, plan.PageBounds.Height, ops);
        }
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
        IReadOnlyList<PresentationNotesPageNoteLine> styledLines,
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
            if (index < styledLines.Count)
                AppendStyledLine(ops, plan, y, styledLines[index]);
            else
                AppendPlainLine(ops, plan, y, line);
            y -= NotesLeading;
        }

        return [];
    }

    private static void AppendPlainLine(
        List<PdfDrawOp> ops,
        PresentationNotesPagePreviewPlan plan,
        double y,
        string line) =>
        ops.Add(new PdfText(
            plan.NotesBounds.Left + NotesInset,
            y,
            NotesFontSize,
            PdfFontFace.Regular,
            NotesText,
            string.IsNullOrWhiteSpace(line) ? " " : line));

    private static void AppendStyledLine(
        List<PdfDrawOp> ops,
        PresentationNotesPagePreviewPlan plan,
        double y,
        PresentationNotesPageNoteLine line)
    {
        if (line.Runs.Count == 0)
        {
            AppendPlainLine(ops, plan, y, line.Text);
            return;
        }

        var x = plan.NotesBounds.Left + NotesInset;
        foreach (var run in line.Runs)
        {
            if (run.Text.Length == 0)
                continue;

            ops.Add(new PdfText(
                x,
                y,
                NotesFontSize,
                ResolveNoteRunFace(run),
                ToPdfColor(run.Color, NotesText),
                string.IsNullOrWhiteSpace(run.Text) ? " " : run.Text));
            x += EstimateTextWidth(run.Text, NotesFontSize);
        }
    }

    private static PdfFontFace ResolveNoteRunFace(PresentationNotesPageNoteTextRun run) =>
        (run.Bold, run.Italic) switch
        {
            (true, true) => PdfFontFace.BoldItalic,
            (true, false) => PdfFontFace.Bold,
            (false, true) => PdfFontFace.Italic,
            _ => PdfFontFace.Regular,
        };

    private static double EstimateTextWidth(string text, double fontSize) =>
        text.Length * fontSize * AverageGlyphWidthPerFontSize;

    private static PdfColor ToPdfColor(SrgbColor? color, PdfColor fallback) =>
        color is { } value ? new PdfColor(value.R, value.G, value.B) : fallback;

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

    private static PdfStrokeRect ToPdfStrokeRect(
        LayoutRect rect,
        double pageHeight,
        PdfColor color,
        double lineWidth) =>
        new(rect.X, pageHeight - rect.Bottom, rect.Width, rect.Height, color, lineWidth);

}
