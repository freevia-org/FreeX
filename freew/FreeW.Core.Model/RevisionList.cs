namespace FreeW.Core.Model;

/// <summary>
/// The kind of tracked change a <see cref="RevisionEntry"/> describes — what Word's Reviewing Pane shows
/// as the revision's verb. <see cref="Insertion"/>/<see cref="Deletion"/> mirror <see cref="RevisionKind"/>;
/// <see cref="Formatting"/> is a tracked formatting change (<c>w:rPrChange</c>) carried on an otherwise
/// ordinary run.
/// </summary>
public enum RevisionEntryKind
{
    Insertion,
    Deletion,
    Formatting
}

/// <summary>
/// One reviewable tracked change, as surfaced by the Reviewing Pane: the change's <see cref="Kind"/>,
/// <see cref="Author"/>, <see cref="DateXml"/> (the raw W3CDTF timestamp, or null), and the affected
/// <see cref="Text"/>. <see cref="BlockIndex"/> is the index of the owning paragraph in the document's
/// body-paragraph walk (top-level paragraphs and those nested in table cells, in order) — a stable handle
/// for click-to-navigate. The owning <see cref="Paragraph"/> and <see cref="Run"/> are carried so a single
/// entry can be accepted or rejected directly. <see cref="Run"/> is null for a tracked change on the
/// paragraph's own end-of-paragraph mark (<see cref="Paragraph.MarkRevision"/>, Word's pilcrow) rather than
/// on one of its runs — <see cref="Text"/> is <see cref="FormattingMarks.Pilcrow"/> for that case.
/// Immutable snapshot: re-enumerate after any accept/reject.
/// </summary>
public sealed record RevisionEntry(
    int BlockIndex,
    RevisionEntryKind Kind,
    string? Author,
    string? DateXml,
    string Text,
    Paragraph Paragraph,
    Run? Run);

