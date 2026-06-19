namespace FreeW.Core.Model;

/// <summary>
/// The bibliographic style governing how <see cref="Citations"/> renders in-text citations,
/// bibliography entries, and the bibliography heading. The numeric values are stable so a chosen
/// style can be persisted, and <see cref="CitationStyle.Apa"/> is the default (value 0) so an
/// unset/zero value matches the original author–year behaviour.
/// </summary>
public enum CitationStyle
{
    /// <summary>American Psychological Association (author–date). The original FreeW behaviour.</summary>
    Apa = 0,

    /// <summary>Modern Language Association (author–page; FreeW has no page, so author-only in-text).</summary>
    Mla = 1,

    /// <summary>Chicago (author–date variant).</summary>
    Chicago = 2,

    /// <summary>Institute of Electrical and Electronics Engineers (numeric in-text; author-first entries).</summary>
    Ieee = 3,
}

/// <summary>
/// Pure, WPF-free formatting of in-text citations and a bibliography from a document's
/// <see cref="TextDocument.Sources"/>. Lives in the model project so it is fully unit-testable
/// without any UI.
/// <para>
/// Formatting is selected by a <see cref="CitationStyle"/>. The no-argument-style overloads default to
/// <see cref="CitationStyle.Apa"/>, which is the original author–year behaviour, so existing call sites
/// are unaffected. Each style is documented on the overload that takes a <see cref="CitationStyle"/>.
/// </para>
/// <list type="bullet">
/// <item><b>In-text</b> (<see cref="FormatInText(Source, CitationStyle)"/>) — APA: <c>(Author, Year)</c>;
/// MLA: <c>(Author)</c> (no page field in FreeW's <see cref="Source"/>); Chicago (author–date):
/// <c>(Author Year)</c>. All degrade gracefully when fields are missing.</item>
/// <item><b>Bibliography entry</b> (<see cref="FormatBibliographyEntry(Source, CitationStyle)"/>) —
/// APA: <c>Author. (Year). Title. Publisher.</c>; MLA / Chicago: <c>Author. Title. Publisher, Year.</c>
/// Each segment is omitted when its field is empty.</item>
/// </list>
/// <para>
/// <see cref="BuildBibliography(TextDocument, CitationStyle)"/> produces ordinary styled
/// <see cref="Paragraph"/>s — a heading (<c>References</c> for APA, <c>Works Cited</c> for MLA,
/// <c>Bibliography</c> for Chicago) followed by one paragraph per source, sorted by author — using
/// dedicated bibliography style ids so they render with distinct formatting, round-trip through docx as
/// normal styled paragraphs (no I/O changes needed), and can be located again for a refresh via
/// <see cref="IsBibliographyParagraph(Block)"/>. Deterministic and side-effect free.
/// </para>
/// </summary>
public static class Citations
{
    /// <summary>Style id of the bibliography's heading paragraph.</summary>
    public const string HeadingStyleId = "BibliographyHeading";

    /// <summary>
    /// Display text of the bibliography's heading paragraph for the default
    /// (<see cref="CitationStyle.Apa"/>) style. See <see cref="HeadingTextFor(CitationStyle)"/> for the
    /// style-specific heading.
    /// </summary>
    public const string HeadingText = "References";

    /// <summary>Style id of each bibliography entry paragraph.</summary>
    public const string EntryStyleId = "BibliographyEntry";

    /// <summary>
    /// The bibliography heading text for <paramref name="style"/>: <c>References</c> (APA / IEEE),
    /// <c>Works Cited</c> (MLA), or <c>Bibliography</c> (Chicago).
    /// </summary>
    public static string HeadingTextFor(CitationStyle style) => style switch
    {
        CitationStyle.Mla => "Works Cited",
        CitationStyle.Chicago => "Bibliography",
        _ => "References",
    };

