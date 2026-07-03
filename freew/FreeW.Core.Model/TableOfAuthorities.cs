namespace FreeW.Core.Model;

/// <summary>
/// Word's tab-leader style for the Table of Authorities (the fill character between the citation text and
/// its page number). The numeric values are stable so a chosen leader can be persisted; <see cref="Dots"/>
/// is the default (value 0), matching Word's default ToA leader.
/// </summary>
public enum ToaTabLeader
{
    /// <summary>Dotted leader "…………………………… 1" (Word's default).</summary>
    Dots = 0,
    /// <summary>Dashed leader "—————————————— 1".</summary>
    Dashes = 1,
    /// <summary>Solid underline leader "______________ 1".</summary>
    Underline = 2,
    /// <summary>No leader character between citation and page number.</summary>
    None = 3
}

/// <summary>
/// Configuration options for <see cref="TableOfAuthorities.Build(TextDocument, ToaOptions)"/> that mirror
/// the Word "Table of Authorities" dialog's expansion controls:
/// <list type="bullet">
/// <item><b>UsePassim</b> — replace five-or-more page occurrences of a citation with the Latin word
/// <c>passim</c> instead of listing each page number individually.</item>
/// <item><b>KeepOriginalFormatting</b> — copy the source run's character formatting into the generated
/// entry paragraph rather than letting the entry style govern it entirely.</item>
/// <item><b>CategoryFilter</b> — when set to a non-null value, emit only that one category; when null
/// (the default) all categories appear in Word's display order.</item>
/// <item><b>TabLeader</b> — the fill character used between the citation text and its page reference.
/// The current <see cref="TableOfAuthorities.Build"/> does not do live page numbers, so the leader
/// is stored on the entry paragraph as a <see cref="ParagraphFormatting.TabLeader"/> hint for the
/// renderer; the default is <see cref="ToaTabLeader.Dots"/>.</item>
/// </list>
/// All properties carry sensible defaults that reproduce the no-options behaviour so existing call sites
/// that pass no options remain unaffected.
/// </summary>
public sealed class ToaOptions
{
    /// <summary>
    /// When true, a citation that appears five or more times is listed as <c>passim</c> in the table
    /// instead of individual page numbers. FreeW currently has no live page-number engine, so <c>passim</c>
    /// is added as a suffix to the entry text (" passim") to signal the intent; the real page renderer can
    /// honour this flag when page numbers become available. Default: <c>false</c>.
    /// </summary>
    public bool UsePassim { get; init; }

    /// <summary>
    /// When true, the entry paragraph preserves the bold/italic/underline formatting of the first
    /// occurrence of that citation text in the document body; when false (the default) the entry inherits
    /// its formatting entirely from the <see cref="TableOfAuthorities.EntryStyleId"/> style.
    /// </summary>
    public bool KeepOriginalFormatting { get; init; }

    /// <summary>
    /// When set, only citations of this category are emitted; when <c>null</c> (the default) all
    /// categories appear in Word's display order.
    /// </summary>
    public CitationCategory? CategoryFilter { get; init; }

    /// <summary>
    /// The fill character between the citation text and its page reference. Stored as a hint on the
    /// generated entry paragraphs' <see cref="ParagraphFormatting.TabLeader"/>. Default:
    /// <see cref="ToaTabLeader.Dots"/>.
    /// </summary>
    public ToaTabLeader TabLeader { get; init; } = ToaTabLeader.Dots;

    /// <summary>The default options — no passim, no formatting carry, all categories, dotted leader.</summary>
    public static readonly ToaOptions Default = new();
}

/// <summary>
/// Pure, WPF-free generation of a Table of Authorities (Word's References &gt; Table of Authorities) from
/// the document's marked legal citations (see <see cref="TextDocument.Citations"/>). Lives in the model
/// project so it is unit-testable without any UI, mirroring <see cref="DocumentIndex"/>.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s — a "Table of Authorities"
/// heading followed, for each category that has citations, by a category heading (e.g. "Cases") and the
/// distinct long-form citations in that category sorted alphabetically (case-insensitive) with duplicates
/// collapsed. The paragraphs carry dedicated style ids (<see cref="HeadingStyleId"/>,
/// <see cref="CategoryStyleId"/> and <see cref="EntryStyleId"/>) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as normal styled paragraphs (no I/O changes needed); and</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted region via
/// <see cref="IsTableOfAuthoritiesParagraph"/>.</item>
/// </list>
/// </summary>
public static class TableOfAuthorities
{
    /// <summary>
    /// Default right-tab position for generated entries when the caller only supplies an enumerable of
    /// citations. It matches the writable width of Word's default letter page (8.5in page, 1in margins).
    /// </summary>
    public const double DefaultEntryRightTabStopPt = 468;

