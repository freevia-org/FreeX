namespace FreeW.Core.Model;

/// <summary>
/// Pure, unit-testable operations over tracked changes (revisions) carried on <see cref="Run.Revision"/>,
/// <see cref="TableRow.RowRevision"/> and <see cref="Paragraph.MarkRevision"/>.
/// Accept turns insertions into ordinary content and drops deletions; Reject does the inverse (drops
/// insertions, restores deletions to ordinary content). Both walk every paragraph and table row in the
/// document body (including paragraphs nested in table cells, and tables nested inside table cells to
/// any depth) and clear the revision marks they resolve.
/// The document is mutated in place; nothing here touches the editor or docx layers.
/// </summary>
public static class TrackChanges
{
    /// <summary>
    /// True when any run, paragraph, or table row anywhere in the document body carries a tracked-change
    /// mark — an insertion or deletion (<see cref="Run.Revision"/>, <see cref="TableRow.RowRevision"/>,
    /// <see cref="Paragraph.MarkRevision"/>), a tracked run-formatting change
    /// (<see cref="Run.FormatRevision"/>), or a tracked paragraph-formatting change
    /// (<see cref="Paragraph.ParagraphFormatRevision"/>).
    /// </summary>
    public static bool HasRevisions(TextDocument document)
    {
        if (BlocksHaveRevisions(document.Blocks))
            return true;

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            if (HeaderFooterHasRevisions(headersFooters.Header) ||
                HeaderFooterHasRevisions(headersFooters.Footer) ||
                HeaderFooterHasRevisions(headersFooters.EvenHeader) ||
                HeaderFooterHasRevisions(headersFooters.EvenFooter) ||
                HeaderFooterHasRevisions(headersFooters.FirstHeader) ||
                HeaderFooterHasRevisions(headersFooters.FirstFooter))
            {
                return true;
            }
        }

        if (document.Footnotes.Values.Any(footnote => footnote.Content.Any(ParagraphHasRevisions)))
            return true;
        if (document.Endnotes.Values.Any(endnote => endnote.Content.Any(ParagraphHasRevisions)))
            return true;