    /// <summary>
    /// The stable style name for <paramref name="style"/> — <c>APA</c>, <c>MLA</c>, <c>Chicago</c> or
    /// <c>IEEE</c> — as used by the References &gt; Citation Style combo and persisted to the docx
    /// bibliography part (<c>b:Sources/@SelectedStyle</c>). Round-trips with <see cref="ParseStyle"/>.
    /// </summary>
    public static string StyleName(CitationStyle style) => style switch
    {
        CitationStyle.Mla => "MLA",
        CitationStyle.Chicago => "Chicago",
        CitationStyle.Ieee => "IEEE",
        _ => "APA",
    };

    /// <summary>
    /// Parses a style name (case-insensitively; <c>APA</c> / <c>MLA</c> / <c>Chicago</c> / <c>IEEE</c>) back
    /// to a <see cref="CitationStyle"/>. An unrecognised or blank value yields the supplied
    /// <paramref name="fallback"/> (default <see cref="CitationStyle.Apa"/>) so unknown persisted styles
    /// degrade to the original behaviour. Inverse of <see cref="StyleName"/>.
    /// </summary>
    public static CitationStyle ParseStyle(string? name, CitationStyle fallback = CitationStyle.Apa) =>
        (name?.Trim().ToUpperInvariant()) switch
        {
            "MLA" => CitationStyle.Mla,
            "CHICAGO" => CitationStyle.Chicago,
            "IEEE" => CitationStyle.Ieee,
            "APA" => CitationStyle.Apa,
            _ => fallback,
        };

    /// <summary>
    /// Formats a source as an in-text citation using the default <see cref="CitationStyle.Apa"/> style:
    /// <c>(Author, Year)</c>, gracefully degrading to <c>(Author)</c>, <c>(Year)</c>, <c>(Tag)</c> or
    /// <c>(Unknown)</c> when fields are missing.
    /// </summary>
    public static string FormatInText(Source source) => FormatInText(source, CitationStyle.Apa);

    /// <summary>
    /// Formats a source as an in-text citation in the given <paramref name="style"/>:
    /// <list type="bullet">
    /// <item><b>APA</b>: <c>(Author, Year)</c> (author and year separated by a comma).</item>
    /// <item><b>MLA</b>: <c>(Author)</c> — MLA is author–page, but FreeW's <see cref="Source"/> carries no
    /// page, so only the author appears; with no author it falls back to the year/tag.</item>
    /// <item><b>Chicago</b> (author–date): <c>(Author Year)</c> (author and year separated by a space).</item>
    /// <item><b>IEEE</b>: numeric — <c>[Tag]</c> (or author/year), wrapped in square brackets. IEEE numbers
    /// citations in reference order; use <see cref="FormatInText(int, CitationStyle)"/> for the numbered
    /// form when the reference's position is known.</item>
    /// </list>
    /// All styles degrade gracefully: with only one of author/year present that value is used; with
    /// neither, the tag is used, else <c>Unknown</c>.
    /// </summary>
    public static string FormatInText(Source source, CitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(source);

        var author = source.Author?.Trim() ?? string.Empty;
        var year = source.Year?.Trim() ?? string.Empty;

        string inner;
        if (style == CitationStyle.Ieee)
        {
            // IEEE is numeric and bracketed; without a known reference index, cite the tag (then author/year)
            // inside square brackets so the in-text marker still resolves to a source.
            if (author.Length > 0)
                inner = author;
            else if (year.Length > 0)
                inner = year;
            else
                inner = FallbackTag(source);
            return $"[{inner}]";
        }

        if (style == CitationStyle.Mla)
        {
            // MLA is author–page; with no page field, cite the author alone, degrading to year/tag.
            if (author.Length > 0)
                inner = author;
            else if (year.Length > 0)
                inner = year;
            else
                inner = FallbackTag(source);
        }
        else if (author.Length > 0 && year.Length > 0)
        {
            // APA separates author and year with a comma; Chicago author–date uses a space.
            inner = style == CitationStyle.Chicago ? $"{author} {year}" : $"{author}, {year}";
        }
        else if (author.Length > 0)
            inner = author;
        else if (year.Length > 0)
            inner = year;
        else
            inner = FallbackTag(source);

        return $"({inner})";
    }

