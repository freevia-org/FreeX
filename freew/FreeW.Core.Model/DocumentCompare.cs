using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// Which document the comparison result is shown in — Word's "Show changes in:" radio-button group.
/// FreeW always produces a new result document (the compare engine never mutates its inputs), so
/// <see cref="NewDocument"/> is the only substantively different option; the other two are kept for
/// round-trip fidelity so the dialog setting can be persisted.
/// </summary>
public enum CompareShowChangesIn
{
    /// <summary>Show the blackline result in a new, separate document (FreeW's only real option).</summary>
    NewDocument = 0,
    /// <summary>Word also supports loading the result back into the original document.</summary>
    Original = 1,
    /// <summary>Word also supports loading the result back into the revised document.</summary>
    Revised = 2
}

/// <summary>
/// Configuration options for <see cref="DocumentCompare.Compare"/> that mirror Word's "Comparison Settings"
/// expansion in the Compare Documents dialog. Every flag defaults to <c>true</c> (on), matching Word's
/// default — all change types are tracked. When a flag is <c>false</c> the corresponding kind of
/// difference is silently excluded from the output (no revision marks for it).
/// <para>
/// <see cref="Insertions"/>, <see cref="Deletions"/>, <see cref="CaseChanges"/>,
/// <see cref="Whitespace"/>, and <see cref="Formatting"/> affect FreeW's current comparison engine.
/// Formatting revisions are emitted when a paragraph's text and run boundaries are unchanged, so each
/// revised run can retain a precise previous-format snapshot. Unique unchanged paragraphs moved to a new
/// position receive paired move revisions. <see cref="Comments"/> preserves the original threads that
/// remain anchored in deleted whole-paragraph content, while allowing that deleted-side markup to be
/// omitted when comment comparison is disabled.
/// </para>
/// </summary>
public sealed class CompareSettings
{
    /// <summary>Track inserted text. Default: <c>true</c>.</summary>
    public bool Insertions { get; init; } = true;

    /// <summary>Track deleted text. Default: <c>true</c>.</summary>
    public bool Deletions { get; init; } = true;

    /// <summary>
    /// Track unique unchanged paragraphs that moved to a new position. Ambiguous or edited paragraphs
    /// remain ordinary deletion/insertion pairs. Default: <c>true</c>.
    /// </summary>
    public bool Moves { get; init; } = true;

    /// <summary>
    /// Preserve review-comment threads anchored in deleted original paragraphs. When disabled, removes
    /// only those deleted-side anchors; revised comments remain part of the comparison result. Default:
    /// <c>true</c>.
    /// </summary>
    public bool Comments { get; init; } = true;

    /// <summary>
    /// Track format-only changes in text-identical paragraphs. FreeW emits native run-format revisions
    /// when corresponding runs retain the same text boundaries. Default: <c>true</c>.
    /// </summary>
    public bool Formatting { get; init; } = true;

    /// <summary>Track case changes as differences. Default: <c>true</c>.</summary>
    public bool CaseChanges { get; init; } = true;

    /// <summary>Track whitespace changes as differences. Default: <c>true</c>.</summary>
    public bool Whitespace { get; init; } = true;

    /// <summary>Which document to show the result in. Default: <see cref="CompareShowChangesIn.NewDocument"/>.</summary>
    public CompareShowChangesIn ShowChangesIn { get; init; } = CompareShowChangesIn.NewDocument;

    /// <summary>The default settings — all change types enabled, result in a new document.</summary>
    public static readonly CompareSettings Default = new();
}

/// <summary>
/// Pure, WPF-free document comparison ("Compare Documents"). Diffs an <c>original</c> against a
/// <c>revised</c> document and produces a NEW document representing <c>revised</c> with the differences
/// marked as tracked changes (see <see cref="Run.Revision"/>): text only in revised is marked
/// <see cref="RevisionKind.Inserted"/>, text only in original is marked <see cref="RevisionKind.Deleted"/>
/// (kept in the result so it renders struck-through), and unchanged text stays an ordinary run.
///
/// Granularity is two-level and deterministic: a paragraph-level LCS matches paragraphs by their plain
/// text, then each paired-but-changed paragraph is diffed at word granularity with a second LCS so only
/// the changed words carry insertion/deletion marks. The author is stamped onto every produced revision;
/// the date is supplied by the caller (never <c>DateTime.Now</c>) so the helper stays pure/deterministic.
/// </summary>
public static class DocumentCompare
{
    /// <summary>
    /// Compare <paramref name="original"/> against <paramref name="revised"/> and return a new document
    /// that is <paramref name="revised"/> with the differences expressed as tracked changes attributed to
    /// <paramref name="author"/>. <paramref name="dateXml"/> is the W3CDTF revision timestamp to stamp on
    /// every produced revision (pass null to leave the date unset); it is never auto-generated here.
    /// Paragraph blocks are compared at word granularity. A table block is compared cell-by-cell against a
    /// same-shaped table sitting at the same ordinal position in <paramref name="original"/> (see
    /// <see cref="DiffTableIfMatched"/>); a table with no such counterpart, or one whose row/cell counts
    /// don't match, is carried through unchanged, like any other non-paragraph block in
    /// <paramref name="revised"/>.
    /// </summary>
    public static TextDocument Compare(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml = null) => Compare(original, revised, author, dateXml, CompareSettings.Default);

    /// <summary>
    /// Compare <paramref name="original"/> against <paramref name="revised"/> with the given
    /// <paramref name="settings"/> (which change types to track). <paramref name="settings"/> with
    /// <see cref="CompareSettings.Insertions"/> and/or <see cref="CompareSettings.Deletions"/> false will
    /// suppress the corresponding revision marks in the output. Disabling
    /// <see cref="CompareSettings.CaseChanges"/> and/or <see cref="CompareSettings.Whitespace"/> ignores only
    /// those differences while preserving revised text in the result. When <see cref="CompareSettings.Formatting"/>
    /// is enabled, format-only changes in text-identical paragraphs become native run-format revisions.
    /// When <see cref="CompareSettings.Moves"/> is enabled, unique unchanged paragraphs moved to a new
    /// position receive paired Word move revisions. When <see cref="CompareSettings.Comments"/> is enabled,
    /// comment threads anchored in deleted whole-paragraph content are retained; disabling it removes only
    /// those deleted-side anchors while retaining the revised document's comments.
    /// </summary>
    public static TextDocument Compare(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml,
        CompareSettings settings) => Compare(original, revised, author, dateXml, settings, out _);

    /// <summary>
    /// Same as <see cref="Compare(TextDocument,TextDocument,string,string?,CompareSettings)"/>, but also hands
    /// back the paragraph-level alignment the engine computed while building the result: for every result
    /// <see cref="Paragraph"/>, which index (if any) it corresponds to in <paramref name="original"/> and/or
    /// <paramref name="revised"/>. A result paragraph that is a whole-paragraph deletion has only an
    /// <see cref="ParagraphAlignment.OriginalIndex"/>; a whole-paragraph insertion has only a
    /// <see cref="ParagraphAlignment.RevisedIndex"/>; every other result paragraph (an unchanged anchor or a
    /// word-diffed pair) has both. <see cref="DocumentCombine"/> reuses this so it can align two independent
    /// blacklines by the shared spine paragraph they both derive from, instead of by raw list position.
    /// </summary>
    internal static TextDocument Compare(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml,
        CompareSettings settings,
        out IReadOnlyDictionary<Paragraph, ParagraphAlignment> alignment)
    {
        var alignmentSink = new Dictionary<Paragraph, ParagraphAlignment>();
        var result = CompareCore(original, revised, author, dateXml, settings, alignmentSink);
        alignment = alignmentSink;
        return result;
    }

