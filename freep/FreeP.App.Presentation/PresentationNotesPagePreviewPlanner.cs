using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationNotesPagePreviewPlan(
    PresentationPrintPlan PrintPlan,
    int? SlideIndex,
    int? SlideNumber,
    string SlideTitle,
    string NotesText,
    string PlaceholderText,
    LayoutRect PageBounds,
    LayoutRect SlideBounds,
    LayoutRect NotesBounds,
    IReadOnlyList<string> NoteLines)
{
    public bool HasSlide => SlideIndex is not null;
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
}

/// <summary>
/// Shared notes-page preview policy for FreeP hosts. Rendering remains platform-local; geometry and text
/// extraction stay common so WPF and Avalonia do not drift while notes-page export is expanded.
/// </summary>
public static class PresentationNotesPagePreviewPlanner
{
    public const string EmptyDeckTitle = "No slide";
    public const string UntitledSlideTitle = "Untitled slide";
    public const string EmptyNotesPlaceholder = "Click to add speaker notes";

    public static PresentationNotesPagePreviewPlan Build(
        Presentation presentation,
        int currentSlideIndex,
        double pageWidth = PresentationExportPlanner.DefaultPrintPageWidth,
        double pageHeight = PresentationExportPlanner.DefaultPrintPageHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slideCount = presentation.Slides.Count;
        var pageBounds = new LayoutRect(0, 0, Math.Max(1, pageWidth), Math.Max(1, pageHeight));
        var slideBounds = BuildSlideBounds(pageBounds, presentation.SlideSizeCxEmu, presentation.SlideSizeCyEmu);
        var notesBounds = BuildNotesBounds(pageBounds, slideBounds);

        if (slideCount == 0)
        {
            return new PresentationNotesPagePreviewPlan(
                PresentationExportPlanner.BuildPrintPlan(
                    new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
                    slideCount),
                SlideIndex: null,
                SlideNumber: null,
                SlideTitle: EmptyDeckTitle,
                NotesText: string.Empty,
                PlaceholderText: EmptyNotesPlaceholder,
                pageBounds,
                slideBounds,
                notesBounds,
                NoteLines: []);
        }

        var normalizedIndex = Math.Clamp(currentSlideIndex, 0, slideCount - 1);
        var slide = presentation.Slides[normalizedIndex];
        var notesText = ExtractPlainText(slide.Notes);

        return new PresentationNotesPagePreviewPlan(
            PresentationExportPlanner.BuildPrintPlan(
                new PresentationPrintRequest(
                    PresentationPrintLayoutKind.NotesPages,
                    new PresentationSlideRangeRequest(
                        PresentationSlideRangeKind.CurrentSlide,
                        CurrentSlideNumber: normalizedIndex + 1)),
                slideCount),
            normalizedIndex,
            normalizedIndex + 1,
            NormalizeTitle(slide.Title),
            notesText,
            EmptyNotesPlaceholder,
            pageBounds,
            slideBounds,
            notesBounds,
            SplitNoteLines(notesText, notesBounds.Width));
    }

    private static LayoutRect BuildSlideBounds(LayoutRect pageBounds, long slideWidthEmu, long slideHeightEmu)
    {
        var margin = Math.Min(48, pageBounds.Width / 6);
        var aspectRatio = ResolveSlideAspectRatio(slideWidthEmu, slideHeightEmu);
        var slideWidth = Math.Max(1, pageBounds.Width - (margin * 2));
        var slideHeight = slideWidth / aspectRatio;
        var maxSlideHeight = pageBounds.Height * 0.44;
        if (slideHeight > maxSlideHeight)
        {
            slideHeight = maxSlideHeight;
            slideWidth = slideHeight * aspectRatio;
        }

        return new LayoutRect(
            pageBounds.X + ((pageBounds.Width - slideWidth) / 2),
            pageBounds.Y + margin,
            slideWidth,
            Math.Max(1, slideHeight));
    }

    private static double ResolveSlideAspectRatio(long slideWidthEmu, long slideHeightEmu)
    {
        if (slideWidthEmu <= 0 || slideHeightEmu <= 0)
            return 16d / 9d;

        return Math.Clamp((double)slideWidthEmu / slideHeightEmu, 0.1, 10d);
    }

    private static LayoutRect BuildNotesBounds(LayoutRect pageBounds, LayoutRect slideBounds)
    {
        var margin = Math.Min(48, pageBounds.Width / 6);
        var top = slideBounds.Bottom + 36;
        return new LayoutRect(
            pageBounds.X + margin,
            top,
            Math.Max(1, pageBounds.Width - (margin * 2)),
            Math.Max(1, pageBounds.Bottom - top - margin));
    }

    private static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? UntitledSlideTitle : title.Trim();

    private static string ExtractPlainText(TextBody? body)
    {
        if (body is null || body.Paragraphs.Count == 0)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            body.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))));
    }

    /// <summary>
    /// Average width, in points, of one Helvetica glyph at font-size 1 (a conservative
    /// approximation since the portable PDF writer has no real font-metrics table). Used only to
    /// decide word-wrap break points; it deliberately over-estimates slightly so wrapped lines
    /// never run past the notes-box width in the rendered PDF.
    /// </summary>
    private const double AverageGlyphWidthPerFontSize = 0.55;

    private static IReadOnlyList<string> SplitNoteLines(
        string notesText,
        double notesBoxWidth,
        double fontSize = PresentationNotesPagePdfExporter.NotesFontSize,
        double inset = PresentationNotesPagePdfExporter.NotesInset)
    {
        if (string.IsNullOrWhiteSpace(notesText))
            return [];

        var paragraphs = notesText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd());

        var maxWidth = Math.Max(1, notesBoxWidth - (2 * inset));
        var maxChars = Math.Max(1, (int)(maxWidth / Math.Max(0.01, fontSize * AverageGlyphWidthPerFontSize)));

        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
            lines.AddRange(WrapParagraph(paragraph, maxChars));

        return lines;
    }

    /// <summary>
    /// Breaks one paragraph into lines that each fit within <paramref name="maxChars"/>,
    /// wrapping at word boundaries. A single word longer than <paramref name="maxChars"/> is
    /// hard-broken so it never overruns the notes box width.
    /// </summary>
    private static IReadOnlyList<string> WrapParagraph(string paragraph, int maxChars)
    {
        if (paragraph.Length == 0)
            return [string.Empty];

        if (paragraph.Length <= maxChars)
            return [paragraph];

        var words = paragraph.Split(' ');
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            var candidateWord = word;
            while (candidateWord.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                lines.Add(candidateWord[..maxChars]);
                candidateWord = candidateWord[maxChars..];
            }

            var separatorLength = current.Length > 0 ? 1 : 0;
            if (current.Length + separatorLength + candidateWord.Length > maxChars)
            {
                if (current.Length > 0)
                    lines.Add(current.ToString());
                current.Clear();
                current.Append(candidateWord);
            }
            else
            {
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(candidateWord);
            }
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return lines.Count == 0 ? [string.Empty] : lines;
    }
}
