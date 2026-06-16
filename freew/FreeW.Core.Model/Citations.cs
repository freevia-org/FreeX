using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free formatting of in-text citations and a bibliography from a document's
/// <see cref="TextDocument.Sources"/>. Lives in the model project so it is fully unit-testable
/// without any UI.
/// <para>
/// The citation style is a simple, consistent author–year style (loosely APA-flavoured):
/// </para>
/// <list type="bullet">
/// <item><b>In-text</b> (<see cref="FormatInText(Source)"/>): <c>(Author, Year)</c>. With only an
/// author it is <c>(Author)</c>; with only a year, <c>(Year)</c>; with neither (but a tag) it falls
/// back to <c>(Tag)</c>, else <c>(Unknown)</c>.</item>
/// <item><b>Bibliography entry</b> (<see cref="FormatBibliographyEntry(Source)"/>):
/// <c>Author. (Year). Title. Publisher.</c> — each segment is omitted when its field is empty, so a
/// source with only a title renders as just <c>Title.</c></item>
/// </list>
/// <para>
/// <see cref="BuildBibliography(TextDocument)"/> produces ordinary styled <see cref="Paragraph"/>s — a
/// "Bibliography" heading followed by one paragraph per source, sorted by author — using dedicated
/// bibliography style ids so they render with distinct formatting, round-trip through docx as normal
/// styled paragraphs (no I/O changes needed), and can be located again for a refresh via
/// <see cref="IsBibliographyParagraph(Block)"/>. Deterministic and side-effect free.
/// </para>
/// </summary>
public static class Citations
{
    /// <summary>Style id of the bibliography's heading paragraph.</summary>
    public const string HeadingStyleId = "BibliographyHeading";

    /// <summary>Display text of the bibliography's heading paragraph.</summary>
    public const string HeadingText = "Bibliography";

    /// <summary>Style id of each bibliography entry paragraph.</summary>
    public const string EntryStyleId = "BibliographyEntry";

    /// <summary>
    /// Formats a source as an in-text citation: <c>(Author, Year)</c>, gracefully degrading when a
    /// field is missing — <c>(Author)</c>, <c>(Year)</c>, <c>(Tag)</c> or <c>(Unknown)</c>.
    /// </summary>
    public static string FormatInText(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var author = source.Author?.Trim() ?? string.Empty;
        var year = source.Year?.Trim() ?? string.Empty;

        string inner;
        if (author.Length > 0 && year.Length > 0)
            inner = $"{author}, {year}";
        else if (author.Length > 0)
            inner = author;
        else if (year.Length > 0)
            inner = year;
        else
        {
            var tag = source.Tag?.Trim() ?? string.Empty;
            inner = tag.Length > 0 ? tag : "Unknown";
        }

        return $"({inner})";
    }

    /// <summary>
    /// Formats a source as a bibliography entry: <c>Author. (Year). Title. Publisher.</c> Each segment
    /// is emitted only when its field is non-empty, so missing fields are dropped cleanly. A source with
    /// no populated fields yields an empty string.
    /// </summary>
    public static string FormatBibliographyEntry(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var segments = new List<string>(4);

        var author = source.Author?.Trim() ?? string.Empty;
        if (author.Length > 0)
            segments.Add(WithPeriod(author));

        var year = source.Year?.Trim() ?? string.Empty;
        if (year.Length > 0)
            segments.Add($"({year}).");

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        var publisher = source.Publisher?.Trim() ?? string.Empty;
        if (publisher.Length > 0)
            segments.Add(WithPeriod(publisher));

        return string.Join(" ", segments);
    }

    // Append a terminating period to a free-text segment, unless it already ends with sentence-ending
    // punctuation (so values like "Knuth, D." are not doubled to "Knuth, D..").
    private static string WithPeriod(string value)
    {
        var last = value[^1];
        return last is '.' or '!' or '?' ? value : value + ".";
    }

    /// <summary>
    /// Builds the bibliography paragraphs for <paramref name="document"/>: a "Bibliography" heading
    /// (<see cref="HeadingStyleId"/>) followed by one paragraph per source (<see cref="EntryStyleId"/>),
    /// sorted by author (case-insensitive, ordinal), then by title and tag as stable tie-breakers. A
    /// document with no sources yields just the heading paragraph. Deterministic and side-effect free —
    /// it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildBibliography(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var ordered = document.Sources
            .OrderBy(s => s.Author?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Title?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Tag?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var source in ordered)
            paragraphs.Add(new Paragraph(FormatBibliographyEntry(source)) { StyleId = EntryStyleId });

        return paragraphs;
    }

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the bibliography styles produced by
    /// <see cref="BuildBibliography(TextDocument)"/> (the heading or an entry style). Used to recognise a
    /// previously inserted bibliography region so a refresh can remove it.
    /// </summary>
    public static bool IsBibliographyStyleId(string? styleId) =>
        string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
        || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal);

    /// <summary>True when <paramref name="block"/> is a paragraph carrying a bibliography style.</summary>
    public static bool IsBibliographyParagraph(Block block) =>
        block is Paragraph paragraph && IsBibliographyStyleId(paragraph.StyleId);

    /// <summary>
    /// Registers the bibliography styles (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>) in
    /// <paramref name="document"/>'s style catalog if not already present, so the inserted paragraphs
    /// resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "Bibliography Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(EntryStyleId, new DocumentStyle
        {
            Id = EntryStyleId,
            Name = "Bibliography Entry",
            BasedOnStyleId = "Normal",
            // A hanging-style entry: indented body with the first line pulled back to the margin.
            Paragraph = new ParagraphFormatting
            {
                SpaceAfterPt = 6,
                IndentLeftPt = 36,
                FirstLineIndentPt = -36
            }
        });
    }
}