/// <summary>
/// Pure, unit-testable enumeration and single-revision resolution over the tracked changes carried on the
/// document's runs — the model behind Word's Reviewing Pane and its Accept/Reject (this one) + Previous/Next
/// commands. <see cref="Enumerate"/> lists every revision in reading order; <see cref="Accept"/> and
/// <see cref="Reject"/> resolve exactly one of them (leaving every other revision untouched), reusing the
/// same accept/reject semantics as <see cref="TrackChanges"/>. Nothing here touches the editor or docx layers.
/// </summary>
public static class RevisionList
{
    /// <summary>
    /// Every tracked change reachable anywhere <see cref="TrackChanges"/> resolves one — the document body
    /// (top-level paragraphs and those nested in table cells, to any depth, plus any text-box shape), every
    /// header/footer slot of every section, and every footnote/endnote — in reading order: for each such
    /// paragraph, each run carrying an insertion/deletion mark, each run carrying a tracked formatting
    /// change, and (if set) the paragraph's own end-of-paragraph-mark revision
    /// (<see cref="Paragraph.MarkRevision"/>, a <see cref="RevisionEntry"/> with a null <see cref="RevisionEntry.Run"/>),
    /// as a <see cref="RevisionEntry"/>. A single run can yield two entries when it is both inserted/deleted
    /// and format-changed (mirroring how <see cref="TrackChanges"/> resolves the two marks independently).
    /// Order matches the Reviewing Pane and drives Previous/Next navigation.
    /// </summary>
    public static IReadOnlyList<RevisionEntry> Enumerate(TextDocument document)
    {
        var entries = new List<RevisionEntry>();
        var blockIndex = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.Revision == RevisionKind.Inserted)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Insertion, run.RevisionAuthor, run.RevisionDateXml, run.Text, paragraph, run));
                else if (run.Revision == RevisionKind.Deleted)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Deletion, run.RevisionAuthor, run.RevisionDateXml, run.Text, paragraph, run));

                if (run.FormatRevision is { } format)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Formatting, format.Author, format.DateXml, run.Text, paragraph, run));
            }

            var markText = FormattingMarks.Pilcrow.ToString();
            if (paragraph.MarkRevision == RevisionKind.Inserted)
                entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Insertion, paragraph.MarkRevisionAuthor, paragraph.MarkRevisionDateXml, markText, paragraph, null));
            else if (paragraph.MarkRevision == RevisionKind.Deleted)
                entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Deletion, paragraph.MarkRevisionAuthor, paragraph.MarkRevisionDateXml, markText, paragraph, null));

            blockIndex++;
        }

        return entries;
    }

    /// <summary>
    /// Accept exactly the change described by <paramref name="entry"/>, leaving every other revision in
    /// place. An insertion becomes ordinary text; a deletion's run is removed; a formatting change keeps the
    /// new formatting and clears its mark. The document is mutated in place. A no-op if the entry's run is no
    /// longer in its paragraph (e.g. the list is stale). Returns true when something was resolved.
    /// </summary>
    public static bool Accept(TextDocument document, RevisionEntry entry) => Resolve(document, entry, accept: true);

    /// <summary>
    /// Reject exactly the change described by <paramref name="entry"/>, leaving every other revision in
    /// place. An insertion's run is removed; a deletion becomes ordinary text; a formatting change restores
    /// the previous formatting and clears its mark. The document is mutated in place. A no-op if the entry's
    /// run is no longer in its paragraph (stale list). Returns true when something was resolved.
    /// </summary>
    public static bool Reject(TextDocument document, RevisionEntry entry) => Resolve(document, entry, accept: false);

    // Resolve one entry's mark only. For an insertion/deletion entry we touch the run's Revision mark; for a
    // formatting entry we touch only its FormatRevision (the two are independent, exactly as TrackChanges
    // treats them). This deliberately does NOT clear the other mark on a doubly-marked run, so accepting an
    // insertion on a run that is also format-changed leaves the format revision pending (and vice versa).
    //
    // A Word move (w:moveFrom/w:moveTo) is modelled as two runs sharing Run.MoveRevisionId: the source run
    // (RevisionKind.Deleted) and the destination run (RevisionKind.Inserted) -- see TextDocument.cs's
    // MoveRevisionId doc comment. Resolving only one half independently corrupts the document (the moved
    // text is duplicated or lost entirely), so when the entry's run carries a MoveRevisionId we look up its
    // paired run anywhere in the document body and resolve it in the SAME accept/reject direction, right
    // here, in the same call. This mirrors how TrackChanges.AcceptAll/RejectAll already behave -- they
    // happen to be safe because every run in the document is resolved in one direction -- so a linked move
    // now gets that same "both halves together" treatment from the single-entry Accept/Reject path too.
    private static bool Resolve(TextDocument document, RevisionEntry entry, bool accept)
    {
        var paragraph = entry.Paragraph;
        var run = entry.Run;
        if (run is null)
            return ResolveMarkRevision(document, paragraph, accept);

        var index = paragraph.Runs.IndexOf(run);
        if (index < 0)
            return false;

        if (entry.Kind == RevisionEntryKind.Formatting)
        {
            if (run.FormatRevision is not { } format)
                return false;
            if (!accept)
                run.Formatting = format.PreviousFormatting;
            run.FormatRevision = null;
            return true;
        }

        // Capture the paired move run (if any) before mutating -- removing entry's run must not disturb
        // the search, and the pair must be resolved even though it lives in a different Run instance (and
        // possibly a different paragraph).
        var moveId = run.MoveRevisionId;
        var pair = moveId is { } id ? FindMovePair(document, run, id) : null;

        if (!ResolveInsertionOrDeletion(paragraph, run, index, accept))
            return false;

        if (pair is { } linked)
        {
            var pairIndex = linked.Paragraph.Runs.IndexOf(linked.Run);
            if (pairIndex >= 0)
                ResolveInsertionOrDeletion(linked.Paragraph, linked.Run, pairIndex, accept);
        }

        return true;
    }

    // Drop the run when the change is being thrown away (insertion rejected or deletion accepted);
    // otherwise keep the run as ordinary text and clear its revision metadata.
    private static bool ResolveInsertionOrDeletion(Paragraph paragraph, Run run, int index, bool accept)
    {
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        if (run.Revision == RevisionKind.None)
            return false;

        if (run.Revision == dropKind)
        {
            paragraph.Runs.RemoveAt(index);
        }
        else
        {
            run.Revision = RevisionKind.None;
            run.RevisionAuthor = null;
            run.RevisionDateXml = null;
        }

        return true;
    }

    // The other half of a move: same MoveRevisionId, a different Run instance, anywhere in the document
    // body (source and destination are usually in different paragraphs).
    private static (Paragraph Paragraph, Run Run)? FindMovePair(TextDocument document, Run self, int moveId)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var candidate in paragraph.Runs)
            {
                if (!ReferenceEquals(candidate, self) && candidate.MoveRevisionId == moveId)
                    return (paragraph, candidate);
            }
        }

        return null;
    }

    // Every paragraph reachable anywhere TrackChanges.HasRevisions/AcceptAll/RejectAll look: the document
    // body (top-level paragraphs and those nested in table cells, including tables nested inside table
    // cells, to any depth, plus the text-box content of any Run.Shape a run carries -- see
    // BodyParagraphWalk), every header/footer slot of every section, and every footnote/endnote. Before
    // this, RevisionList only walked the body, so a tracked change living in a header/footer/footnote/
    // endnote produced an empty Reviewing Pane and no per-item Accept/Reject even though
    // TrackChanges.HasRevisions/AcceptAll/RejectAll (TrackChanges.cs's own Resolve) already reached it.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var paragraph in BodyParagraphWalk.Enumerate(document))
            yield return paragraph;

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.Header)) yield return paragraph;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.Footer)) yield return paragraph;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.EvenHeader)) yield return paragraph;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.EvenFooter)) yield return paragraph;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.FirstHeader)) yield return paragraph;
            foreach (var paragraph in HeaderFooterParagraphs(headersFooters.FirstFooter)) yield return paragraph;
        }

        foreach (var footnote in document.Footnotes.Values)
            foreach (var paragraph in BodyParagraphWalk.Enumerate(footnote.Content))
                yield return paragraph;
        foreach (var endnote in document.Endnotes.Values)
            foreach (var paragraph in BodyParagraphWalk.Enumerate(endnote.Content))
                yield return paragraph;
    }

    private static IEnumerable<Paragraph> HeaderFooterParagraphs(HeaderFooter? headerFooter) =>
        headerFooter is null ? [] : BodyParagraphWalk.Enumerate(headerFooter.Paragraphs);

    // Resolve a paragraph-mark revision (Paragraph.MarkRevision) in isolation, leaving every other
    // revision untouched -- the mark-revision counterpart to ResolveInsertionOrDeletion above. Mirrors
    // TrackChanges.ResolveBlockList/ResolveParagraphContainer's own paragraph-mark handling: an accepted
    // deletion (or rejected insertion) of the mark merges this paragraph's runs into the following
    // paragraph in the SAME container (or, if there is no following paragraph and this one is empty and
    // unanchored, drops it outright); otherwise the mark is simply cleared. Unlike TrackChanges' bulk
    // Resolve, only the ONE target paragraph is touched -- every other paragraph's mark (and every run
    // revision anywhere) is left exactly as it was.
    private static bool ResolveMarkRevision(TextDocument document, Paragraph target, bool accept)
    {
        if (target.MarkRevision == RevisionKind.None)
            return false;

        if (TryResolveMarkInBlockList(document.Blocks, target, accept))
            return true;

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            if (TryResolveMarkInHeaderFooter(headersFooters.Header, target, accept)) return true;
            if (TryResolveMarkInHeaderFooter(headersFooters.Footer, target, accept)) return true;
            if (TryResolveMarkInHeaderFooter(headersFooters.EvenHeader, target, accept)) return true;
            if (TryResolveMarkInHeaderFooter(headersFooters.EvenFooter, target, accept)) return true;
            if (TryResolveMarkInHeaderFooter(headersFooters.FirstHeader, target, accept)) return true;
            if (TryResolveMarkInHeaderFooter(headersFooters.FirstFooter, target, accept)) return true;
        }

        foreach (var footnote in document.Footnotes.Values)
            if (TryResolveMarkInParagraphList(footnote.Content, target, accept))
                return true;
        foreach (var endnote in document.Endnotes.Values)
            if (TryResolveMarkInParagraphList(endnote.Content, target, accept))
                return true;

        return false;
    }

    private static bool TryResolveMarkInHeaderFooter(HeaderFooter? headerFooter, Paragraph target, bool accept) =>
        headerFooter is not null && TryResolveMarkInParagraphList(headerFooter.Paragraphs, target, accept);

    // Top-level body block list: a paragraph may merge into a following paragraph in the SAME list, but
    // only when that next block is itself a paragraph (a pilcrow cannot merge text into a table). Tables
    // (including nested tables and any cell's text-box shapes) are searched recursively for the target.
    private static bool TryResolveMarkInBlockList(IList<Block> blocks, Paragraph target, bool accept)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            switch (blocks[index])
            {
                case Paragraph paragraph when ReferenceEquals(paragraph, target):
                    ResolveMarkAtBlockIndex(blocks, index, accept);
                    return true;
                case Paragraph paragraph when TryResolveMarkInShapes(paragraph, target, accept):
                    return true;
                case Table table when TryResolveMarkInTable(table, target, accept):
                    return true;
            }
        }

        return false;
    }

    private static void ResolveMarkAtBlockIndex(IList<Block> blocks, int index, bool accept)
    {
        var paragraph = (Paragraph)blocks[index];
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        if (paragraph.MarkRevision == dropKind)
        {
            if (index + 1 < blocks.Count && blocks[index + 1] is Paragraph next)
            {
                next.Runs.InsertRange(0, paragraph.Runs);
                blocks.RemoveAt(index);
                return;
            }

            if (IsEmptyUnanchoredParagraph(paragraph) && blocks.Count > 1)
            {
                blocks.RemoveAt(index);
                return;
            }
        }

        ClearMarkRevision(paragraph);
    }

    private static bool TryResolveMarkInTable(Table table, Paragraph target, bool accept)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (TryResolveMarkInParagraphList(cell.Paragraphs, target, accept))
                    return true;
                foreach (var nested in cell.NestedTables)
                    if (TryResolveMarkInTable(nested, target, accept))
                        return true;
            }
        }

        return false;
    }

    // A self-contained paragraph list (a table cell's paragraphs, a header/footer slot, or a
    // footnote's/endnote's content) resolved the same way as the top-level body: the next paragraph in
    // the SAME list is always a valid merge target (there is no sibling table to worry about).
    private static bool TryResolveMarkInParagraphList(IList<Paragraph> paragraphs, Paragraph target, bool accept)
    {
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            if (ReferenceEquals(paragraph, target))
            {
                ResolveMarkAtParagraphIndex(paragraphs, index, accept);
                return true;
            }

            if (TryResolveMarkInShapes(paragraph, target, accept))
                return true;
        }

        return false;
    }

    private static void ResolveMarkAtParagraphIndex(IList<Paragraph> paragraphs, int index, bool accept)
    {
        var paragraph = paragraphs[index];
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        if (paragraph.MarkRevision == dropKind)
        {
            if (index + 1 < paragraphs.Count)
            {
                paragraphs[index + 1].Runs.InsertRange(0, paragraph.Runs);
                paragraphs.RemoveAt(index);
                return;
            }

            if (IsEmptyUnanchoredParagraph(paragraph) && paragraphs.Count > 1)
            {
                paragraphs.RemoveAt(index);
                return;
            }
        }

        ClearMarkRevision(paragraph);
    }

    private static bool TryResolveMarkInShapes(Paragraph host, Paragraph target, bool accept)
    {
        foreach (var run in host.Runs)
        {
            if (run.Shape is { } shape && TryResolveMarkInParagraphList(shape.TextParagraphs, target, accept))
                return true;
        }

        return false;
    }

    // A paragraph whose dropped mark can safely take the whole paragraph with it: no surviving run
    // content and no bookmark anchored on it (mirrors TrackChanges.IsEmptyUnanchoredParagraph).
    private static bool IsEmptyUnanchoredParagraph(Paragraph paragraph) =>
        paragraph.Runs.Count == 0 && paragraph.BookmarkNames.Count == 0;

    private static void ClearMarkRevision(Paragraph paragraph)
    {
        paragraph.MarkRevision = RevisionKind.None;
        paragraph.MarkRevisionAuthor = null;
        paragraph.MarkRevisionDateXml = null;
    }
}
