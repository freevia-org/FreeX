using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The kind of document object a cross-reference points at. Mirrors the categories the Insert &gt;
/// Cross-reference dialog offers: a <see cref="Heading"/> (from the document outline), a named
/// <see cref="Bookmark"/>, a figure <see cref="Figure"/> / <see cref="Table"/> caption, a
/// <see cref="Footnote"/> / <see cref="Endnote"/>, or a <see cref="NumberedItem"/> (a numbered-list
/// paragraph).
/// </summary>
public enum CrossRefType
{
    Heading,
    Bookmark,
    Figure,
    Table,
    Equation,
    Footnote,
    Endnote,
    NumberedItem
}

/// <summary>
/// What a cross-reference inserts about its target — Word's "Insert reference to" choice. Mirrors the
/// WordprocessingML field that backs each: plain <see cref="Text"/> and <see cref="HeadingNumber"/> /
/// <see cref="ParagraphNumber"/> are <c>REF</c> fields (the latter with the <c>\n</c>/<c>\w</c> switch),
/// <see cref="PageNumber"/> is a <c>PAGEREF</c> field, <see cref="AboveBelow"/> is a <c>REF … \p</c>
/// field, and a foot/endnote number is a <c>NOTEREF</c> field.
/// </summary>
public enum CrossRefInsertAs
{
    /// <summary>The target's text (heading text, caption text, bookmark text). A plain <c>REF</c> field.</summary>
    Text,

    /// <summary>The page the target sits on. A <c>PAGEREF</c> field.</summary>
    PageNumber,

    /// <summary>The target heading's outline number (e.g. "1.2"). A <c>REF … \w</c> field.</summary>
    HeadingNumber,

    /// <summary>The relative position word "above"/"below". A <c>REF … \p</c> field.</summary>
    AboveBelow,

    /// <summary>The target's paragraph/list number (e.g. "1)"). A <c>REF … \n</c> field.</summary>
    ParagraphNumber
}

/// <summary>
/// One candidate cross-reference target. Pure data produced by <see cref="CrossReferences.Targets"/>.
/// </summary>
/// <param name="Display">
/// The human-readable label shown in the picker (e.g. a heading's text, a bookmark name, a caption's
/// text, or "Footnote 1").
/// </param>
/// <param name="Anchor">
/// The bookmark name this target can be linked to (so the inserted reference can be a clickable
/// internal link and a <c>REF</c>/<c>PAGEREF</c> field can resolve it), or null when the target has no
/// anchor yet. Bookmark targets are always anchored (the anchor is the bookmark name itself); headings
/// and captions carry an anchor only when their paragraph already has one.
/// </param>
/// <param name="BlockIndex">
/// The body block index of the originating paragraph (heading/bookmark/caption/numbered item), or null
/// for targets (footnotes/endnotes) that are not body blocks.
/// </param>
/// <param name="NoteId">
/// The foot/endnote id this target points at (so a <c>NOTEREF</c> field can resolve it), or null for
/// body-block targets.
/// </param>
public readonly record struct CrossRefTarget(string Display, string? Anchor, int? BlockIndex, int? NoteId = null);

/// <summary>
/// A cross-reference field carried by a <see cref="Run"/> via <see cref="Run.CrossReference"/> — Word's
/// Insert &gt; Cross-reference output. It serialises as a <c>w:fldSimple</c> whose <c>w:instr</c> is a
/// <c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c> instruction over a bookmark name (body targets) or a note id
/// (foot/endnote targets), optionally with a <c>\w</c>/<c>\n</c>/<c>\p</c> switch and a <c>\h</c>
/// hyperlink switch. The run's <see cref="Run.Text"/> doubles as the cached/last-resolved display value
/// so field-unaware consumers still render something. Mirrors <see cref="TableFormulaField"/>.
/// </summary>
/// <param name="Kind">REF, PAGEREF or NOTEREF — the field keyword.</param>
/// <param name="Target">
/// The bookmark name (REF/PAGEREF) the field resolves, or the note id as text (NOTEREF). Together with
/// <see cref="Kind"/> this is the field's first argument.
/// </param>
/// <param name="InsertAs">Which aspect of the target the field shows (text/page/number/above-below).</param>
/// <param name="Hyperlink">When true the field carries the <c>\h</c> switch (a clickable reference).</param>
public sealed record CrossReferenceField(
    CrossRefFieldKind Kind, string Target, CrossRefInsertAs InsertAs, bool Hyperlink);

