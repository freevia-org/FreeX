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
/// field, caption variants are plain <c>REF</c> fields over distinct bookmark spans, and a foot/endnote
/// number is a <c>NOTEREF</c> field.
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
    ParagraphNumber,

    /// <summary>A caption's label and sequence number without its descriptive text.</summary>
    CaptionLabelAndNumber,

    /// <summary>A caption's descriptive text without its label, number, or separator.</summary>
    CaptionText
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
/// The top-level body block index of the originating paragraph. For a foot/endnote inside a table this is
/// the owning table block; otherwise it is the marker paragraph itself.
/// </param>
/// <param name="NoteId">
/// The foot/endnote id this target points at (so a <c>NOTEREF</c> field can resolve it), or null for
/// body-block targets.
/// </param>
/// <param name="RunIndex">
/// For a foot/endnote target, the run index of its physical note-reference marker. Null for body targets
/// and for legacy notes whose marker cannot be located in the body.
/// </param>
public readonly record struct CrossRefTarget(
    string Display,
    string? Anchor,
    int? BlockIndex,
    int? NoteId = null,
    int? RunIndex = null);

/// <summary>
/// A cross-reference field carried by a <see cref="Run"/> via <see cref="Run.CrossReference"/> — Word's
/// Insert &gt; Cross-reference output. It serialises as a <c>w:fldSimple</c> whose <c>w:instr</c> is a
/// <c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c> instruction over a bookmark name, optionally with a
/// <c>\w</c>/<c>\n</c>/<c>\p</c> switch and a <c>\h</c>
/// hyperlink switch. The run's <see cref="Run.Text"/> doubles as the cached/last-resolved display value
/// so field-unaware consumers still render something. Legacy numeric NOTEREF operands remain readable.
/// Mirrors <see cref="TableFormulaField"/>.
/// </summary>
/// <param name="Kind">REF, PAGEREF or NOTEREF — the field keyword.</param>
/// <param name="Target">
/// The bookmark name the field resolves. Legacy imported NOTEREF fields may carry a note id as text.
/// Together with <see cref="Kind"/> this is the field's first argument.
/// </param>
/// <param name="InsertAs">Which aspect of the target the field shows (text/page/number/above-below).</param>
/// <param name="Hyperlink">When true the field carries the <c>\h</c> switch (a clickable reference).</param>
public sealed record CrossReferenceField(
    CrossRefFieldKind Kind, string Target, CrossRefInsertAs InsertAs, bool Hyperlink);

