namespace FreeW.Core.Model;

/// <summary>
/// The semantic payload of one hidden Word <c>XE</c> mark. <paramref name="Subentry"/> may contain
/// colon-separated second/third-level text, matching Word's field-code convention. A non-empty
/// <paramref name="CrossReference"/> is the exact text carried by XE's <c>\t</c> switch (for example,
/// <c>See Vehicles</c>) and replaces the page number for that occurrence.
/// </summary>
public sealed record IndexMark(string MainEntry, string Subentry = "", string CrossReference = "")
{
    /// <summary>The colon-delimited entry text serialized as XE's first argument.</summary>
    public string EntryText => Subentry.Length == 0 ? MainEntry : MainEntry + ":" + Subentry;
}

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
                if (MarkedEntry(run) is not { } mark)
                    continue;
                occurrences.Add(new IndexOccurrence(mark, blockIndex));
                bodyTerms.Add(mark.EntryText);
            }
        }

        foreach (var entry in document.IndexEntries)
        {
            if (entry.Term.Length > 0 && !bodyTerms.Contains(entry.Term))
                occurrences.Add(new IndexOccurrence(new IndexMark(entry.Term), BlockIndex: null));
        }

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };
        var roots = new Dictionary<string, IndexNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in occurrences)
        {
            var levels = SplitLevels(occurrence.Mark.EntryText);
            if (levels.Count == 0)
                continue;

            var siblings = roots;
            IndexNode? node = null;
            foreach (var level in levels)
            {
                if (!siblings.TryGetValue(level, out node))
                {
                    node = new IndexNode(level);
                    siblings.Add(level, node);
                }
                siblings = node.Children;
            }
            node!.Occurrences.Add(occurrence);
        }

        foreach (var root in Ordered(roots.Values))
            AppendNode(paragraphs, root, depth: 0, document, pageTextOf);

        return paragraphs;
    }

    /// <summary>Creates Word's hidden <c>XE</c> field mark for one index term.</summary>
    public static Run MarkRun(string term)
        => MarkRun(new IndexMark((term ?? string.Empty).Trim()));

    /// <summary>Creates Word's hidden <c>XE</c> field mark from a structured index entry.</summary>
    public static Run MarkRun(IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var normalized = Normalize(mark);
        var instruction = $" XE \"{Escape(normalized.EntryText)}\"";
        if (normalized.CrossReference.Length > 0)
            instruction += $" \\t \"{Escape(normalized.CrossReference)}\"";
        return Run.ComplexFieldRun(instruction + " ");
    }

    /// <summary>Returns the term carried by a hidden <c>XE</c> field run, or null for another run.</summary>
    public static string? MarkedTerm(Run run)
        => MarkedEntry(run)?.EntryText;

    /// <summary>Returns the structured payload carried by a hidden <c>XE</c> field run.</summary>
    public static IndexMark? MarkedEntry(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.ComplexField is not { Keyword: "XE" } field)
            return null;

        var term = ComplexFieldEngine.FirstArgument(field.Instruction)?.Trim();
        if (string.IsNullOrEmpty(term))
            return null;

        var separator = term.IndexOf(':');
        var mainEntry = separator < 0 ? term : term[..separator];
        var subentry = separator < 0 ? string.Empty : term[(separator + 1)..];
        return Normalize(new IndexMark(
            mainEntry,
            subentry,
            ComplexFieldEngine.SwitchValue(field.Instruction, 't') ?? string.Empty));
    }

    private static void AppendNode(
        ICollection<Paragraph> paragraphs,
        IndexNode node,
        int depth,
        TextDocument document,
        Func<int, string?>? pageTextOf)
    {
        var pages = node.Occurrences
            .Where(occurrence => occurrence.Mark.CrossReference.Length == 0)
            .Select(occurrence => ResolvePageText(document, occurrence.BlockIndex, pageTextOf))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var crossReferences = node.Occurrences
            .Select(occurrence => occurrence.Mark.CrossReference)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var text = node.Label;
        if (pages.Count > 0)
            text += ", " + string.Join(", ", pages);
        if (crossReferences.Count > 0)
            text += ". " + string.Join("; ", crossReferences);

        paragraphs.Add(new Paragraph(text)
        {
            StyleId = EntryStyleId,
            Formatting = new ParagraphFormatting
            {
                IndentLeftPt = (depth + 1) * 12,
                FirstLineIndentPt = -12,
                SpaceAfterPt = 2,
                SpaceAfterIsSet = true
            }
        });

        foreach (var child in Ordered(node.Children.Values))
            AppendNode(paragraphs, child, depth + 1, document, pageTextOf);
    }

    private static IEnumerable<IndexNode> Ordered(IEnumerable<IndexNode> nodes) =>
        nodes.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Label, StringComparer.Ordinal);

    private static IReadOnlyList<string> SplitLevels(string entryText) =>
        entryText.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IndexMark Normalize(IndexMark mark) =>
        new(mark.MainEntry.Trim(), mark.Subentry.Trim(), mark.CrossReference.Trim());

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

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

    private sealed record IndexOccurrence(IndexMark Mark, int? BlockIndex);

    private sealed class IndexNode(string label)
    {
        public string Label { get; } = label;
        public Dictionary<string, IndexNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IndexOccurrence> Occurrences { get; } = [];
    }

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
