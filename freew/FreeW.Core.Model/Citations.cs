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

    /// <summary>
    /// Turabian (Notes-Bibliography variant; author–date in-text similar to Chicago, bibliography
    /// ordering and heading identical to Chicago). Numeric values are stable.
    /// </summary>
    Turabian = 4,

    /// <summary>
    /// Harvard (author–date; in-text <c>(Author, Year)</c> like APA; bibliography: <c>Author Year, Title,
    /// Publisher.</c> — the year comes immediately after the author).
    /// </summary>
    Harvard = 5,

    /// <summary>
    /// Vancouver (numeric; in-text <c>[n]</c>; bibliography entries are numbered and omit the title's
    /// quotation marks, following the NLM/ICMJE medical-journal style).
    /// </summary>
    Vancouver = 6,

    /// <summary>
    /// GOST R 7.0.5-2008 (Russian national standard; Cyrillic-aware but ASCII-compatible for FreeW's
    /// model; author–year in-text; bibliography: Author. Title. City: Publisher, Year. — pages/journal
    /// come after the publisher).
    /// </summary>
    Gost = 7,

    /// <summary>
    /// ISO 690 (international bibliographic standard; author–date in-text identical to Harvard/APA;
    /// bibliography: AUTHOR, Year. Title. Publisher. DOI/URL. — family-name in SMALL-CAPS is approximated
    /// in FreeW with ALL-CAPS on the author segment).
    /// </summary>
    Iso690 = 8,
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
/// <see cref="BuildBibliography(TextDocument, CitationStyle)"/> produces styled <see cref="Paragraph"/>s
/// inside Word's native multi-paragraph <c>BIBLIOGRAPHY</c> field ownership boundary. The heading remains
/// outside the field, matching Word's built-in bibliography region, while the entries stay updateable in
/// Word and can be located again for a refresh via <see cref="IsBibliographyParagraph(Block)"/>.
/// Deterministic and side-effect free.
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

    /// <summary>Native Word field instruction for an English-language bibliography result.</summary>
    public const string NativeFieldInstruction = " BIBLIOGRAPHY \\l 1033 ";

    /// <summary>Word's cached result when the current document has no bibliography sources.</summary>
    public const string EmptyResultText = "There are no sources in the current document.";

    /// <summary>
    /// The bibliography heading text for <paramref name="style"/>:
    /// <c>Works Cited</c> (MLA), <c>Bibliography</c> (Chicago / Turabian), or <c>References</c> for all
    /// others (APA, IEEE, Harvard, Vancouver, GOST, ISO-690).
    /// </summary>
    public static string HeadingTextFor(CitationStyle style) => style switch
    {
        CitationStyle.Mla => "Works Cited",
        CitationStyle.Chicago or CitationStyle.Turabian => "Bibliography",
        _ => "References",
    };

    /// <summary>
    /// The stable style name for <paramref name="style"/> — <c>APA</c>, <c>MLA</c>, <c>Chicago</c>,
    /// <c>IEEE</c>, <c>Turabian</c>, <c>Harvard</c>, <c>Vancouver</c>, <c>GOST</c>, <c>ISO690</c> — as used
    /// by the References &gt; Citation Style combo and persisted to the docx bibliography part
    /// (<c>b:Sources/@SelectedStyle</c>). Round-trips with <see cref="ParseStyle"/>.
    /// </summary>
    public static string StyleName(CitationStyle style) => style switch
    {
        CitationStyle.Mla => "MLA",
        CitationStyle.Chicago => "Chicago",
        CitationStyle.Ieee => "IEEE",
        CitationStyle.Turabian => "Turabian",
        CitationStyle.Harvard => "Harvard",
        CitationStyle.Vancouver => "Vancouver",
        CitationStyle.Gost => "GOST",
        CitationStyle.Iso690 => "ISO690",
        _ => "APA",
    };

    /// <summary>
    /// Parses a style name (case-insensitively) back to a <see cref="CitationStyle"/>. An unrecognised or
    /// blank value yields the supplied <paramref name="fallback"/> (default <see cref="CitationStyle.Apa"/>)
    /// so unknown persisted styles degrade to the original behaviour. Inverse of <see cref="StyleName"/>.
    /// </summary>
    public static CitationStyle ParseStyle(string? name, CitationStyle fallback = CitationStyle.Apa) =>
        (name?.Trim().ToUpperInvariant()) switch
        {
            "MLA" => CitationStyle.Mla,
            "CHICAGO" => CitationStyle.Chicago,
            "IEEE" => CitationStyle.Ieee,
            "TURABIAN" => CitationStyle.Turabian,
            "HARVARD" => CitationStyle.Harvard,
            "VANCOUVER" => CitationStyle.Vancouver,
            "GOST" => CitationStyle.Gost,
            "ISO690" => CitationStyle.Iso690,
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
    /// Formats <paramref name="source"/> as an in-text citation using the source's 1-based order in
    /// <paramref name="document"/> for numeric styles (<see cref="CitationStyle.Ieee"/> and
    /// <see cref="CitationStyle.Vancouver"/>). When the source is not present in the document, numeric
    /// styles fall back to the placeholder form from <see cref="FormatInText(Source, CitationStyle)"/>.
    /// Author-date styles keep their existing source-only formatting.
    /// </summary>
    public static string FormatInText(TextDocument document, Source source, CitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        if (!IsNumericStyle(style))
            return FormatInText(source, style);

        var number = ReferenceNumberFor(document, source);
        return number > 0 ? FormatInText(number, style) : FormatInText(source, style);
    }

    /// <summary>
    /// Builds a Word-like <c>CITATION tag</c> complex-field run for a tagged source. The run's cached text is
    /// the current in-text citation display, so it remains readable before a later Update Fields pass.
    /// Untagged sources cannot be addressed by Word's CITATION field and should keep the plain-text fallback.
    /// </summary>
    public static bool TryCreateCitationFieldRun(
        TextDocument document,
        Source source,
        CitationStyle style,
        out Run run,
        RunFormatting? formatting = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        var tag = source.Tag?.Trim() ?? string.Empty;
        if (tag.Length == 0)
        {
            run = new Run(string.Empty);
            return false;
        }

        var instruction = $" CITATION {QuoteFieldArgument(tag)} ";
        var cached = FormatInText(document, source, style);
        run = Run.ComplexFieldRun(instruction, cached, showCode: false, formatting);
        return true;
    }

    /// <summary>
    /// Resolves a <c>CITATION tag</c> field against the document's current source list and active style.
    /// Missing/deleted sources keep their cached display text, matching Word's dangling reference behavior.
    /// </summary>
    public static string ResolveCitationField(TextDocument document, ComplexField field, string cached)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);

        var tag = ComplexFieldEngine.Argument(field.Instruction).Trim();
        if (tag.Length == 0)
            return cached;

        var source = document.Sources.FirstOrDefault(s =>
            string.Equals(s.Tag?.Trim() ?? string.Empty, tag, StringComparison.Ordinal));
        return source is null
            ? cached
            : FormatInText(document, source, document.BibliographyStyle);
    }

    private static string QuoteFieldArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

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

        var author = FormatInTextAuthor(ResponsibleName(source));
        var year = source.Year?.Trim() ?? string.Empty;

        string inner;

        // Numeric styles — IEEE and Vancouver — use bracketed reference numbers when cited by position;
        // without a known position, bracket the author or tag as a placeholder.
        if (IsNumericStyle(style))
        {
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
            // Chicago and Turabian (author–date): (Author Year) — space between.
            // APA, Harvard, GOST, ISO-690: (Author, Year) — comma between.
            inner = style is CitationStyle.Chicago or CitationStyle.Turabian
                ? $"{author} {year}"
                : $"{author}, {year}";
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

    private static string FormatInTextAuthor(string? author)
    {
        var trimmed = author?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var authors = trimmed.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (authors.Length == 0)
            return string.Empty;

        var familyNames = new List<string>(authors.Length);
        foreach (var value in authors)
        {
            if (!TryGetPersonalFamilyName(value, out var familyName))
                return trimmed;

            familyNames.Add(familyName);
        }

        return familyNames.Count switch
        {
            1 => familyNames[0],
            2 => $"{familyNames[0]} & {familyNames[1]}",
            _ => $"{familyNames[0]} et al."
        };
    }

    private static bool TryGetPersonalFamilyName(string author, out string familyName)
    {
        familyName = string.Empty;
        var trimmed = author.Trim();
        if (trimmed.Length == 0 || LooksCorporateOrAmbiguous(trimmed))
            return false;

        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex >= 0)
        {
            var commaFamilyName = trimmed[..commaIndex].Trim();
            if (!IsFamilyNameCandidate(commaFamilyName))
                return false;

            familyName = TrimOuterNamePunctuation(commaFamilyName);
            return true;
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            if (!IsFamilyNameCandidate(tokens[0]))
                return false;

            familyName = TrimOuterNamePunctuation(tokens[0]);
            return true;
        }

        if (tokens.Length > 5)
            return false;

        var familyTokenIndex = tokens.Length - 1;
        if (IsNameSuffix(tokens[familyTokenIndex]) && familyTokenIndex > 0)
            familyTokenIndex--;

        if (!IsFamilyNameCandidate(tokens[familyTokenIndex]))
            return false;

        for (var i = 0; i < familyTokenIndex; i++)
        {
            if (!IsGivenNameToken(tokens[i]))
                return false;
        }

        familyName = TrimOuterNamePunctuation(tokens[familyTokenIndex]);
        return true;
    }

    private static bool LooksCorporateOrAmbiguous(string author)
    {
        if (author.Contains('&', StringComparison.Ordinal) ||
            author.Contains('/', StringComparison.Ordinal) ||
            author.Any(char.IsDigit))
        {
            return true;
        }

        foreach (var token in SplitNameWords(author))
        {
            if (CorporateAuthorTerms.Contains(token) || token.Equals("and", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsFamilyNameCandidate(string value)
    {
        var trimmed = TrimOuterNamePunctuation(value);
        if (trimmed.Length == 0)
            return false;

        foreach (var token in SplitNameWords(trimmed))
        {
            if (CorporateAuthorTerms.Contains(token) || !IsNameWordToken(token, allowInitial: false))
                return false;
        }

        return true;
    }

    private static bool IsGivenNameToken(string value)
    {
        var trimmed = TrimOuterNamePunctuation(value);
        return trimmed.Length > 0 &&
            !CorporateAuthorTerms.Contains(trimmed) &&
            IsNameWordToken(trimmed, allowInitial: true);
    }

    private static bool IsNameWordToken(string value, bool allowInitial)
    {
        var sawLetter = false;
        var sawLowercase = false;
        foreach (var c in value)
        {
            if (char.IsLetter(c))
            {
                sawLetter = true;
                sawLowercase |= char.IsLower(c);
                continue;
            }

            if (c is '-' or '\'' || allowInitial && c == '.')
                continue;

            return false;
        }

        if (!sawLetter)
            return false;

        return allowInitial || value.Length == 1 || sawLowercase || !value.All(c => !char.IsLetter(c) || char.IsUpper(c));
    }

    private static bool IsNameSuffix(string value)
    {
        var trimmed = TrimOuterNamePunctuation(value);
        return NameSuffixes.Contains(trimmed);
    }

    private static IEnumerable<string> SplitNameWords(string value) =>
        value.Split(NameWordSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string TrimOuterNamePunctuation(string value) =>
        value.Trim().Trim('.', ',');

    private static readonly char[] NameWordSeparators =
    [
        ' ', '\t', '\r', '\n', '.', ',', ';', ':', '-', '_', '/', '\\', '&', '(', ')', '[', ']'
    ];

    private static readonly HashSet<string> CorporateAuthorTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "academy",
        "administration",
        "agency",
        "association",
        "bank",
        "board",
        "bureau",
        "center",
        "centre",
        "college",
        "committee",
        "company",
        "corp",
        "corporation",
        "council",
        "department",
        "division",
        "federal",
        "foundation",
        "fund",
        "government",
        "group",
        "health",
        "inc",
        "institute",
        "international",
        "laboratory",
        "labs",
        "library",
        "ltd",
        "llc",
        "ministry",
        "museum",
        "national",
        "nations",
        "office",
        "organization",
        "organisation",
        "press",
        "project",
        "research",
        "school",
        "sciences",
        "society",
        "staff",
        "team",
        "university",
        "world"
    };

    private static readonly HashSet<string> NameSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr",
        "sr",
        "ii",
        "iii",
        "iv"
    };

    /// <summary>
    /// Formats a numbered in-text citation marker. For <see cref="CitationStyle.Ieee"/> this is the
    /// bracketed reference number <c>[n]</c> (IEEE numbers references in the order they appear); for the
    /// author–date styles, which do not number their citations, it returns an empty string so callers can
    /// fall back to <see cref="FormatInText(Source, CitationStyle)"/>. <paramref name="number"/> is 1-based.
    /// </summary>
    public static string FormatInText(int number, CitationStyle style) =>
        IsNumericStyle(style) ? $"[{number}]" : string.Empty;

    /// <summary>
    /// Returns the 1-based numeric reference number for <paramref name="source"/> in
    /// <paramref name="document"/>'s source list, or <c>0</c> when the source is not present. Identity is
    /// preferred, then the Word-facing tag, then exact trimmed field equality for cloned untagged sources.
    /// </summary>
    public static int ReferenceNumberFor(TextDocument document, Source source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        for (var i = 0; i < document.Sources.Count; i++)
        {
            if (ReferenceEquals(document.Sources[i], source))
                return i + 1;
        }

        var tag = source.Tag?.Trim() ?? string.Empty;
        if (tag.Length > 0)
        {
            for (var i = 0; i < document.Sources.Count; i++)
            {
                if (string.Equals(document.Sources[i].Tag?.Trim(), tag, StringComparison.Ordinal))
                    return i + 1;
            }

            return 0;
        }

        for (var i = 0; i < document.Sources.Count; i++)
        {
            if (SameSource(document.Sources[i], source))
                return i + 1;
        }

        return 0;
    }

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
    /// journal/volume/issue/pages, a <see cref="SourceType.ConferenceProceedings"/> its conference
    /// name/pages, a <see cref="SourceType.WebSite"/> its URL/accessed date, a <see cref="SourceType.Book"/>
    /// its publisher):
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
            CitationStyle.Vancouver => FormatVancouverEntry(source),
            CitationStyle.Harvard => FormatHarvardEntry(source),
            CitationStyle.Gost => FormatGostEntry(source),
            CitationStyle.Iso690 => FormatIso690Entry(source),
            // Turabian author–date bibliography is identical to Chicago's ordering.
            _ => FormatAuthorTitlePublisherYearEntry(source),
        };
    }

    /// <summary>
    /// Formats a numeric-style bibliography/reference-list entry by prefixing the source's assigned
    /// 1-based reference number. Non-numeric styles return the ordinary unnumbered entry.
    /// </summary>
    public static string FormatBibliographyEntry(Source source, CitationStyle style, int number)
    {
        ArgumentNullException.ThrowIfNull(source);

        var entry = FormatBibliographyEntry(source, style);
        if (!IsNumericStyle(style))
            return entry;

        var prefix = ReferenceListNumber(number, style);
        return entry.Length == 0 ? prefix : $"{prefix} {entry}";
    }

    // The type-specific "source detail" common to several styles, comma-joined:
    //  - Book:           Editor/translator roles, Publisher
    //  - BookSection:    BookTitle, editor/translator roles, ChapterNumber, Pages, City: Publisher
    //  - JournalArticle / ArticleInPeriodical: Journal, Volume, "no. Issue", "pp. Pages"
    //  - ConferenceProceedings: ConferenceName, "pp. Pages", City: Publisher
    //  - WebSite / ElectronicSource: Publisher, Url, "accessed AccessedDate"
    //  - Report:         Institution, City, Publisher
    //  - Patent:         patent number, jurisdiction, date
    //  - Case:           case number, court, reporter, jurisdiction, date
    //  - Interview:      interviewer, medium, date
    //  - Misc:           source kind, medium, date
    //  - Film:           producer/writer/performer, production company, medium
    //  - SoundRecording: album/contributors, recording number, medium
    //  - Art:            medium, institution, city
    //  - Performance:    conductor, theater, city, medium, date
    // Returns an empty list when nothing applies so callers can drop the segment entirely.
    private static List<string> SourceDetail(Source source)
    {
        var parts = new List<string>(4);
        switch (source.Type)
        {
            case SourceType.JournalArticle:
            case SourceType.ArticleInPeriodical:
                AddIfPresent(parts, source.Journal);
                if (NonEmpty(source.Volume) is { } vol)
                    parts.Add($"vol. {vol}");
                if (NonEmpty(source.Issue) is { } issue)
                    parts.Add($"no. {issue}");
                if (NonEmpty(source.Pages) is { } pages)
                    parts.Add($"pp. {pages}");
                break;
            case SourceType.WebSite:
            case SourceType.ElectronicSource:
            case SourceType.InternetSite:
                AddIfPresent(parts, source.Publisher);
                AddIfPresent(parts, source.Url);
                if (AccessedDateText(source) is { } accessed)
                    parts.Add($"accessed {accessed}");
                break;
            case SourceType.Report:
                AddIfPresent(parts, source.Institution);
                AddIfPresent(parts, source.City);
                AddIfPresent(parts, source.Publisher);
                break;
            case SourceType.Patent:
                if (NonEmpty(source.PatentNumber) is { } patentNumber)
                    parts.Add($"patent {patentNumber}");
                AddIfPresent(parts, source.CountryRegion);
                AddIfPresent(parts, source.StateProvince);
                AddIfPresent(parts, SourceDateText(source));
                break;
            case SourceType.Case:
                if (NonEmpty(source.CaseNumber) is { } caseNumber)
                    parts.Add($"case no. {caseNumber}");
                AddIfPresent(parts, source.Court);
                AddIfPresent(parts, source.Reporter);
                AddIfPresent(parts, source.CountryRegion);
                AddIfPresent(parts, source.StateProvince);
                AddIfPresent(parts, source.City);
                AddIfPresent(parts, SourceDateText(source));
                break;
            case SourceType.Interview:
                if (NonEmpty(source.Interviewer) is { } interviewer)
                    parts.Add($"interview by {interviewer}");
                AddIfPresent(parts, source.Medium);
                AddIfPresent(parts, SourceDateText(source));
                break;
            case SourceType.Misc:
                AddIfPresent(parts, source.SourceKind);
                AddIfPresent(parts, source.Medium);
                AddIfPresent(parts, SourceDateText(source));
                break;
            case SourceType.Film:
                if (NonEmpty(source.ProducerName) is { } producer)
                    parts.Add($"produced by {producer}");
                if (NonEmpty(source.Writer) is { } writer)
                    parts.Add($"written by {writer}");
                if (NonEmpty(source.Performer) is { } performer)
                    parts.Add($"performed by {performer}");
                AddIfPresent(parts, source.ProductionCompany);
                AddIfPresent(parts, source.Medium);
                break;
            case SourceType.SoundRecording:
                AddIfPresent(parts, source.AlbumTitle);
                if (NonEmpty(source.Composer) is { } composer)
                    parts.Add($"composed by {composer}");
                if (NonEmpty(source.Conductor) is { } conductor)
                    parts.Add($"conducted by {conductor}");
                if (NonEmpty(source.Performer) is { } recordingPerformer)
                    parts.Add($"performed by {recordingPerformer}");
                if (NonEmpty(source.ProducerName) is { } recordingProducer)
                    parts.Add($"produced by {recordingProducer}");
                if (NonEmpty(source.RecordingNumber) is { } recordingNumber)
                    parts.Add($"recording {recordingNumber}");
                AddIfPresent(parts, source.Medium);
                break;
            case SourceType.Art:
                AddIfPresent(parts, source.Medium);
                AddIfPresent(parts, source.Institution);
                AddIfPresent(parts, source.City);
                break;
            case SourceType.Performance:
                if (NonEmpty(source.Conductor) is { } performanceConductor)
                    parts.Add($"conducted by {performanceConductor}");
                AddIfPresent(parts, source.Theater);
                AddIfPresent(parts, source.City);
                AddIfPresent(parts, source.Medium);
                AddIfPresent(parts, SourceDateText(source));
                break;
            case SourceType.BookSection:
                AddIfPresent(parts, source.BookTitle);
                AddContributorRoleSegments(parts, source);
                if (NonEmpty(source.ChapterNumber) is { } chapterNumber)
                    parts.Add($"chap. {chapterNumber}");
                if (NonEmpty(source.Pages) is { } bookPages)
                    parts.Add($"pp. {bookPages}");
                if (PlacePublisher(source) is { } placePublisher)
                    parts.Add(placePublisher);
                break;
            case SourceType.ConferenceProceedings:
                AddIfPresent(parts, source.ConferenceName);
                if (NonEmpty(source.Pages) is { } proceedingsPages)
                    parts.Add($"pp. {proceedingsPages}");
                if (PlacePublisher(source) is { } proceedingsPlacePublisher)
                    parts.Add(proceedingsPlacePublisher);
                break;
            default: // Book
                AddContributorRoleSegments(parts, source);
                AddIfPresent(parts, source.Publisher);
                break;
        }

        return parts;
    }

    private static string? AccessedDateText(Source source)
    {
        var structured = new[]
            {
                NonEmpty(source.AccessedDay),
                NonEmpty(source.AccessedMonth),
                NonEmpty(source.AccessedYear)
            }
            .Where(part => part is not null)
            .ToArray();

        return structured.Length > 0 ? string.Join(" ", structured) : NonEmpty(source.Accessed);
    }

    private static string? SourceDateText(Source source)
    {
        var structured = new[]
            {
                NonEmpty(source.Day),
                NonEmpty(source.Month),
                NonEmpty(source.Year)
            }
            .Where(part => part is not null)
            .ToArray();

        return structured.Length > 1 ? string.Join(" ", structured) : null;
    }

    private static string ResponsibleName(Source source) =>
        source.Type switch
        {
            SourceType.Patent when NonEmpty(source.Inventor) is { } inventor => inventor,
            SourceType.Interview when NonEmpty(source.Interviewee) is { } interviewee => interviewee,
            SourceType.Film when NonEmpty(source.Director) is { } director => director,
            SourceType.SoundRecording when NonEmpty(source.Artist) is { } artist => artist,
            SourceType.SoundRecording when NonEmpty(source.Performer) is { } performer => performer,
            SourceType.SoundRecording when NonEmpty(source.Composer) is { } composer => composer,
            SourceType.Art when NonEmpty(source.Artist) is { } artArtist => artArtist,
            SourceType.Performance when NonEmpty(source.Performer) is { } performancePerformer => performancePerformer,
            _ => source.Author?.Trim() ?? string.Empty,
        };

    // APA: Author. (Year). Title. <detail>.
    private static string FormatApaEntry(Source source)
    {
        var segments = new List<string>(4);

        var author = ResponsibleName(source);
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

        var author = ResponsibleName(source);
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

        var author = ResponsibleName(source);
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

    // Vancouver: [N]. Author. Title. Journal. Year;Volume(Issue):Pages.  /  book: Author. Title. City: Publisher; Year.
    // Vancouver is primarily numeric; without a reference number the author is the lead segment.
    // The core NLM style: Author. Title. Journal Year;Vol(Issue):Pages.
    private static string FormatVancouverEntry(Source source)
    {
        var segments = new List<string>(5);

        var author = ResponsibleName(source);
        if (author.Length > 0)
            segments.Add(WithPeriod(author));

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        // Type-specific: journal uses condensed Vancouver citation string; book sections and conference papers
        // name their containing publication context.
        if (IsPeriodicalSource(source.Type))
        {
            // Build: Journal. Year;Vol(Issue):Pages.
            var journalParts = new List<string>(2);
            if (NonEmpty(source.Journal) is { } j)
                journalParts.Add(j);
            var yearVolIssuePage = new System.Text.StringBuilder();
            if (NonEmpty(source.Year) is { } y)
                yearVolIssuePage.Append(y);
            if (NonEmpty(source.Volume) is { } vol)
                yearVolIssuePage.Append(';').Append(vol);
            if (NonEmpty(source.Issue) is { } iss)
                yearVolIssuePage.Append('(').Append(iss).Append(')');
            if (NonEmpty(source.Pages) is { } pg)
                yearVolIssuePage.Append(':').Append(pg);
            if (yearVolIssuePage.Length > 0)
                journalParts.Add(yearVolIssuePage.ToString());
            if (journalParts.Count > 0)
                segments.Add(WithPeriod(string.Join(". ", journalParts)));
        }
        else if (source.Type == SourceType.BookSection)
        {
            AddIfPresent(segments, source.BookTitle);
            AddContributorRoleSegments(segments, source, terminate: true);
            if (NonEmpty(source.ChapterNumber) is { } chapterNumber)
                segments.Add($"chap. {chapterNumber}.");
            if (NonEmpty(source.Pages) is { } pages)
                segments.Add($"pp. {pages}.");

            var tail = new List<string>(2);
            if (PlacePublisher(source) is { } placePublisher)
                tail.Add(placePublisher);
            AddIfPresent(tail, source.Year);
            if (tail.Count > 0)
                segments.Add(WithPeriod(string.Join("; ", tail)));
        }
        else if (source.Type == SourceType.ConferenceProceedings)
        {
            AddIfPresent(segments, source.ConferenceName);
            if (NonEmpty(source.Pages) is { } pages)
                segments.Add($"pp. {pages}.");

            var tail = new List<string>(2);
            if (PlacePublisher(source) is { } placePublisher)
                tail.Add(placePublisher);
            AddIfPresent(tail, source.Year);
            if (tail.Count > 0)
                segments.Add(WithPeriod(string.Join("; ", tail)));
        }
        else
        {
            // Book / website / electronic source: Publisher; Year. Books use City: Publisher when present.
            var tail = new List<string>(4);
            if (source.Type == SourceType.Book)
            {
                AddContributorRoleSegments(segments, source, terminate: true);
                if (PlacePublisher(source) is { } placePublisher)
                    tail.Add(placePublisher);
            }
            else if (IsElectronicSource(source.Type))
                tail.AddRange(SourceDetail(source));
            else
                AddIfPresent(tail, source.Publisher);
            AddIfPresent(tail, source.Year);
            if (tail.Count > 0)
                segments.Add(WithPeriod(string.Join("; ", tail)));
        }

        return string.Join(" ", segments);
    }

    // Harvard: Author Year, Title, Publisher.
    // In-text: (Author, Year) — identical to APA. Bibliography year comes directly after the author.
    private static string FormatHarvardEntry(Source source)
    {
        var segments = new List<string>(4);

        var author = ResponsibleName(source);
        var year = source.Year?.Trim() ?? string.Empty;

        // Author Year combined: "Author Year," or just "Author." or just "Year," etc.
        if (author.Length > 0 && year.Length > 0)
            segments.Add($"{author} {year},");
        else if (author.Length > 0)
            segments.Add(WithPeriod(author));
        else if (year.Length > 0)
            segments.Add($"{year},");

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        var detail = string.Join(", ", SourceDetail(source));
        if (detail.Length > 0)
            segments.Add(WithPeriod(detail));

        return string.Join(" ", segments);
    }

    // GOST R 7.0.5-2008: Author. Title. City: Publisher, Year. — pages/journal come after publisher.
    // For journal articles: Author. Title. Journal. Year. Vol. Issue. Pp. Pages.
    private static string FormatGostEntry(Source source)
    {
        var segments = new List<string>(5);

        var author = ResponsibleName(source);
        if (author.Length > 0)
            segments.Add(WithPeriod(author));

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        if (IsPeriodicalSource(source.Type))
        {
            // Journal. Year. Vol. Volume. Issue. Pages.
            AddIfPresent(segments, source.Journal);
            var year = source.Year?.Trim() ?? string.Empty;
            if (year.Length > 0)
                segments.Add(year + ".");
            if (NonEmpty(source.Volume) is { } vol)
                segments.Add($"Vol. {vol}.");
            if (NonEmpty(source.Issue) is { } iss)
                segments.Add($"No. {iss}.");
            if (NonEmpty(source.Pages) is { } pg)
                segments.Add($"Pp. {pg}.");
        }
        else
        {
            if (source.Type == SourceType.BookSection)
            {
                AddIfPresent(segments, source.BookTitle);
                AddContributorRoleSegments(segments, source, terminate: true);
                if (NonEmpty(source.ChapterNumber) is { } chapterNumber)
                    segments.Add($"Chap. {chapterNumber}.");
                if (NonEmpty(source.Pages) is { } pages)
                    segments.Add($"Pp. {pages}.");
            }
            else if (source.Type == SourceType.ConferenceProceedings)
            {
                AddIfPresent(segments, source.ConferenceName);
                if (NonEmpty(source.Pages) is { } pages)
                    segments.Add($"Pp. {pages}.");
            }
            else if (source.Type == SourceType.ElectronicSource)
            {
                var detail = string.Join(", ", SourceDetail(source));
                if (detail.Length > 0)
                    segments.Add(WithPeriod(detail));
            }
            else if (source.Type == SourceType.Book)
            {
                AddContributorRoleSegments(segments, source, terminate: true);
            }

            // City: Publisher, Year.
            var publisher = PlacePublisher(source) ?? (source.Publisher?.Trim() ?? string.Empty);
            var year = source.Year?.Trim() ?? string.Empty;
            if (source.Type == SourceType.ElectronicSource && year.Length > 0)
                segments.Add($"{year}.");
            else if (publisher.Length > 0 && year.Length > 0)
                segments.Add($"{publisher}, {year}.");
            else if (publisher.Length > 0)
                segments.Add(WithPeriod(publisher));
            else if (year.Length > 0)
                segments.Add($"{year}.");
        }

        return string.Join(" ", segments);
    }

    // ISO 690: AUTHOR, Year. Title. Place: Publisher.
    // Family name in SMALL CAPS; FreeW approximates with ALL-CAPS on the author segment.
    // For journal articles: AUTHOR, Year. Title. Journal, Volume(Issue), pages.
    private static string FormatIso690Entry(Source source)
    {
        var segments = new List<string>(5);

        var author = ResponsibleName(source);
        var year = source.Year?.Trim() ?? string.Empty;

        // Author in ALL-CAPS + year: "AUTHOR, Year."
        if (author.Length > 0 && year.Length > 0)
            segments.Add($"{author.ToUpperInvariant()}, {year}.");
        else if (author.Length > 0)
            segments.Add(WithPeriod(author.ToUpperInvariant()));
        else if (year.Length > 0)
            segments.Add($"{year}.");

        var title = source.Title?.Trim() ?? string.Empty;
        if (title.Length > 0)
            segments.Add(WithPeriod(title));

        if (IsPeriodicalSource(source.Type))
        {
            // Journal, Volume(Issue), Pages.
            var detailParts = new List<string>(3);
            AddIfPresent(detailParts, source.Journal);
            if (NonEmpty(source.Volume) is { } vol)
            {
                var volStr = vol;
                if (NonEmpty(source.Issue) is { } iss)
                    volStr = vol + $"({iss})";
                detailParts.Add(volStr);
            }
            AddIfPresent(detailParts, source.Pages);
            if (detailParts.Count > 0)
                segments.Add(WithPeriod(string.Join(", ", detailParts)));
        }
        else
        {
            if (source.Type == SourceType.BookSection)
            {
                AddIfPresent(segments, source.BookTitle);
                AddContributorRoleSegments(segments, source, terminate: true);
                if (NonEmpty(source.ChapterNumber) is { } chapterNumber)
                    segments.Add($"chap. {chapterNumber}.");
                if (NonEmpty(source.Pages) is { } pages)
                    segments.Add($"pp. {pages}.");
            }
            else if (source.Type == SourceType.ConferenceProceedings)
            {
                AddIfPresent(segments, source.ConferenceName);
                if (NonEmpty(source.Pages) is { } pages)
                    segments.Add($"pp. {pages}.");
            }
            else if (source.Type == SourceType.ElectronicSource)
            {
                var detail = string.Join(", ", SourceDetail(source));
                if (detail.Length > 0)
                    segments.Add(WithPeriod(detail));
            }
            else if (source.Type == SourceType.Book)
            {
                AddContributorRoleSegments(segments, source, terminate: true);
            }

            var publisher = PlacePublisher(source) ?? (source.Publisher?.Trim() ?? string.Empty);
            if (source.Type != SourceType.ElectronicSource && publisher.Length > 0)
                segments.Add(WithPeriod(publisher));
        }

        return string.Join(" ", segments);
    }

    private static string? NonEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? PlacePublisher(Source source)
    {
        if (source.Type is not (SourceType.Book or SourceType.BookSection or SourceType.ConferenceProceedings))
            return null;

        var city = NonEmpty(source.City);
        var publisher = NonEmpty(source.Publisher);
        return (city, publisher) switch
        {
            ({ } c, { } p) => $"{c}: {p}",
            ({ } c, null) => c,
            (null, { } p) => p,
            _ => null,
        };
    }

    private static bool IsPeriodicalSource(SourceType type) =>
        type is SourceType.JournalArticle or SourceType.ArticleInPeriodical;

    private static bool IsElectronicSource(SourceType type) =>
        type is SourceType.WebSite or SourceType.ElectronicSource or SourceType.InternetSite;

    private static void AddContributorRoleSegments(List<string> parts, Source source, bool terminate = false)
    {
        AddContributorRoleSegment(parts, "Ed.", source.Editors, terminate);
        AddContributorRoleSegment(parts, "Trans.", source.Translators, terminate);
    }

    private static void AddContributorRoleSegment(
        List<string> parts,
        string label,
        IEnumerable<SourceAuthorPerson>? people,
        bool terminate)
    {
        var names = people is null ? string.Empty : SourceAuthorPerson.FormatDisplayText(people).Trim();
        if (names.Length == 0)
            return;

        var segment = $"{label} {names}";
        parts.Add(terminate ? WithPeriod(segment) : segment);
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
    /// <paramref name="style"/>. Numeric styles keep source-list order and prefix entries with the assigned
    /// reference number; other styles sort by author (case-insensitive, ordinal), then by title and tag as
    /// stable tie-breakers. The entry paragraphs are owned by a native multi-paragraph
    /// <c>BIBLIOGRAPHY \l 1033</c> field; a document with no sources yields Word's native empty result after
    /// the heading. Deterministic and side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildBibliography(TextDocument document, CitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingTextFor(style)) { StyleId = HeadingStyleId }
        };

        if (IsNumericStyle(style))
        {
            for (var i = 0; i < document.Sources.Count; i++)
            {
                paragraphs.Add(
                    new Paragraph(FormatBibliographyEntry(document.Sources[i], style, i + 1))
                    {
                        StyleId = EntryStyleId
                    });
            }

            return AttachNativeBibliographyField(paragraphs);
        }

        var ordered = document.Sources
            .OrderBy(ResponsibleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Title?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Tag?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var source in ordered)
            paragraphs.Add(new Paragraph(FormatBibliographyEntry(source, style)) { StyleId = EntryStyleId });

        return AttachNativeBibliographyField(paragraphs);
    }

    private static IReadOnlyList<Paragraph> AttachNativeBibliographyField(List<Paragraph> paragraphs)
    {
        if (paragraphs.Count == 1)
        {
            var empty = new Paragraph { StyleId = EntryStyleId };
            empty.Runs.Add(Run.ComplexFieldRun(NativeFieldInstruction, EmptyResultText));
            paragraphs.Add(empty);
            return paragraphs;
        }

        var field = new ComplexField(NativeFieldInstruction);
        for (var index = 1; index < paragraphs.Count; index++)
            paragraphs[index].SpanningFieldOwner = field;
        paragraphs[1].SpanningFieldStart = field;
        paragraphs[^1].EndsSpanningField = true;
        return paragraphs;
    }

    private static bool IsNumericStyle(CitationStyle style) =>
        style is CitationStyle.Ieee or CitationStyle.Vancouver;

    private static string ReferenceListNumber(int number, CitationStyle style) =>
        style == CitationStyle.Vancouver ? $"{number}." : FormatInText(number, style);

    internal static bool SameSource(Source left, Source right) =>
        left.Type == right.Type
        && Same(left.Author, right.Author)
        && PeopleEqual(left.PersonalAuthors, right.PersonalAuthors)
        && Same(left.CorporateAuthor, right.CorporateAuthor)
        && PeopleEqual(left.Editors, right.Editors)
        && PeopleEqual(left.Translators, right.Translators)
        && Same(left.Title, right.Title)
        && Same(left.BookTitle, right.BookTitle)
        && Same(left.ConferenceName, right.ConferenceName)
        && Same(left.Inventor, right.Inventor)
        && Same(left.Interviewee, right.Interviewee)
        && Same(left.Interviewer, right.Interviewer)
        && Same(left.Artist, right.Artist)
        && Same(left.Composer, right.Composer)
        && Same(left.Conductor, right.Conductor)
        && Same(left.Director, right.Director)
        && Same(left.Performer, right.Performer)
        && Same(left.ProducerName, right.ProducerName)
        && Same(left.Writer, right.Writer)
        && Same(left.Year, right.Year)
        && Same(left.Month, right.Month)
        && Same(left.Day, right.Day)
        && Same(left.Institution, right.Institution)
        && Same(left.Publisher, right.Publisher)
        && Same(left.City, right.City)
        && Same(left.Edition, right.Edition)
        && Same(left.StandardNumber, right.StandardNumber)
        && Same(left.ChapterNumber, right.ChapterNumber)
        && Same(left.PatentNumber, right.PatentNumber)
        && Same(left.CaseNumber, right.CaseNumber)
        && Same(left.Court, right.Court)
        && Same(left.Reporter, right.Reporter)
        && Same(left.CountryRegion, right.CountryRegion)
        && Same(left.StateProvince, right.StateProvince)
        && Same(left.Medium, right.Medium)
        && Same(left.SourceKind, right.SourceKind)
        && Same(left.AlbumTitle, right.AlbumTitle)
        && Same(left.ProductionCompany, right.ProductionCompany)
        && Same(left.RecordingNumber, right.RecordingNumber)
        && Same(left.Theater, right.Theater)
        && Same(left.ShortTitle, right.ShortTitle)
        && Same(left.Comments, right.Comments)
        && Same(left.Journal, right.Journal)
        && Same(left.Volume, right.Volume)
        && Same(left.Issue, right.Issue)
        && Same(left.Pages, right.Pages)
        && Same(left.Url, right.Url)
        && Same(left.Accessed, right.Accessed)
        && Same(left.AccessedDay, right.AccessedDay)
        && Same(left.AccessedMonth, right.AccessedMonth)
        && Same(left.AccessedYear, right.AccessedYear);

    private static bool PeopleEqual(
        IReadOnlyList<SourceAuthorPerson> left,
        IReadOnlyList<SourceAuthorPerson> right) =>
        left.SequenceEqual(right);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.Ordinal);

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
