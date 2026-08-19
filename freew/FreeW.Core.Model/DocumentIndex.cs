using System.Globalization;
using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// The semantic payload of one hidden Word <c>XE</c> mark. <paramref name="Subentry"/> may contain
/// colon-separated second/third-level text, matching Word's field-code convention. A non-empty
/// <paramref name="CrossReference"/> is the exact text carried by XE's <c>\t</c> switch (for example,
/// <c>See Vehicles</c>) and replaces the page number for that occurrence. A non-empty
/// <paramref name="BookmarkName"/> carries XE's <c>\r</c> switch and resolves the first-to-last page of
/// that bookmark instead of the XE field's own page. <paramref name="Identifier"/> carries XE's
/// <c>\f</c> entry type so multiple independently filtered indexes can share one document.
/// </summary>
public sealed record IndexMark(
    string MainEntry,
    string Subentry = "",
    string CrossReference = "",
    bool BoldPageNumber = false,
    bool ItalicPageNumber = false,
    string BookmarkName = "",
    string Identifier = "")
{
    /// <summary>The colon-delimited entry text serialized as XE's first argument.</summary>
    public string EntryText => Subentry.Length == 0 ? MainEntry : MainEntry + ":" + Subentry;
}

/// <summary>
/// Recursive address of a paragraph inside a table cell. When <see cref="NestedTableIndex"/> is set,
/// <see cref="NestedParagraph"/> continues inside that nested table; otherwise
/// <see cref="ParagraphIndex"/> selects the final paragraph in the addressed cell.
/// </summary>
public sealed record TableParagraphAddress(
    int RowIndex,
    int CellIndex,
    int ParagraphIndex,
    int? NestedTableIndex = null,
    TableParagraphAddress? NestedParagraph = null);

/// <summary>One body-paragraph insertion point selected by Word-style Mark All.</summary>
public sealed record IndexMarkTarget(
    int BlockIndex,
    int TextOffset,
    TableParagraphAddress? TableParagraph = null);

/// <summary>Layout and collation choices for a generated Word INDEX field result.</summary>
public sealed record IndexBuildOptions(
    bool IncludeAlphabeticHeadings = true,
    bool IncludeTitle = false,
    string CultureName = "en-US")
{
    public static IndexBuildOptions WordDefault { get; } = new();
    public static IndexBuildOptions LegacyTitleOnly { get; } = new(false, true);
}

/// <summary>
/// Physical identity plus display text for one generated index page reference. The physical index is
/// zero-based and remains stable when separate sections restart their visible page numbering.
/// </summary>
public readonly record struct IndexPageReferenceAddress(int PhysicalPageIndex, string DisplayText);

/// <summary>
/// Pure, WPF-free generation of a document index from hidden body <c>XE</c> marks plus legacy
/// <see cref="TextDocument.IndexEntries"/>. Lives in the model project so it is unit-testable without
/// any UI, mirroring <see cref="TableOfContents"/>.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s: Word-style alphabetic group
/// headings followed by one paragraph per distinct marked term and its page list, sorted with English
/// index collation and duplicate pages collapsed. The paragraphs carry dedicated index style ids (<see cref="HeadingStyleId"/>
/// and <see cref="EntryStyleId"/>) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct index formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as styled cached-result paragraphs owned by one native INDEX field; and</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted index region
/// via <see cref="IsIndexParagraph"/>.</item>
/// </list>
/// </summary>
public static class DocumentIndex
{
    /// <summary>Style id of the index's "Index" heading paragraph.</summary>
    public const string HeadingStyleId = "IndexHeading";

    /// <summary>Display text used only by the explicit legacy-title build option.</summary>
    public const string HeadingText = "Index";

    /// <summary>Style id carried by each generated index entry paragraph.</summary>
    public const string EntryStyleId = "IndexEntry";

    /// <summary>Word-visible result when an XE <c>\\r</c> switch names no bookmark.</summary>
    public const string BrokenBookmarkText = "Error! Bookmark not defined.";

    /// <summary>
    /// Builds the index paragraphs for <paramref name="document"/> using Word's default INDEX result:
    /// alphabetic group headings followed by one paragraph per distinct hidden or legacy marked term,
    /// sorted with English index collation and distinct page labels. A document with no marked entries
    /// yields no paragraphs. Deterministic and
    /// side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        Func<int, string?>? pageTextOf = null,
        string? identifier = null,
        IndexBuildOptions? options = null,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= IndexBuildOptions.WordDefault;