/// <summary>Pure insertion data; the host applies the optional target bookmark through its native mutation path.</summary>
public sealed record CrossReferenceInsertionPlan(
    CrossRefTarget Target,
    Run FieldRun,
    string? BookmarkNameToAdd,
    int? TargetRunIndex = null,
    int? TargetNoteId = null,
    bool? TargetIsFootnote = null,
    int? TargetTextStartOffset = null,
    int? TargetTextEndOffset = null);

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
            CrossRefType.Footnote => NoteTargets(doc, doc.Footnotes.Keys, "Footnote", footnote: true),
            CrossRefType.Endnote => NoteTargets(doc, doc.Endnotes.Keys, "Endnote", footnote: false),
            CrossRefType.NumberedItem => NumberedItemTargets(doc),
            _ => []
        };
    }

    /// <summary>
    /// The "Insert reference to" choices valid for <paramref name="type"/>, in the order Word lists them.
    /// Foot/endnotes offer their note number and page; numbered items and headings add the number/position
    /// options; captions offer entire-caption, label-and-number, caption-text, page, and position variants.
    /// Always non-empty (every type at least offers a usable option).
    /// </summary>
    public static IReadOnlyList<CrossRefInsertAs> InsertOptions(CrossRefType type) => type switch
    {
        CrossRefType.Heading =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.HeadingNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.Bookmark =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.ParagraphNumber, CrossRefInsertAs.AboveBelow],
        CrossRefType.Figure or CrossRefType.Table or CrossRefType.Equation =>
            [CrossRefInsertAs.Text, CrossRefInsertAs.CaptionLabelAndNumber, CrossRefInsertAs.CaptionText,
                CrossRefInsertAs.PageNumber, CrossRefInsertAs.AboveBelow],
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
        if (type is CrossRefType.Footnote or CrossRefType.Endnote
            && insertAs is CrossRefInsertAs.Text or CrossRefInsertAs.AboveBelow)
            return CrossRefFieldKind.NoteRef;
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
        var argument = target.Anchor
            ?? (kind == CrossRefFieldKind.NoteRef
                ? target.NoteId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty);
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

        var isNoteTarget = type is CrossRefType.Footnote or CrossRefType.Endnote
            && target.NoteId is not null
            && target.RunIndex is not null;
        var isBodyParagraphTarget = target.BlockIndex is { } targetBlock
            && targetBlock >= 0
            && targetBlock < doc.Blocks.Count
            && doc.Blocks[targetBlock] is Paragraph;
        var captionRange = CaptionRangeFor(doc, type, target, insertAs);
        var selectedAnchor = captionRange is { } range
            ? FindBookmarkForTextRange((Paragraph)doc.Blocks[target.BlockIndex!.Value], range.Start, range.End)
            : target.Anchor;
        var needsAnchor = string.IsNullOrEmpty(selectedAnchor)
            && (isNoteTarget || isBodyParagraphTarget);
        var bookmarkNameToAdd = needsAnchor ? AllocateCrossReferenceAnchor(doc) : null;
        var resolved = target with { Anchor = bookmarkNameToAdd ?? selectedAnchor };
        var field = BuildField(type, resolved, insertAs, hyperlink);
        return new CrossReferenceInsertionPlan(
            resolved,
            Run.CrossReferenceFieldRun(field, ResolveText(doc, type, resolved, insertAs, sourceBlockIndex)),
            bookmarkNameToAdd,
            bookmarkNameToAdd is null ? null : resolved.RunIndex,
            bookmarkNameToAdd is null || !isNoteTarget ? null : resolved.NoteId,
            bookmarkNameToAdd is null || !isNoteTarget ? null : type == CrossRefType.Footnote,
            bookmarkNameToAdd is null ? null : captionRange?.Start,
            bookmarkNameToAdd is null ? null : captionRange?.End);
    }

    /// <summary>
    /// The cached display text a freshly-inserted reference shows, computed from <paramref name="doc"/>.
    /// <see cref="CrossRefInsertAs.Text"/> is the target's text/mark; <see cref="CrossRefInsertAs.AboveBelow"/>
    /// is "above"/"below" relative to <paramref name="sourceBlockIndex"/>; caption variants select their
    /// exact label/number or descriptive-text span; the number options use outline/list numbering; and
    /// <see cref="CrossRefInsertAs.PageNumber"/> falls back to "1" (real pagination is an app-layer concern).
    /// Deterministic and side-effect free.
    /// </summary>
    public static string ResolveText(
        TextDocument doc, CrossRefType type, CrossRefTarget target, CrossRefInsertAs insertAs, int sourceBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return insertAs switch
        {
            CrossRefInsertAs.Text when type is CrossRefType.Footnote or CrossRefType.Endnote
                => ResolveNoteDisplayText(doc, type, target, target.Display),
            CrossRefInsertAs.Text => target.Display,
            CrossRefInsertAs.CaptionLabelAndNumber => CaptionTextFor(doc, type, target, labelAndNumber: true),
            CrossRefInsertAs.CaptionText => CaptionTextFor(doc, type, target, labelAndNumber: false),
            CrossRefInsertAs.PageNumber => "1",
            CrossRefInsertAs.HeadingNumber => HeadingNumberAt(doc, target.BlockIndex),
            CrossRefInsertAs.ParagraphNumber => ParagraphNumberAt(doc, target.BlockIndex),
            CrossRefInsertAs.AboveBelow when type is CrossRefType.Footnote or CrossRefType.Endnote
                => ResolveNoteDisplayText(doc, type, target, target.Display)
                    + " " + AboveBelow(target.BlockIndex, sourceBlockIndex),
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
        Func<int, string?>? pageTextOf = null,
        int? sourceRunIndex = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind switch
        {
            CrossRefFieldKind.Ref => ResolveBookmarkedRef(doc, field, cached, sourceBlockIndex),
            CrossRefFieldKind.PageRef => ResolveBookmarkedPageRef(doc, field, cached, pageOf, pageTextOf),
            CrossRefFieldKind.NoteRef => ResolveNoteRef(
                doc, field, cached, sourceBlockIndex, sourceRunIndex),
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
        var targets = new List<CrossRefTarget>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph
                && Captions.IsCaptionParagraph(paragraph)
                && TryCaptionRanges(paragraph, Captions.LabelText(label), out _, out _, out _))
                targets.Add(new CrossRefTarget(paragraph.PlainText, AnchorAt(doc, i), i));
        }
        return targets;
    }

    private static List<CrossRefTarget> NoteTargets(
        TextDocument doc,
        IEnumerable<int> ids,
        string label,
        bool footnote)
    {
        var targets = new List<CrossRefTarget>();
        foreach (var id in ids.OrderBy(k => k))
        {
            var marker = FindNoteMarker(doc, id, footnote);
            if (marker is null)
                continue;
            targets.Add(new CrossRefTarget(
                label + " " + id.ToString(CultureInfo.InvariantCulture),
                marker?.Anchor,
                marker?.BlockIndex,
                id,
                marker?.RunIndex));
        }
        return targets;
    }

    private static TextRange? CaptionRangeFor(
        TextDocument doc,
        CrossRefType type,
        CrossRefTarget target,
        CrossRefInsertAs insertAs)
    {
        if (type is not (CrossRefType.Figure or CrossRefType.Table or CrossRefType.Equation)
            || target.BlockIndex is not { } blockIndex
            || blockIndex < 0
            || blockIndex >= doc.Blocks.Count
            || doc.Blocks[blockIndex] is not Paragraph paragraph
            || !TryCaptionRanges(
                paragraph, CaptionLabelFor(type), out var whole, out var labelAndNumber, out var captionText))
        {
            return null;
        }

        return insertAs switch
        {
            CrossRefInsertAs.CaptionLabelAndNumber => labelAndNumber,
            CrossRefInsertAs.CaptionText => captionText,
            CrossRefInsertAs.Text or CrossRefInsertAs.PageNumber or CrossRefInsertAs.AboveBelow => whole,
            _ => null
        };
    }

    private static string CaptionTextFor(
        TextDocument doc,
        CrossRefType type,
        CrossRefTarget target,
        bool labelAndNumber)
    {
        var range = CaptionRangeFor(
            doc,
            type,
            target,
            labelAndNumber ? CrossRefInsertAs.CaptionLabelAndNumber : CrossRefInsertAs.CaptionText);
        if (range is not { } selected
            || target.BlockIndex is not { } blockIndex
            || doc.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return target.Display;
        }

        return paragraph.PlainText[selected.Start..selected.End];
    }

    private static bool TryCaptionRanges(
        Paragraph paragraph,
        string expectedLabel,
        out TextRange whole,
        out TextRange labelAndNumber,
        out TextRange captionText)
    {
        whole = default;
        labelAndNumber = default;
        captionText = default;
        if (!Captions.IsCaptionParagraph(paragraph))
            return false;

        var sequenceRunIndex = paragraph.Runs.FindIndex(run =>
            run.ComplexField is { Keyword: "SEQ" } field
            && string.Equals(SequenceLabel(field.Instruction), expectedLabel, StringComparison.Ordinal));
        if (sequenceRunIndex < 0)
            return false;

        var plainText = paragraph.PlainText;
        var labelEnd = paragraph.Runs.Take(sequenceRunIndex + 1).Sum(run => run.Text.Length);
        var textStart = labelEnd;
        while (textStart < plainText.Length && char.IsWhiteSpace(plainText[textStart]))
            textStart++;
        if (textStart < plainText.Length && plainText[textStart] is ':' or '.' or '-' or '\u2013' or '\u2014')
            textStart++;
        while (textStart < plainText.Length && char.IsWhiteSpace(plainText[textStart]))
            textStart++;

        whole = new TextRange(0, plainText.Length);
        labelAndNumber = new TextRange(0, labelEnd);
        captionText = new TextRange(textStart, plainText.Length);
        return true;
    }

    private static string CaptionLabelFor(CrossRefType type) => type switch
    {
        CrossRefType.Figure => Captions.FigureLabelText,
        CrossRefType.Table => Captions.TableLabelText,
        CrossRefType.Equation => Captions.EquationLabelText,
        _ => string.Empty
    };

    private static string SequenceLabel(string instruction)
    {
        var span = instruction.AsSpan().Trim();
        var keywordEnd = span.IndexOfAny(' ', '\t', '\\');
        if (keywordEnd < 0)
            return string.Empty;
        span = span[keywordEnd..].TrimStart();
        if (span.Length == 0)
            return string.Empty;
        if (span[0] == '"')
        {
            var closingQuote = span[1..].IndexOf('"');
            return closingQuote < 0 ? string.Empty : span.Slice(1, closingQuote).ToString();
        }

        var end = span.IndexOfAny(' ', '\t', '\\');
        return (end < 0 ? span : span[..end]).ToString();
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
    // Internal so ComplexFieldEngine's STYLEREF \n switch can reuse the same outline-number computation.
    internal static string HeadingNumberAt(TextDocument doc, int? blockIndex)
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
    // belongs to, formatted as "N)". A break in numbered-list paragraphs restarts the count, and so does a
    // paragraph carrying ListStartOverride (docx's w:lvlOverride/startOverride) -- the same restart rule
    // DocumentListMarkerSequencePlanner uses for the on-screen list markers, shared here via
    // ListRestartCounter so the two never diverge. Empty when the paragraph is not a numbered item.
    private static string ParagraphNumberAt(TextDocument doc, int? blockIndex)
    {
        if (blockIndex is not { } target)
            return string.Empty;

        var blocks = doc.Blocks;
        var count = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var paragraph = blocks[i] as Paragraph;
            var numbered = paragraph?.Formatting.ListKind is ListKind.Number or ListKind.MultiLevel;
            count = numbered ? ListRestartCounter.NextCount(count, paragraph!.Formatting.ListStartOverride) : 0;
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
            CrossRefInsertAs.Text or CrossRefInsertAs.CaptionLabelAndNumber or CrossRefInsertAs.CaptionText
                => TryBookmarkedText(doc, field.Target, out var bookmarkedText) ? bookmarkedText : cached,
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

    private static string ResolveNoteRef(
        TextDocument doc,
        CrossReferenceField field,
        string cached,
        int sourceBlockIndex,
        int? sourceRunIndex)
    {
        var marker = FindBookmarkedNoteMarker(doc, field.Target);
        if (marker is null
            && int.TryParse(field.Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyId))
        {
            marker = doc.Footnotes.ContainsKey(legacyId)
                ? FindNoteMarker(doc, legacyId, footnote: true)
                    ?? new NoteMarker(legacyId, Footnote: true, BlockIndex: null, RunIndex: null, Anchor: null)
                : doc.Endnotes.ContainsKey(legacyId)
                    ? FindNoteMarker(doc, legacyId, footnote: false)
                        ?? new NoteMarker(legacyId, Footnote: false, BlockIndex: null, RunIndex: null, Anchor: null)
                    : null;
        }

        if (marker is not { } noteMarker)
            return cached;

        var number = noteMarker.Footnote
            ? NoteDisplayNumber(doc.Footnotes.Keys, noteMarker.Id, doc.FootnoteNumbering, cached)
            : NoteDisplayNumber(doc.Endnotes.Keys, noteMarker.Id, doc.EndnoteNumbering, cached);
        return field.InsertAs == CrossRefInsertAs.AboveBelow
            ? number + " " + AboveBelow(
                noteMarker.BlockIndex,
                noteMarker.RunIndex,
                sourceBlockIndex,
                sourceRunIndex)
            : number;
    }

    private static string ResolveNoteDisplayText(
        TextDocument doc,
        CrossRefType type,
        CrossRefTarget target,
        string cached)
    {
        if (target.NoteId is not { } id)
            return cached;

        return type == CrossRefType.Footnote
            ? NoteDisplayNumber(doc.Footnotes.Keys, id, doc.FootnoteNumbering, cached)
            : NoteDisplayNumber(doc.Endnotes.Keys, id, doc.EndnoteNumbering, cached);
    }

    private static NoteMarker? FindNoteMarker(TextDocument doc, int id, bool footnote)
    {
        for (var blockIndex = 0; blockIndex < doc.Blocks.Count; blockIndex++)
        {
            foreach (var paragraph in ParagraphsIn(doc.Blocks[blockIndex]))
            {
                for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                {
                    var run = paragraph.Runs[runIndex];
                    if ((footnote ? run.FootnoteId : run.EndnoteId) != id)
                        continue;

                    return new NoteMarker(
                        id,
                        footnote,
                        blockIndex,
                        runIndex,
                        FindBookmarkAroundRun(paragraph, runIndex));
                }
            }
        }

        return null;
    }

    private static NoteMarker? FindBookmarkedNoteMarker(TextDocument doc, string bookmarkName)
    {
        if (string.IsNullOrEmpty(bookmarkName))
            return null;

        for (var blockIndex = 0; blockIndex < doc.Blocks.Count; blockIndex++)
        {
            foreach (var paragraph in ParagraphsIn(doc.Blocks[blockIndex]))
            {
                if (!paragraph.BookmarkNames.Contains(bookmarkName, StringComparer.Ordinal))
                    continue;

                var start = paragraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                    boundary.Kind == BookmarkBoundaryKind.Start
                    && string.Equals(boundary.Name, bookmarkName, StringComparison.Ordinal));
                var end = start is null
                    ? null
                    : paragraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                        boundary.Kind == BookmarkBoundaryKind.End
                        && string.Equals(boundary.PairKey, start.PairKey, StringComparison.Ordinal));
                var from = Math.Clamp(start?.RunIndex ?? 0, 0, paragraph.Runs.Count);
                var to = Math.Clamp(end?.RunIndex ?? paragraph.Runs.Count, from, paragraph.Runs.Count);

                var markers = Enumerable.Range(from, to - from)
                    .Select(runIndex => (RunIndex: runIndex, Run: paragraph.Runs[runIndex]))
                    .Where(item => item.Run.FootnoteId is not null || item.Run.EndnoteId is not null)
                    .ToList();
                if (markers.Count != 1)
                    continue;

                var marker = markers[0];
                if (marker.Run.FootnoteId is { } footnoteId)
                    return new NoteMarker(footnoteId, true, blockIndex, marker.RunIndex, bookmarkName);
                if (marker.Run.EndnoteId is { } endnoteId)
                    return new NoteMarker(endnoteId, false, blockIndex, marker.RunIndex, bookmarkName);
            }
        }

        return null;
    }

    private static string? FindBookmarkAroundRun(Paragraph paragraph, int runIndex)
    {
        foreach (var start in paragraph.BookmarkBoundaries.Where(boundary =>
                     boundary.Kind == BookmarkBoundaryKind.Start
                     && boundary.Name is { Length: > 0 } name
                     && paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal)
                     && boundary.RunIndex <= runIndex))
        {
            var end = paragraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                boundary.Kind == BookmarkBoundaryKind.End
                && string.Equals(boundary.PairKey, start.PairKey, StringComparison.Ordinal)
                && boundary.RunIndex > runIndex);
            if (end is not null)
            {
                var from = Math.Clamp(start.RunIndex, 0, paragraph.Runs.Count);
                var to = Math.Clamp(end.RunIndex, from, paragraph.Runs.Count);
                if (paragraph.Runs.Skip(from).Take(to - from)
                    .Count(run => run.FootnoteId is not null || run.EndnoteId is not null) == 1)
                {
                    return start.Name;
                }
            }
        }

        return null;
    }

    private readonly record struct NoteMarker(
        int Id,
        bool Footnote,
        int? BlockIndex,
        int? RunIndex,
        string? Anchor);

    private static IEnumerable<Paragraph> ParagraphsIn(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;

        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;
                foreach (var nestedTable in cell.NestedTables)
                    foreach (var nestedParagraph in ParagraphsIn(nestedTable))
                        yield return nestedParagraph;
            }
        }
    }

    private static int? FindBookmarkBlock(TextDocument doc, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        for (var blockIndex = 0; blockIndex < doc.Blocks.Count; blockIndex++)
        {
            if (ParagraphsIn(doc.Blocks[blockIndex]).Any(paragraph =>
                    paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal)))
                return blockIndex;
        }

        return null;
    }

    private static bool TryBookmarkedText(TextDocument doc, string name, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(name))
            return false;

        var paragraphs = doc.Blocks.SelectMany(ParagraphsIn).ToList();
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            var paragraph = paragraphs[paragraphIndex];
            if (!paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal))
                continue;

            var start = paragraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                boundary.Kind == BookmarkBoundaryKind.Start
                && string.Equals(boundary.Name, name, StringComparison.Ordinal));
            if (start is null)
            {
                text = paragraph.PlainText.TrimEnd();
                return true;
            }

            for (var endParagraphIndex = paragraphIndex; endParagraphIndex < paragraphs.Count; endParagraphIndex++)
            {
                var endParagraph = paragraphs[endParagraphIndex];
                var end = endParagraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                    boundary.Kind == BookmarkBoundaryKind.End
                    && string.Equals(boundary.PairKey, start.PairKey, StringComparison.Ordinal));
                if (end is null)
                    continue;

                var from = Math.Clamp(start.RunIndex, 0, paragraph.Runs.Count);
                var to = Math.Clamp(end.RunIndex, 0, endParagraph.Runs.Count);
                if (endParagraphIndex == paragraphIndex)
                {
                    to = Math.Max(from, to);
                    text = string.Concat(paragraph.Runs.Skip(from).Take(to - from).Select(run => run.Text));
                    return true;
                }

                var parts = new List<string>
                {
                    string.Concat(paragraph.Runs.Skip(from).Select(run => run.Text))
                };
                for (var index = paragraphIndex + 1; index < endParagraphIndex; index++)
                    parts.Add(paragraphs[index].PlainText);
                parts.Add(string.Concat(endParagraph.Runs.Take(to).Select(run => run.Text)));
                text = string.Join('\n', parts);
                return true;
            }

            return false;
        }

        return false;
    }

    private static string? FindBookmarkForTextRange(Paragraph paragraph, int startOffset, int endOffset)
    {
        var offsets = RunOffsets(paragraph);
        foreach (var start in paragraph.BookmarkBoundaries.Where(boundary =>
                     boundary.Kind == BookmarkBoundaryKind.Start
                     && boundary.Name is { Length: > 0 } name
                     && paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal)))
        {
            var end = paragraph.BookmarkBoundaries.FirstOrDefault(boundary =>
                boundary.Kind == BookmarkBoundaryKind.End
                && string.Equals(boundary.PairKey, start.PairKey, StringComparison.Ordinal));
            if (end is not null
                && offsets[Math.Clamp(start.RunIndex, 0, paragraph.Runs.Count)] == startOffset
                && offsets[Math.Clamp(end.RunIndex, 0, paragraph.Runs.Count)] == endOffset)
            {
                return start.Name;
            }
        }

        if (startOffset != 0 || endOffset != paragraph.PlainText.Length)
            return null;

        return paragraph.BookmarkNames.FirstOrDefault(name =>
            name is { Length: > 0 }
            && !paragraph.BookmarkBoundaries.Any(boundary =>
                boundary.Kind == BookmarkBoundaryKind.Start
                && string.Equals(boundary.Name, name, StringComparison.Ordinal)));
    }

    private static int[] RunOffsets(Paragraph paragraph)
    {
        var offsets = new int[paragraph.Runs.Count + 1];
        for (var index = 0; index < paragraph.Runs.Count; index++)
            offsets[index + 1] = offsets[index] + paragraph.Runs[index].Text.Length;
        return offsets;
    }

    private static string NoteDisplayNumber(
        IEnumerable<int> ids, int targetId, NoteNumberingOptions options, string cached)
    {
        var sequence = Math.Max(1, options.StartAt);
        foreach (var id in ids.OrderBy(k => k))
        {
            if (id == targetId)
                return NoteNumberFormatter.Format(sequence, options.NumberFormat);
            sequence++;
        }

        return cached;
    }

    private static string NonEmptyOrCached(string value, string cached) =>
        value.Length > 0 ? value : cached;

    private static string AboveBelow(int? targetBlockIndex, int sourceBlockIndex) =>
        targetBlockIndex is { } target && target > sourceBlockIndex ? "below" : "above";

    private static string AboveBelow(
        int? targetBlockIndex,
        int? targetRunIndex,
        int sourceBlockIndex,
        int? sourceRunIndex)
    {
        if (targetBlockIndex is not { } targetBlock)
            return "above";
        if (targetBlock != sourceBlockIndex)
            return targetBlock > sourceBlockIndex ? "below" : "above";
        return targetRunIndex is { } targetRun
            && sourceRunIndex is { } sourceRun
            && targetRun > sourceRun
                ? "below"
                : "above";
    }

    // The bookmark name on the body paragraph at blockIndex, or null when it carries none.
    private static string? AnchorAt(TextDocument doc, int blockIndex) =>
        blockIndex >= 0 && blockIndex < doc.Blocks.Count
        && doc.Blocks[blockIndex] is Paragraph { BookmarkName: { Length: > 0 } name }
            ? name
            : null;

    private readonly record struct TextRange(int Start, int End);

    private static string AllocateCrossReferenceAnchor(TextDocument doc)
    {
        var used = new HashSet<string>(
            doc.Blocks.SelectMany(ParagraphsIn)
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
