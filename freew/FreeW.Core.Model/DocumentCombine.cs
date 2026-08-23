using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free "Combine Documents" (Word's Review › Compare › Combine): merge the revisions of two
/// reviewers — <c>revisedA</c> (author <c>authorA</c>) and <c>revisedB</c> (author <c>authorB</c>) — that
/// were both edited from the same <c>original</c> base, into ONE document whose differences are expressed
/// as tracked changes (<see cref="Run.Revision"/>) <em>preserving each reviewer's own authorship</em>: a
/// word A changed is attributed to <c>authorA</c>, a word B changed to <c>authorB</c>. The result opens
/// with full markup and can be Accepted/Rejected per author, exactly like a real combined document.
///
/// The combine is built by reusing the existing legal-blackline engine: <see cref="DocumentCompare"/>
/// produces base→A (authored to A); that blackline already carries A's text as ordinary+inserted runs, so
/// comparing A's text against B and layering B's edits (authored to B) on top yields a single document that
/// holds <em>both</em> authors' tracked insertions and deletions. Deterministic and input-non-mutating;
/// the <c>dateXml</c> revision timestamp is supplied by the caller (never <see cref="DateTime.Now"/>).
/// </summary>
public static class DocumentCombine
{
    /// <summary>
    /// Combine the two reviewers' edits of a shared <paramref name="original"/> base into one document
    /// carrying both authors' tracked changes. <paramref name="revisedA"/>/<paramref name="authorA"/> and
    /// <paramref name="revisedB"/>/<paramref name="authorB"/> are the two reviewed copies and their authors.
    /// <paramref name="dateXml"/> is the W3CDTF timestamp stamped on every produced revision (null to leave
    /// the date unset). The result renders like <paramref name="revisedA"/>'s shell (defaults/styles/page).
    /// </summary>
    public static TextDocument Combine(
        TextDocument original,
        TextDocument revisedA,
        string authorA,
        TextDocument revisedB,
        string authorB,
        string? dateXml = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revisedA);
        ArgumentNullException.ThrowIfNull(authorA);
        ArgumentNullException.ThrowIfNull(revisedB);
        ArgumentNullException.ThrowIfNull(authorB);

        // Step 1: base→A blackline. This carries A's revisions attributed to authorA; accepting it would
        // yield exactly revisedA's text, so its "A view" (what survives an accept) equals revisedA. The
        // alignment tells us, for every paragraph the comparison produced, which revisedA paragraph (if
        // any) it corresponds to — the same LCS-based matcher DocumentCompare already runs internally.
        var blacklineA = DocumentCompare.Compare(
            original, revisedA, authorA, dateXml, CompareSettings.Default, out var alignmentA);

        // Step 2: layer B's edits on top. We compare A's surviving text (revisedA) against revisedB and walk
        // B's word-level changes paragraph by paragraph, re-marking only the runs B touched as authorB while
        // leaving A's existing marks intact. Because both comparisons are anchored on the same base text,
        // a paragraph A left unchanged but B changed gets B's marks; a paragraph both changed keeps A's
        // deletion/insertion runs and additionally carries B's, each under its own author. Here the
        // alignment's OriginalIndex is the revisedA paragraph each blacklineB paragraph corresponds to.
        var blacklineB = DocumentCompare.Compare(
            revisedA, revisedB, authorB, dateXml, CompareSettings.Default, out var alignmentB);

        // Both blacklines were independently diffed against/from revisedA, so a paragraph from one only
        // truly corresponds to a paragraph from the other when they share the same revisedA paragraph index
        // — NOT when they merely land at the same position in their own Blocks list, which the alignment
        // above lets us tell apart (see MergeBlacklines).
        return MergeBlacklines(blacklineA, alignmentA, blacklineB, alignmentB, authorA, authorB, dateXml);
    }

    // Merge two blacklines that were both diffed against the same revisedA spine: blacklineA is base→A
    // (carries A's ins/del; accepting it yields revisedA) and blacklineB is revisedA→B (carries B's ins/del;
    // rejecting it yields revisedA). Naively zipping their paragraph lists by raw list position breaks the
    // moment either side inserts or deletes a whole paragraph, because each comparison independently splices
    // its own off-spine whole-paragraph deletions/insertions into its Blocks list — those extra entries
    // shift every later paragraph's index out of step between the two lists (see the finding this fixes:
    // freew-combine-positional-misalignment). Instead we align by the shared revisedA paragraph index that
    // <paramref name="alignmentA"/>/<paramref name="alignmentB"/> attach to each produced paragraph — the
    // very alignment DocumentCompare's own LCS-based matcher already computed while building each blackline,
    // reused here rather than re-deriving a second, independent match from the output text/markup.
    private static TextDocument MergeBlacklines(
        TextDocument blacklineA,
        IReadOnlyDictionary<Paragraph, DocumentCompare.ParagraphAlignment> alignmentA,
        TextDocument blacklineB,
        IReadOnlyDictionary<Paragraph, DocumentCompare.ParagraphAlignment> alignmentB,
        string authorA,
        string authorB,
        string? dateXml)
    {
        var result = new TextDocument();
        CopyShell(blacklineB, result);
        var commentIdMapA = MergeComments(blacklineA, blacklineB, result);
        var (footnoteIdMapA, endnoteIdMapA) = MergeNotes(blacklineA, result);

        // Bucket blacklineA's paragraphs by the revisedA spine index they align to. A paragraph with no
        // spine index (RevisedIndex is null) is base-only content A deleted entirely — it never existed in
        // revisedA, so B's comparison could never have produced (or touched) a counterpart for it; keep it
        // as its own standalone off-spine deletion, tagged with the spine index it immediately follows, so
        // it can be spliced back into the merged result at the right point instead of being fused onto
        // whatever blacklineB paragraph happened to share its former list position.
        var aBySpine = new Dictionary<int, Paragraph>();
        var aStandalone = new List<(int PrecedingSpineIndex, Paragraph Paragraph)>();
        var lastSpineSeen = -1;
        foreach (var block in blacklineA.Blocks)
        {
            if (block is not Paragraph aParagraph)
                continue;

            var spineIndex = alignmentA.TryGetValue(aParagraph, out var entry) ? entry.RevisedIndex : null;
            if (spineIndex is int idx)
            {
                aBySpine[idx] = aParagraph;
                lastSpineSeen = idx;
            }
            else
            {
                aStandalone.Add((lastSpineSeen, aParagraph));
            }
        }

        var standaloneCursor = 0;

        foreach (var block in blacklineB.Blocks)
        {
            if (block is not Paragraph bParagraph)
            {
                result.Blocks.Add(DocumentModelCloner.CloneBlock(block, RevisionClonePolicy.Preserve));
                continue;
            }

            var spineIndex = alignmentB.TryGetValue(bParagraph, out var entry) ? entry.OriginalIndex : null;
            if (spineIndex is int sIdx)
                FlushStandaloneA(sIdx);

            var aParagraph = spineIndex is int spine && aBySpine.TryGetValue(spine, out var matched)
                ? matched
                : null;
            result.Blocks.Add(MergeParagraph(
                aParagraph,
                bParagraph,
                authorA,
                authorB,
                dateXml,
                commentIdMapA,
                footnoteIdMapA,
                endnoteIdMapA));
        }

        FlushStandaloneA(int.MaxValue);
        return result;

        // Emit every buffered A-only whole-paragraph deletion that precedes revisedA spine index
        // `uptoSpineIndexExclusive`, in their original relative order.
        void FlushStandaloneA(int uptoSpineIndexExclusive)
        {
            while (standaloneCursor < aStandalone.Count
                   && aStandalone[standaloneCursor].PrecedingSpineIndex < uptoSpineIndexExclusive)
            {
                var clone = DocumentModelCloner.CloneParagraph(
                    aStandalone[standaloneCursor].Paragraph,
                    RevisionClonePolicy.Preserve);
                foreach (var run in clone.Runs)
                {
                    RemapAComment(run, commentIdMapA);
                    RemapANotes(run, footnoteIdMapA, endnoteIdMapA);
                }
                result.Blocks.Add(clone);
                standaloneCursor++;
            }
        }
    }

    private static Dictionary<int, int> MergeComments(
        TextDocument blacklineA,
        TextDocument blacklineB,
        TextDocument result)
    {
        foreach (var (id, comment) in blacklineB.Comments)
            result.Comments[id] = CloneComment(comment, static commentId => commentId);

        var usedIds = result.Comments.Values
            .SelectMany(comment => comment.ThreadInOrder())
            .Select(comment => comment.Id)
            .ToHashSet();
        var commentIdMapA = new Dictionary<int, int>();

        foreach (var comment in blacklineA.Comments.Values)
        {
            foreach (var node in comment.ThreadInOrder())
            {
                var id = node.Id;
                if (!usedIds.Add(id))
                    id = NextUnusedCommentId(usedIds);
                commentIdMapA[node.Id] = id;
            }

            var remapped = CloneComment(comment, id => commentIdMapA[id]);
            result.Comments[remapped.Id] = remapped;
        }

        return commentIdMapA;
    }

    private static int NextUnusedCommentId(HashSet<int> usedIds)
    {
        var id = usedIds.Count == 0 ? 0 : usedIds.Max() + 1;
        while (!usedIds.Add(id))
            id++;
        return id;
    }

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
        {
            clone.Content.Add(DocumentModelCloner.CloneParagraph(
                paragraph,
                RevisionClonePolicy.Preserve));
        }
        foreach (var reply in source.Replies)
            clone.Replies.Add(CloneComment(reply, mapId));
        return clone;
    }

    private static Run RemapAComment(
        Run run,
        IReadOnlyDictionary<int, int> commentIdMapA)
    {
        if (run.CommentId is int id && commentIdMapA.TryGetValue(id, out var mapped))
            run.CommentId = mapped;
        return run;
    }

    // Merges blacklineA's footnote/endnote catalog into `result` (already seeded from blacklineB by
    // CopyShell), keyed so ids independently allocated by the two DocumentCompare.Compare calls can never
    // collide. blacklineA is base→revisedA: when revisedA deletes a paragraph whose footnote/endnote exists
    // only in `original`, blacklineA's own DocumentCompare.ReconcileDeletedNoteAnchors already reconciled
    // that note into blacklineA.Footnotes/Endnotes under blacklineA-local numbering. CopyShell only ever
    // saw blacklineB's catalog, so that note — and any other note reachable only through an A-authored run
    // (an off-spine A-deletion, or A-only tail content) — would otherwise be a dangling reference in the
    // merged result. Returns the id maps RemapANotes needs to keep A-sourced runs pointing at the right
    // (possibly renumbered) entry.
    private static (Dictionary<int, int> FootnoteIdMapA, Dictionary<int, int> EndnoteIdMapA) MergeNotes(
        TextDocument blacklineA,
        TextDocument result)
    {
        var footnoteIdMapA = new Dictionary<int, int>();
        var usedFootnoteIds = result.Footnotes.Keys.ToHashSet();
        foreach (var (id, footnote) in blacklineA.Footnotes)
        {
            var mappedId = usedFootnoteIds.Add(id) ? id : NextUnusedNoteId(usedFootnoteIds);
            footnoteIdMapA[id] = mappedId;
            var clone = new Footnote(mappedId) { HasAutomaticReferenceMark = footnote.HasAutomaticReferenceMark };
            foreach (var paragraph in footnote.Content)
                clone.Content.Add(DocumentModelCloner.CloneParagraph(paragraph, RevisionClonePolicy.Strip));
            result.Footnotes[mappedId] = clone;
        }

        var endnoteIdMapA = new Dictionary<int, int>();
        var usedEndnoteIds = result.Endnotes.Keys.ToHashSet();
        foreach (var (id, endnote) in blacklineA.Endnotes)
        {
            var mappedId = usedEndnoteIds.Add(id) ? id : NextUnusedNoteId(usedEndnoteIds);
            endnoteIdMapA[id] = mappedId;
            var clone = new Endnote(mappedId) { HasAutomaticReferenceMark = endnote.HasAutomaticReferenceMark };
            foreach (var paragraph in endnote.Content)
                clone.Content.Add(DocumentModelCloner.CloneParagraph(paragraph, RevisionClonePolicy.Strip));
            result.Endnotes[mappedId] = clone;
        }

        return (footnoteIdMapA, endnoteIdMapA);
    }

    private static int NextUnusedNoteId(HashSet<int> usedIds)
    {
        var id = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
        while (!usedIds.Add(id))
            id++;
        return id;
    }

    private static Run RemapANotes(
        Run run,
        IReadOnlyDictionary<int, int> footnoteIdMapA,
        IReadOnlyDictionary<int, int> endnoteIdMapA)
    {
        if (run.FootnoteId is int footnoteId && footnoteIdMapA.TryGetValue(footnoteId, out var mappedFootnote))
            run.FootnoteId = mappedFootnote;
        if (run.EndnoteId is int endnoteId && endnoteIdMapA.TryGetValue(endnoteId, out var mappedEndnote))
            run.EndnoteId = mappedEndnote;
        return run;
    }

    // Merge one paragraph from A's blackline and the positionally-matching paragraph from B's blackline into
    // a single paragraph carrying both authors' tracked changes.
    //
    // Both blacklines share a common SPINE: the revisedA token sequence (text that survives accepting A and
    // rejecting B). In A's blackline the spine runs are Inserted (new in A) or None (in base and A), with A's
    // Deleted runs (base-only) interleaved. In B's blackline the spine runs are Deleted (B removed an A word)
    // or None (kept by B), with B's Inserted runs (new in B) interleaved. We walk both run lists in lockstep
    // over the spine: at each spine position we emit A's pending deletions, B's pending insertions, then the
    // spine token itself carrying the union of A's and B's marks for that token. This is deterministic and
    // needs no text-equality guessing — alignment is purely positional over the shared spine.
    private static Paragraph MergeParagraph(
        Paragraph? aParagraph,
        Paragraph bParagraph,
        string authorA,
        string authorB,
        string? dateXml,
        IReadOnlyDictionary<int, int> commentIdMapA,
        IReadOnlyDictionary<int, int> footnoteIdMapA,
        IReadOnlyDictionary<int, int> endnoteIdMapA)
    {
        var merged = DocumentModelCloner.CloneParagraph(bParagraph, RevisionClonePolicy.Preserve);
        merged.BlockContentControl = bParagraph.BlockContentControl ?? aParagraph?.BlockContentControl;
        merged.BlockCustomXml = bParagraph.BlockCustomXml ?? aParagraph?.BlockCustomXml;
        merged.SpanningFieldStart = bParagraph.SpanningFieldStart ?? aParagraph?.SpanningFieldStart;
        merged.SpanningFieldOwner = bParagraph.SpanningFieldOwner ?? aParagraph?.SpanningFieldOwner;
        merged.EndsSpanningField = bParagraph.EndsSpanningField || aParagraph?.EndsSpanningField == true;
        merged.Runs.Clear();
        merged.BookmarkBoundaries.Clear();

        var aRuns = aParagraph?.Runs ?? new List<Run>();
        var bRuns = bParagraph.Runs;
        var bBoundaryOutputIndices = new int?[bRuns.Count + 1];
        var ai = 0;
        var bi = 0;

        while (ai < aRuns.Count || bi < bRuns.Count)
        {
            bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
            // A's deletions (base-only text struck by A) are off-spine: emit them, attributed to authorA.
            if (ai < aRuns.Count && aRuns[ai].Revision == RevisionKind.Deleted)
            {
                merged.Runs.Add(RemapANotes(
                    RemapAComment(
                        Stamp(aRuns[ai], RevisionKind.Deleted, authorA, dateXml),
                        commentIdMapA),
                    footnoteIdMapA,
                    endnoteIdMapA));
                ai++;
                continue;
            }

            // B's insertions (new in B) are off-spine: emit them, keeping B's authorship/date.
            if (bi < bRuns.Count && bRuns[bi].Revision == RevisionKind.Inserted)
            {
                var bRun = bRuns[bi];
                merged.Runs.Add(Stamp(bRun, RevisionKind.Inserted, bRun.RevisionAuthor ?? authorB, bRun.RevisionDateXml ?? dateXml));
                bi++;
                bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
                continue;
            }

            // Both cursors are now on a spine token (or one side is exhausted). The A side tells us if A
            // inserted this token (vs the base); the B side tells us if B deleted it. Combine the two:
            //   A inserted & B deleted  → B struck text A had added → keep as B-deletion (attributed to B)
            //   A inserted & B kept     → A's insertion (attributed to A)
            //   A kept     & B deleted  → B's deletion (attributed to B)
            //   A kept     & B kept     → ordinary text, no marks
            var aRun = ai < aRuns.Count ? aRuns[ai] : null;
            var bSpine = bi < bRuns.Count ? bRuns[bi] : null;
            if (bSpine is null)
            {
                // A still has spine runs but B is exhausted. By construction blacklineA's spine text (its
                // None+Inserted runs) and blacklineB's spine text (its None+Deleted runs) both equal
                // revisedA's plain text for this paragraph, so if B's run list runs out first, whatever
                // aRun content remains here was already emitted verbatim as part of the wider B run(s)
                // already stamped above -- the two blacklines simply split the identical spine text into a
                // different number of runs (e.g. only one reviewer's copy has a comment/hyperlink/bookmark
                // anchor splitting a run boundary inside an otherwise-untouched span). Re-adding it would
                // duplicate that text (see finding freew-compare-merge F2), so just advance past it.
                if (aRun is not null)
                    ai++;
                continue;
            }

            var aInserted = aRun is { Revision: RevisionKind.Inserted };
            var bDeleted = bSpine.Revision == RevisionKind.Deleted;

            // The spine token's text/formatting comes from B's blackline (it is the authority for the spine).
            if (bDeleted)
                merged.Runs.Add(Stamp(bSpine, RevisionKind.Deleted, bSpine.RevisionAuthor ?? authorB, bSpine.RevisionDateXml ?? dateXml));
            else if (aInserted)
                merged.Runs.Add(Stamp(bSpine, RevisionKind.Inserted, authorA, dateXml));
            else
                merged.Runs.Add(Stamp(bSpine, RevisionKind.None, null, null));

            if (aRun is not null)
                ai++;
            bi++;
            bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
        }

        bBoundaryOutputIndices[bRuns.Count] ??= merged.Runs.Count;
        merged.BookmarkBoundaries.AddRange(bParagraph.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = bBoundaryOutputIndices[Math.Clamp(boundary.RunIndex, 0, bRuns.Count)] ?? merged.Runs.Count
        }));

        return merged;
    }

    // Clone a run and stamp it with one revision kind/author/date (clearing the mark when kind is None).
    private static Run Stamp(Run source, RevisionKind kind, string? author, string? dateXml)
    {
        var copy = DocumentModelCloner.CloneRun(source, RevisionClonePolicy.Preserve);
        copy.Revision = kind;
        copy.RevisionAuthor = kind == RevisionKind.None ? null : author;
        copy.RevisionDateXml = kind == RevisionKind.None ? null : dateXml;
        copy.MoveRevisionId = kind == RevisionKind.None ? null : copy.MoveRevisionId;
        return copy;
    }

    private static void CopyShell(TextDocument source, TextDocument target)
    {
        DocumentModelCloner.CopyShellBase(source, target);

        // Mirrors DocumentCompare.CopyDocumentShell: footnotes/endnotes/final-section headers-footers live
        // on the document, not on any Paragraph, so the merged body's surviving footnote/endnote references
        // and page headers/footers would otherwise vanish (and dangle) even though blacklineB already
        // carries them correctly from its own DocumentCompare.Compare call.
        foreach (var (id, footnote) in source.Footnotes)
            target.Footnotes[id] = DocumentModelCloner.CloneFootnote(footnote, RevisionClonePolicy.Strip);
        foreach (var (id, endnote) in source.Endnotes)
            target.Endnotes[id] = DocumentModelCloner.CloneEndnote(endnote, RevisionClonePolicy.Strip);

        var finalHeadersFooters = DocumentModelCloner.CloneSectionHeadersFooters(
            source.FinalSectionHeadersFooters,
            RevisionClonePolicy.Strip);
        target.FinalSectionHeadersFooters.Header = finalHeadersFooters.Header;
        target.FinalSectionHeadersFooters.Footer = finalHeadersFooters.Footer;
        target.FinalSectionHeadersFooters.EvenHeader = finalHeadersFooters.EvenHeader;
        target.FinalSectionHeadersFooters.EvenFooter = finalHeadersFooters.EvenFooter;
        target.FinalSectionHeadersFooters.FirstHeader = finalHeadersFooters.FirstHeader;
        target.FinalSectionHeadersFooters.FirstFooter = finalHeadersFooters.FirstFooter;

        target.Preserved.CopyFrom(source.Preserved);
    }

}