    private static string FallbackTag(Source source)
    {
        var tag = source.Tag?.Trim() ?? string.Empty;
        return tag.Length > 0 ? tag : "Unknown";
    }

    /// <summary>
    /// Formats a numbered in-text citation marker. For <see cref="CitationStyle.Ieee"/> this is the
    /// bracketed reference number <c>[n]</c> (IEEE numbers references in the order they appear); for the
    /// author–date styles, which do not number their citations, it returns an empty string so callers can
    /// fall back to <see cref="FormatInText(Source, CitationStyle)"/>. <paramref name="number"/> is 1-based.
    /// </summary>
    public static string FormatInText(int number, CitationStyle style) =>
        style == CitationStyle.Ieee ? $"[{number}]" : string.Empty;

    /// <summary>
    /// Formats a source as a bibliography entry using the default <see cref="CitationStyle.Apa"/> style:
    /// <c>Author. (Year). Title. Publisher.</c> Each segment is emitted only when its field is non-empty,
    /// so missing fields are dropped cleanly. A source with no populated fields yields an empty string.
    /// </summary>
    public static string FormatBibliographyEntry(Source source) =>
        FormatBibliographyEntry(source, CitationStyle.Apa);

    /// <summary>
    /// Formats a source as a bibliography entry in the given <paramref name="style"/>, taking the source's
    /// <see cref="Source.Type"/> into account (a <see cref="SourceType.JournalArticle"/> cites its
    /// journal/volume/issue/pages, a <see cref="SourceType.WebSite"/> its URL/accessed date, a
    /// <see cref="SourceType.Book"/> its publisher):
    /// <list type="bullet">
    /// <item><b>APA</b>: author–date — <c>Author. (Year). Title. &lt;type-specific&gt;.</c></item>
    /// <item><b>MLA</b> / <b>Chicago</b>: author-first with the year last — <c>Author. Title. &lt;type-specific&gt;, Year.</c></item>
    /// <item><b>IEEE</b>: author-first with the year near the end — <c>Author, "Title," &lt;type-specific&gt;, Year.</c></item>
    /// </list>
    /// Each segment is emitted only when its field is non-empty, so missing fields are dropped cleanly. A
    /// source with no populated fields yields an empty string.
    /// </summary>
    public static string FormatBibliographyEntry(Source source, CitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(source);

        return style switch
        {
            CitationStyle.Apa => FormatApaEntry(source),
            CitationStyle.Ieee => FormatIeeeEntry(source),
            _ => FormatAuthorTitlePublisherYearEntry(source),
        };
    }

    // The type-specific "source detail" common to several styles, comma-joined:
    //  - Book:           Publisher
    //  - JournalArticle: Journal, Volume, "no. Issue", "pp. Pages"
    //  - WebSite:        Publisher, Url, "accessed Accessed"
    // Returns an empty list when nothing applies so callers can drop the segment entirely.
    private static List<string> SourceDetail(Source source)
    {
        var parts = new List<string>(4);
        switch (source.Type)
        {
            case SourceType.JournalArticle:
                AddIfPresent(parts, source.Journal);
                if (NonEmpty(source.Volume) is { } vol)
                    parts.Add($"vol. {vol}");
                if (NonEmpty(source.Issue) is { } issue)
                    parts.Add($"no. {issue}");
                if (NonEmpty(source.Pages) is { } pages)
                    parts.Add($"pp. {pages}");
                break;
            case SourceType.WebSite:
                AddIfPresent(parts, source.Publisher);
                AddIfPresent(parts, source.Url);
                if (NonEmpty(source.Accessed) is { } accessed)
                    parts.Add($"accessed {accessed}");
                break;
            default: // Book
                AddIfPresent(parts, source.Publisher);
                break;
        }

        return parts;
    }

    // APA: Author. (Year). Title. <detail>.
    private static string FormatApaEntry(Source source)
    {
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

        var detail = SourceDetail(source);
        if (detail.Count > 0)
            segments.Add(WithPeriod(string.Join(", ", detail)));

        return string.Join(" ", segments);
    }

