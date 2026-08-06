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
/// The leader is stored on the entry paragraph as a <see cref="ParagraphFormatting.TabLeader"/> hint for the
/// renderer; the default is <see cref="ToaTabLeader.Dots"/>.</item>
/// </list>
/// All properties carry sensible defaults that reproduce the no-options behaviour so existing call sites
/// that pass no options remain unaffected.
/// </summary>
public sealed class ToaOptions
{
    /// <summary>
    /// When true, a citation that appears five or more times is listed as <c>passim</c> in the table
    /// instead of individual page numbers. When the document builder can derive an explicit-break-based
    /// page-reference segment, <c>passim</c> is emitted in that segment; otherwise the legacy text suffix is
    /// kept so no caller receives invented page numbers. Default: <c>false</c>.
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
/// Page-reference evidence for a marked TOA citation. <paramref name="PageNumber"/> is a one-based physical
/// page number used for ordering and de-duplication; <paramref name="DisplayText"/> is the emitted TOA text.
/// </summary>
public readonly record struct ToaCitationPageReference(int PageNumber, string DisplayText);

/// <summary>
/// Resolves the rendered page reference for a citation mark at a top-level document block/run location.
/// Host layers answer from their pagination surfaces; shared TOA generation owns grouping and formatting.
/// </summary>
public delegate ToaCitationPageReference? ToaCitationPageResolver(
    TextDocument document,
    int blockIndex,
    int runIndex,
    Citation citation);

/// <summary>
/// Pure, WPF-free generation of a Table of Authorities (Word's References &gt; Table of Authorities) from
/// the document's marked legal citations (see <see cref="TextDocument.Citations"/>). Lives in the model
/// project so it is unit-testable without any UI, mirroring <see cref="DocumentIndex"/>.
/// <para>
/// <see cref="Build"/> produces a heading followed, for each used category, by a category heading and the
/// distinct long-form citations sorted alphabetically. Each category/result segment is owned by the same
/// native Word <c>TOA</c> field that Word emits for that category. The paragraphs also carry dedicated
/// style ids (<see cref="HeadingStyleId"/>, <see cref="CategoryStyleId"/> and <see cref="EntryStyleId"/>) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx with their native field ownership intact; and</item>
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

    private const int PassimOccurrenceThreshold = 5;