    /// <summary>Style id of the table's "Table of Authorities" heading paragraph.</summary>
    public const string HeadingStyleId = "TableOfAuthoritiesHeading";

    /// <summary>Display text of the table's heading paragraph.</summary>
    public const string HeadingText = "Table of Authorities";

    /// <summary>Style id carried by each category heading paragraph (e.g. "Cases").</summary>
    public const string CategoryStyleId = "TableOfAuthoritiesCategory";

    /// <summary>Style id carried by each generated citation entry paragraph.</summary>
    public const string EntryStyleId = "TableOfAuthoritiesEntry";

    /// <summary>The categories rendered, in Word's display order.</summary>
    private static readonly CitationCategory[] CategoryOrder =
    {
        CitationCategory.Cases,
        CitationCategory.Statutes,
        CitationCategory.OtherAuthorities,
        CitationCategory.Rules,
        CitationCategory.Treatises,
        CitationCategory.Regulations,
        CitationCategory.ConstitutionalProvisions
    };

    /// <summary>The human-readable heading text for each category.</summary>
    public static string CategoryHeading(CitationCategory category) => category switch
    {
        CitationCategory.Cases => "Cases",
        CitationCategory.Statutes => "Statutes",
        CitationCategory.OtherAuthorities => "Other Authorities",
        CitationCategory.Rules => "Rules",
        CitationCategory.Treatises => "Treatises",
        CitationCategory.Regulations => "Regulations",
        CitationCategory.ConstitutionalProvisions => "Constitutional Provisions",
        _ => "Other Authorities"
    };

    /// <summary>
    /// Builds the Table of Authorities paragraphs for <paramref name="document"/> using default options.
    /// See <see cref="Build(TextDocument, ToaOptions)"/> for the options-aware overload.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document, ToaOptions.Default);
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs for <paramref name="document"/> using the given
    /// <paramref name="options"/>: a "Table of Authorities" heading (<see cref="HeadingStyleId"/>) followed,
    /// per non-empty category (limited by <see cref="ToaOptions.CategoryFilter"/> when set) in Word's display
    /// order, by a category heading (<see cref="CategoryStyleId"/>) and the distinct long-form citations of
    /// that category sorted alphabetically with duplicates collapsed. When <see cref="ToaOptions.UsePassim"/>
    /// is true, a citation that appears five or more times in <paramref name="document"/> is annotated with
    /// " passim". Deterministic and side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(TextDocument document, ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var allCitations = CollectCitations(document);

        // When UsePassim, count per (long-form, category) pair so we know which get the suffix.
        Dictionary<(string Long, CitationCategory Cat), int>? occurrenceCounts = null;
        if (options.UsePassim)
        {
            occurrenceCounts = new Dictionary<(string, CitationCategory), int>(
                EqualityComparer<(string, CitationCategory)>.Default);
            foreach (var c in allCitations)
            {
                if (c.LongCitation.Length == 0)
                    continue;
                var key = (c.LongCitation, c.Category);
                occurrenceCounts.TryGetValue(key, out var count);
                occurrenceCounts[key] = count + 1;
            }
        }

        var formatting = options.KeepOriginalFormatting
            ? CollectFirstCitationFormatting(document)
            : null;

