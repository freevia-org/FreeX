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
        var slideBounds = BuildSlideBounds(pageBounds);
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
            SplitNoteLines(notesText));
    }

    private static LayoutRect BuildSlideBounds(LayoutRect pageBounds)
    {
        var margin = Math.Min(48, pageBounds.Width / 6);
        var slideWidth = Math.Max(1, pageBounds.Width - (margin * 2));
        var slideHeight = slideWidth * 9d / 16d;
        var maxSlideHeight = pageBounds.Height * 0.44;
        if (slideHeight > maxSlideHeight)
        {
            slideHeight = maxSlideHeight;
            slideWidth = slideHeight * 16d / 9d;
        }

        return new LayoutRect(
            pageBounds.X + ((pageBounds.Width - slideWidth) / 2),
            pageBounds.Y + margin,
            slideWidth,
            Math.Max(1, slideHeight));
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

    private static IReadOnlyList<string> SplitNoteLines(string notesText) =>
        string.IsNullOrWhiteSpace(notesText)
            ? []
            : notesText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.TrimEnd())
                .ToArray();
}