    private static readonly ToaEntryKeyComparer EntryKeyComparer = new();

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
    /// Builds the Table of Authorities paragraphs using default options plus host pagination evidence.
    /// Entries in <paramref name="blockPageAssignments"/> are zero-based page indexes for top-level
    /// <see cref="TextDocument.Blocks"/>; generated TOA text uses one-based Word page numbers.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        IReadOnlyList<int>? blockPageAssignments)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document, ToaOptions.Default, blockPageAssignments);
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs using default options plus host pagination evidence.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        ToaCitationPageResolver? pageResolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document, ToaOptions.Default, pageResolver);
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
        return Build(document, options, pageResolver: null);
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs for <paramref name="document"/> using the given
    /// <paramref name="options"/> and optional host pagination evidence. Entries in
    /// <paramref name="blockPageAssignments"/> are zero-based page indexes for top-level
    /// <see cref="TextDocument.Blocks"/>; generated TOA text uses one-based Word page numbers.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        ToaOptions options,
        IReadOnlyList<int>? blockPageAssignments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return Build(document, options, ToBlockPageAssignmentResolver(blockPageAssignments));
    }

    /// <summary>
    /// Builds the Table of Authorities paragraphs for <paramref name="document"/> using the given
    /// <paramref name="options"/> and optional host pagination evidence. When <paramref name="pageResolver"/>
    /// returns a page reference for a citation mark, that reference is used for the generated entry; otherwise
    /// the legacy explicit-break fallback is used.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        ToaOptions options,
        ToaCitationPageResolver? pageResolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var occurrences = CanonicalizeOccurrences(
            CollectCitationOccurrences(document, pageResolver));

        // When UsePassim, count per canonical (long-form, category) pair so short-form marks contribute
        // to the full authority entry they reference.
        Dictionary<ToaEntryKey, int>? occurrenceCounts = null;
        if (options.UsePassim)
            occurrenceCounts = BuildOccurrenceCounts(occurrences);

        var formatting = options.KeepOriginalFormatting
            ? CollectFirstCitationFormatting(occurrences)
            : null;

        return Build(
            occurrences,
            options,
            occurrenceCounts,
            EntryRightTabStopPt(document.Page),
            formatting,
            BuildPageReferences(occurrences));
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
            CanonicalizeOccurrences(ToCitationOccurrences(citations)),
            ToaOptions.Default,
            occurrenceCounts: null,
            DefaultEntryRightTabStopPt,
            sourceFormatting: null,
            pageReferences: null);
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
            CanonicalizeOccurrences(ToCitationOccurrences(citations)),
            options,
            occurrenceCounts: null,
            DefaultEntryRightTabStopPt,
            sourceFormatting: null,
            pageReferences: null);
    }

    // Core builder shared by all public overloads.
    private static IReadOnlyList<Paragraph> Build(
        IReadOnlyList<ToaCanonicalCitation> citations,
        ToaOptions options,
        Dictionary<ToaEntryKey, int>? occurrenceCounts,
        double entryRightTabStopPt,
        Dictionary<ToaEntryKey, RunFormatting>? sourceFormatting,
        Dictionary<ToaEntryKey, IReadOnlyList<ToaCitationPageReference>>? pageReferences)
    {
        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var byCategory = citations
            .Where(c => c.EntryKey.LongCitation.Length > 0)
            .GroupBy(c => c.EntryKey.Category)
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
                .Select(c => c.EntryKey.LongCitation)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t, StringComparer.Ordinal)
                .ToList();

            if (distinct.Count == 0)
                continue;

            var categoryStart = paragraphs.Count;
            paragraphs.Add(new Paragraph(CategoryHeading(category)) { StyleId = CategoryStyleId });
            foreach (var entry in distinct)
            {
                // UsePassim: with page references, passim belongs after the tab; without them, keep the
                // legacy suffix so callers never receive fabricated page numbers.
                var key = new ToaEntryKey(entry, category);
                var text = entry;
                string? pageReferenceText = null;
                if (pageReferences is not null
                    && pageReferences.TryGetValue(key, out var pages)
                    && pages.Count > 0)
                {
                    pageReferenceText = FormatPageReference(entry, category, pages, options, occurrenceCounts);
                }
                else if (options.UsePassim && IsPassimEntry(entry, category, occurrenceCounts))
                {
                    text = entry + " passim";
                }

                RunFormatting? runFormatting = null;
                sourceFormatting?.TryGetValue(key, out runFormatting);
                paragraphs.Add(CreateEntryParagraph(text, pageReferenceText, options, entryRightTabStopPt, runFormatting));
            }

            // Word represents "All" as one TOA field per used category, not as category zero.
            var field = new ComplexField(NativeFieldInstructionFor(options, category));
            for (var index = categoryStart; index < paragraphs.Count; index++)
                paragraphs[index].SpanningFieldOwner = field;
            paragraphs[categoryStart].SpanningFieldStart = field;
            paragraphs[^1].EndsSpanningField = true;
        }

        return paragraphs;
    }

    /// <summary>Builds Word's native Table of Authorities instruction for the generated result.</summary>
    public static string NativeFieldInstructionFor(ToaOptions options, CitationCategory category)
    {
        ArgumentNullException.ThrowIfNull(options);

        var categoryNumber = ((int)category).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var instruction = $" TOA \\h \\c \"{categoryNumber}\"";
        if (options.UsePassim)
            instruction += " \\p";
        if (!options.KeepOriginalFormatting)
            instruction += " \\f";
        return instruction + " ";
    }

    /// <summary>
    /// Recovers options from an imported native TOA field. Word stores the tab leader in result
    /// paragraph formatting rather than in the field instruction.
    /// </summary>
    public static ToaOptions? ExistingOptions(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var leader = document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.SpanningFieldStart is { Keyword: "TOA" }
                || paragraph.SpanningFieldOwner is { Keyword: "TOA" }
                || paragraph.Runs.Any(run => run.ComplexField is { Keyword: "TOA" }))
            .SelectMany(paragraph => paragraph.Formatting.TabStops)
            .Select(tab => FromTabLeader(tab.Leader))
            .FirstOrDefault(ToaTabLeader.Dots);

        var nativeOptions = document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs
                .Select(run => run.ComplexField)
                .Prepend(paragraph.SpanningFieldOwner)
                .Prepend(paragraph.SpanningFieldStart))
            .Where(field => field is { Keyword: "TOA" })
            .Select(field => field!)
            .GroupBy(field => field.Instruction, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(field => TryGetNativeOptions(field, leader, out var options) ? options : null)
            .Where(options => options is not null)
            .Cast<ToaOptions>()
            .ToArray();
        if (nativeOptions.Length == 0)
            return null;

        var first = nativeOptions[0];
        var categories = nativeOptions
            .Select(options => options.CategoryFilter)
            .Distinct()
            .ToArray();
        return new ToaOptions
        {
            CategoryFilter = categories.Length == 1 ? categories[0] : null,
            UsePassim = first.UsePassim,
            KeepOriginalFormatting = first.KeepOriginalFormatting,
            TabLeader = leader
        };
    }

    /// <summary>True when <paramref name="field"/> is a native TOA field and its options were parsed.</summary>
    public static bool TryGetNativeOptions(
        ComplexField? field,
        ToaTabLeader tabLeader,
        out ToaOptions options)
    {
        options = ToaOptions.Default;
        if (field is not { Keyword: "TOA" })
            return false;

        CitationCategory? category = null;
        var rawCategory = ComplexFieldEngine.SwitchValue(field.Instruction, 'c');
        if (!string.IsNullOrWhiteSpace(rawCategory)
            && int.TryParse(
                rawCategory,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var categoryNumber)
            && Enum.IsDefined(typeof(CitationCategory), categoryNumber))
        {
            category = (CitationCategory)categoryNumber;
        }

        options = new ToaOptions
        {
            CategoryFilter = category,
            UsePassim = ComplexFieldEngine.HasSwitch(field.Instruction, 'p'),
            KeepOriginalFormatting = !ComplexFieldEngine.HasSwitch(field.Instruction, 'f'),
            TabLeader = tabLeader
        };
        return true;
    }

    private static Paragraph CreateEntryParagraph(
        string text,
        string? pageReferenceText,
        ToaOptions options,
        double entryRightTabStopPt,
        RunFormatting? sourceFormatting)
    {
        var paragraph = new Paragraph
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
        paragraph.Runs.Add(new Run(text));

        if (!string.IsNullOrWhiteSpace(pageReferenceText))
        {
            paragraph.Runs.Add(new Run("\t"));
            paragraph.Runs.Add(new Run(pageReferenceText));
        }

        if (sourceFormatting is not null && paragraph.Runs.Count > 0)
            paragraph.Runs[0].Formatting = sourceFormatting;

        return paragraph;
    }

    private static string FormatPageReference(
        string entry,
        CitationCategory category,
        IReadOnlyList<ToaCitationPageReference> pages,
        ToaOptions options,
        Dictionary<ToaEntryKey, int>? occurrenceCounts)
    {
        if (options.UsePassim && IsPassimEntry(entry, category, occurrenceCounts))
            return "passim";

        return string.Join(", ", pages.Select(page => page.DisplayText));
    }

    private static bool IsPassimEntry(
        string entry,
        CitationCategory category,
        Dictionary<ToaEntryKey, int>? occurrenceCounts)
    {
        if (occurrenceCounts is null)
            return false;

        occurrenceCounts.TryGetValue(new ToaEntryKey(entry, category), out var count);
        return count >= PassimOccurrenceThreshold;
    }

    private static IReadOnlyList<ToaCitationOccurrence> ToCitationOccurrences(IEnumerable<Citation> citations)
    {
        var occurrences = new List<ToaCitationOccurrence>();
        foreach (var citation in citations)
        {
            if (citation is not null)
                occurrences.Add(new ToaCitationOccurrence(citation, null, null));
        }

        return occurrences;
    }

    private static IReadOnlyList<ToaCanonicalCitation> CanonicalizeOccurrences(
        IReadOnlyList<ToaCitationOccurrence> occurrences)
    {
        if (occurrences.Count == 0)
            return [];

        var aliasMap = BuildShortCitationAliasMap(occurrences);
        var activeAliases = new HashSet<ToaEntryKey>(EntryKeyComparer);
        var canonical = new List<ToaCanonicalCitation>(occurrences.Count);

        foreach (var occurrence in occurrences)
        {
            var citation = occurrence.Citation;
            var key = new ToaEntryKey(citation.LongCitation, citation.Category);
            if (citation.LongCitation.Length > 0
                && activeAliases.Contains(key)
                && aliasMap.TryGetValue(key, out var canonicalLongCitation))
            {
                key = new ToaEntryKey(canonicalLongCitation, citation.Category);
            }

            canonical.Add(new ToaCanonicalCitation(citation, key, occurrence.PageReference, occurrence.SourceFormatting));

            if (citation.LongCitation.Length == 0 || citation.ShortCitation.Length == 0)
                continue;

            var aliasKey = new ToaEntryKey(citation.ShortCitation, citation.Category);
            if (aliasMap.TryGetValue(aliasKey, out var mappedLongCitation)
                && StringComparer.OrdinalIgnoreCase.Equals(mappedLongCitation, citation.LongCitation))
            {
                activeAliases.Add(aliasKey);
            }
        }

        return canonical;
    }

    private static Dictionary<ToaEntryKey, string> BuildShortCitationAliasMap(
        IReadOnlyList<ToaCitationOccurrence> occurrences)
    {
        var aliases = new Dictionary<ToaEntryKey, string>(EntryKeyComparer);
        var ambiguousAliases = new HashSet<ToaEntryKey>(EntryKeyComparer);

        foreach (var occurrence in occurrences)
        {
            var citation = occurrence.Citation;
            if (citation.LongCitation.Length == 0 || citation.ShortCitation.Length == 0)
                continue;

            var aliasKey = new ToaEntryKey(citation.ShortCitation, citation.Category);
            if (ambiguousAliases.Contains(aliasKey))
                continue;

            if (!aliases.TryGetValue(aliasKey, out var existingLongCitation))
            {
                aliases[aliasKey] = citation.LongCitation;
                continue;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(existingLongCitation, citation.LongCitation))
                continue;

            aliases.Remove(aliasKey);
            ambiguousAliases.Add(aliasKey);
        }

        return aliases;
    }

    private static Dictionary<ToaEntryKey, int> BuildOccurrenceCounts(
        IReadOnlyList<ToaCanonicalCitation> occurrences)
    {
        var counts = new Dictionary<ToaEntryKey, int>(EntryKeyComparer);
        foreach (var occurrence in occurrences)
        {
            if (occurrence.EntryKey.LongCitation.Length == 0)
                continue;

            counts.TryGetValue(occurrence.EntryKey, out var count);
            counts[occurrence.EntryKey] = count + 1;
        }

        return counts;
    }

    private static Dictionary<ToaEntryKey, RunFormatting> CollectFirstCitationFormatting(
        IReadOnlyList<ToaCanonicalCitation> occurrences)
    {
        var formatting = new Dictionary<ToaEntryKey, RunFormatting>(EntryKeyComparer);
        foreach (var occurrence in occurrences)
        {
            if (occurrence.EntryKey.LongCitation.Length == 0 || occurrence.SourceFormatting is null)
                continue;

            formatting.TryAdd(occurrence.EntryKey, occurrence.SourceFormatting);
        }

        return formatting;
    }

    private static IReadOnlyList<ToaCitationOccurrence> CollectCitationOccurrences(
        TextDocument document,
        ToaCitationPageResolver? pageResolver = null)
    {
        var useExplicitPageReferences = HasExplicitPageBoundary(document);
        var occurrences = new List<ToaCitationOccurrence>();
        var pageNumber = 1;

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block is not Paragraph paragraph)
                continue;

            if (paragraph.Formatting.PageBreakBefore)
                pageNumber++;

            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var run = paragraph.Runs[runIndex];
                if (run.IsPageBreak)
                {
                    pageNumber++;
                    continue;
                }

                if (run.Citation is { } citation)
                {
                    var resolvedPageReference = pageResolver?.Invoke(document, blockIndex, runIndex, citation);
                    occurrences.Add(new ToaCitationOccurrence(
                        citation,
                        resolvedPageReference ?? (useExplicitPageReferences ? CreatePageReference(pageNumber) : null),
                        run.Formatting));
                }
            }

            if (paragraph.SectionBreak is { } sectionBreak)
                pageNumber = AdvanceForSectionBreak(pageNumber, sectionBreak.BreakKind);
        }

        occurrences.AddRange(document.Citations.Select(citation => new ToaCitationOccurrence(citation, null, null)));
        return occurrences;
    }

    public static ToaCitationPageReference? ResolveFromBlockPageAssignments(
        IReadOnlyList<int>? blockPageAssignments,
        int blockIndex)
    {
        if (blockPageAssignments is null || blockIndex < 0 || blockIndex >= blockPageAssignments.Count)
            return null;

        var pageIndex = blockPageAssignments[blockIndex];
        return pageIndex >= 0 && pageIndex < int.MaxValue
            ? CreatePageReference(pageIndex + 1)
            : null;
    }

    /// <summary>
    /// Creates normalized page-reference evidence for a live host resolver. Hosts should only call this
    /// when they have real layout evidence; no-layout callers must keep passing no resolver so entries stay
    /// text-only instead of receiving invented page numbers.
    /// </summary>
    public static ToaCitationPageReference CreatePageReference(int pageNumber)
    {
        var safePageNumber = Math.Max(1, pageNumber);
        return new ToaCitationPageReference(
            safePageNumber,
            safePageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ToaCitationPageResolver? ToBlockPageAssignmentResolver(
        IReadOnlyList<int>? blockPageAssignments) =>
        blockPageAssignments is null
            ? null
            : (_, blockIndex, _, _) => ResolveFromBlockPageAssignments(blockPageAssignments, blockIndex);

    private static bool HasExplicitPageBoundary(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Any(paragraph =>
            paragraph.Formatting.PageBreakBefore
            || paragraph.Runs.Any(run => run.IsPageBreak)
            || paragraph.SectionBreak is { BreakKind: SectionBreakKind.NextPage or SectionBreakKind.EvenPage or SectionBreakKind.OddPage });

    private static int AdvanceForSectionBreak(int pageNumber, SectionBreakKind breakKind) => breakKind switch
    {
        SectionBreakKind.NextPage => pageNumber + 1,
        SectionBreakKind.EvenPage => pageNumber % 2 == 0 ? pageNumber + 2 : pageNumber + 1,
        SectionBreakKind.OddPage => pageNumber % 2 == 0 ? pageNumber + 1 : pageNumber + 2,
        _ => pageNumber
    };

    private static Dictionary<ToaEntryKey, IReadOnlyList<ToaCitationPageReference>>? BuildPageReferences(
        IReadOnlyList<ToaCanonicalCitation> occurrences)
    {
        Dictionary<ToaEntryKey, SortedDictionary<int, string>>? pages = null;
        foreach (var occurrence in occurrences)
        {
            if (occurrence.PageReference is not { } pageReference || occurrence.EntryKey.LongCitation.Length == 0)
                continue;

            pages ??= new Dictionary<ToaEntryKey, SortedDictionary<int, string>>(EntryKeyComparer);
            var key = occurrence.EntryKey;
            if (!pages.TryGetValue(key, out var entryPages))
            {
                entryPages = [];
                pages[key] = entryPages;
            }

            var safePageNumber = Math.Max(1, pageReference.PageNumber);
            var displayText = string.IsNullOrWhiteSpace(pageReference.DisplayText)
                ? safePageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : pageReference.DisplayText.Trim();
            entryPages.TryAdd(safePageNumber, displayText);
        }

        return pages?.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ToaCitationPageReference>)pair.Value
                .Select(page => new ToaCitationPageReference(page.Key, page.Value))
                .ToList(),
            EntryKeyComparer);
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

    private static ToaTabLeader FromTabLeader(TabLeader leader) => leader switch
    {
        TabLeader.Dashes => ToaTabLeader.Dashes,
        TabLeader.Underline => ToaTabLeader.Underline,
        TabLeader.None => ToaTabLeader.None,
        _ => ToaTabLeader.Dots
    };

    private readonly record struct ToaEntryKey(string LongCitation, CitationCategory Category);

    private sealed record ToaCitationOccurrence(
        Citation Citation,
        ToaCitationPageReference? PageReference,
        RunFormatting? SourceFormatting);

    private sealed record ToaCanonicalCitation(
        Citation Citation,
        ToaEntryKey EntryKey,
        ToaCitationPageReference? PageReference,
        RunFormatting? SourceFormatting);

    private sealed class ToaEntryKeyComparer : IEqualityComparer<ToaEntryKey>
    {
        public bool Equals(ToaEntryKey x, ToaEntryKey y) =>
            x.Category == y.Category
            && StringComparer.OrdinalIgnoreCase.Equals(x.LongCitation, y.LongCitation);

        public int GetHashCode(ToaEntryKey obj) =>
            HashCode.Combine(obj.Category, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LongCitation));
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

    /// <summary>True when a paragraph is owned by a native TOA field or carries a generated TOA style.</summary>
    public static bool IsTableOfAuthoritiesParagraph(Block block) =>
        block is Paragraph paragraph
        && (paragraph.SpanningFieldStart is { Keyword: "TOA" }
            || paragraph.SpanningFieldOwner is { Keyword: "TOA" }
            || paragraph.Runs.Any(run => run.ComplexField is { Keyword: "TOA" })
            || IsTableOfAuthoritiesStyleId(paragraph.StyleId));

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