        return Build(
            allCitations,
            options,
            occurrenceCounts,
            EntryRightTabStopPt(document.Page),
            formatting);
    }

    /// <summary>
    /// Gathers every marked citation in <paramref name="document"/>: the hidden <c>TA</c> field marks in the
    /// body paragraphs (the durable record that round-trips through docx) plus any in the
    /// <see cref="TextDocument.Citations"/> side-store (newly marked but not yet serialised). Body marks come
    /// first so they win de-duplication. This keeps <see cref="Build"/> working both for a freshly edited
    /// document and one just reopened from disk.
    /// </summary>
    public static IReadOnlyList<Citation> CollectCitations(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var citations = new List<Citation>();
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;
            foreach (var run in paragraph.Runs)
                if (run.Citation is { } citation)
                    citations.Add(citation);
        }
        citations.AddRange(document.Citations);
        return citations;
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs from an arbitrary set of <paramref name="citations"/> using
    /// default options. See <see cref="Build(IEnumerable{Citation}, ToaOptions)"/> for the options-aware overload.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(IEnumerable<Citation> citations)
    {
        ArgumentNullException.ThrowIfNull(citations);
        return Build(
            citations,
            ToaOptions.Default,
            occurrenceCounts: null,
            DefaultEntryRightTabStopPt,
            sourceFormatting: null);
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs from an arbitrary set of <paramref name="citations"/> using
    /// the given <paramref name="options"/>: a heading, then for each non-empty category (filtered by
    /// <see cref="ToaOptions.CategoryFilter"/> when set) a category heading and the distinct, non-blank long
    /// citations sorted alphabetically, one per paragraph. When <see cref="ToaOptions.UsePassim"/> is true and
    /// an occurrence count of five or more is reached, the entry text gets a " passim" suffix. Citations with a
    /// blank long form are skipped. An empty/filtered set yields just the heading.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(IEnumerable<Citation> citations, ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(options);
        return Build(
            citations,
            options,
            occurrenceCounts: null,
            DefaultEntryRightTabStopPt,
            sourceFormatting: null);
    }

    // Core builder shared by all public overloads.
    private static IReadOnlyList<Paragraph> Build(
        IEnumerable<Citation> citations,
        ToaOptions options,
        Dictionary<(string Long, CitationCategory Cat), int>? occurrenceCounts,
        double entryRightTabStopPt,
        Dictionary<(string Long, CitationCategory Cat), RunFormatting>? sourceFormatting)
    {
        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var byCategory = citations
            .Where(c => c is not null && c.LongCitation.Length > 0)
            .GroupBy(c => c.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Respect the category filter: when set, only emit that one category.
        var categoriesToEmit = options.CategoryFilter.HasValue
            ? CategoryOrder.Where(cat => cat == options.CategoryFilter.Value)
            : CategoryOrder;

        foreach (var category in categoriesToEmit)
        {
            if (!byCategory.TryGetValue(category, out var inCategory))
                continue;

            var distinct = inCategory
                .Select(c => c.LongCitation)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t, StringComparer.Ordinal)
                .ToList();

            if (distinct.Count == 0)
                continue;

            paragraphs.Add(new Paragraph(CategoryHeading(category)) { StyleId = CategoryStyleId });
            foreach (var entry in distinct)
            {
                // UsePassim: if this long-form appears 5+ times, append " passim" as per Word's convention.
                var text = entry;
                if (options.UsePassim && occurrenceCounts is not null)
                {
                    occurrenceCounts.TryGetValue((entry, category), out var count);
                    if (count >= 5)
                        text = entry + " passim";
                }

                RunFormatting? runFormatting = null;
                sourceFormatting?.TryGetValue((entry, category), out runFormatting);
                paragraphs.Add(CreateEntryParagraph(text, options, entryRightTabStopPt, runFormatting));
            }
        }

        return paragraphs;
    }

    private static Paragraph CreateEntryParagraph(
        string text,
        ToaOptions options,
        double entryRightTabStopPt,
        RunFormatting? sourceFormatting)
    {
        var paragraph = new Paragraph(text)
        {
            StyleId = EntryStyleId,
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(
                        Math.Max(0, entryRightTabStopPt),
                        TabStopAlignment.Right,
                        ToTabLeader(options.TabLeader))
                ]
            }
        };

        if (sourceFormatting is not null && paragraph.Runs.Count > 0)
            paragraph.Runs[0].Formatting = sourceFormatting;

        return paragraph;
    }

    private static Dictionary<(string Long, CitationCategory Cat), RunFormatting> CollectFirstCitationFormatting(
        TextDocument document)
    {
        var formatting = new Dictionary<(string Long, CitationCategory Cat), RunFormatting>();
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.Citation is not { } citation || citation.LongCitation.Length == 0)
                    continue;

                formatting.TryAdd((citation.LongCitation, citation.Category), run.Formatting);
            }
        }

        return formatting;
    }

    private static double EntryRightTabStopPt(PageSettings page) =>
        Math.Max(0, page.WidthPt - page.MarginLeftPt - page.MarginRightPt);

    private static TabLeader ToTabLeader(ToaTabLeader leader) => leader switch
    {
        ToaTabLeader.Dashes => TabLeader.Dashes,
        ToaTabLeader.Underline => TabLeader.Underline,
        ToaTabLeader.None => TabLeader.None,
        _ => TabLeader.Dots
    };

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the Table of Authorities styles produced by
    /// <see cref="Build"/> (the heading, category, or entry style). Used to recognise a previously inserted
    /// region so a refresh can remove it.
    /// </summary>
    public static bool IsTableOfAuthoritiesStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, CategoryStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="block"/> is a paragraph carrying a Table of Authorities style.</summary>
    public static bool IsTableOfAuthoritiesParagraph(Block block) =>
        block is Paragraph paragraph && IsTableOfAuthoritiesStyleId(paragraph.StyleId);

    /// <summary>
    /// Registers the Table of Authorities styles in <paramref name="document"/>'s style catalog if they are
    /// not already present, so the inserted paragraphs resolve their formatting. Idempotent — existing
    /// styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "Table of Authorities Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(CategoryStyleId, new DocumentStyle
        {
            Id = CategoryStyleId,
            Name = "Table of Authorities Category",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 12, ColorHex = "#1F3864" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 8, SpaceAfterPt = 4 }
        });

        document.Styles.TryAdd(EntryStyleId, new DocumentStyle
        {
            Id = EntryStyleId,
            Name = "Table of Authorities Entry",
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }
}