        return false;
    }

    private static bool HeaderFooterHasRevisions(HeaderFooter? headerFooter) =>
        headerFooter is not null &&
        (headerFooter.Paragraphs.Any(ParagraphHasRevisions) ||
         (headerFooter.Table is { } table && TableHasRevisions(table)));

    /// <summary>
    /// Accept every tracked change: inserted runs/rows/paragraph marks become ordinary content (their
    /// revision mark cleared) and deleted runs/rows/paragraph marks are removed entirely (a deleted row
    /// disappears; a deleted paragraph mark merges this paragraph into the following one). The document
    /// is mutated in place.
    /// </summary>
    public static void AcceptAll(TextDocument document) => Resolve(document, accept: true);

    /// <summary>
    /// Reject every tracked change: inserted runs/rows/paragraph marks are removed entirely (an inserted
    /// row disappears; an inserted paragraph mark's split is undone, merging this paragraph into the
    /// following one) and deleted runs/rows/paragraph marks are restored to ordinary content (their
    /// revision mark cleared). The document is mutated in place.
    /// </summary>
    public static void RejectAll(TextDocument document) => Resolve(document, accept: false);

    // --- HasRevisions detection ---

    private static bool BlocksHaveRevisions(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph && ParagraphHasRevisions(paragraph))
                return true;
            if (block is Table table && TableHasRevisions(table))
                return true;
        }
        return false;
    }

    private static bool ParagraphHasRevisions(Paragraph paragraph) =>
        paragraph.ParagraphFormatRevision is not null ||
        paragraph.MarkRevision != RevisionKind.None ||
        paragraph.Runs.Any(r => r.Revision != RevisionKind.None || r.FormatRevision is not null);

    private static bool TableHasRevisions(Table table) =>
        table.Rows.Any(row =>
            row.RowRevision != RevisionKind.None ||
            row.Cells.Any(cell =>
                cell.Paragraphs.Any(ParagraphHasRevisions) ||
                cell.NestedTables.Any(TableHasRevisions)));

    // --- Accept/Reject resolution ---

    // Resolves the document body plus every header/footer slot of every section, every footnote and
    // every endnote — anywhere DocxReader can attach a Run.Revision/Paragraph.MarkRevision/
    // TableRow.RowRevision mark. Headers/footers and footnotes/endnotes are parsed through the same
    // ReadParagraph path as the body (DocxReader), so a tracked change can land in any of them; before
    // this, Accept All / Reject All (and Document Inspector's "Remove Revisions", which is this same
    // AcceptAll) silently left those revisions in the saved document while reporting "no revisions".
    private static void Resolve(TextDocument document, bool accept)
    {
        ResolveBlockList(document.Blocks, accept);

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            ResolveHeaderFooter(headersFooters.Header, accept);
            ResolveHeaderFooter(headersFooters.Footer, accept);
            ResolveHeaderFooter(headersFooters.EvenHeader, accept);
            ResolveHeaderFooter(headersFooters.EvenFooter, accept);
            ResolveHeaderFooter(headersFooters.FirstHeader, accept);
            ResolveHeaderFooter(headersFooters.FirstFooter, accept);
        }

        foreach (var footnote in document.Footnotes.Values)
            ResolveParagraphContainer(footnote.Content, accept);
        foreach (var endnote in document.Endnotes.Values)
            ResolveParagraphContainer(endnote.Content, accept);
    }

    // Resolves one header/footer slot. Most headers/footers are plain paragraph content
    // (HeaderFooter.Table is null), resolved exactly like a table cell's paragraph list. A minority carry
    // a preserved side-by-side layout table (HeaderFooter.Table set), whose cells hold the SAME Paragraph
    // instances flattened into HeaderFooter.Paragraphs (see HeaderFooterTableParagraphMap) — resolving the
    // table (which may merge/drop paragraphs within a cell, or drop a whole row) and then re-flattening
    // Paragraphs from it keeps that "same instances, same order" invariant intact for every other
    // header/footer editing command that relies on it.
    private static void ResolveHeaderFooter(HeaderFooter? headerFooter, bool accept)
    {
        if (headerFooter is null)
            return;

        if (headerFooter.Table is { } table)
        {
            ResolveTable(table, accept, accept ? RevisionKind.Deleted : RevisionKind.Inserted);
            headerFooter.Paragraphs.Clear();
            foreach (var row in table.Rows)
                foreach (var cell in row.Cells)
                    headerFooter.Paragraphs.AddRange(cell.Paragraphs);
            return;
        }

        ResolveParagraphContainer(headerFooter.Paragraphs, accept);
    }

    // Walk a body's block list (top-level document blocks). Tables are resolved row-by-row (rows may be
    // removed); paragraphs are resolved for their runs/formatting and their paragraph-mark revision. A
    // paragraph whose mark revision resolves to "removed" (an accepted deletion or a rejected insertion)
    // merges into the following paragraph in the SAME list, if one exists — mirroring how deleting a
    // pilcrow in Word joins two paragraphs into one, taking the surviving (next) paragraph's formatting.
    //
    // When there is no following paragraph to merge into (the paragraph is the last block, or the next
    // block is a table — a pilcrow cannot merge text into a table cell), the drop still has to be
    // *decided*, not silently discarded: an empty paragraph (no runs left after ResolveRunsAndFormat, and
    // no bookmark anchored on it) was purely a separator, so dropping its mark removes the whole paragraph
    // outright — exactly what merging into an (absent) next paragraph would have done to it. A paragraph
    // that still carries visible text or a bookmark cannot be silently discarded, so it keeps its (now
    // resolved) mark cleared and stays in place — the safe, non-destructive fallback.
    private static void ResolveBlockList(IList<Block> blocks, bool accept)
    {
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        var index = 0;
        while (index < blocks.Count)
        {
            switch (blocks[index])
            {
                case Paragraph paragraph:
                    ResolveRunsAndFormat(paragraph, accept, dropKind);

                    if (paragraph.MarkRevision == RevisionKind.None)
                    {
                        index++;
                        break;
                    }

                    if (paragraph.MarkRevision == dropKind)
                    {
                        if (index + 1 < blocks.Count && blocks[index + 1] is Paragraph nextParagraph)
                        {
                            nextParagraph.Runs.InsertRange(0, paragraph.Runs);
                            blocks.RemoveAt(index);
                            // blocks[index] is now the merged (former next) paragraph — re-visit it so
                            // its own runs/format/mark revision are resolved too.
                            break;
                        }

                        if (IsEmptyUnanchoredParagraph(paragraph) && blocks.Count > 1)
                        {
                            blocks.RemoveAt(index);
                            break;
                        }
                    }

                    ClearMarkRevision(paragraph);
                    index++;
                    break;

                case Table table:
                    ResolveTable(table, accept, dropKind);
                    index++;
                    break;

                default:
                    index++;
                    break;
            }
        }
    }

    // A paragraph whose dropped mark can safely take the whole paragraph with it: no surviving run
    // content and no bookmark anchored on it (removing the paragraph would otherwise silently delete the
    // bookmark — the paragraph-mark resolution here must not widen into destroying that).
    private static bool IsEmptyUnanchoredParagraph(Paragraph paragraph) =>
        paragraph.Runs.Count == 0 && paragraph.BookmarkNames.Count == 0;

    // Resolve every row of a table: a row whose RowRevision resolves to "removed" (an accepted deletion
    // or a rejected insertion) is dropped entirely; a kept row has its revision mark cleared and its
    // cells' paragraphs resolved. Any table nested inside a cell (to any depth) is resolved the same way,
    // recursively — a tracked change inside a nested table must not survive Accept/Reject All.
    private static void ResolveTable(Table table, bool accept, RevisionKind dropKind)
    {
        for (var i = table.Rows.Count - 1; i >= 0; i--)
        {
            var row = table.Rows[i];
            if (row.RowRevision != RevisionKind.None)
            {
                if (row.RowRevision == dropKind)
                {
                    table.Rows.RemoveAt(i);
                    continue;
                }
                row.RowRevision = RevisionKind.None;
                row.RowRevisionAuthor = null;
                row.RowRevisionDateXml = null;
            }

            foreach (var cell in row.Cells)
            {
                ResolveParagraphContainer(cell.Paragraphs, accept);
                foreach (var nestedTable in cell.NestedTables)
                    ResolveTable(nestedTable, accept, dropKind);
            }
        }
    }

    // Resolve a self-contained paragraph list (a table cell's paragraphs) the same way as the top-level
    // body: runs/formatting per paragraph, plus paragraph-mark merges within the same cell. When the
    // paragraph is the cell's last one (nothing to merge into), an empty/unanchored paragraph is dropped
    // outright — unless it is the cell's only remaining paragraph, since every table cell must keep at
    // least one (see ResolveBlockList for the full rationale, shared with the top-level body walk).
    private static void ResolveParagraphContainer(IList<Paragraph> paragraphs, bool accept)
    {
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        var index = 0;
        while (index < paragraphs.Count)
        {
            var paragraph = paragraphs[index];
            ResolveRunsAndFormat(paragraph, accept, dropKind);

            if (paragraph.MarkRevision == RevisionKind.None)
            {
                index++;
                continue;
            }

            if (paragraph.MarkRevision == dropKind)
            {
                if (index + 1 < paragraphs.Count)
                {
                    var next = paragraphs[index + 1];
                    next.Runs.InsertRange(0, paragraph.Runs);
                    paragraphs.RemoveAt(index);
                    continue; // re-visit the merged (former next) paragraph at the same index
                }

                if (IsEmptyUnanchoredParagraph(paragraph) && paragraphs.Count > 1)
                {
                    paragraphs.RemoveAt(index);
                    continue;
                }
            }

            ClearMarkRevision(paragraph);
            index++;
        }
    }

    private static void ClearMarkRevision(Paragraph paragraph)
    {
        paragraph.MarkRevision = RevisionKind.None;
        paragraph.MarkRevisionAuthor = null;
        paragraph.MarkRevisionDateXml = null;
    }

    // Resolve all run-level and paragraph-formatting revision marks in one paragraph. On accept,
    // deletions are dropped and insertions kept; on reject, insertions are dropped and deletions kept. A
    // tracked formatting change (FormatRevision on runs, ParagraphFormatRevision on the paragraph) is
    // resolved independently of any insert/delete mark: accept keeps the current formatting and clears
    // the mark; reject restores the previous formatting. Kept runs have their revision metadata cleared.
    private static void ResolveRunsAndFormat(Paragraph paragraph, bool accept, RevisionKind dropKind)
    {
        // Paragraph-level tracked formatting change (w:pPrChange): accept keeps current formatting,
        // reject restores the previous paragraph formatting captured in ParagraphFormatRevision.
        if (paragraph.ParagraphFormatRevision is { } pFormatRevision)
        {
            if (!accept)
                paragraph.Formatting = pFormatRevision.PreviousParagraphFormatting;
            paragraph.ParagraphFormatRevision = null;
        }

        for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
        {
            var run = paragraph.Runs[i];

            // Formatting change: on reject, restore the previous formatting; either way clear the mark.
            // A run dropped below (insertion rejected / deletion accepted) needs no formatting fix-up.
            if (run.FormatRevision is { } formatRevision)
            {
                if (!accept)
                    run.Formatting = formatRevision.PreviousFormatting;
                run.FormatRevision = null;
            }

            if (run.Revision == RevisionKind.None)
                continue;
            if (run.Revision == dropKind)
            {
                paragraph.Runs.RemoveAt(i);
            }
            else
            {
                run.Revision = RevisionKind.None;
                run.RevisionAuthor = null;
                run.RevisionDateXml = null;
            }
        }
    }
}
