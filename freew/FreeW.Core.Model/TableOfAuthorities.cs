namespace FreeW.Core.Model;

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
    /// Builds the Table of Authorities paragraphs for <paramref name="document"/>: a "Table of Authorities"
    /// heading (<see cref="HeadingStyleId"/>) followed, per non-empty category in Word's display order, by a
    /// category heading (<see cref="CategoryStyleId"/>) and the distinct long-form citations of that
    /// category sorted alphabetically (case-insensitive) with duplicates collapsed. A document with no
    /// marked citations yields just the heading paragraph. Deterministic and side-effect free — it never
    /// mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(CollectCitations(document));
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
    /// Builds the Table of Authorities paragraphs from an arbitrary set of <paramref name="citations"/>:
    /// a heading, then for each non-empty category (in Word's display order) a category heading and the
    /// distinct, non-blank long citations sorted alphabetically (case-insensitive, ordinal tie-breaker so
    /// the order is deterministic), one per paragraph. Citations with a blank long form are skipped. An
    /// empty set yields just the heading.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(IEnumerable<Citation> citations)
    {
        ArgumentNullException.ThrowIfNull(citations);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var byCategory = citations
            .Where(c => c is not null && c.LongCitation.Length > 0)
            .GroupBy(c => c.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var category in CategoryOrder)
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
                paragraphs.Add(new Paragraph(entry) { StyleId = EntryStyleId });
        }

        return paragraphs;
    }

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