    /// <summary>Which source-paragraph index(es) a produced comparison-result paragraph corresponds to.</summary>
    internal readonly record struct ParagraphAlignment(int? OriginalIndex, int? RevisedIndex);

    private static TextDocument CompareCore(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml,
        CompareSettings settings,
        Dictionary<Paragraph, ParagraphAlignment>? alignmentSink)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revised);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(settings);

        // Records a produced result paragraph's source index(es) for the caller-supplied alignment sink.
        // TryAdd guards against a paragraph object being tracked twice (it never legitimately is, but a
        // silent overwrite would be worse than a no-op if some future branch ever re-adds one).
        void Track(Paragraph paragraph, int? originalIndex, int? revisedIndex) =>
            alignmentSink?.TryAdd(paragraph, new ParagraphAlignment(originalIndex, revisedIndex));

        var result = new TextDocument();
        // Carry over the revised document's defaults, styles and page setup so the result renders like it.
        CopyDocumentShell(original, revised, result, author, dateXml, settings);

        // Whole-paragraph deletions below copy the original document's runs (and their StyleId) verbatim.
        // If original defines a style revised no longer has (renamed/removed), the deleted paragraph would
        // otherwise reference an id missing from result's style catalog. Backfill only the ids revised
        // doesn't already define, so revised's own style wins on any id both documents share.
        foreach (var (id, style) in original.Styles)
            result.Styles.TryAdd(id, style);

        var originalParagraphs = original.Blocks.OfType<Paragraph>().ToList();
        var revisedBlocks = revised.Blocks;
        var revisedParagraphs = revisedBlocks.OfType<Paragraph>().ToList();

        // Tables are matched by ordinal position among Table blocks only (the Nth table in revised pairs
        // with the Nth table in original, when one exists) -- a much narrower correspondence than the
        // paragraph-level LCS above, but sufficient to diff the common case (a table edited in place) while
        // never guessing at a structural table insertion/move. See DiffTableIfMatched.
        var originalTables = original.Blocks.OfType<Table>().ToList();
        var tableOrdinal = 0;

        // Paragraph-level LCS over plain text picks the "anchors": revised paragraphs whose text is exactly
        // an original paragraph's. Anchors copy through unchanged; the unmatched paragraphs that fall in the
        // gaps between anchors are paired up (and word-diffed) or, when unpaired, marked whole-insert/delete.
        var matches = LongestCommonSubsequence(
            originalParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList(),
            revisedParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList());

        var revisedAnchorToOriginal = new Dictionary<int, int>();
        foreach (var (originalIndex, revisedIndex) in matches)
            revisedAnchorToOriginal[revisedIndex] = originalIndex;

        var moveIds = FindWholeParagraphMoves(
            originalParagraphs,
            revisedParagraphs,
            matches,
            settings,
            revised.DoNotTrackMoves);

        // Drive the walk off the revised block order so non-paragraph blocks keep their place. Each revised
        // paragraph is either an anchor (identical to some original) or part of a "gap" since the previous
        // anchor; we buffer gap paragraphs and resolve them against the original gap when we hit the next
        // anchor (or the end). prevOriginalAnchor tracks how far into the original list we have consumed.
        var prevOriginalAnchor = -1; // index in originalParagraphs of the last consumed anchor
        var gapRevised = new List<(Paragraph Paragraph, int Index)>();
        var revisedParagraphOrdinal = 0;

        foreach (var block in revisedBlocks)
        {
            if (block is not Paragraph revisedParagraph)
            {
                // A non-paragraph block ends the current gap region: resolve buffered paragraphs up to the
                // end of the original list, then clone (or, for a matched table, diff) the block through in
                // place.
                ResolveGap(originalParagraphs.Count);
                if (block is Table revisedTable)
                {
                    var matchedOriginalTable = tableOrdinal < originalTables.Count ? originalTables[tableOrdinal] : null;
                    tableOrdinal++;
                    result.Blocks.Add(DiffTableIfMatched(matchedOriginalTable, revisedTable, author, dateXml, settings));
                }
                else
                {
                    result.Blocks.Add(CloneBlock(block));
                }
                continue;
            }

            var revisedIndex = revisedParagraphOrdinal++;
            if (revisedAnchorToOriginal.TryGetValue(revisedIndex, out var anchorOriginalIndex))
            {
                // Resolve the gap that precedes this anchor against the original paragraphs sitting between
                // the previous anchor and this one, then copy the anchor (identical text) through unchanged.
                ResolveGap(anchorOriginalIndex);
                var anchor = ClonePlainWithFormatRevisions(
                    originalParagraphs[anchorOriginalIndex],
                    revisedParagraph,
                    author,
                    dateXml,
                    settings.Formatting && !revised.DoNotTrackFormatting);
                result.Blocks.Add(anchor);
                Track(anchor, anchorOriginalIndex, revisedIndex);
                prevOriginalAnchor = anchorOriginalIndex;
            }
            else
            {
                gapRevised.Add((revisedParagraph, revisedIndex));
            }
        }

        // Resolve the trailing gap (everything after the last anchor) against the remaining originals.
        ResolveGap(originalParagraphs.Count);

        // Whole-paragraph deletions retain the original runs verbatim, including comment anchors. Carry
        // the matching original comment threads so those anchors remain valid when the comparison is saved.
        // If comment comparison is disabled, drop only the deleted-side markers; revised comments continue
        // to describe the resulting document.
        ReconcileDeletedCommentAnchors(original, result, settings.Comments);
        ReconcileDeletedNoteAnchors(original, result);
        ReconcileMatchedNoteContent(original, result, originalParagraphs, revisedParagraphs, matches, author, dateXml, settings);
        return result;

        // Resolve the currently-buffered revised gap paragraphs against the original paragraphs in
        // (prevOriginalAnchor, originalLimit). Paired positionally: each pair is word-diffed; surplus
        // original paragraphs become whole-paragraph deletions, surplus revised ones whole insertions.
        // Deletions are emitted before insertions so removed text reads ahead of the replacement.
        // When settings.Deletions is false, surplus original paragraphs are dropped (not carried as deletions).
        // When settings.Insertions is false, surplus revised paragraphs are copied through unmarked.
        void ResolveGap(int originalLimit)
        {
            var gapOriginal = new List<(Paragraph Paragraph, int Index)>();
            for (var i = prevOriginalAnchor + 1; i < originalLimit && i < originalParagraphs.Count; i++)
                gapOriginal.Add((originalParagraphs[i], i));

            var pairCount = Math.Min(gapOriginal.Count, gapRevised.Count);
            for (var i = 0; i < pairCount; i++)
            {
                var originalEntry = gapOriginal[i];
                var revisedEntry = gapRevised[i];
                var originalMoveId = moveIds.GetOriginalId(originalEntry.Index);
                var revisedMoveId = moveIds.GetRevisedId(revisedEntry.Index);
                if (originalMoveId is null && revisedMoveId is null)
                {
                    var diffed = DiffParagraph(
                        originalEntry.Paragraph,
                        revisedEntry.Paragraph,
                        author,
                        dateXml,
                        settings);
                    result.Blocks.Add(diffed);
                    Track(diffed, originalEntry.Index, revisedEntry.Index);
                    continue;
                }

                if (settings.Deletions)
                {
                    var deleted = MarkWholeParagraph(
                        originalEntry.Paragraph,
                        RevisionKind.Deleted,
                        author,
                        dateXml,
                        originalMoveId);
                    result.Blocks.Add(deleted);
                    Track(deleted, originalEntry.Index, null);
                }
                if (settings.Insertions)
                {
                    var inserted = MarkWholeParagraph(
                        revisedEntry.Paragraph,
                        RevisionKind.Inserted,
                        author,
                        dateXml,
                        revisedMoveId);
                    result.Blocks.Add(inserted);
                    Track(inserted, null, revisedEntry.Index);
                }
            }

            for (var i = pairCount; i < gapOriginal.Count; i++)
            {
                if (settings.Deletions)
                {
                    var deleted = MarkWholeParagraph(
                        gapOriginal[i].Paragraph,
                        RevisionKind.Deleted,
                        author,
                        dateXml,
                        moveIds.GetOriginalId(gapOriginal[i].Index));
                    result.Blocks.Add(deleted);
                    Track(deleted, gapOriginal[i].Index, null);
                }
                // When deletions are suppressed, the original-only paragraph is simply dropped.
            }

            for (var i = pairCount; i < gapRevised.Count; i++)
            {
                if (settings.Insertions)
                {
                    var inserted = MarkWholeParagraph(
                        gapRevised[i].Paragraph,
                        RevisionKind.Inserted,
                        author,
                        dateXml,
                        moveIds.GetRevisedId(gapRevised[i].Index));
                    result.Blocks.Add(inserted);
                    Track(inserted, null, gapRevised[i].Index);
                }
                else
                {
                    var plain = ClonePlain(gapRevised[i].Paragraph); // carry through unmarked
                    result.Blocks.Add(plain);
                    Track(plain, null, gapRevised[i].Index);
                }
            }

            prevOriginalAnchor = originalLimit - 1;
            gapRevised.Clear();
        }
    }

    // Word-level diff of two paragraphs whose text differs. Runs an LCS over whitespace-delimited tokens:
    // common tokens become ordinary runs, revised-only tokens become inserted runs, original-only tokens
    // become deleted runs. Tokens keep their trailing spacing so the reconstructed text reads naturally.
    // settings.Insertions/Deletions gate whether those revision kinds appear in the output. Runs that carry
    // non-text content (inline images/charts/SmartArt/embedded objects/shapes/WordArt/equations/drawing
    // groups, footnote/endnote references, comment-reference/page-break/column-break markers and field runs)
    // are never tokenized: BuildDiffUnits keeps each such run as one atomic, un-splittable unit so it is
    // cloned through with its special content intact rather than flattened into a plain-text run that would
    // silently discard it (see IsCompareAtomicRun).
    private static Paragraph DiffParagraph(Paragraph original, Paragraph revised, string author, string? dateXml, CompareSettings settings)
    {
        // Text that differs only in disabled comparison categories copies through verbatim.
        if (string.Equals(
                ComparisonKey(original.PlainText, settings),
                ComparisonKey(revised.PlainText, settings),
                StringComparison.Ordinal))
            return ClonePlain(revised);

        var result = ClonePlain(revised);
        result.Runs.Clear();
        result.BookmarkBoundaries.Clear();

        var useExactTokens = settings.CaseChanges && settings.Whitespace;
        var originalUnits = BuildDiffUnits(original, useExactTokens);
        var revisedUnits = BuildDiffUnits(revised, useExactTokens);

        // Atomic (object-run) units are given a comparison key that can never equal any other unit's key
        // (a fresh Guid per unit), so the LCS never treats two object runs as "common" — every object run
        // is always emitted through the deletion/insertion branches below, which clone it (with its special
        // content) rather than discarding it. This is deliberately conservative: an unmodified inline object
        // sitting in an edited paragraph is emitted as a delete+insert pair instead of matching through as
        // unchanged, but the content itself is never lost.
        var common = LongestCommonSubsequence(
            originalUnits.Select(unit => DiffUnitKey(unit, settings)).ToList(),
            revisedUnits.Select(unit => DiffUnitKey(unit, settings)).ToList());

        var commonOriginal = new HashSet<int>(common.Select(m => m.OriginalIndex));
        var commonRevised = new HashSet<int>(common.Select(m => m.RevisedIndex));

        var oi = 0;
        var ri = 0;
        var nextMatch = 0;
        while (oi < originalUnits.Count || ri < revisedUnits.Count)
        {
            // At the next aligned common token, both cursors are on a match: emit it as ordinary text.
            if (nextMatch < common.Count
                && oi == common[nextMatch].OriginalIndex
                && ri == common[nextMatch].RevisedIndex)
            {
                AppendDiffUnit(result, revisedUnits[ri], RevisionKind.None, author, dateXml);
                oi++;
                ri++;
                nextMatch++;
                continue;
            }

            // Emit original-only tokens (deletions) until we reach the next common original token.
            if (oi < originalUnits.Count && !commonOriginal.Contains(oi))
            {
                // When deletions are suppressed, skip (do not emit the deleted token at all).
                if (settings.Deletions)
                    AppendDiffUnit(result, originalUnits[oi], RevisionKind.Deleted, author, dateXml);
                oi++;
                continue;
            }

            // Then emit revised-only tokens (insertions) until we reach the next common revised token.
            if (ri < revisedUnits.Count && !commonRevised.Contains(ri))
            {
                // When insertions are suppressed, emit the token as plain text (no revision mark).
                var kind = settings.Insertions ? RevisionKind.Inserted : RevisionKind.None;
                AppendDiffUnit(result, revisedUnits[ri], kind, author, dateXml);
                ri++;
                continue;
            }

            // Defensive: if one side is exhausted but the other still has a "common" token not yet aligned
            // (can't normally happen), advance the lagging cursor so the loop always terminates.
            if (oi < originalUnits.Count)
                oi++;
            else if (ri < revisedUnits.Count)
                ri++;
        }

        BookmarkBoundaryMapper.CopyMapped(
            revised,
            result,
            static run => run.Revision != RevisionKind.Deleted);
        return result;
    }

    // One word-diff unit: either a plain-text token (ObjectRun is null) or a single non-text run carried
    // through atomically (ObjectRun is the source run; Text is only its literal text, used for offsetting).
    private readonly record struct DiffUnit(string Text, Run? ObjectRun);

    // Splits a paragraph's runs into diff units. Consecutive plain-text runs are concatenated and tokenized
    // together (so a word split across two runs by a formatting change, e.g. bold "Hel" + "lo", still
    // tokenizes as one word "Hello" exactly as whole-paragraph tokenization did before this method existed);
    // a run flagged by IsCompareAtomicRun becomes its own single unit and is never handed to the tokenizer,
    // so its special content survives the diff instead of being silently dropped.
    private static List<DiffUnit> BuildDiffUnits(Paragraph paragraph, bool useExactTokens)
    {
        var units = new List<DiffUnit>();
        var plainBuffer = new System.Text.StringBuilder();

        void FlushPlain()
        {
            if (plainBuffer.Length == 0)
                return;
            var text = plainBuffer.ToString();
            plainBuffer.Clear();
            var tokens = useExactTokens ? Tokenize(text) : TokenizeComparisonSegments(text);
            foreach (var token in tokens)
                units.Add(new DiffUnit(token, null));
        }

        foreach (var run in paragraph.Runs)
        {
            if (IsCompareAtomicRun(run))
            {
                FlushPlain();
                units.Add(new DiffUnit(run.Text, run));
                continue;
            }

            plainBuffer.Append(run.Text);
        }
        FlushPlain();
        return units;
    }

    // True for a run whose content cannot be represented as plain text: cloning just its literal Text into
    // a new Run (as the word-diff's text-token path does) would silently discard the run's actual payload.
    // Covers the run kinds DocxReader/DocxWriter serialize as something other than a bare w:t: inline
    // pictures, equations, shapes, WordArt, SmartArt, charts, embedded OLE objects, drawing groups, footnote
    // and endnote reference markers, comment-reference/page-break/column-break markers, and field runs.
    // Also true for a run that already carries a revision mark from BEFORE this compare ran (an
    // unaccepted tracked change left over from a prior review pass): merging its text into the plain-text
    // token buffer would erase which run it came from, so AppendDiffUnit could no longer tell "this text
    // already has its own revision" from "this text is new to this compare" and would silently overwrite
    // the pre-existing author/kind with whatever this compare's own classification produced (see the
    // remark on AppendDiffUnit's object-run branch).
    private static bool IsCompareAtomicRun(Run run) =>
        run.Image is not null
        || run.Equation is not null
        || run.Shape is not null
        || run.WordArt is not null
        || run.SmartArt is not null
        || run.Chart is not null
        || run.EmbeddedObject is not null
        || run.PreservedDrawing is not null
        || run.DrawingGroup is not null
        || run.FootnoteId is not null
        || run.EndnoteId is not null
        || run.IsCommentReference
        || run.IsPageBreak
        || run.IsColumnBreak
        || run.FieldKind != RunFieldKind.None
        || run.Revision != RevisionKind.None;

    // Comparison key for one diff unit. Plain-text units use the same settings-aware key as before; object
    // units get a globally-unique key so the LCS never folds two distinct object runs together (see the
    // remark on the call site in DiffParagraph).
    private static string DiffUnitKey(DiffUnit unit, CompareSettings settings) =>
        unit.ObjectRun is null
            ? ComparisonKey(unit.Text, settings)
            : "OBJ:" + Guid.NewGuid().ToString("N");

    // Emit one diff unit as a run with the given revision kind. A plain-text unit becomes a new text run
    // (as before); an object unit is cloned in full (via DocumentModelCloner, which preserves Image/Chart/
    // SmartArt/EmbeddedObject/Shape/WordArt/Equation/DrawingGroup/FootnoteId/EndnoteId/etc.) so its special
    // content survives instead of being replaced by a plain-text run holding only its literal text.
    private static void AppendDiffUnit(Paragraph paragraph, DiffUnit unit, RevisionKind kind, string author, string? dateXml)
    {
        if (unit.ObjectRun is { } sourceRun)
        {
            // A run that already carries a revision mark predates this compare (see IsCompareAtomicRun);
            // that mark belongs to whoever/whenever produced it and must survive untouched, so preserve it
            // and ignore `kind` -- this compare's own del/insert classification of the unit (which is only
            // ever "unmatched" for an atomic unit, since its key can never equal another unit's) must not
            // overwrite a PriorReviewer's still-pending deletion with a fabricated insertion under the
            // current author, or vice versa. An ordinary atomic run with no pre-existing revision keeps the
            // previous behaviour exactly: stripped, then stamped with this compare's classification.
            var hasExistingRevision = sourceRun.Revision != RevisionKind.None;
            var clone = DocumentModelCloner.CloneRun(
                sourceRun,
                hasExistingRevision ? RevisionClonePolicy.Preserve : RevisionClonePolicy.Strip);
            if (!hasExistingRevision && kind != RevisionKind.None)
            {
                clone.Revision = kind;
                clone.RevisionAuthor = author;
                clone.RevisionDateXml = dateXml;
            }
            paragraph.Runs.Add(clone);
            return;
        }

        AppendRun(paragraph, unit.Text, kind, author, dateXml);
    }

    // Append one token as a run with the given revision kind. None-kind runs carry no revision metadata.
    private static void AppendRun(Paragraph paragraph, string token, RevisionKind kind, string author, string? dateXml)
    {
        if (token.Length == 0)
            return;
        var run = new Run(token);
        if (kind != RevisionKind.None)
        {
            run.Revision = kind;
            run.RevisionAuthor = author;
            run.RevisionDateXml = dateXml;
        }
        paragraph.Runs.Add(run);
    }

    // Produce a copy of a paragraph with every run marked with one revision kind (whole-paragraph
    // insertion or deletion). Run text/formatting is preserved; the revision metadata is stamped on.
    private static Paragraph MarkWholeParagraph(
        Paragraph source,
        RevisionKind kind,
        string author,
        string? dateXml,
        int? moveRevisionId = null)
    {
        var clone = ClonePlain(source);
        // An empty paragraph (no runs) still needs to register as inserted/deleted; the paragraph stays
        // empty in the result but is otherwise carried so block ordering is preserved.
        foreach (var copy in clone.Runs)
        {
            copy.Revision = kind;
            copy.RevisionAuthor = author;
            copy.RevisionDateXml = dateXml;
            copy.MoveRevisionId = moveRevisionId;
        }
        return clone;
    }

    // Word can recognize complex edits as moves. This bounded pass intentionally marks only exact,
    // unique paragraph content outside the LCS anchors; duplicates and edited paragraphs retain the
    // ordinary insertion/deletion behavior rather than risking false move attribution.
    private static WholeParagraphMoveIds FindWholeParagraphMoves(
        IReadOnlyList<Paragraph> original,
        IReadOnlyList<Paragraph> revised,
        IReadOnlyList<(int OriginalIndex, int RevisedIndex)> anchors,
        CompareSettings settings,
        bool doNotTrackMoves)
    {
        var result = new WholeParagraphMoveIds();
        if (doNotTrackMoves || !settings.Moves || !settings.Insertions || !settings.Deletions)
            return result;

        var anchoredOriginal = anchors.Select(anchor => anchor.OriginalIndex).ToHashSet();
        var anchoredRevised = anchors.Select(anchor => anchor.RevisedIndex).ToHashSet();
        var originalByText = original
            .Select((paragraph, index) => (Text: paragraph.PlainText, Index: index))
            .GroupBy(entry => entry.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Index).ToList(), StringComparer.Ordinal);
        var revisedByText = revised
            .Select((paragraph, index) => (Text: paragraph.PlainText, Index: index))
            .GroupBy(entry => entry.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Index).ToList(), StringComparer.Ordinal);

        var nextMoveId = 1;
        for (var originalIndex = 0; originalIndex < original.Count; originalIndex++)
        {
            if (anchoredOriginal.Contains(originalIndex))
                continue;

            var text = original[originalIndex].PlainText;
            if (text.Length == 0
                || !originalByText.TryGetValue(text, out var originalMatches)
                || originalMatches.Count != 1
                || !revisedByText.TryGetValue(text, out var revisedMatches)
                || revisedMatches.Count != 1
                || anchoredRevised.Contains(revisedMatches[0]))
                continue;

            result.Add(originalIndex, revisedMatches[0], nextMoveId++);
        }

        return result;
    }

    private sealed class WholeParagraphMoveIds
    {
        private readonly Dictionary<int, int> _original = [];
        private readonly Dictionary<int, int> _revised = [];

        public void Add(int originalIndex, int revisedIndex, int moveId)
        {
            _original[originalIndex] = moveId;
            _revised[revisedIndex] = moveId;
        }

        public int? GetOriginalId(int index) => _original.TryGetValue(index, out var id) ? id : null;

        public int? GetRevisedId(int index) => _revised.TryGetValue(index, out var id) ? id : null;
    }

    // Split text into tokens that each keep their trailing whitespace, so concatenating the tokens
    // reproduces the original text exactly. A run of whitespace attaches to the preceding word token.
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            // Consume the word characters (non-whitespace).
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            // Then consume the trailing whitespace so it travels with the word.
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            tokens.Add(text.Substring(start, i - start));
        }
        return tokens;
    }

    private static List<string> TokenizeComparisonSegments(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var isWhitespace = char.IsWhiteSpace(text[index]);
            var start = index;
            while (index < text.Length && char.IsWhiteSpace(text[index]) == isWhitespace)
                index++;
            tokens.Add(text[start..index]);
        }

        return tokens;
    }

    private static string ComparisonKey(string text, CompareSettings settings)
    {
        if (!settings.Whitespace && text.All(char.IsWhiteSpace))
            text = " ";
        else if (!settings.Whitespace)
        {
            var normalizedWhitespace = new System.Text.StringBuilder(text.Length);
            var previousWasWhitespace = false;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace)
                        normalizedWhitespace.Append(' ');
                    previousWasWhitespace = true;
                }
                else
                {
                    normalizedWhitespace.Append(character);
                    previousWasWhitespace = false;
                }
            }

            text = normalizedWhitespace.ToString();
        }

        return settings.CaseChanges ? text : text.ToUpperInvariant();
    }

    // Classic dynamic-programming LCS returning the matched index pairs (OriginalIndex, RevisedIndex) in
    // increasing order. Deterministic: ties prefer keeping the left (original) sequence's element.
    private static List<(int OriginalIndex, int RevisedIndex)> LongestCommonSubsequence(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var n = left.Count;
        var m = right.Count;
        var lengths = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var matches = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                matches.Add((x, y));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }
        return matches;
    }

    // Copy a document's defaults, style catalog and page geometry so the comparison result renders like
    // the revised (`source`) document. Body blocks are added by the caller; this only seeds the
    // surrounding shell. `original` is used only to diff the final section's header/footer content against
    // `source`'s (see DiffHeaderFooterSlot) -- everything else here is still seeded from `source` alone.
    private static void CopyDocumentShell(
        TextDocument original,
        TextDocument source,
        TextDocument target,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        target.DefaultRun = source.DefaultRun;
        target.DefaultParagraph = source.DefaultParagraph;
        target.DoNotTrackMoves = source.DoNotTrackMoves;
        target.DoNotTrackFormatting = source.DoNotTrackFormatting;
        target.DoNotAutoCompressPictures = source.DoNotAutoCompressPictures;
        target.EmbedSystemFonts = source.EmbedSystemFonts;
        target.SaveSubsetFonts = source.SaveSubsetFonts;
        target.PageBordersDoNotSurroundHeader = source.PageBordersDoNotSurroundHeader;
        target.PageBordersDoNotSurroundFooter = source.PageBordersDoNotSurroundFooter;
        foreach (var (id, style) in source.Styles)
            target.Styles[id] = style;

        target.Page.WidthPt = source.Page.WidthPt;
        target.Page.HeightPt = source.Page.HeightPt;
        target.Page.MarginLeftPt = source.Page.MarginLeftPt;
        target.Page.MarginRightPt = source.Page.MarginRightPt;
        target.Page.MarginTopPt = source.Page.MarginTopPt;
        target.Page.MarginBottomPt = source.Page.MarginBottomPt;
        target.Page.Landscape = source.Page.Landscape;
        target.Page.ColumnCount = source.Page.ColumnCount;
        target.Page.ColumnSpacingPt = source.Page.ColumnSpacingPt;
        target.Page.ColumnsLineBetween = source.Page.ColumnsLineBetween;
        target.Page.ColumnWidthsPt = source.Page.ColumnWidthsPt is null ? null : new List<double>(source.Page.ColumnWidthsPt);
        target.Page.PageBorder = source.Page.PageBorder;
        target.Page.Watermark = source.Page.Watermark;
        target.Page.LineNumberMode = source.Page.LineNumberMode;
        target.Page.LineNumberCountBy = source.Page.LineNumberCountBy;
        target.Page.LineNumberStartAt = source.Page.LineNumberStartAt;
        target.Page.AutoHyphenation = source.Page.AutoHyphenation;
        target.Page.VerticalAlignment = source.Page.VerticalAlignment;
        target.Page.DifferentFirstPage = source.Page.DifferentFirstPage;
        foreach (var (id, comment) in source.Comments)
            target.Comments[id] = CloneComment(comment);

        // Body runs compared/carried through below can reference footnotes/endnotes (Run.FootnoteId/
        // EndnoteId) and the final section's headers/footers are otherwise-unreferenced document state; none
        // of that lives on a Paragraph, so it must be copied here alongside the rest of the shell or the
        // comparison result silently loses every footnote/endnote and header/footer while still emitting
        // dangling footnote/endnote references for any that survive in the compared body.
        foreach (var (id, footnote) in source.Footnotes)
            target.Footnotes[id] = DocumentModelCloner.CloneFootnote(footnote, RevisionClonePolicy.Strip);
        foreach (var (id, endnote) in source.Endnotes)
            target.Endnotes[id] = DocumentModelCloner.CloneEndnote(endnote, RevisionClonePolicy.Strip);

        // Each header/footer slot is diffed against original's corresponding slot (an unambiguous 1:1
        // correspondence, unlike footnotes/endnotes, which need the anchor-paragraph correlation in
        // ReconcileMatchedNoteContent to avoid confusing two documents' independently numbered notes).
        var originalHeadersFooters = original.FinalSectionHeadersFooters;
        var sourceHeadersFooters = source.FinalSectionHeadersFooters;
        target.FinalSectionHeadersFooters.Header = DiffHeaderFooterSlot(
            originalHeadersFooters.Header, sourceHeadersFooters.Header, author, dateXml, settings);
        target.FinalSectionHeadersFooters.Footer = DiffHeaderFooterSlot(
            originalHeadersFooters.Footer, sourceHeadersFooters.Footer, author, dateXml, settings);
        target.FinalSectionHeadersFooters.EvenHeader = DiffHeaderFooterSlot(
            originalHeadersFooters.EvenHeader, sourceHeadersFooters.EvenHeader, author, dateXml, settings);
        target.FinalSectionHeadersFooters.EvenFooter = DiffHeaderFooterSlot(
            originalHeadersFooters.EvenFooter, sourceHeadersFooters.EvenFooter, author, dateXml, settings);
        target.FinalSectionHeadersFooters.FirstHeader = DiffHeaderFooterSlot(
            originalHeadersFooters.FirstHeader, sourceHeadersFooters.FirstHeader, author, dateXml, settings);
        target.FinalSectionHeadersFooters.FirstFooter = DiffHeaderFooterSlot(
            originalHeadersFooters.FirstFooter, sourceHeadersFooters.FirstFooter, author, dateXml, settings);

        target.Preserved.CopyFrom(source.Preserved);
    }

    // Diffs one header/footer slot (F3 fix). A slot revised doesn't populate stays null; a slot only
    // revised populates (no original counterpart) is cloned through unmarked exactly as before. When BOTH
    // sides populate the slot with plain paragraph content, diff their paragraphs with the same engine used
    // for table cells and footnotes/endnotes (DiffParagraphList) so an edit confined to a header/footer
    // carries real revision marks instead of silently showing revised's text with none. A side-by-side
    // layout header/footer (HeaderFooter.Table set) is left cloned-through unmarked on either side: its
    // Paragraphs are required to be the SAME instances flattened from Table's cells, an invariant a fresh
    // word-diffed paragraph list would break.
    private static HeaderFooter? DiffHeaderFooterSlot(
        HeaderFooter? original,
        HeaderFooter? revised,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        if (revised is null)
            return null;
        if (original is null || original.Table is not null || revised.Table is not null)
            return DocumentModelCloner.CloneHeaderFooter(revised, RevisionClonePolicy.Strip);

        var diffed = new HeaderFooter();
        diffed.Paragraphs.AddRange(DiffParagraphList(original.Paragraphs, revised.Paragraphs, author, dateXml, settings));
        return diffed;
    }

    // Compared body runs retain their comment ids. Carry the revised document's comment graph as owned
    // model objects as well, otherwise a saved compare result would emit orphaned comment anchors.
    private static Comment CloneComment(Comment source)
        => CloneComment(source, static id => id);

    private static Comment CloneComment(Comment source, Func<int, int> mapId)
    {
        var clone = new Comment(mapId(source.Id))
        {
            Author = source.Author,
            Initials = source.Initials,
            DateXml = source.DateXml,
            Resolved = source.Resolved,
        };
        foreach (var paragraph in source.Content)
            clone.Content.Add(ClonePlain(paragraph));
        foreach (var reply in source.Replies)
            clone.Replies.Add(CloneComment(reply, mapId));
        return clone;
    }

    private static void ReconcileDeletedCommentAnchors(
        TextDocument original,
        TextDocument result,
        bool includeDeletedComments)
    {
        var deletedCommentIds = EnumerateParagraphs(result.Blocks)
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Revision == RevisionKind.Deleted && run.CommentId is not null)
            .Select(run => run.CommentId!.Value)
            .Distinct()
            .ToList();

        if (!includeDeletedComments)
        {
            foreach (var commentId in deletedCommentIds)
                RemoveDeletedCommentMarkers(result, commentId);
            return;
        }

        var usedIds = result.Comments.Values
            .SelectMany(comment => comment.ThreadInOrder())
            .Select(comment => comment.Id)
            .ToHashSet();

        foreach (var originalId in deletedCommentIds)
        {
            if (!original.Comments.TryGetValue(originalId, out var originalComment))
            {
                // Do not leave an invalid anchor behind when the source document itself lacks its thread.
                RemoveDeletedCommentMarkers(result, originalId);
                continue;
            }

            var idMap = new Dictionary<int, int>();
            foreach (var comment in originalComment.ThreadInOrder())
            {
                var id = comment.Id;
                if (!usedIds.Add(id))
                    id = NextUnusedCommentId(usedIds);
                idMap[comment.Id] = id;
            }

            var copiedId = idMap[originalId];
            result.Comments[copiedId] = CloneComment(originalComment, id => idMap[id]);
            if (copiedId != originalId)
                RemapDeletedCommentMarkers(result, originalId, copiedId);
        }
    }

    private static int NextUnusedCommentId(HashSet<int> usedIds)
    {
        var id = usedIds.Count == 0 ? 0 : usedIds.Max() + 1;
        while (!usedIds.Add(id))
            id++;
        return id;
    }

    // Whole-paragraph deletions (MarkWholeParagraph) and word-level deletions (the original-only branch of
    // DiffParagraph/AppendDiffUnit) retain the ORIGINAL document's footnote/endnote reference runs verbatim.
    // Every RevisionKind.Deleted run emitted anywhere in the result is always cloned from `original`, never
    // from `revised` (MarkWholeParagraph stamps Deleted only on runs cloned from its `source` when that
    // source is an original-side paragraph; DiffParagraph's original-only diff units are the only ones ever
    // marked Deleted). CopyDocumentShell only seeds result.Footnotes/Endnotes from `revised`, so a
    // footnote/endnote that exists only in `original` -- referenced by one of those deleted runs -- would
    // otherwise be a dangling reference: a Run.FootnoteId/EndnoteId with no matching entry in the result's
    // note catalog. Copy in the specific original notes those deleted runs reference, allocating a fresh
    // numeric id whenever the original's id collides with one `revised` already contributed, so the two
    // documents' independent, unrelated numbering can never be confused for one another.
    private static void ReconcileDeletedNoteAnchors(TextDocument original, TextDocument result)
    {
        var footnoteIdMap = new Dictionary<int, int>();
        var endnoteIdMap = new Dictionary<int, int>();
        var usedFootnoteIds = result.Footnotes.Keys.ToHashSet();
        var usedEndnoteIds = result.Endnotes.Keys.ToHashSet();

        foreach (var paragraph in EnumerateParagraphs(result.Blocks))
        foreach (var run in paragraph.Runs)
        {
            if (run.Revision != RevisionKind.Deleted)
                continue;

            if (run.FootnoteId is { } footnoteId
                && !footnoteIdMap.ContainsKey(footnoteId)
                && original.Footnotes.TryGetValue(footnoteId, out var footnote))
            {
                var mappedId = AllocateNoteId(footnoteId, usedFootnoteIds);
                footnoteIdMap[footnoteId] = mappedId;
                var clone = new Footnote(mappedId) { HasAutomaticReferenceMark = footnote.HasAutomaticReferenceMark };
                foreach (var content in footnote.Content)
                    clone.Content.Add(ClonePlain(content));
                result.Footnotes[mappedId] = clone;
            }

            if (run.EndnoteId is { } endnoteId
                && !endnoteIdMap.ContainsKey(endnoteId)
                && original.Endnotes.TryGetValue(endnoteId, out var endnote))
            {
                var mappedId = AllocateNoteId(endnoteId, usedEndnoteIds);
                endnoteIdMap[endnoteId] = mappedId;
                var clone = new Endnote(mappedId) { HasAutomaticReferenceMark = endnote.HasAutomaticReferenceMark };
                foreach (var content in endnote.Content)
                    clone.Content.Add(ClonePlain(content));
                result.Endnotes[mappedId] = clone;
            }
        }

        if (footnoteIdMap.Count == 0 && endnoteIdMap.Count == 0)
            return;

        foreach (var paragraph in EnumerateParagraphs(result.Blocks))
        foreach (var run in paragraph.Runs)
        {
            if (run.Revision != RevisionKind.Deleted)
                continue;
            if (run.FootnoteId is { } footnoteId && footnoteIdMap.TryGetValue(footnoteId, out var mappedFootnote))
                run.FootnoteId = mappedFootnote;
            if (run.EndnoteId is { } endnoteId && endnoteIdMap.TryGetValue(endnoteId, out var mappedEndnote))
                run.EndnoteId = mappedEndnote;
        }
    }

    private static int AllocateNoteId(int sourceId, HashSet<int> usedIds)
    {
        if (usedIds.Add(sourceId))
            return sourceId;
        var candidate = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
        while (!usedIds.Add(candidate))
            candidate++;
        return candidate;
    }

    // Diffs footnote/endnote CONTENT for ids confirmed to name the SAME logical note on both sides (F3
    // fix). CopyDocumentShell already seeded result.Footnotes/Endnotes wholesale from `revised` with no
    // comparison against `original` at all; this overwrites just the confirmed ones' content with a real
    // word-level diff, so an edit confined to a footnote/endnote carries revision marks instead of silently
    // showing only revised's text.
    //
    // A footnote id is confirmed ONLY when its reference run appears on BOTH sides of the SAME anchor
    // paragraph pair -- an original paragraph and a revised paragraph whose plain text matched exactly in
    // the body-level LCS above. That is deliberately narrower than "the same raw numeric id exists in both
    // documents' Footnotes catalogs": footnote/endnote numbering is local to each document, so two
    // documents can each have an unrelated "footnote 1" that just happens to share a number -- exactly the
    // case DeletedParagraph_RemapsOriginalFootnoteIdThatCollidesWithARevisedFootnoteId locks in (a footnote
    // reference that exists only in a paragraph original deletes entirely, with no counterpart anywhere in
    // revised, must never be treated as "the same note" as an unrelated revised-side footnote of the same
    // number). Requiring the reference to sit inside a paragraph that matched byte-for-byte on both sides is
    // strong evidence the two ids really do name one note that was merely edited, not two coincidentally
    // co-numbered ones.
    private static void ReconcileMatchedNoteContent(
        TextDocument original,
        TextDocument result,
        IReadOnlyList<Paragraph> originalParagraphs,
        IReadOnlyList<Paragraph> revisedParagraphs,
        IReadOnlyList<(int OriginalIndex, int RevisedIndex)> matches,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        var confirmedFootnoteIds = new HashSet<int>();
        var confirmedEndnoteIds = new HashSet<int>();
        foreach (var (originalIndex, revisedIndex) in matches)
        {
            var originalRuns = originalParagraphs[originalIndex].Runs;
            var revisedRuns = revisedParagraphs[revisedIndex].Runs;
            confirmedFootnoteIds.UnionWith(
                originalRuns.Where(r => r.FootnoteId is not null).Select(r => r.FootnoteId!.Value)
                    .Intersect(revisedRuns.Where(r => r.FootnoteId is not null).Select(r => r.FootnoteId!.Value)));
            confirmedEndnoteIds.UnionWith(
                originalRuns.Where(r => r.EndnoteId is not null).Select(r => r.EndnoteId!.Value)
                    .Intersect(revisedRuns.Where(r => r.EndnoteId is not null).Select(r => r.EndnoteId!.Value)));
        }

        foreach (var id in confirmedFootnoteIds)
        {
            if (!original.Footnotes.TryGetValue(id, out var originalFootnote)
                || !result.Footnotes.TryGetValue(id, out var resultFootnote))
                continue;
            var diffedContent = DiffParagraphList(originalFootnote.Content, resultFootnote.Content, author, dateXml, settings);
            resultFootnote.Content.Clear();
            resultFootnote.Content.AddRange(diffedContent);
        }

        foreach (var id in confirmedEndnoteIds)
        {
            if (!original.Endnotes.TryGetValue(id, out var originalEndnote)
                || !result.Endnotes.TryGetValue(id, out var resultEndnote))
                continue;
            var diffedContent = DiffParagraphList(originalEndnote.Content, resultEndnote.Content, author, dateXml, settings);
            resultEndnote.Content.Clear();
            resultEndnote.Content.AddRange(diffedContent);
        }
    }

    // Diffs two flat paragraph lists that have no interleaved non-paragraph content and no whole-list move
    // tracking: table cells, header/footer slots, and footnote/endnote content -- everywhere the main body
    // walk above (CompareCore's foreach over revisedBlocks) does not reach. Shares the same paragraph-level
    // LCS anchor matching and word-level diff (DiffParagraph/MarkWholeParagraph/ClonePlainWithFormatRevisions)
    // as that walk; what it deliberately omits is move-tracking (FindWholeParagraphMoves) and the alignment
    // sink (Track), neither of which is meaningful for content that isn't the document body.
    private static List<Paragraph> DiffParagraphList(
        IReadOnlyList<Paragraph> originalParagraphs,
        IReadOnlyList<Paragraph> revisedParagraphs,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        var matches = LongestCommonSubsequence(
            originalParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList(),
            revisedParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList());

        var revisedAnchorToOriginal = new Dictionary<int, int>();
        foreach (var (originalIndex, revisedIndex) in matches)
            revisedAnchorToOriginal[revisedIndex] = originalIndex;

        var result = new List<Paragraph>();
        var prevOriginalAnchor = -1;
        var gapRevised = new List<(Paragraph Paragraph, int Index)>();

        void ResolveGap(int originalLimit)
        {
            var gapOriginal = new List<(Paragraph Paragraph, int Index)>();
            for (var i = prevOriginalAnchor + 1; i < originalLimit && i < originalParagraphs.Count; i++)
                gapOriginal.Add((originalParagraphs[i], i));

            var pairCount = Math.Min(gapOriginal.Count, gapRevised.Count);
            for (var i = 0; i < pairCount; i++)
                result.Add(DiffParagraph(gapOriginal[i].Paragraph, gapRevised[i].Paragraph, author, dateXml, settings));

            for (var i = pairCount; i < gapOriginal.Count; i++)
            {
                if (settings.Deletions)
                    result.Add(MarkWholeParagraph(gapOriginal[i].Paragraph, RevisionKind.Deleted, author, dateXml));
            }

            for (var i = pairCount; i < gapRevised.Count; i++)
            {
                if (settings.Insertions)
                    result.Add(MarkWholeParagraph(gapRevised[i].Paragraph, RevisionKind.Inserted, author, dateXml));
                else
                    result.Add(ClonePlain(gapRevised[i].Paragraph));
            }

            prevOriginalAnchor = originalLimit - 1;
            gapRevised.Clear();
        }

        for (var revisedIndex = 0; revisedIndex < revisedParagraphs.Count; revisedIndex++)
        {
            if (revisedAnchorToOriginal.TryGetValue(revisedIndex, out var anchorOriginalIndex))
            {
                ResolveGap(anchorOriginalIndex);
                result.Add(ClonePlainWithFormatRevisions(
                    originalParagraphs[anchorOriginalIndex],
                    revisedParagraphs[revisedIndex],
                    author,
                    dateXml,
                    settings.Formatting));
                prevOriginalAnchor = anchorOriginalIndex;
            }
            else
            {
                gapRevised.Add((revisedParagraphs[revisedIndex], revisedIndex));
            }
        }

        ResolveGap(originalParagraphs.Count);
        return result;
    }

    // Diffs a table pair (F2 fix). When `original` is null (no table sits at the matching ordinal position
    // -- see the comment on CompareCore's `originalTables`) the whole table is new to this comparison and
    // is cloned through unmarked, exactly as before this fix. When `original` is non-null but the two
    // tables' shapes differ (a different row count, or any row pair with a different cell count), diffing
    // cell-by-cell would require guessing which row/column was added or removed, so this also falls back to
    // cloning revised through unmarked rather than risking a wrong structural alignment. Only when both
    // tables have identical row/column shape does this diff each cell's paragraph content against its
    // positional counterpart, using the same engine as table cells everywhere else (DiffParagraphList).
    // Nested tables inside a cell are not diffed either way (out of scope for this fix): they are always
    // cloned through unmarked.
    private static Table DiffTableIfMatched(Table? original, Table revised, string author, string? dateXml, CompareSettings settings)
    {
        if (original is null
            || original.Rows.Count != revised.Rows.Count
            || original.Rows.Zip(revised.Rows, (o, r) => o.Cells.Count == r.Cells.Count).Any(sameShape => !sameShape))
            return (Table)CloneBlock(revised);

        var clone = new Table
        {
            BlockContentControl = revised.BlockContentControl,
            BlockCustomXml = revised.BlockCustomXml,
            Formatting = revised.Formatting,
            TableStyleId = revised.TableStyleId,
            Borders = revised.Borders,
            PreferredWidthPt = revised.PreferredWidthPt,
            Alignment = revised.Alignment,
            IndentFromLeftPt = revised.IndentFromLeftPt,
            FloatingPosition = revised.FloatingPosition,
            FloatingTableAllowsOverlap = revised.FloatingTableAllowsOverlap,
            DefaultCellMargins = revised.DefaultCellMargins,
            CellSpacingPt = revised.CellSpacingPt,
            AutoFit = revised.AutoFit
        };
        clone.ColumnWidthsPt.AddRange(revised.ColumnWidthsPt);

        for (var r = 0; r < revised.Rows.Count; r++)
        {
            var originalRow = original.Rows[r];
            var revisedRow = revised.Rows[r];
            var rowClone = new TableRow
            {
                HeightPt = revisedRow.HeightPt,
                HeightRule = revisedRow.HeightRule,
                AllowBreakAcrossPages = revisedRow.AllowBreakAcrossPages,
                RowRevision = revisedRow.RowRevision,
                RowRevisionAuthor = revisedRow.RowRevisionAuthor,
                RowRevisionDateXml = revisedRow.RowRevisionDateXml,
                IsRepeatingHeader = revisedRow.IsRepeatingHeader
            };
            for (var c = 0; c < revisedRow.Cells.Count; c++)
                rowClone.Cells.Add(DiffTableCell(originalRow.Cells[c], revisedRow.Cells[c], author, dateXml, settings));
            clone.Rows.Add(rowClone);
        }
        return clone;
    }

    private static TableCell DiffTableCell(TableCell original, TableCell revised, string author, string? dateXml, CompareSettings settings)
    {
        var clone = new TableCell
        {
            ShadingColorHex = revised.ShadingColorHex,
            WidthPt = revised.WidthPt,
            GridSpan = revised.GridSpan,
            VerticalMerge = revised.VerticalMerge,
            VerticalAlignment = revised.VerticalAlignment,
            Margins = revised.Margins,
            Borders = revised.Borders,
            TextDirection = revised.TextDirection,
            WrapText = revised.WrapText,
            FitText = revised.FitText
        };
        clone.Paragraphs.AddRange(DiffParagraphList(original.Paragraphs, revised.Paragraphs, author, dateXml, settings));
        // Nested tables are out of scope for this fix (see DiffTableIfMatched); carried through unmarked.
        foreach (var nestedTable in revised.NestedTables)
            clone.NestedTables.Add((Table)CloneBlock(nestedTable));
        return clone;
    }

    private static void RemapDeletedCommentMarkers(TextDocument document, int oldId, int newId)
    {
        foreach (var paragraph in EnumerateParagraphs(document.Blocks))
        foreach (var run in paragraph.Runs)
            if (run.Revision == RevisionKind.Deleted && run.CommentId == oldId)
                run.CommentId = newId;
    }

    private static void RemoveDeletedCommentMarkers(TextDocument document, int commentId)
    {
        foreach (var paragraph in EnumerateParagraphs(document.Blocks))
        {
            for (var index = paragraph.Runs.Count - 1; index >= 0; index--)
            {
                var run = paragraph.Runs[index];
                if (run.Revision != RevisionKind.Deleted || run.CommentId != commentId)
                    continue;

                if (run.IsCommentReference)
                    paragraph.Runs.RemoveAt(index);
                else
                    run.CommentId = null;
            }
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not Table table)
                continue;

            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            foreach (var cellParagraph in cell.Paragraphs)
                yield return cellParagraph;
        }
    }

    // Clone a paragraph with its runs verbatim, preserving whatever revision marks the source paragraph
    // already carried (used for content this compare is passing through unchanged/unmarked, e.g. an
    // anchor paragraph or a whole-inserted paragraph's base clone). This does NOT stamp any NEW revision
    // -- callers that need to mark the whole thing inserted/deleted (MarkWholeParagraph) do that themselves
    // on the returned runs. Using Preserve rather than Strip here is deliberate: a run can already carry a
    // revision mark from BEFORE this compare ran (an unaccepted tracked change left over from a prior
    // review pass), and silently clearing it would misrepresent that pending change as already accepted.
    private static Paragraph ClonePlain(Paragraph source)
        => DocumentModelCloner.CloneParagraph(source, RevisionClonePolicy.Preserve);

    // A paragraph-level LCS anchor has matching comparison text. When the source text and run boundaries
    // are also exact, preserve the revised appearance and mark only format differences with w:rPrChange.
    // Mixed text-and-formatting edits stay on the word-diff path, where there is no unambiguous source run
    // snapshot for a formatting revision.
    private static Paragraph ClonePlainWithFormatRevisions(
        Paragraph original,
        Paragraph revised,
        string author,
        string? dateXml,
        bool trackFormatting)
    {
        var clone = ClonePlain(revised);
        if (!trackFormatting
            || !string.Equals(original.PlainText, revised.PlainText, StringComparison.Ordinal)
            || original.Runs.Count != revised.Runs.Count)
            return clone;

        for (var index = 0; index < revised.Runs.Count; index++)
        {
            var originalRun = original.Runs[index];
            var revisedRun = revised.Runs[index];
            if (originalRun.Text.Length == 0
                || !string.Equals(originalRun.Text, revisedRun.Text, StringComparison.Ordinal)
                || Equals(originalRun.Formatting, revisedRun.Formatting))
                continue;

            clone.Runs[index].FormatRevision = new FormatRevision(
                originalRun.Formatting,
                author,
                dateXml);
        }

        return clone;
    }

    // Clone non-paragraph content while removing revisions that belong to the compared inputs.
    private static Block CloneBlock(Block block)
        => DocumentModelCloner.CloneBlock(block, RevisionClonePolicy.Strip);

}