    // MLA / Chicago: Author. Title. <detail>, Year.
    // The detail and Year combine into a single final segment so missing fields never leave a stray comma.
    private static string FormatAuthorTitlePublisherYearEntry(Source source)
    {
        var segments = new List<string>(3);

        var author = source.Author?.Trim() ?? string.Empty;
        if (author.Length > 0)
            segments.Add(WithPeriod(author));

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        var detail = string.Join(", ", SourceDetail(source));
        var year = source.Year?.Trim() ?? string.Empty;
        if (detail.Length > 0 && year.Length > 0)
            segments.Add($"{detail}, {year}.");
        else if (detail.Length > 0)
            segments.Add(WithPeriod(detail));
        else if (year.Length > 0)
            segments.Add($"{year}.");

        return string.Join(" ", segments);
    }

    // IEEE: Author, "Title," <detail>, Year.
    // Author/detail/year are plain comma-joined segments; the title is quoted with IEEE's punctuation INSIDE
    // the closing quote (a comma when more segments follow, else the terminating period), e.g.
    //   Author, "Title," Journal, vol. V, Year.   /   only-title -> "Title."
    private static string FormatIeeeEntry(Source source)
    {
        var before = new List<string>(1);
        var after = new List<string>(4);

        var author = source.Author?.Trim() ?? string.Empty;
        if (author.Length > 0)
            before.Add(author);

        after.AddRange(SourceDetail(source));
        var year = source.Year?.Trim() ?? string.Empty;
        if (year.Length > 0)
            after.Add(year);

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            // No title: just the plain segments, period-terminated.
            var plain = before.Concat(after).ToList();
            return plain.Count == 0 ? string.Empty : WithPeriod(string.Join(", ", plain));
        }

        // Title present: the punctuation that would follow the title goes inside its closing quote — a comma
        // when more segments follow, else the final period.
        var quotedTitle = after.Count > 0 ? $"\"{title},\"" : $"\"{title}.\"";
        var tail = after.Count > 0 ? WithPeriod(string.Join(", ", after)) : string.Empty;

        var head = before.Count > 0 ? string.Join(", ", before) + ", " : string.Empty;
        var body = tail.Length > 0 ? $"{quotedTitle} {tail}" : quotedTitle;
        return head + body;
    }

    private static string? NonEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void AddIfPresent(List<string> parts, string? value)
    {
        if (NonEmpty(value) is { } v)
            parts.Add(v);
    }

    // Append a terminating period to a free-text segment, unless it already ends with sentence-ending
    // punctuation (so values like "Knuth, D." are not doubled to "Knuth, D..").
    private static string WithPeriod(string value)
    {
        var last = value[^1];
        return last is '.' or '!' or '?' ? value : value + ".";
    }

    /// <summary>
    /// Builds the bibliography paragraphs for <paramref name="document"/> using the default
    /// <see cref="CitationStyle.Apa"/> style. See <see cref="BuildBibliography(TextDocument, CitationStyle)"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildBibliography(TextDocument document) =>
        BuildBibliography(document, CitationStyle.Apa);

    /// <summary>
    /// Builds the bibliography paragraphs for <paramref name="document"/> in the given
    /// <paramref name="style"/>: a heading (<see cref="HeadingStyleId"/>) whose text is the
    /// style-specific <see cref="HeadingTextFor(CitationStyle)"/> (<c>References</c>/<c>Works Cited</c>/
    /// <c>Bibliography</c>), followed by one paragraph per source (<see cref="EntryStyleId"/>) formatted in
    /// <paramref name="style"/>, sorted by author (case-insensitive, ordinal), then by title and tag as
    /// stable tie-breakers. A document with no sources yields just the heading paragraph. Deterministic and
    /// side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildBibliography(TextDocument document, CitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingTextFor(style)) { StyleId = HeadingStyleId }
        };

        var ordered = document.Sources
            .OrderBy(s => s.Author?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Title?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Tag?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var source in ordered)
            paragraphs.Add(new Paragraph(FormatBibliographyEntry(source, style)) { StyleId = EntryStyleId });

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