/// <summary>Pure insertion data; the host applies <see cref="BookmarkNameToAdd"/> through its native mutation path.</summary>
public sealed record CrossReferenceInsertionPlan(
    CrossRefTarget Target,
    Run FieldRun,
    string? BookmarkNameToAdd);

/// <summary>The WordprocessingML field keyword a cross-reference uses.</summary>
public enum CrossRefFieldKind
{
    /// <summary>A <c>REF</c> field (text / heading number / paragraph number / above-below).</summary>
    Ref,

    /// <summary>A <c>PAGEREF</c> field (the target's page number).</summary>
    PageRef,

    /// <summary>A <c>NOTEREF</c> field (a foot/endnote's mark number).</summary>
    NoteRef
}

/// <summary>
/// Pure, WPF-free enumeration of cross-reference targets and the field/text a reference inserts. Lives
/// in the model project so it is fully unit-testable without any UI.
/// <para>
/// Targets are derived deterministically from existing document structure:
/// </para>
/// <list type="bullet">
/// <item><see cref="CrossRefType.Heading"/> — the heading paragraphs from <see cref="DocumentOutline"/>.</item>
/// <item><see cref="CrossRefType.Bookmark"/> — body paragraphs carrying a <see cref="Paragraph.BookmarkName"/>.</item>
/// <item><see cref="CrossRefType.Figure"/>/<see cref="CrossRefType.Table"/> — <c>Caption</c>-styled paragraphs.</item>
/// <item><see cref="CrossRefType.Footnote"/>/<see cref="CrossRefType.Endnote"/> — the note stores.</item>
/// <item><see cref="CrossRefType.NumberedItem"/> — paragraphs in a numbered/multilevel list.</item>
/// </list>
/// </summary>
public static class CrossReferences
{
    /// <summary>
    /// Enumerates the cross-reference targets of <paramref name="type"/> in <paramref name="doc"/>, in
    /// document order (notes ordered by ascending id). Returns an empty list when the document has no
    /// targets of that type. Deterministic and side-effect free.
    /// </summary>
    public static IReadOnlyList<CrossRefTarget> Targets(TextDocument doc, CrossRefType type)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return type switch
        {
            CrossRefType.Heading => HeadingTargets(doc),
            CrossRefType.Bookmark => BookmarkTargets(doc),
            CrossRefType.Figure => CaptionTargets(doc, CaptionLabel.Figure),
            CrossRefType.Table => CaptionTargets(doc, CaptionLabel.Table),
            CrossRefType.Equation => CaptionTargets(doc, CaptionLabel.Equation),
            CrossRefType.Footnote => NoteTargets(doc.Footnotes.Keys, "Footnote"),
            CrossRefType.Endnote => NoteTargets(doc.Endnotes.Keys, "Endnote"),
            CrossRefType.NumberedItem => NumberedItemTargets(doc),
            _ => []
        };
    }

    /// <summary>
    /// The "Insert reference to" choices valid for <paramref name="type"/>, in the order Word lists them.
    /// Foot/endnotes offer their note number and page; numbered items and headings add the number/position
    /// options; bookmarks/captions offer text, page, and (for captions) the caption's number machinery via
    /// paragraph number. Always non-empty (every type at least offers a usable option).
    /// </summary>
    public static IReadOnlyList<CrossRefInsertAs> InsertOptions(CrossRefType type) => type switch
    {
        CrossRefType.Heading =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.HeadingNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.Bookmark =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.ParagraphNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.Figure or CrossRefType.Table or CrossRefType.Equation =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.ParagraphNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.Footnote or CrossRefType.Endnote =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.NumberedItem =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.ParagraphNumber, CrossRefInsertAs.AboveBelow],
        _ => [CrossRefInsertAs.Text]
    };

    /// <summary>The field keyword that backs <paramref name="insertAs"/> for the given <paramref name="type"/>.</summary>
    public static CrossRefFieldKind FieldKindFor(CrossRefType type, CrossRefInsertAs insertAs)
    {
        if (insertAs == CrossRefInsertAs.PageNumber)
            return CrossRefFieldKind.PageRef;
        if (type is CrossRefType.Footnote or CrossRefType.Endnote && insertAs == CrossRefInsertAs.Text)
            return CrossRefFieldKind.NoteRef; // a note's "text" is its mark number
        return CrossRefFieldKind.Ref;
    }

    /// <summary>
    /// The text a reference to <paramref name="target"/> inserts: its <see cref="CrossRefTarget.Display"/>
    /// label (the heading/caption text, the bookmark name, or "Footnote N").
    /// </summary>
    public static string ReferenceText(CrossRefTarget target) => target.Display;

    /// <summary>
    /// Builds the cross-reference field for inserting <paramref name="insertAs"/> of
    /// <paramref name="target"/> (of <paramref name="type"/>), optionally as a clickable hyperlink. The
    /// field's <c>Target</c> is the target's bookmark anchor (body targets) or note id (foot/endnotes); a
    /// caller that wants a clickable/resolvable field must ensure the target carries an
    /// <see cref="CrossRefTarget.Anchor"/> first (see the app layer's anchor-ensuring helper).
    /// </summary>
    public static CrossReferenceField BuildField(
        CrossRefType type, CrossRefTarget target, CrossRefInsertAs insertAs, bool hyperlink)
    {
        var kind = FieldKindFor(type, insertAs);
        var argument = kind == CrossRefFieldKind.NoteRef
            ? (target.NoteId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            : (target.Anchor ?? string.Empty);
        return new CrossReferenceField(kind, argument, insertAs, hyperlink);
    }

    /// <summary>Plans the target, field run, cached text, and any missing body anchor without mutation.</summary>
    public static CrossReferenceInsertionPlan PlanInsertion(
        TextDocument doc,
        CrossRefType type,
        CrossRefTarget target,
        CrossRefInsertAs insertAs,
        bool hyperlink,
        int sourceBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var needsAnchor = FieldKindFor(type, insertAs) != CrossRefFieldKind.NoteRef
            && string.IsNullOrEmpty(target.Anchor)
            && target.BlockIndex is { } targetBlock
            && targetBlock >= 0
            && targetBlock < doc.Blocks.Count
            && doc.Blocks[targetBlock] is Paragraph;
        var bookmarkNameToAdd = needsAnchor ? AllocateCrossReferenceAnchor(doc) : null;
        var resolved = bookmarkNameToAdd is null ? target : target with { Anchor = bookmarkNameToAdd };
        var field = BuildField(type, resolved, insertAs, hyperlink);
        return new CrossReferenceInsertionPlan(
            resolved,
            Run.CrossReferenceFieldRun(field, ResolveText(doc, type, resolved, insertAs, sourceBlockIndex)),
            bookmarkNameToAdd);
    }

    /// <summary>
    /// The cached display text a freshly-inserted reference shows, computed from <paramref name="doc"/>.
    /// <see cref="CrossRefInsertAs.Text"/> is the target's text/mark; <see cref="CrossRefInsertAs.AboveBelow"/>
    /// is "above"/"below" relative to <paramref name="sourceBlockIndex"/>; the number options use the
    /// outline/list numbering; <see cref="CrossRefInsertAs.PageNumber"/> falls back to "1" (real pagination
    /// is an app-layer concern). Deterministic and side-effect free.
    /// </summary>
    public static string ResolveText(
        TextDocument doc, CrossRefType type, CrossRefTarget target, CrossRefInsertAs insertAs, int sourceBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return insertAs switch
        {
            CrossRefInsertAs.Text => target.Display,
            CrossRefInsertAs.PageNumber => "1",
            CrossRefInsertAs.HeadingNumber => HeadingNumberAt(doc, target.BlockIndex),
            CrossRefInsertAs.ParagraphNumber => ParagraphNumberAt(doc, target.BlockIndex),
            CrossRefInsertAs.AboveBelow => AboveBelow(target.BlockIndex, sourceBlockIndex),
            _ => target.Display
        };
    }

    /// <summary>
    /// Recomputes the cached display text for an existing <see cref="Run.CrossReference"/> field against
    /// the current document. Dangling bookmarks/notes and unsupported combinations preserve
    /// <paramref name="cached"/>, matching Word's field-result fallback behavior.
    /// </summary>
    /// <param name="doc">The document whose current bookmarks, headings, lists, and notes are inspected.</param>
    /// <param name="field">The modeled REF/PAGEREF/NOTEREF field carried by the run.</param>
    /// <param name="cached">The run's previous display text, returned when the target cannot resolve.</param>
    /// <param name="sourceBlockIndex">The body block containing the field run, used by above/below.</param>
    /// <param name="pageOf">
    /// Optional page-number resolver mapping a target body block index to its 1-based page. Null, or a
    /// null return, keeps the model default of page 1 because the core model has no pagination.
    /// </param>
    /// <param name="pageTextOf">
    /// Optional display-text resolver for the target page. Hosts use this for section restarts, Roman or
    /// letter formats, and chapter prefixes. A null or empty result falls back to <paramref name="pageOf"/>.
    /// </param>
    public static string ResolveField(
        TextDocument doc,
        CrossReferenceField field,
        string cached,
        int sourceBlockIndex,
        Func<int, int?>? pageOf = null,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind switch
        {
            CrossRefFieldKind.Ref => ResolveBookmarkedRef(doc, field, cached, sourceBlockIndex),
            CrossRefFieldKind.PageRef => ResolveBookmarkedPageRef(doc, field, cached, pageOf, pageTextOf),
            CrossRefFieldKind.NoteRef => ResolveNoteRef(doc, field, cached),
            _ => cached
        };
    }

    /// <summary>
    /// Resolves the 1-based physical page containing the start of a body block from authored page and
    /// section boundaries alone. Returns null when the document has no explicit page boundary, so live
    /// hosts can distinguish exact package evidence from an unpaginated one-page guess.
    /// </summary>
    public static int? ExplicitPageNumberAtBlock(TextDocument doc, int blockIndex)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (blockIndex < 0 || blockIndex >= doc.Blocks.Count || !HasExplicitPageBoundary(doc))
            return null;

        var pageNumber = 1;
        for (var index = 0; index <= blockIndex; index++)
        {
            if (doc.Blocks[index] is not Paragraph paragraph)
                continue;

            if (paragraph.Formatting.PageBreakBefore)
                pageNumber++;

            if (index == blockIndex)
                return pageNumber;

            foreach (var run in paragraph.Runs)
            {
                if (run.IsPageBreak)
                    pageNumber++;
            }

            if (paragraph.SectionBreak is { } sectionBreak)
                pageNumber = AdvanceForSectionBreak(pageNumber, sectionBreak.BreakKind);
        }

        return pageNumber;
    }

    private static bool HasExplicitPageBoundary(TextDocument doc) =>
        doc.Blocks.OfType<Paragraph>().Any(paragraph =>
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

    private static List<CrossRefTarget> HeadingTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        foreach (var entry in DocumentOutline.Of(doc))
            targets.Add(new CrossRefTarget(entry.Text, AnchorAt(doc, entry.BlockIndex), entry.BlockIndex));
        return targets;
    }

    private static List<CrossRefTarget> BookmarkTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph { BookmarkName: { Length: > 0 } name } && seen.Add(name))
                targets.Add(new CrossRefTarget(name, name, i));
        }
        return targets;
    }

    private static List<CrossRefTarget> CaptionTargets(TextDocument doc, CaptionLabel label)
    {
        var prefix = Captions.LabelText(label) + " ";
        var targets = new List<CrossRefTarget>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph
                && Captions.IsCaptionParagraph(paragraph)
                && paragraph.PlainText.StartsWith(prefix, StringComparison.Ordinal))
                targets.Add(new CrossRefTarget(paragraph.PlainText, AnchorAt(doc, i), i));
        }
        return targets;
    }

    private static List<CrossRefTarget> NoteTargets(IEnumerable<int> ids, string label)
    {
        var targets = new List<CrossRefTarget>();
        foreach (var id in ids.OrderBy(k => k))
            targets.Add(new CrossRefTarget(
                label + " " + id.ToString(CultureInfo.InvariantCulture), Anchor: null, BlockIndex: null, NoteId: id));
        return targets;
    }

    private static List<CrossRefTarget> NumberedItemTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph
                && paragraph.Formatting.ListKind is ListKind.Number or ListKind.MultiLevel
                && paragraph.PlainText.Length > 0)
                targets.Add(new CrossRefTarget(paragraph.PlainText, AnchorAt(doc, i), i));
        }
        return targets;
    }

    // The outline number of the heading at blockIndex (e.g. "2.1"): one segment per outline level, each
    // counting same-or-deeper-level resets among the preceding headings. Empty when not a heading.
    private static string HeadingNumberAt(TextDocument doc, int? blockIndex)
    {
        if (blockIndex is not { } target)
            return string.Empty;

        var counters = new List<int>();
        foreach (var entry in DocumentOutline.Of(doc))
        {
            var level = Math.Max(1, entry.Level); // Title (level 0) shares the top counter slot
            while (counters.Count < level)
                counters.Add(0);
            while (counters.Count > level)
                counters.RemoveAt(counters.Count - 1);
            counters[level - 1]++;

            if (entry.BlockIndex == target)
                return string.Join('.', counters);
        }
        return string.Empty;
    }

    // The 1-based ordinal of the numbered-list paragraph at blockIndex among the run of numbered items it
    // belongs to, formatted as "N)". A break in numbered-list paragraphs restarts the count. Empty when the
    // paragraph is not a numbered item.
    private static string ParagraphNumberAt(TextDocument doc, int? blockIndex)
    {
        if (blockIndex is not { } target)
            return string.Empty;

        var blocks = doc.Blocks;
        var count = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var numbered = blocks[i] is Paragraph p
                && p.Formatting.ListKind is ListKind.Number or ListKind.MultiLevel;
            count = numbered ? count + 1 : 0;
            if (i == target)
                return numbered ? count.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
        }
        return string.Empty;
    }

    private static string ResolveBookmarkedRef(
        TextDocument doc, CrossReferenceField field, string cached, int sourceBlockIndex)
    {
        if (FindBookmarkBlock(doc, field.Target) is not { } targetBlock)
            return cached;

        return field.InsertAs switch
        {
            CrossRefInsertAs.Text => NonEmptyOrCached(ParagraphTextAt(doc, targetBlock), cached),
            CrossRefInsertAs.HeadingNumber => NonEmptyOrCached(HeadingNumberAt(doc, targetBlock), cached),
            CrossRefInsertAs.ParagraphNumber => NonEmptyOrCached(ParagraphNumberAt(doc, targetBlock), cached),
            CrossRefInsertAs.AboveBelow => AboveBelow(targetBlock, sourceBlockIndex),
            _ => cached
        };
    }

    private static string ResolveBookmarkedPageRef(
        TextDocument doc,
        CrossReferenceField field,
        string cached,
        Func<int, int?>? pageOf,
        Func<int, string?>? pageTextOf)
    {
        if (FindBookmarkBlock(doc, field.Target) is not { } targetBlock)
            return cached;

        if (pageTextOf?.Invoke(targetBlock) is { Length: > 0 } pageText)
            return pageText;

        var page = pageOf?.Invoke(targetBlock) ?? 1;
        return Math.Max(1, page).ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveNoteRef(TextDocument doc, CrossReferenceField field, string cached)
    {
        if (!int.TryParse(field.Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return cached;

        if (doc.Footnotes.ContainsKey(id))
            return NoteDisplayNumber(doc.Footnotes.Keys, id, doc.FootnoteNumbering, cached);
        if (doc.Endnotes.ContainsKey(id))
            return NoteDisplayNumber(doc.Endnotes.Keys, id, doc.EndnoteNumbering, cached);

        return cached;
    }

    private static int? FindBookmarkBlock(TextDocument doc, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var location in Bookmarks.List(doc))
        {
            if (string.Equals(location.Name, name, StringComparison.Ordinal)
                && location.BlockIndex >= 0
                && location.BlockIndex < doc.Blocks.Count
                && doc.Blocks[location.BlockIndex] is Paragraph)
                return location.BlockIndex;
        }

        return null;
    }

    private static string ParagraphTextAt(TextDocument doc, int blockIndex) =>
        doc.Blocks[blockIndex] is Paragraph paragraph ? paragraph.PlainText.TrimEnd() : string.Empty;

    private static string NoteDisplayNumber(
        IEnumerable<int> ids, int targetId, NoteNumberingOptions options, string cached)
    {
        var sequence = Math.Max(1, options.StartAt);
        foreach (var id in ids.OrderBy(k => k))
        {
            if (id == targetId)
                return FormatNoteNumber(sequence, options.NumberFormat);
            sequence++;
        }

        return cached;
    }

    private static string FormatNoteNumber(int value, NoteNumberFormat format)
    {
        var n = Math.Max(1, value);
        return format switch
        {
            NoteNumberFormat.LowerRoman => ToRoman(n).ToLowerInvariant(),
            NoteNumberFormat.UpperRoman => ToRoman(n),
            NoteNumberFormat.LowerLetter => ToLetter(n, lower: true),
            NoteNumberFormat.UpperLetter => ToLetter(n, lower: false),
            NoteNumberFormat.Chicago => ToChicago(n),
            _ => n.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string ToRoman(int value)
    {
        (int Value, string Symbol)[] map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];

        var remaining = Math.Clamp(value, 1, 3999);
        var result = string.Empty;
        foreach (var (number, symbol) in map)
        {
            while (remaining >= number)
            {
                result += symbol;
                remaining -= number;
            }
        }

        return result;
    }

    private static string ToLetter(int value, bool lower)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var chars = new List<char>();
        while (value > 0)
        {
            value--;
            chars.Insert(0, (char)((lower ? 'a' : 'A') + value % 26));
            value /= 26;
        }

        return new string(chars.ToArray());
    }

    private static string ToChicago(int value)
    {
        string[] symbols = ["*", "+", "#", "S", "P"];
        var symbol = symbols[(value - 1) % symbols.Length];
        var repeat = (value - 1) / symbols.Length + 1;
        return string.Concat(Enumerable.Repeat(symbol, repeat));
    }

    private static string NonEmptyOrCached(string value, string cached) =>
        value.Length > 0 ? value : cached;

    private static string AboveBelow(int? targetBlockIndex, int sourceBlockIndex) =>
        targetBlockIndex is { } target && target > sourceBlockIndex ? "below" : "above";

    // The bookmark name on the body paragraph at blockIndex, or null when it carries none.
    private static string? AnchorAt(TextDocument doc, int blockIndex) =>
        blockIndex >= 0 && blockIndex < doc.Blocks.Count
        && doc.Blocks[blockIndex] is Paragraph { BookmarkName: { Length: > 0 } name }
            ? name
            : null;

    private static string AllocateCrossReferenceAnchor(TextDocument doc)
    {
        var used = new HashSet<string>(
            doc.Blocks.OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.BookmarkNames)
                .Where(name => name is { Length: > 0 })!,
            StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var name = "_Ref" + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(name))
                return name;
        }
    }
}
