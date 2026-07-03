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
                BuildNotesPlaceholder(string.Empty, notesBounds),
                HeaderFooterPlaceholders: [],
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
            BuildNotesPlaceholder(notesText, notesBounds),
            BuildHeaderFooterPlaceholders(slide, normalizedIndex + 1, pageBounds),
            SplitNoteLines(slide.Notes, notesBounds.Width));
    }

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
        Slide slide,
        int slideNumber,
        LayoutRect pageBounds)
    {
        var header = FindPlaceholderShape(slide, PlaceholderType.Header);
        var dateTime = FindPlaceholderShape(slide, PlaceholderType.DateTime);
        var footer = FindPlaceholderShape(slide, PlaceholderType.Footer);
        var slideNumberShape = FindPlaceholderShape(slide, PlaceholderType.SlideNumber);
        var flags = slide.HfVisibility;

        var result = new List<PresentationNotesPagePlaceholder>(4);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.Header,
            PlaceholderType.Header,
            header,
            ResolveHeaderFooterVisibility(flags?.ShowHeader, header),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.DateTime,
            PlaceholderType.DateTime,
            dateTime,
            ResolveHeaderFooterVisibility(flags?.ShowDate, dateTime),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.Footer,
            PlaceholderType.Footer,
            footer,
            ResolveHeaderFooterVisibility(flags?.ShowFooter, footer),
            pageBounds,
            slideNumber);
        AddIfPresent(
            result,
            PresentationNotesPagePlaceholderKind.SlideNumber,
            PlaceholderType.SlideNumber,
            slideNumberShape,
            ResolveHeaderFooterVisibility(flags?.ShowSlideNum, slideNumberShape),
            pageBounds,
            slideNumber);

        return result;
    }

    private static void AddIfPresent(
        List<PresentationNotesPagePlaceholder> result,
        PresentationNotesPagePlaceholderKind kind,
        PlaceholderType sourceType,
        SlideShape? shape,
        bool isVisible,
        LayoutRect pageBounds,
        int slideNumber)
    {
        if (shape is null && !isVisible)
            return;

        result.Add(new PresentationNotesPagePlaceholder(
            kind,
            sourceType,
            ResolveHeaderFooterText(kind, shape, slideNumber),
            BuildHeaderFooterBounds(kind, pageBounds),
            isVisible));
    }

    private static bool ResolveHeaderFooterVisibility(bool? flag, SlideShape? shape) =>
        flag ?? shape is not null;

    private static LayoutRect BuildHeaderFooterBounds(
        PresentationNotesPagePlaceholderKind kind,
        LayoutRect pageBounds)
    {
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
        var text = shape is null ? string.Empty : ExtractHeaderFooterText(shape);
        if (!string.IsNullOrWhiteSpace(text))
            return text.Trim();

        return kind == PresentationNotesPagePlaceholderKind.SlideNumber
            ? slideNumber.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ExtractHeaderFooterText(SlideShape shape)
    {
        if (shape.TextBody is null)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            shape.TextBody.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run =>
                run.Field is { } field && !string.IsNullOrEmpty(field.CachedText)
                    ? field.CachedText
                    : run.Text))));
    }

    private static SlideShape? FindPlaceholderShape(Slide slide, PlaceholderType placeholderType) =>
        Flatten(slide.Shapes)
            .FirstOrDefault(shape => shape.Placeholder?.Type == placeholderType);

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

    private sealed record NoteParagraph(string Text, string Prefix, string ContinuationPrefix);

    private static IReadOnlyList<NoteParagraph> ExtractNoteParagraphs(TextBody? body)
    {
        if (body is null || body.Paragraphs.Count == 0)
            return [];

        var counters = new int[9];
        var result = new List<NoteParagraph>(body.Paragraphs.Count);
        foreach (var paragraph in body.Paragraphs)
        {
            var prefix = BuildParagraphPrefix(paragraph, counters);
            var text = string.Concat(paragraph.Runs.Select(run => run.Text));
            var levelIndent = new string(' ', Math.Clamp(paragraph.Level, 0, 8) * 2);
            result.Add(new NoteParagraph(
                text,
                levelIndent + prefix,
                new string(' ', levelIndent.Length + prefix.Length)));
        }

        return result;
    }

    private static string BuildParagraphPrefix(Paragraph paragraph, int[] counters)
    {
        var level = Math.Clamp(paragraph.Level, 0, counters.Length - 1);
        for (var i = level + 1; i < counters.Length; i++)
            counters[i] = 0;

        return paragraph.BulletKind switch
        {
            BulletKind.Char => $"{(string.IsNullOrEmpty(paragraph.BulletChar) ? "\u2022" : paragraph.BulletChar)} ",
            BulletKind.Auto => $"{BuildAutoNumberText(paragraph, counters, level)} ",
            _ => string.Empty,
        };
    }

    private static string BuildAutoNumberText(Paragraph paragraph, int[] counters, int level)
    {
        if (counters[level] == 0)
            counters[level] = Math.Max(1, paragraph.AutoNumStartAt);
        else
            counters[level]++;

        var value = counters[level];
        return paragraph.AutoNumType switch
        {
            AutoNumType.ArabicParenR => $"{value})",
            AutoNumType.ArabicParenBoth => $"({value})",
            AutoNumType.RomanUcPeriod => $"{ToRoman(value).ToUpperInvariant()}.",
            AutoNumType.RomanLcPeriod => $"{ToRoman(value).ToLowerInvariant()}.",
            AutoNumType.RomanUcParenR => $"{ToRoman(value).ToUpperInvariant()})",
            AutoNumType.RomanLcParenR => $"{ToRoman(value).ToLowerInvariant()})",
            AutoNumType.AlphaUcPeriod => $"{ToAlpha(value).ToUpperInvariant()}.",
            AutoNumType.AlphaLcPeriod => $"{ToAlpha(value).ToLowerInvariant()}.",
            AutoNumType.AlphaUcParenR => $"{ToAlpha(value).ToUpperInvariant()})",
            AutoNumType.AlphaLcParenR => $"{ToAlpha(value).ToLowerInvariant()})",
            AutoNumType.AlphaUcParenBoth => $"({ToAlpha(value).ToUpperInvariant()})",
            AutoNumType.AlphaLcParenBoth => $"({ToAlpha(value).ToLowerInvariant()})",
            _ => $"{value}.",
        };
    }

    private static string ToAlpha(int value)
    {
        value = Math.Max(1, value);
        var chars = new Stack<char>();
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + (value % 26)));
            value /= 26;
        }

        return new string(chars.ToArray());
    }

    private static string ToRoman(int value)
    {
        value = Math.Clamp(value, 1, 3999);
        var map = new (int Value, string Text)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        };
        var result = new System.Text.StringBuilder();
        foreach (var (number, text) in map)
        {
            while (value >= number)
            {
                result.Append(text);
                value -= number;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Average width, in points, of one Helvetica glyph at font-size 1 (a conservative
    /// approximation since the portable PDF writer has no real font-metrics table). Used only to
    /// decide word-wrap break points; it deliberately over-estimates slightly so wrapped lines
    /// never run past the notes-box width in the rendered PDF.
    /// </summary>
    private const double AverageGlyphWidthPerFontSize = 0.55;

    private static IReadOnlyList<string> SplitNoteLines(
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

        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var logicalLines = paragraph.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.TrimEnd())
                .ToArray();

            for (var index = 0; index < logicalLines.Length; index++)
            {
                var prefix = index == 0 ? paragraph.Prefix : paragraph.ContinuationPrefix;
                lines.AddRange(WrapParagraph(
                    prefix + logicalLines[index],
                    maxChars,
                    paragraph.ContinuationPrefix));
            }
        }

        return lines;
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
}
