using System.Globalization;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationNotesPagePlaceholderKind
{
    Header,
    DateTime,
    Footer,
    SlideNumber
}

public sealed record PresentationNotesPagePlaceholder(
    PresentationNotesPagePlaceholderKind Kind,
    PlaceholderType SourcePlaceholderType,
    string Text,
    LayoutRect Bounds,
    bool IsVisible);

public sealed record PresentationNotesPageNotesPlaceholder(
    PlaceholderType SourcePlaceholderType,
    string PlaceholderText,
    string DisplayText,
    LayoutRect Bounds,
    bool IsVisible,
    bool HasContent)
{
    public bool ShouldShowPlaceholder => IsVisible && !HasContent;
}

public sealed record PresentationNotesPageRenderedPagePlan(
    int PageIndex,
    int PageNumber,
    bool IsContinuation,
    int FirstNoteLineIndex,
    int NoteLineCount,
    bool ShowsPlaceholder,
    string ThumbnailLabel,
    string Detail);

public sealed record PresentationNotesPageNoteTextRun(
    string Text,
    bool Bold,
    bool Italic,
    SrgbColor? Color);

public sealed record PresentationNotesPageNoteLine(
    string Text,
    IReadOnlyList<PresentationNotesPageNoteTextRun> Runs);

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
    PresentationNotesPageNotesPlaceholder NotesPlaceholder,
    IReadOnlyList<PresentationNotesPagePlaceholder> HeaderFooterPlaceholders,
    IReadOnlyList<string> NoteLines,
    IReadOnlyList<PresentationNotesPageNoteLine> StyledNoteLines,
    int LinesPerRenderedPage,
    IReadOnlyList<PresentationNotesPageRenderedPagePlan> RenderPages)
{
    public bool HasSlide => SlideIndex is not null;
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
    public int RenderedPageCount => RenderPages.Count;

    // PowerPoint treats a native notes master with no renderable placeholder
    // shapes as a text-only notes surface, rather than synthesizing the usual
    // thumbnail and notes-box fallback geometry.
    public bool UsesEmptyNativeNotesMaster { get; init; }
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
    private const double EmuPerPoint = 12700.0;

    public static PresentationNotesPagePreviewPlan Build(
        Presentation presentation,
        int currentSlideIndex,
        double? pageWidth = null,
        double? pageHeight = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slideCount = presentation.Slides.Count;
        var pageBounds = new LayoutRect(
            0,
            0,
            ResolveNotesPageWidthPoints(presentation, pageWidth),
            ResolveNotesPageHeightPoints(presentation, pageHeight));
        var usesEmptyNativeNotesMaster = HasEmptyNativeNotesMaster(presentation);
        var slideBounds = usesEmptyNativeNotesMaster
            ? new LayoutRect(0, 0, 0, 0)
            : BuildSlideBounds(pageBounds, presentation);
        var notesBounds = usesEmptyNativeNotesMaster
            ? pageBounds
            : BuildNotesBounds(pageBounds, slideBounds, presentation);

        if (slideCount == 0)
        {
            var emptyNotesPlaceholder = BuildNotesPlaceholder(string.Empty, notesBounds);
            var emptyNoteLines = Array.Empty<string>();
            var emptyLinesPerRenderedPage = CountLinesPerRenderedPage(pageBounds, notesBounds);
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
                emptyNotesPlaceholder,
                HeaderFooterPlaceholders: [],
                emptyNoteLines,
                StyledNoteLines: [],
                emptyLinesPerRenderedPage,
                BuildRenderedPages(null, emptyNoteLines, emptyNotesPlaceholder, emptyLinesPerRenderedPage))
            {
                UsesEmptyNativeNotesMaster = usesEmptyNativeNotesMaster
            };
        }

        var normalizedIndex = Math.Clamp(currentSlideIndex, 0, slideCount - 1);
        var slide = presentation.Slides[normalizedIndex];
        var notesText = ExtractPlainText(slide.Notes);
        var notesPlaceholder = BuildNotesPlaceholder(notesText, notesBounds);
        var styledNoteLines = SplitStyledNoteLines(slide.Notes, notesBounds.Width);
        var noteLines = styledNoteLines.Select(line => line.Text).ToArray();
        var linesPerPage = usesEmptyNativeNotesMaster
            ? 1
            : CountLinesPerRenderedPage(pageBounds, notesBounds);

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
            notesPlaceholder,
            BuildHeaderFooterPlaceholders(presentation, slide, normalizedIndex + 1, pageBounds),
            noteLines,
            styledNoteLines,
            linesPerPage,
            BuildRenderedPages(normalizedIndex + 1, noteLines, notesPlaceholder, linesPerPage))
        {
            UsesEmptyNativeNotesMaster = usesEmptyNativeNotesMaster
        };
    }

    internal static bool HasEmptyNativeNotesMaster(Presentation presentation) =>
        presentation.NotesMasterXml is { Length: > 0 } &&
        presentation.NotesMasterPlaceholders.Count == 0;

    public static double ResolveNotesPageWidthPoints(Presentation presentation, double? pageWidth = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return ResolvePageDimension(pageWidth, presentation.NotesPageSizeCxEmu, Presentation.DefaultNotesPageSizeCxEmu);
    }

    public static double ResolveNotesPageHeightPoints(Presentation presentation, double? pageHeight = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return ResolvePageDimension(pageHeight, presentation.NotesPageSizeCyEmu, Presentation.DefaultNotesPageSizeCyEmu);
    }

    private static double ResolvePageDimension(double? explicitValue, long modeledEmu, long fallbackEmu)
    {
        if (explicitValue is > 0)
            return explicitValue.Value;

        var emu = modeledEmu > 0 ? modeledEmu : fallbackEmu;
        return Math.Max(1, emu / EmuPerPoint);
    }

    private static LayoutRect BuildSlideBounds(LayoutRect pageBounds, Presentation presentation)
    {
        var nativeSlideImage = FindPlaceholderShape(
            presentation.NotesMasterPlaceholders,
            PlaceholderType.Picture);
        if (nativeSlideImage is { ExtentCxEmu: > 0, ExtentCyEmu: > 0 })
        {
            return new LayoutRect(
                nativeSlideImage.OffsetXEmu / EmuPerPoint,
                nativeSlideImage.OffsetYEmu / EmuPerPoint,
                nativeSlideImage.ExtentCxEmu / EmuPerPoint,
                nativeSlideImage.ExtentCyEmu / EmuPerPoint);
        }

        var margin = Math.Min(48, pageBounds.Width / 6);
        var aspectRatio = ResolveSlideAspectRatio(presentation.SlideSizeCxEmu, presentation.SlideSizeCyEmu);
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

    private static LayoutRect BuildNotesBounds(
        LayoutRect pageBounds,
        LayoutRect slideBounds,
        Presentation presentation)
    {
        var nativeBody = FindPlaceholderShape(presentation.NotesMasterPlaceholders, PlaceholderType.Body);
        if (nativeBody is { ExtentCxEmu: > 0, ExtentCyEmu: > 0 })
        {
            return new LayoutRect(
                nativeBody.OffsetXEmu / EmuPerPoint,
                nativeBody.OffsetYEmu / EmuPerPoint,
                nativeBody.ExtentCxEmu / EmuPerPoint,
                nativeBody.ExtentCyEmu / EmuPerPoint);
        }

        var margin = Math.Min(48, pageBounds.Width / 6);
        var top = slideBounds.Bottom + 36;
        return new LayoutRect(
            pageBounds.X + margin,
            top,
            Math.Max(1, pageBounds.Width - (margin * 2)),
            Math.Max(1, pageBounds.Bottom - top - margin));
    }

    private static PresentationNotesPageNotesPlaceholder BuildNotesPlaceholder(
        string notesText,
        LayoutRect notesBounds)
    {
        var hasContent = !string.IsNullOrWhiteSpace(notesText);
        return new PresentationNotesPageNotesPlaceholder(
            PlaceholderType.Body,
            EmptyNotesPlaceholder,
            hasContent ? notesText : EmptyNotesPlaceholder,
            notesBounds,
            IsVisible: true,
            hasContent);
    }

    private static IReadOnlyList<PresentationNotesPagePlaceholder> BuildHeaderFooterPlaceholders(
        Presentation presentation,
        Slide slide,
        int slideNumber,
        LayoutRect pageBounds)
    {
        var header = FindPlaceholderShape(slide, PlaceholderType.Header);
        var dateTime = FindPlaceholderShape(slide, PlaceholderType.DateTime);
        var footer = FindPlaceholderShape(slide, PlaceholderType.Footer);
        var slideNumberShape = FindPlaceholderShape(slide, PlaceholderType.SlideNumber);
        var notesHeader = FindPlaceholderShape(presentation.NotesMasterPlaceholders, PlaceholderType.Header);
        var notesDateTime = FindPlaceholderShape(presentation.NotesMasterPlaceholders, PlaceholderType.DateTime);
        var notesFooter = FindPlaceholderShape(presentation.NotesMasterPlaceholders, PlaceholderType.Footer);
        var notesSlideNumber = FindPlaceholderShape(presentation.NotesMasterPlaceholders, PlaceholderType.SlideNumber);
        var flags = slide.HfVisibility;

        var result = new List<PresentationNotesPagePlaceholder>(4);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.Header,
            PlaceholderType.Header,
            header ?? notesHeader,
            notesHeader,
            ResolveHeaderFooterVisibility(flags?.ShowHeader, header ?? notesHeader),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.DateTime,
            PlaceholderType.DateTime,
            dateTime ?? notesDateTime,
            notesDateTime,
            ResolveHeaderFooterVisibility(flags?.ShowDate, dateTime ?? notesDateTime),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.Footer,
            PlaceholderType.Footer,
            footer ?? notesFooter,
            notesFooter,
            ResolveHeaderFooterVisibility(flags?.ShowFooter, footer ?? notesFooter),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.SlideNumber,
            PlaceholderType.SlideNumber,
            slideNumberShape ?? notesSlideNumber,
            notesSlideNumber,
            ResolveHeaderFooterVisibility(flags?.ShowSlideNum, slideNumberShape ?? notesSlideNumber),
            pageBounds,
            slideNumber);

        return result;
    }

    private static void AddIfPresent(
        List<PresentationNotesPagePlaceholder> result,
        PresentationNotesPagePlaceholderKind kind,
        PlaceholderType sourceType,
        SlideShape? textShape,
        SlideShape? geometryShape,
        bool isVisible,
        LayoutRect pageBounds,
        int slideNumber)
    {
        if (textShape is null && !isVisible)
            return;

        result.Add(new PresentationNotesPagePlaceholder(
            kind,
            sourceType,
            ResolveHeaderFooterText(kind, textShape, slideNumber),
            BuildHeaderFooterBounds(kind, pageBounds, geometryShape),
            isVisible));
    }

    private static bool ResolveHeaderFooterVisibility(bool? flag, SlideShape? shape) =>
        flag ?? shape is not null;

    private static LayoutRect BuildHeaderFooterBounds(
        PresentationNotesPagePlaceholderKind kind,
        LayoutRect pageBounds,
        SlideShape? nativeShape)
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
        var margin = Math.Min(36, pageBounds.Width / 8);
        var width = Math.Max(1, (pageBounds.Width - (margin * 2)) * 0.34);
        var top = pageBounds.Top + margin / 2;
        var bottom = pageBounds.Bottom - margin / 2 - height;

        return kind switch
        {
            PresentationNotesPagePlaceholderKind.Header => new LayoutRect(
                pageBounds.Left + margin,
                top,
                width,
                height),
            PresentationNotesPagePlaceholderKind.DateTime => new LayoutRect(
                pageBounds.Right - margin - width,
                top,
                width,
                height),
            PresentationNotesPagePlaceholderKind.Footer => new LayoutRect(
                pageBounds.Left + margin,
                bottom,
                width,
                height),
            PresentationNotesPagePlaceholderKind.SlideNumber => new LayoutRect(
                pageBounds.Right - margin - width,
                bottom,
                width,
                height),
            _ => new LayoutRect(pageBounds.Left + margin, bottom, width, height),
        };
    }

    private static string ResolveHeaderFooterText(
        PresentationNotesPagePlaceholderKind kind,
        SlideShape? shape,
        int slideNumber)
    {
        var text = shape is null ? string.Empty : ExtractHeaderFooterText(shape, slideNumber);
        if (!string.IsNullOrWhiteSpace(text))
            return text.Trim();

        return kind == PresentationNotesPagePlaceholderKind.SlideNumber
            ? slideNumber.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ExtractHeaderFooterText(SlideShape shape, int slideNumber)
    {
        if (shape.TextBody is null)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            shape.TextBody.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run =>
                run.Field is { } field
                    ? ResolveHeaderFooterFieldText(field, slideNumber)
                    : run.Text))));
    }

    private static string ResolveHeaderFooterFieldText(FieldRun field, int slideNumber)
    {
        var fieldType = field.FieldType.ToLowerInvariant();
        if (fieldType.Contains("slidenum") || fieldType == "\\slidenum" || fieldType == "ppslidenum")
            return slideNumber.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(field.CachedText))
            return field.CachedText;

        return HeaderFooterDateTimeFormatter.IsDateTimeField(fieldType)
            ? HeaderFooterDateTimeFormatter.Format(fieldType, DateTime.Now)
            : string.Empty;
    }

    private static SlideShape? FindPlaceholderShape(Slide slide, PlaceholderType placeholderType) =>
        Flatten(slide.Shapes)
            .FirstOrDefault(shape => shape.Placeholder?.Type == placeholderType);

    private static SlideShape? FindPlaceholderShape(
        IEnumerable<SlideShape> shapes,
        PlaceholderType placeholderType) =>
        Flatten(shapes).FirstOrDefault(shape => shape.Placeholder?.Type == placeholderType);

    private static IEnumerable<SlideShape> Flatten(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in Flatten(shape.Children))
                yield return child;
        }
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

    private sealed record NoteTextSegment(string Text, bool Bold, bool Italic, SrgbColor? Color);

    private sealed record NoteParagraph(
        IReadOnlyList<NoteTextSegment> Segments,
        string Prefix,
        string ContinuationPrefix)
    {
        public string Text => string.Concat(Segments.Select(segment => segment.Text));
    }

    private static IReadOnlyList<NoteParagraph> ExtractNoteParagraphs(TextBody? body)
    {
        if (body is null || body.Paragraphs.Count == 0)
            return [];

        var markerState = new PresentationListMarkerContinuationState();
        var result = new List<NoteParagraph>(body.Paragraphs.Count);
        foreach (var paragraph in body.Paragraphs)
        {
            var marker = PresentationListMarkerPlanner.Resolve(
                paragraph,
                body.LstStyle?.Resolve(paragraph.Level),
                markerState);
            string prefix = marker.Kind is BulletKind.Char or BulletKind.Auto
                ? $"{marker.Text} "
                : string.Empty;
            var textSegments = ExtractTextSegments(paragraph.Runs);
            var levelIndent = new string(' ', Math.Clamp(paragraph.Level, 0, 8) * 2);
            result.Add(new NoteParagraph(
                textSegments,
                levelIndent + prefix,
                new string(' ', levelIndent.Length + prefix.Length)));
        }

        return result;
    }

    private static IReadOnlyList<NoteTextSegment> ExtractTextSegments(IEnumerable<Run> runs)
    {
        var result = new List<NoteTextSegment>();
        foreach (var run in runs)
        {
            var text = run.Field is { } field && !string.IsNullOrEmpty(field.CachedText)
                ? field.CachedText
                : run.Text;
            if (text.Length == 0)
                continue;

            result.Add(new NoteTextSegment(
                text,
                run.Field?.Bold ?? run.Bold,
                run.Field?.Italic ?? run.Italic,
                run.Field?.Color ?? run.Color?.Resolved));
        }

        return result;
    }

    /// <summary>
    /// Average width, in points, of one Helvetica glyph at font-size 1 (a conservative
    /// approximation since the portable PDF writer has no real font-metrics table). Used only to
    /// decide word-wrap break points; it deliberately over-estimates slightly so wrapped lines
    /// never run past the notes-box width in the rendered PDF.
    /// </summary>
    private const double AverageGlyphWidthPerFontSize = 0.55;

    private static IReadOnlyList<PresentationNotesPageNoteLine> SplitStyledNoteLines(
        TextBody? notes,
        double notesBoxWidth,
        double fontSize = PresentationNotesPagePdfExporter.NotesFontSize,
        double inset = PresentationNotesPagePdfExporter.NotesInset)
    {
        var paragraphs = ExtractNoteParagraphs(notes);
        if (paragraphs.Count == 0 || paragraphs.All(paragraph => string.IsNullOrWhiteSpace(paragraph.Text)))
            return [];

        var maxWidth = Math.Max(1, notesBoxWidth - (2 * inset));
        var maxChars = Math.Max(1, (int)(maxWidth / Math.Max(0.01, fontSize * AverageGlyphWidthPerFontSize)));

        var lines = new List<PresentationNotesPageNoteLine>();
        foreach (var paragraph in paragraphs)
        {
            var logicalLines = SplitSegmentsIntoLogicalLines(paragraph.Segments);

            for (var index = 0; index < logicalLines.Length; index++)
            {
                var prefix = index == 0 ? paragraph.Prefix : paragraph.ContinuationPrefix;
                var prefixedLine = PrefixLine(logicalLines[index], prefix);
                lines.AddRange(WrapStyledParagraph(
                    prefixedLine,
                    maxChars,
                    paragraph.ContinuationPrefix));
            }
        }

        return lines;
    }

    private static IReadOnlyList<NoteTextSegment>[] SplitSegmentsIntoLogicalLines(
        IReadOnlyList<NoteTextSegment> segments)
    {
        var lines = new List<IReadOnlyList<NoteTextSegment>>();
        var current = new List<NoteTextSegment>();
        foreach (var segment in segments)
        {
            var normalized = segment.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            var parts = normalized.Split('\n');
            for (var index = 0; index < parts.Length; index++)
            {
                if (index > 0)
                {
                    TrimTrailingWhitespace(current);
                    lines.Add(current.ToArray());
                    current = [];
                }

                if (parts[index].Length > 0)
                    current.Add(segment with { Text = parts[index] });
            }
        }

        TrimTrailingWhitespace(current);
        lines.Add(current.ToArray());
        return lines.ToArray();
    }

    private static void TrimTrailingWhitespace(List<NoteTextSegment> segments)
    {
        while (segments.Count > 0)
        {
            var last = segments[^1];
            var trimmed = last.Text.TrimEnd();
            if (trimmed.Length == last.Text.Length)
                return;

            if (trimmed.Length == 0)
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments[^1] = last with { Text = trimmed };
            return;
        }
    }

    private static IReadOnlyList<NoteTextSegment> PrefixLine(
        IReadOnlyList<NoteTextSegment> line,
        string prefix)
    {
        if (prefix.Length == 0)
            return line;

        var result = new List<NoteTextSegment>(line.Count + 1)
        {
            new(prefix, Bold: false, Italic: false, Color: null)
        };
        result.AddRange(line);
        return result;
    }

    /// <summary>
    /// Breaks one paragraph into lines that each fit within <paramref name="maxChars"/>,
    /// wrapping at word boundaries. A single word longer than <paramref name="maxChars"/> is
    /// hard-broken so it never overruns the notes box width.
    /// </summary>
    private static IReadOnlyList<string> WrapParagraph(
        string paragraph,
        int maxChars,
        string continuationPrefix = "")
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
                if (!string.IsNullOrEmpty(continuationPrefix) &&
                    candidateWord.Length + continuationPrefix.Length <= maxChars)
                {
                    current.Append(continuationPrefix);
                }
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

    private static IReadOnlyList<PresentationNotesPageNoteLine> WrapStyledParagraph(
        IReadOnlyList<NoteTextSegment> segments,
        int maxChars,
        string continuationPrefix)
    {
        var paragraph = string.Concat(segments.Select(segment => segment.Text));
        var wrappedLines = WrapParagraph(paragraph, maxChars, continuationPrefix);
        var result = new List<PresentationNotesPageNoteLine>(wrappedLines.Count);
        var cursor = 0;
        for (var lineIndex = 0; lineIndex < wrappedLines.Count; lineIndex++)
        {
            var line = wrappedLines[lineIndex];
            var outputRuns = new List<PresentationNotesPageNoteTextRun>();
            var mappedText = line;
            if (lineIndex > 0 &&
                continuationPrefix.Length > 0 &&
                mappedText.StartsWith(continuationPrefix, StringComparison.Ordinal))
            {
                outputRuns.Add(new PresentationNotesPageNoteTextRun(
                    continuationPrefix,
                    Bold: false,
                    Italic: false,
                    Color: null));
                mappedText = mappedText[continuationPrefix.Length..];
            }

            while (cursor < paragraph.Length &&
                   char.IsWhiteSpace(paragraph[cursor]) &&
                   (mappedText.Length == 0 || !char.IsWhiteSpace(mappedText[0])))
            {
                cursor++;
            }

            if (mappedText.Length > 0)
            {
                outputRuns.AddRange(MapStyledRuns(segments, cursor, mappedText.Length));
                cursor = Math.Min(paragraph.Length, cursor + mappedText.Length);
            }

            result.Add(new PresentationNotesPageNoteLine(line, MergeRuns(outputRuns)));
        }

        return result;
    }

    private static IReadOnlyList<PresentationNotesPageNoteTextRun> MapStyledRuns(
        IReadOnlyList<NoteTextSegment> segments,
        int start,
        int length)
    {
        if (length <= 0)
            return [];

        var result = new List<PresentationNotesPageNoteTextRun>();
        var absolute = 0;
        var remainingStart = start;
        var remainingLength = length;
        foreach (var segment in segments)
        {
            var segmentStart = absolute;
            var segmentEnd = absolute + segment.Text.Length;
            absolute = segmentEnd;

            if (segmentEnd <= remainingStart)
                continue;

            if (segmentStart >= remainingStart + remainingLength)
                break;

            var localStart = Math.Max(0, remainingStart - segmentStart);
            var available = segment.Text.Length - localStart;
            var take = Math.Min(available, remainingLength);
            if (take <= 0)
                continue;

            result.Add(new PresentationNotesPageNoteTextRun(
                segment.Text.Substring(localStart, take),
                segment.Bold,
                segment.Italic,
                segment.Color));
            remainingStart += take;
            remainingLength -= take;
            if (remainingLength == 0)
                break;
        }

        return result.Count == 0 ? [] : MergeRuns(result);
    }

    private static IReadOnlyList<PresentationNotesPageNoteTextRun> MergeRuns(
        IReadOnlyList<PresentationNotesPageNoteTextRun> runs)
    {
        if (runs.Count <= 1)
            return runs;

        var merged = new List<PresentationNotesPageNoteTextRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
                continue;

            if (merged.Count > 0 &&
                merged[^1].Bold == run.Bold &&
                merged[^1].Italic == run.Italic &&
                merged[^1].Color == run.Color)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
            }
            else
            {
                merged.Add(run);
            }
        }

        return merged;
    }

    private static int CountLinesPerRenderedPage(
        LayoutRect pageBounds,
        LayoutRect notesBounds)
    {
        var top = pageBounds.Height - notesBounds.Top -
            PresentationNotesPagePdfExporter.NotesInset -
            PresentationNotesPagePdfExporter.NotesFontSize;
        var bottom = pageBounds.Height - notesBounds.Bottom + PresentationNotesPagePdfExporter.NotesInset;
        if (top < bottom)
            return 0;

        var linesPerPage = 0;
        for (var y = top; y >= bottom; y -= PresentationNotesPagePdfExporter.NotesLeading)
            linesPerPage++;

        return Math.Max(1, linesPerPage);
    }

    private static IReadOnlyList<PresentationNotesPageRenderedPagePlan> BuildRenderedPages(
        int? slideNumber,
        IReadOnlyList<string> noteLines,
        PresentationNotesPageNotesPlaceholder notesPlaceholder,
        int linesPerRenderedPage)
    {
        var pageCount = noteLines.Count == 0
            ? 1
            : (int)Math.Ceiling(noteLines.Count / (double)Math.Max(1, linesPerRenderedPage));
        var result = new List<PresentationNotesPageRenderedPagePlan>(pageCount);
        for (var index = 0; index < pageCount; index++)
        {
            var firstLine = noteLines.Count == 0 ? 0 : index * Math.Max(1, linesPerRenderedPage);
            var lineCount = noteLines.Count == 0
                ? 0
                : Math.Min(Math.Max(1, linesPerRenderedPage), noteLines.Count - firstLine);
            var isContinuation = index > 0;
            result.Add(new PresentationNotesPageRenderedPagePlan(
                index,
                index + 1,
                isContinuation,
                firstLine,
                lineCount,
                ShowsPlaceholder: index == 0 && noteLines.Count == 0 && notesPlaceholder.ShouldShowPlaceholder,
                BuildThumbnailLabel(slideNumber, isContinuation),
                BuildDetail(slideNumber, isContinuation)));
        }

        return result;
    }

    private static string BuildThumbnailLabel(int? slideNumber, bool isContinuation)
    {
        if (slideNumber is not { } value || value <= 0)
            return isContinuation ? "No slide notes continued" : "No slide notes";

        return isContinuation ? $"Slide {value} notes continued" : $"Slide {value} notes";
    }

    private static string BuildDetail(int? slideNumber, bool isContinuation)
    {
        if (slideNumber is not { } value || value <= 0)
            return isContinuation ? "Notes continuation page without a slide" : "Notes page without a slide";

        return isContinuation
            ? $"Notes continuation page for slide {value}"
            : $"Notes page for slide {value}";
    }
}
