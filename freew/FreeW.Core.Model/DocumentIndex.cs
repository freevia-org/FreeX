namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free generation of a document index from hidden body <c>XE</c> marks plus legacy
/// <see cref="TextDocument.IndexEntries"/>. Lives in the model project so it is unit-testable without
/// any UI, mirroring <see cref="TableOfContents"/>.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s — an "Index" heading
/// followed by one paragraph per distinct marked term and its page list, sorted alphabetically
/// (case-insensitive) with duplicate pages collapsed. The paragraphs carry dedicated index style ids (<see cref="HeadingStyleId"/>
/// and <see cref="EntryStyleId"/>) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct index formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as normal styled paragraphs (no I/O changes needed); and</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted index region
/// via <see cref="IsIndexParagraph"/>.</item>
/// </list>
/// </summary>
public static class DocumentIndex
{
    /// <summary>Style id of the index's "Index" heading paragraph.</summary>
    public const string HeadingStyleId = "IndexHeading";

    /// <summary>Display text of the index's heading paragraph.</summary>
    public const string HeadingText = "Index";

    /// <summary>Style id carried by each generated index entry paragraph.</summary>
    public const string EntryStyleId = "IndexEntry";

    /// <summary>
    /// Builds the index paragraphs for <paramref name="document"/>: an "Index" heading
    /// (<see cref="HeadingStyleId"/>) followed by one paragraph per distinct hidden or legacy marked term,
    /// sorted alphabetically with its distinct page labels. A document with no marked entries yields just
    /// the heading paragraph. Deterministic and
    /// side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var occurrences = new List<IndexOccurrence>();
        var bodyTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            foreach (var run in paragraph.Runs)
            {
                if (MarkedTerm(run) is not { Length: > 0 } term)
                    continue;
                occurrences.Add(new IndexOccurrence(term, blockIndex));
                bodyTerms.Add(term);
            }
        }

        foreach (var entry in document.IndexEntries)
        {
            if (entry.Term.Length > 0 && !bodyTerms.Contains(entry.Term))
                occurrences.Add(new IndexOccurrence(entry.Term, BlockIndex: null));
        }

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };
        foreach (var group in occurrences
            .GroupBy(occurrence => occurrence.Term, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var pages = group
                .Select(occurrence => ResolvePageText(document, occurrence.BlockIndex, pageTextOf))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            paragraphs.Add(new Paragraph(group.First().Term + ", " + string.Join(", ", pages))
            {
                StyleId = EntryStyleId
            });
        }

        return paragraphs;
    }

    /// <summary>Creates Word's hidden <c>XE</c> field mark for one index term.</summary>
    public static Run MarkRun(string term)
    {
        var normalized = (term ?? string.Empty).Trim();
        var escaped = normalized.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return Run.ComplexFieldRun($" XE \"{escaped}\" ");
    }

    /// <summary>Returns the term carried by a hidden <c>XE</c> field run, or null for another run.</summary>
    public static string? MarkedTerm(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.ComplexField is not { Keyword: "XE" } field)
            return null;

        var term = ComplexFieldEngine.FirstArgument(field.Instruction)?.Trim();
        return string.IsNullOrEmpty(term) ? null : term;
    }

    private static string ResolvePageText(
        TextDocument document,
        int? blockIndex,
        Func<int, string?>? pageTextOf)
    {
        if (blockIndex is not { } index)
            return "1";

        var pageText = pageTextOf?.Invoke(index);
        if (!string.IsNullOrEmpty(pageText))
            return pageText;

        return (CrossReferences.ExplicitPageNumberAtBlock(document, index) ?? 1)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record IndexOccurrence(string Term, int? BlockIndex);

    /// <summary>
    /// Builds the index paragraphs from an arbitrary set of <paramref name="terms"/>: an "Index" heading
    /// followed by the distinct, non-blank terms sorted alphabetically (case-insensitive, ordinal as the
    /// tie-breaker so the order is deterministic), one per paragraph. Blank/whitespace terms are skipped
    /// and surrounding whitespace is trimmed before de-duplication. An empty set yields just the heading.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var distinct = terms
            .Where(t => t is not null)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t, StringComparer.Ordinal);

        foreach (var term in distinct)
            paragraphs.Add(new Paragraph(term) { StyleId = EntryStyleId });

        return paragraphs;
    }

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the index styles produced by <see cref="Build"/>
    /// (the heading style or the entry style). Used to recognise a previously inserted index region so a
    /// refresh can remove it.
    /// </summary>
    public static bool IsIndexStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="block"/> is a paragraph carrying an index style (see <see cref="IsIndexStyleId"/>).</summary>
    public static bool IsIndexParagraph(Block block) =>
        block is Paragraph paragraph && IsIndexStyleId(paragraph.StyleId);

    /// <summary>
    /// Registers the index styles (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>) in
    /// <paramref name="document"/>'s style catalog if they are not already present, so the inserted index
    /// paragraphs resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "Index Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(EntryStyleId, new DocumentStyle
        {
            Id = EntryStyleId,
            Name = "Index Entry",
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }
}
