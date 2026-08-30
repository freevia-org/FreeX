using Free.Shared.Drawing;
using Free.Shared.Localization;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationNotesPagePdfExportRequest(
    PresentationPrintRequest? PrintRequest = null,
    double? PageWidth = null,
    double? PageHeight = null,
    PresentationNotesPageTextWidthMeasurer? MeasureRunWidth = null);

public sealed record PresentationNotesPagePdfRenderPlan(
    PresentationPrintPlan PrintPlan,
    IReadOnlyList<PresentationNotesPagePreviewPlan> PreviewPlans,
    IReadOnlyList<PdfContentPage> Pages,
    LocalizedTextDescriptor StatusText);

/// <summary>Host-supplied writer for a laid-out vector PDF content document.</summary>
public delegate byte[] PresentationPdfContentWriter(PdfContentDocument document);

/// <summary>
/// Host-supplied real text measurement for one styled notes run, following the same
/// caller-supplies-the-platform-capability shape as
/// <see cref="TextLayoutPlanner.PlanTabLeaderFill"/>'s <c>measureGlyphWidth</c> parameter and
/// <see cref="TextNativeRenderSequence.RenderTabs{TArtifact}"/>'s <c>format</c> delegate. A host
/// that can measure real glyphs (e.g. WPF <c>FormattedText.WidthIncludingTrailingWhitespace</c>
/// or Avalonia's text layout) should supply this so mixed-formatting notes lines position their
/// later runs where the actual glyphs end. When absent, the exporter falls back to real
/// Helvetica AFM advance widths for the printable ASCII range -- see
/// <see cref="PresentationNotesPagePdfExporter"/>'s built-in fallback -- because
/// <see cref="Free.Shared.Pdf.PortablePdfWriter"/> always renders this text in one of the four
/// standard Helvetica faces, so that fallback is real per-glyph metrics, not a re-tuned guess.
/// </summary>
public delegate double PresentationNotesPageTextWidthMeasurer(string text, PdfFontFace face, double fontSize);

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
                    printPlan.Options.IncludeCommentsAndInkMarkup,
                    request.MeasureRunWidth).ToArray(),
                PresentationShellTextCatalog.NotesPagePdfPlannedStatus);
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
                printPlan.Options.IncludeCommentsAndInkMarkup,
                request.MeasureRunWidth))
            .ToArray();
        return new PresentationNotesPagePdfRenderPlan(
            printPlan,
            previewPlans,
            pages,
            PresentationShellTextCatalog.NotesPagePdfPlannedStatus);
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
        bool includeCommentsAndInkMarkup,
        PresentationNotesPageTextWidthMeasurer? measureRunWidth)
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
            AppendNotesText(ops, textPlan, pageLines, styledPageLines, renderedPage.ShowsPlaceholder, measureRunWidth);

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
        bool showPlaceholder,
        PresentationNotesPageTextWidthMeasurer? measureRunWidth)
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
                AppendStyledLine(ops, plan, y, styledLines[index], measureRunWidth);
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
        PresentationNotesPageNoteLine line,
        PresentationNotesPageTextWidthMeasurer? measureRunWidth)
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

            var face = ResolveNoteRunFace(run);
            ops.Add(new PdfText(
                x,
                y,
                NotesFontSize,
                face,
                ToPdfColor(run.Color, NotesText),
                string.IsNullOrWhiteSpace(run.Text) ? " " : run.Text));
            x += measureRunWidth?.Invoke(run.Text, face, NotesFontSize)
                ?? MeasureHelveticaRunWidth(run.Text, face, NotesFontSize);
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

    // Published Adobe standard-14-font AFM advance widths (in thousandths of an em) for
    // Helvetica / Helvetica-Bold, indexed by (char - HelveticaTabledFirstChar), covering the
    // printable WinAnsi/ASCII range 0x20-0x7E. Helvetica-Oblique and Helvetica-BoldOblique share
    // their upright counterpart's widths (the AFM data defines no separate metrics for the
    // oblique faces), so only two tables are needed to cover all four PdfFontFace values that
    // PortablePdfWriter's Helvetica-only backend ever draws. This is the actual per-glyph metric
    // of the font this exporter renders, not a re-tuned average -- it replaces the flat
    // character-count estimate that caused runs after a bold/italic word to drift out of place.
    private const char HelveticaTabledFirstChar = ' ';
    private const char HelveticaTabledLastChar = '~';

    private static readonly int[] HelveticaRegularAdvanceWidths1000 =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static readonly int[] HelveticaBoldAdvanceWidths1000 =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    /// <summary>
    /// Real per-glyph Helvetica advance-width measurement used when no host measurer is supplied
    /// (see <see cref="PresentationNotesPageTextWidthMeasurer"/>). Characters outside the tabled
    /// printable-ASCII range fall back to the flat average, matching this exporter's prior
    /// behaviour for that narrow slice of untabled input (e.g. accented or non-Latin notes text).
    /// </summary>
    /// <summary>
    /// r173 remediation: the width the WORD-WRAP side must budget with, so wrapping and rendering
    /// can never disagree. It takes the wider of the regular and bold advance for every character,
    /// because the wrap planner concatenates a line's styled runs before breaking it and so does
    /// not know which face each character will finally be drawn in.
    ///
    /// <para>Over-estimating here is the safe direction and is what the wrap planner has always
    /// wanted (see its own comment): a line that measures a little narrow when rendered merely
    /// wraps a word early, whereas a line that measures wider than budgeted runs off the page. The
    /// flat 0.55-per-character average this replaces was NOT safe once the exporter began
    /// positioning runs by real Helvetica metrics -- a bold capitalised run measures far wider than
    /// the average, so a line the wrap planner believed fitted could be drawn clean off the sheet.
    /// </para>
    /// </summary>
    internal static double MeasureWidestFaceRunWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        double total = 0;
        foreach (var ch in text)
        {
            if (ch is >= HelveticaTabledFirstChar and <= HelveticaTabledLastChar)
            {
                var index = ch - HelveticaTabledFirstChar;
                var widest = Math.Max(
                    HelveticaRegularAdvanceWidths1000[index],
                    HelveticaBoldAdvanceWidths1000[index]);
                total += widest * fontSize / 1000.0;
            }
            else
            {
                total += fontSize * AverageGlyphWidthPerFontSize;
            }
        }

        return total;
    }

    private static double MeasureHelveticaRunWidth(string text, PdfFontFace face, double fontSize)
    {
        if (text.Length == 0)
            return 0;

        var widths = face is PdfFontFace.Bold or PdfFontFace.BoldItalic
            ? HelveticaBoldAdvanceWidths1000
            : HelveticaRegularAdvanceWidths1000;

        double total = 0;
        foreach (var ch in text)
        {
            total += ch is >= HelveticaTabledFirstChar and <= HelveticaTabledLastChar
                ? widths[ch - HelveticaTabledFirstChar] * fontSize / 1000.0
                : fontSize * AverageGlyphWidthPerFontSize;
        }

        return total;
    }

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