        var occurrences = new List<IndexOccurrence>();
        var bodyTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in DocumentBodyParagraphs.Enumerate(document))
        {
            var blockIndex = location.BlockIndex;
            var paragraph = location.Paragraph;
            foreach (var run in paragraph.Runs)
            {
                if (MarkedEntry(run) is not { } mark)
                    continue;
                if (!IdentifiersMatch(mark.Identifier, identifier))
                    continue;
                occurrences.Add(new IndexOccurrence(mark, blockIndex));
                bodyTerms.Add(mark.EntryText);
            }
        }

        if (IsDefaultIdentifier(identifier))
        {
            foreach (var entry in document.IndexEntries)
            {
                if (entry.Term.Length > 0 && !bodyTerms.Contains(entry.Term))
                    occurrences.Add(new IndexOccurrence(new IndexMark(entry.Term), BlockIndex: null));
            }
        }

        var paragraphs = new List<Paragraph>();
        if (options.IncludeTitle)
            paragraphs.Add(new Paragraph(HeadingText) { StyleId = HeadingStyleIdFor(identifier) });
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

        string? currentHeading = null;
        foreach (var root in Ordered(roots.Values, options.CultureName))
        {
            var heading = AlphabeticHeading(root.Label, options.CultureName);
            if (options.IncludeAlphabeticHeadings
                && !string.Equals(currentHeading, heading, StringComparison.Ordinal))
            {
                paragraphs.Add(new Paragraph(heading) { StyleId = HeadingStyleIdFor(identifier) });
                currentHeading = heading;
            }
            AppendNode(
                paragraphs,
                root,
                depth: 0,
                document,
                pageTextOf,
                pageReferenceOf,
                EntryStyleIdFor(identifier),
                options.CultureName);
        }

        if (paragraphs.Count > 0)
        {
            var field = new ComplexField(IndexFieldInstruction(identifier, options));
            foreach (var paragraph in paragraphs)
                paragraph.SpanningFieldOwner = field;
            paragraphs[0].SpanningFieldStart = field;
            paragraphs[^1].EndsSpanningField = true;
        }

        return paragraphs;
    }

    private static string IndexFieldInstruction(string? identifier, IndexBuildOptions options)
    {
        var instruction = new StringBuilder(" INDEX");
        if (!IsDefaultIdentifier(identifier))
            instruction.Append(" \\f \"").Append(Escape(EffectiveIdentifier(identifier))).Append('"');
        if (options.IncludeAlphabeticHeadings)
            instruction.Append(" \\h \"A\"");

        var culture = System.Globalization.CultureInfo.GetCultureInfo(options.CultureName);
        instruction.Append(" \\z \"")
            .Append(culture.LCID.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("\" ");
        return instruction.ToString();
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
        if (normalized.Identifier.Length > 0)
            instruction += $" \\f \"{Escape(normalized.Identifier)}\"";
        if (normalized.CrossReference.Length > 0)
            instruction += $" \\t \"{Escape(normalized.CrossReference)}\"";
        if (normalized.BookmarkName.Length > 0)
            instruction += $" \\r \"{Escape(normalized.BookmarkName)}\"";
        if (normalized.BoldPageNumber)
            instruction += " \\b";
        if (normalized.ItalicPageNumber)
            instruction += " \\i";
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
            ComplexFieldEngine.SwitchValue(field.Instruction, 't') ?? string.Empty,
            ComplexFieldEngine.HasSwitch(field.Instruction, 'b'),
            ComplexFieldEngine.HasSwitch(field.Instruction, 'i'),
            ComplexFieldEngine.SwitchValue(field.Instruction, 'r') ?? string.Empty,
            ComplexFieldEngine.SwitchValue(field.Instruction, 'f') ?? string.Empty));
    }

    /// <summary>
    /// Finds every insertion point containing <paramref name="sourceText"/> as a whole term,
    /// case-insensitively. Generated index rows and occurrences already carrying an equivalent mark at
    /// the same text offset are skipped. Each returned offset follows its matching occurrence.
    /// </summary>
    public static IReadOnlyList<IndexMarkTarget> MarkAllTargets(
        TextDocument document,
        string sourceText,
        IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mark);
        var needle = (sourceText ?? string.Empty).Trim();
        if (needle.Length == 0)
            return [];

        var targets = new List<IndexMarkTarget>();
        foreach (var location in DocumentBodyParagraphs.Enumerate(document))
        {
            var paragraph = location.Paragraph;
            if (IsIndexParagraph(paragraph))
                continue;

            var markedOffsets = new HashSet<int>();
            var textOffset = 0;
            foreach (var run in paragraph.Runs)
            {
                if (MarksEquivalent(MarkedEntry(run), mark))
                    markedOffsets.Add(textOffset);
                textOffset += run.Text.Length;
            }

            foreach (var match in FindWholeTerms(paragraph.PlainText, needle))
            {
                var insertionOffset = match + needle.Length;
                if (!markedOffsets.Contains(insertionOffset))
                {
                    targets.Add(new IndexMarkTarget(
                        location.BlockIndex,
                        insertionOffset,
                        location.TableParagraph));
                }
            }
        }

        return targets;
    }

    /// <summary>Case-insensitive equality for the complete semantic XE mark payload.</summary>
    public static bool MarksEquivalent(IndexMark? left, IndexMark right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return left is not null
            && string.Equals(left.EntryText, right.EntryText, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.CrossReference, right.CrossReference, StringComparison.OrdinalIgnoreCase)
            && left.BoldPageNumber == right.BoldPageNumber
            && left.ItalicPageNumber == right.ItalicPageNumber
            && string.Equals(left.BookmarkName, right.BookmarkName, StringComparison.Ordinal)
            && string.Equals(left.Identifier, right.Identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<int> FindWholeTerms(string text, string needle)
    {
        var start = 0;
        while (start <= text.Length - needle.Length)
        {
            var match = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
                yield break;
            var leftBoundary = match == 0 || !char.IsLetterOrDigit(text[match - 1]);
            var rightIndex = match + needle.Length;
            var rightBoundary = rightIndex == text.Length || !char.IsLetterOrDigit(text[rightIndex]);
            if (leftBoundary && rightBoundary)
                yield return match;
            start = match + 1;
        }
    }

    private static void AppendNode(
        ICollection<Paragraph> paragraphs,
        IndexNode node,
        int depth,
        TextDocument document,
        Func<int, string?>? pageTextOf,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf,
        string entryStyleId,
        string cultureName)
    {
        var pages = node.Occurrences
            .Where(occurrence => occurrence.Mark.CrossReference.Length == 0)
            .Select(occurrence => new
            {
                Occurrence = occurrence,
                Reference = ResolvePageReference(document, occurrence, pageTextOf, pageReferenceOf)
            })
            .GroupBy(
                item => item.Reference.Identity,
                StringComparer.Ordinal)
            .Select(group => new PageItem(
                group.First().Reference.StartIndex,
                group.First().Reference.EndIndex,
                group.First().Reference.FirstLabel,
                group.First().Reference.LastLabel,
                group.First().Reference.Label,
                group.Any(item => item.Occurrence.Mark.BoldPageNumber),
                group.Any(item => item.Occurrence.Mark.ItalicPageNumber),
                group.First().Reference.IsRange))
            // Grouping above only collapses exact-duplicate marks; it otherwise preserves the
            // document (mark-occurrence) order the marks were encountered in, which does not match
            // ascending page order once a \r bookmark-ranged mark is involved (its own literal
            // location in the document has no relation to the pages its bookmark resolves to). Sort
            // ascending by resolved physical page so an entry never reads e.g. "12, 4-7, 9" instead
            // of "4-7, 9, 12". Entries whose page could not be resolved to a real physical index (no
            // host page-layout evidence available) sort last, stably preserving their relative
            // document order among themselves (OrderBy is a stable sort) rather than guessing at an
            // order the model cannot actually know.
            .OrderBy(item => item.StartIndex < 0 ? int.MaxValue : item.StartIndex)
            .ToList();
        var mergedPages = MergeAdjacentPageReferences(pages);
        var crossReferences = node.Occurrences
            .Select(occurrence => occurrence.Mark.CrossReference)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paragraph = new Paragraph
        {
            StyleId = entryStyleId,
            Formatting = new ParagraphFormatting
            {
                IndentLeftPt = (depth + 1) * 12,
                FirstLineIndentPt = -12,
                SpaceAfterPt = 2,
                SpaceAfterIsSet = true
            }
        };
        paragraph.Runs.Add(new Run(node.Label));
        foreach (var page in mergedPages)
        {
            paragraph.Runs.Add(new Run(", "));
            paragraph.Runs.Add(new Run(page.Label)
            {
                Formatting = new RunFormatting
                {
                    Bold = page.Bold,
                    Italic = page.Italic
                }
            });
        }
        if (crossReferences.Count > 0)
            paragraph.Runs.Add(new Run(". " + string.Join("; ", crossReferences)));
        paragraphs.Add(paragraph);

        foreach (var child in Ordered(node.Children.Values, cultureName))
            AppendNode(paragraphs, child, depth + 1, document, pageTextOf, pageReferenceOf, entryStyleId, cultureName);
    }

    /// <summary>
    /// Collapses adjacent entries of an ascending-sorted page-reference list when their resolved
    /// physical page spans overlap or abut (the later entry's start falls at-or-before one page past
    /// the earlier entry's end) AND at least one of the two originates from an explicit <c>\r</c>
    /// bookmark-ranged mark, matching Word's INDEX behaviour of merging a ranged mark with a
    /// contiguous page into one continuous range instead of listing them separately. Two ordinary
    /// single-page marks that merely happen to land on consecutive physical pages are intentionally
    /// left as distinct entries (Word does not auto-collapse those). Entries with an unresolved
    /// physical page (<see cref="PageItem.StartIndex"/> or <see cref="PageItem.EndIndex"/> below zero)
    /// are never merged, since their true page position relative to other entries is unknown.
    /// </summary>
    private static List<IndexPageReference> MergeAdjacentPageReferences(IReadOnlyList<PageItem> sortedItems)
    {
        var result = new List<IndexPageReference>();
        PageItem? pending = null;
        foreach (var item in sortedItems)
        {
            if (pending is { } current
                && (current.IsRange || item.IsRange)
                && current.EndIndex >= 0
                && item.StartIndex >= 0
                && item.StartIndex <= current.EndIndex + 1)
            {
                pending = MergePageItems(current, item);
                continue;
            }

            if (pending is { } finished)
                result.Add(new IndexPageReference(finished.Label, finished.Bold, finished.Italic));
            pending = item;
        }

        if (pending is { } last)
            result.Add(new IndexPageReference(last.Label, last.Bold, last.Italic));

        return result;
    }

    private static PageItem MergePageItems(PageItem left, PageItem right)
    {
        var extendsRange = right.EndIndex > left.EndIndex;
        var endIndex = extendsRange ? right.EndIndex : left.EndIndex;
        var lastLabel = extendsRange ? right.LastLabel : left.LastLabel;
        var label = left.StartIndex == endIndex
            ? left.FirstLabel
            : left.FirstLabel + "–" + lastLabel;
        return new PageItem(
            left.StartIndex,
            endIndex,
            left.FirstLabel,
            lastLabel,
            label,
            left.Bold || right.Bold,
            left.Italic || right.Italic,
            IsRange: true);
    }

    /// <summary>One collated page reference before final display formatting. <see cref="StartIndex"/>
    /// and <see cref="EndIndex"/> are resolved physical page indexes (below zero when unresolved),
    /// used to sort entries ascending and to detect overlapping/abutting ranges to merge.
    /// <see cref="IsRange"/> is true when this entry originates from an explicit <c>\r</c>
    /// bookmark-ranged mark (or from a prior merge), which gates automatic merging with a neighbour —
    /// two ordinary single-page marks are never auto-collapsed together.</summary>
    private sealed record PageItem(
        int StartIndex,
        int EndIndex,
        string FirstLabel,
        string LastLabel,
        string Label,
        bool Bold,
        bool Italic,
        bool IsRange);

    private static IEnumerable<IndexNode> Ordered(IEnumerable<IndexNode> nodes, string cultureName) =>
        nodes.OrderBy(node => node.Label, IndexLabelComparer(cultureName))
            .ThenBy(node => node.Label, StringComparer.Ordinal);

    private static IComparer<string> IndexLabelComparer(string cultureName)
    {
        var compareInfo = CultureInfo.GetCultureInfo(cultureName).CompareInfo;
        return Comparer<string>.Create((left, right) => compareInfo.Compare(
            left,
            right,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));
    }

    private static string AlphabeticHeading(string label, string cultureName)
    {
        var element = StringInfo.GetNextTextElement(label.Trim());
        var decomposed = element.Normalize(NormalizationForm.FormD);
        var baseElement = string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) is not UnicodeCategory.NonSpacingMark
                and not UnicodeCategory.SpacingCombiningMark
                and not UnicodeCategory.EnclosingMark));
        if (baseElement.Length == 0)
            baseElement = element;
        return baseElement.Normalize(NormalizationForm.FormC)
            .ToUpper(CultureInfo.GetCultureInfo(cultureName));
    }

    private static IReadOnlyList<string> SplitLevels(string entryText) =>
        entryText.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IndexMark Normalize(IndexMark mark) =>
        new(
            mark.MainEntry.Trim(),
            mark.Subentry.Trim(),
            mark.CrossReference.Trim(),
            mark.BoldPageNumber,
            mark.ItalicPageNumber,
            mark.BookmarkName.Trim(),
            mark.Identifier.Trim());

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static ResolvedIndexPageReference ResolvePageReference(
        TextDocument document,
        IndexOccurrence occurrence,
        Func<int, string?>? pageTextOf,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf)
    {
        if (occurrence.Mark.BookmarkName.Length > 0)
        {
            if (ResolveBookmarkRange(document, occurrence.Mark.BookmarkName) is not { } range)
            {
                return new ResolvedIndexPageReference(
                    "broken-bookmark:" + occurrence.Mark.BookmarkName,
                    BrokenBookmarkText,
                    StartIndex: -1,
                    EndIndex: -1,
                    FirstLabel: BrokenBookmarkText,
                    LastLabel: BrokenBookmarkText,
                    IsRange: true);
            }

            var first = ResolveBlockPageReference(
                document, range.StartBlockIndex, range.StartTableRowIndex, pageTextOf, pageReferenceOf);
            var last = ResolveBlockPageReference(
                document, range.EndBlockIndex, range.EndTableRowIndex, pageTextOf, pageReferenceOf);
            var samePage = first.PhysicalPageIndex >= 0 && last.PhysicalPageIndex >= 0
                ? first.PhysicalPageIndex == last.PhysicalPageIndex
                : string.Equals(first.DisplayText, last.DisplayText, StringComparison.Ordinal);
            var label = samePage
                ? first.DisplayText
                : first.DisplayText + "\u2013" + last.DisplayText;
            return new ResolvedIndexPageReference(
                $"range:{first.PhysicalPageIndex}:{last.PhysicalPageIndex}:{occurrence.Mark.BookmarkName}",
                label,
                StartIndex: first.PhysicalPageIndex,
                EndIndex: last.PhysicalPageIndex,
                FirstLabel: first.DisplayText,
                LastLabel: last.DisplayText,
                IsRange: true);
        }

        if (occurrence.BlockIndex is not { } index)
        {
            return new ResolvedIndexPageReference(
                "label:1", "1", StartIndex: -1, EndIndex: -1, FirstLabel: "1", LastLabel: "1", IsRange: false);
        }

        var reference = ResolveBlockPageReference(document, index, tableRowIndex: null, pageTextOf, pageReferenceOf);
        var identity = reference.PhysicalPageIndex >= 0
            ? $"page:{reference.PhysicalPageIndex}"
            : "label:" + reference.DisplayText;
        return new ResolvedIndexPageReference(
            identity,
            reference.DisplayText,
            StartIndex: reference.PhysicalPageIndex,
            EndIndex: reference.PhysicalPageIndex,
            FirstLabel: reference.DisplayText,
            LastLabel: reference.DisplayText,
            IsRange: false);
    }

    // tableRowIndex is null for a non-bookmark XE occurrence (which never carries row data) and for the
    // block-less stories BookmarkPageResolution.Find can report (header/footer/footnote/endnote/comment);
    // both fall straight through with rowOffset 0, unchanged from before this method took the parameter.
    private static IndexPageReferenceAddress ResolveBlockPageReference(
        TextDocument document,
        int blockIndex,
        int? tableRowIndex,
        Func<int, string?>? pageTextOf,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf)
    {
        // Every row of a table shares one Blocks entry, so a bookmark on a later row would otherwise
        // resolve to the same page as row zero -- exactly the ComplexFieldEngine/CrossReferences finding,
        // via the same canonical math, now applied here too.
        var rowOffset = tableRowIndex is { } rowIndex
            && blockIndex >= 0 && blockIndex < document.Blocks.Count
            && document.Blocks[blockIndex] is Table table
                ? BookmarkPageResolution.PageBreaksBeforeTableRow(table, rowIndex)
                : 0;

        if (pageReferenceOf?.Invoke(blockIndex) is { PhysicalPageIndex: >= 0, DisplayText.Length: > 0 } reference)
            return rowOffset == 0 ? reference : OffsetPageReference(reference, rowOffset);

        var pageText = pageTextOf?.Invoke(blockIndex);
        var explicitPageNumber = CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex);
        if (!string.IsNullOrEmpty(pageText))
            return new IndexPageReferenceAddress((explicitPageNumber ?? 0) - 1, pageText);

        if (explicitPageNumber is not { } basePageNumber)
            return new IndexPageReferenceAddress(-1, "1");

        var pageNumber = basePageNumber + rowOffset;
        return new IndexPageReferenceAddress(
            pageNumber - 1, pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Applies a table-row page-break correction to a host-supplied page/label pair. The physical page
    // index (a pure sort/group key) always shifts by rowOffset; the display label only shifts when it is a
    // plain decimal number -- a roman-numeral or chapter-prefixed label (a front-matter page-numbering
    // format this model does not reconstruct here) is left exactly as the host supplied it, the same
    // convention already used for an explicit pageTextOf override, which is never adjusted either.
    private static IndexPageReferenceAddress OffsetPageReference(IndexPageReferenceAddress reference, int rowOffset) =>
        int.TryParse(
            reference.DisplayText,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var numeric)
            ? new IndexPageReferenceAddress(
                reference.PhysicalPageIndex + rowOffset,
                (numeric + rowOffset).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : new IndexPageReferenceAddress(reference.PhysicalPageIndex + rowOffset, reference.DisplayText);

    private static BookmarkBlockRange? ResolveBookmarkRange(TextDocument document, string bookmarkName)
    {
        var paragraphs = DocumentBodyParagraphs.Enumerate(document).ToList();
        for (var startParagraphIndex = 0; startParagraphIndex < paragraphs.Count; startParagraphIndex++)
        {
            var startLocation = paragraphs[startParagraphIndex];
            var startBlockIndex = startLocation.BlockIndex;
            var startParagraph = startLocation.Paragraph;

            var startBoundary = startParagraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                boundary.Kind == BookmarkBoundaryKind.Start
                && string.Equals(boundary.Name, bookmarkName, StringComparison.Ordinal));
            if (startBoundary is null)
                continue;

            for (var endParagraphIndex = startParagraphIndex; endParagraphIndex < paragraphs.Count; endParagraphIndex++)
            {
                var endLocation = paragraphs[endParagraphIndex];
                var endBlockIndex = endLocation.BlockIndex;
                var endParagraph = endLocation.Paragraph;
                if (endParagraph.BookmarkBoundaries.Any(boundary =>
                        boundary.Kind == BookmarkBoundaryKind.End
                        && string.Equals(boundary.PairKey, startBoundary.PairKey, StringComparison.Ordinal)))
                {
                    return new BookmarkBlockRange(
                        startBlockIndex,
                        startLocation.TableParagraph?.RowIndex,
                        endBlockIndex,
                        endLocation.TableParagraph?.RowIndex);
                }
            }

            return new BookmarkBlockRange(
                startBlockIndex,
                startLocation.TableParagraph?.RowIndex,
                startBlockIndex,
                startLocation.TableParagraph?.RowIndex);
        }

        // Widened, via the shared canonical walk, beyond a plain body/table-cell point bookmark to also
        // find one placed in a header, footer, footnote, endnote, or text box -- those previously fell
        // through to "no such bookmark" (a broken-bookmark entry) even though the bookmark genuinely
        // existed. A block-less story has no page ResolveBlockPageReference can attribute to it, so it
        // reports that gracefully via its own explicitPageNumber-null branch ("1", unresolved) rather than
        // as a broken-bookmark error -- the same "found but no page" outcome BookmarkPageResolution.
        // ResolvePageText's own header/footer/footnote/endnote fallback documents for PAGEREF/cross-ref
        // fields.
        var target = BookmarkPageResolution.Find(document, bookmarkName);
        return target is { } found
            ? new BookmarkBlockRange(found.BlockIndex, found.TableRowIndex, found.BlockIndex, found.TableRowIndex)
            : null;
    }

    private sealed record IndexOccurrence(IndexMark Mark, int? BlockIndex);

    private sealed record BookmarkBlockRange(
        int StartBlockIndex, int? StartTableRowIndex, int EndBlockIndex, int? EndTableRowIndex);

    private sealed record IndexPageReference(string Label, bool Bold, bool Italic);

    /// <summary><see cref="StartIndex"/>/<see cref="EndIndex"/> are the resolved physical page indexes
    /// (below zero when unresolvable) used to sort and merge page references ascending; <see cref="FirstLabel"/>
    /// and <see cref="LastLabel"/> are the individual endpoint display texts (equal for a single-page
    /// reference) used to recompute a merged range's display text. <see cref="IsRange"/> is true for a
    /// reference resolved from an explicit <c>\r</c> bookmark-ranged mark, gating automatic merging.</summary>
    private sealed record ResolvedIndexPageReference(
        string Identity,
        string Label,
        int StartIndex,
        int EndIndex,
        string FirstLabel,
        string LastLabel,
        bool IsRange);

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
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal)
            || styleId.StartsWith(HeadingStyleId + "_f_", StringComparison.Ordinal)
            || styleId.StartsWith(EntryStyleId + "_f_", StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="block"/> is a paragraph carrying an index style (see <see cref="IsIndexStyleId"/>).</summary>
    public static bool IsIndexParagraph(Block block) =>
        block is Paragraph paragraph
        && (paragraph.SpanningFieldOwner is { Keyword: "INDEX" }
            || IsIndexStyleId(paragraph.StyleId));

    /// <summary>True only for the generated region belonging to the requested INDEX/XE identifier.</summary>
    public static bool IsIndexParagraph(Block block, string? identifier) =>
        block is Paragraph paragraph
        && (paragraph.SpanningFieldOwner is { Keyword: "INDEX" } field
            ? IdentifiersMatch(
                ComplexFieldEngine.SwitchValue(field.Instruction, 'f') ?? string.Empty,
                identifier)
            : string.Equals(paragraph.StyleId, HeadingStyleIdFor(identifier), StringComparison.Ordinal)
                || string.Equals(paragraph.StyleId, EntryStyleIdFor(identifier), StringComparison.Ordinal));

    public static string HeadingStyleIdFor(string? identifier) =>
        HeadingStyleId + IdentifierStyleSuffix(identifier);

    public static string EntryStyleIdFor(string? identifier) =>
        EntryStyleId + IdentifierStyleSuffix(identifier);

    /// <summary>
    /// Registers the index styles (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>) in
    /// <paramref name="document"/>'s style catalog if they are not already present, so the inserted index
    /// paragraphs resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document, string? identifier = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var headingStyleId = HeadingStyleIdFor(identifier);
        var entryStyleId = EntryStyleIdFor(identifier);
        var labelSuffix = IsDefaultIdentifier(identifier) ? string.Empty : $" ({identifier!.Trim()})";

        document.Styles.TryAdd(headingStyleId, new DocumentStyle
        {
            Id = headingStyleId,
            Name = "Index Heading" + labelSuffix,
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(entryStyleId, new DocumentStyle
        {
            Id = entryStyleId,
            Name = "Index Entry" + labelSuffix,
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }

    private static bool IdentifiersMatch(string markIdentifier, string? requestedIdentifier) =>
        string.Equals(
            EffectiveIdentifier(markIdentifier),
            EffectiveIdentifier(requestedIdentifier),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDefaultIdentifier(string? identifier) =>
        string.Equals(EffectiveIdentifier(identifier), "I", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveIdentifier(string? identifier)
    {
        var normalized = (identifier ?? string.Empty).Trim();
        return normalized.Length == 0 ? "I" : normalized;
    }

    private static string IdentifierStyleSuffix(string? identifier)
    {
        if (IsDefaultIdentifier(identifier))
            return string.Empty;

        var bytes = System.Text.Encoding.UTF8.GetBytes(EffectiveIdentifier(identifier).ToUpperInvariant());
        return "_f_" + Convert.ToHexString(bytes);
    }
}
