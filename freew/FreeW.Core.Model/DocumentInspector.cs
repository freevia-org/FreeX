using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// The report produced by <see cref="DocumentInspector.Inspect(TextDocument)"/>: a snapshot of the
/// metadata a document carries that a user may want to strip before sharing. Immutable record of plain
/// counts plus convenience <c>Has*</c> flags. <see cref="Revisions"/> counts tracked insertions and
/// deletions together; <see cref="NonEmptyProperties"/> counts the populated core document properties
/// (title/author/etc.); <see cref="Bookmarks"/> counts named paragraph bookmarks across the whole body
/// (including table cells).
/// </summary>
public sealed record InspectionResult(
    int Comments,
    int Revisions,
    int NonEmptyProperties,
    int Bookmarks)
{
    /// <summary>True when the document carries at least one review comment.</summary>
    public bool HasComments => Comments > 0;

    /// <summary>True when the document carries at least one tracked revision (insertion or deletion).</summary>
    public bool HasRevisions => Revisions > 0;

    /// <summary>True when at least one core document property is populated.</summary>
    public bool HasProperties => NonEmptyProperties > 0;

    /// <summary>True when the document carries at least one named bookmark.</summary>
    public bool HasBookmarks => Bookmarks > 0;

    /// <summary>True when the document carries none of the inspected metadata categories.</summary>
    public bool IsClean => Comments == 0 && Revisions == 0 && NonEmptyProperties == 0 && Bookmarks == 0;
}

/// <summary>Categories selected for removal by the Document Inspector.</summary>
public sealed record InspectionRemovalSelection(
    bool Comments,
    bool Revisions,
    bool Properties,
    bool Bookmarks)
{
    public bool Any => Comments || Revisions || Properties || Bookmarks;
}

/// <summary>Inspection snapshots immediately before and after a selected removal operation.</summary>
public sealed record InspectionRemovalResult(InspectionResult Before, InspectionResult After)
{
    public InspectionResult Removed => new(
        Math.Max(0, Before.Comments - After.Comments),
        Math.Max(0, Before.Revisions - After.Revisions),
        Math.Max(0, Before.NonEmptyProperties - After.NonEmptyProperties),
        Math.Max(0, Before.Bookmarks - After.Bookmarks));
}

/// <summary>
/// Pure, WPF-free "Document Inspector": reports — and optionally removes — the metadata a document
/// accumulates that a user may want to strip before sharing (review comments, tracked revisions,
/// core document properties, and named bookmarks). Lives in the model project so it is fully
/// unit-testable without any UI and touches no docx I/O — it only mutates the in-memory
/// <see cref="TextDocument"/>; the existing writer then emits the cleaned document unchanged.
/// <para>
/// All removal operations <b>mutate the passed document in place</b> (mirroring
/// <see cref="TrackChanges"/>) and return nothing; they are deterministic and idempotent (running one
/// twice leaves the same result, and re-inspecting reports zero for that category).
/// </para>
/// </summary>
public static class DocumentInspector
{
    /// <summary>
    /// Inspect <paramref name="document"/> and report counts of comments, tracked revisions
    /// (insertions + deletions), populated core document properties, and named bookmarks. Pure: it never
    /// mutates the document.
    /// </summary>
    public static InspectionResult Inspect(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var comments = document.Comments.Count;

        var revisions = CountRevisions(document);

        var properties = CountNonEmptyProperties(document.Properties);

        var bookmarks = EnumerateParagraphs(document)
            .Sum(p => p.BookmarkNames.Count(n => !string.IsNullOrEmpty(n)));

        return new InspectionResult(comments, revisions, properties, bookmarks);
    }

    /// <summary>
    /// Applies exactly the categories selected by the inspector dialog and returns before/after evidence.
    /// This is the canonical renderer-independent execution path; unselected categories remain untouched.
    /// </summary>
    public static InspectionRemovalResult RemoveSelected(
        TextDocument document,
        InspectionRemovalSelection selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);

        var before = Inspect(document);
        if (selection.Comments)
            RemoveComments(document);
        if (selection.Revisions)
            RemoveRevisions(document);
        if (selection.Properties)
            RemoveProperties(document);
        if (selection.Bookmarks)
            RemoveBookmarks(document);
        return new InspectionRemovalResult(before, Inspect(document));
    }

    /// <summary>
    /// Remove every review comment: clears the document's <see cref="TextDocument.Comments"/> store and
    /// strips the comment marks from every run that can carry one — both the <see cref="Run.CommentId"/>
    /// on covered runs and the textless comment-reference anchor runs (see
    /// <see cref="Run.IsCommentReference"/>), which are removed entirely. Comments legitimately live
    /// outside the body too (Word allows anchoring one in a header, footer, footnote, or endnote), so this
    /// walks every such paragraph store via <see cref="EnumerateCommentAnchorParagraphs"/> — not just
    /// <see cref="EnumerateParagraphs"/>'s body/table paragraphs — so no anchor is left dangling with a
    /// <see cref="Run.CommentId"/> that no longer resolves to any entry in
    /// <see cref="TextDocument.Comments"/> (the docx writer would otherwise still emit its
    /// w:commentRangeStart/End/w:commentReference, producing a package Word must repair). Mutates
    /// <paramref name="document"/> in place.
    /// </summary>
    public static void RemoveComments(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Comments.Clear();

        foreach (var paragraph in EnumerateCommentAnchorParagraphs(document))
        {
            for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
            {
                var run = paragraph.Runs[i];
                if (run.IsCommentReference)
                {
                    // The anchor run carries no literal text — drop it outright.
                    paragraph.Runs.RemoveAt(i);
                    continue;
                }
                if (run.CommentId is not null)
                {
                    run.CommentId = null;
                }
            }
        }
    }

    /// <summary>
    /// Removes <see cref="TextDocument.Footnotes"/>/<see cref="TextDocument.Endnotes"/>/<see cref="TextDocument.Comments"/>
    /// entries that no longer have any live reference/anchor run anywhere in the document (body, tables,
    /// headers/footers, or footnote/endnote content). A host can delete a footnote/endnote/comment
    /// reference-mark run directly out of its live editing surface (e.g. a native RichTextBox Backspace/
    /// Delete that a model-aware edit path declined to handle) with no awareness of the note/comment
    /// dictionaries; call this after such an edit is read back into the model so the dictionary entry is
    /// pruned instead of lingering unreachable in the saved package — mirroring how Word drops a
    /// footnote/endnote/comment when the text carrying its reference mark is deleted. Only entries with
    /// NO surviving anchor anywhere are removed; an entry whose anchor still exists (even elsewhere, e.g.
    /// a header/footer) is left untouched. Mutates <paramref name="document"/> in place.
    /// </summary>
    public static void PruneOrphanedNoteAndCommentAnchors(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Footnotes.Count == 0 && document.Endnotes.Count == 0 && document.Comments.Count == 0)
            return;

        var usedFootnoteIds = new HashSet<int>();
        var usedEndnoteIds = new HashSet<int>();
        var usedCommentIds = new HashSet<int>();

        foreach (var paragraph in EnumerateCommentAnchorParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.FootnoteId is { } footnoteId)
                    usedFootnoteIds.Add(footnoteId);
                if (run.EndnoteId is { } endnoteId)
                    usedEndnoteIds.Add(endnoteId);
                if (run.CommentId is { } commentId)
                    usedCommentIds.Add(commentId);
            }
        }

        foreach (var id in document.Footnotes.Keys.Where(id => !usedFootnoteIds.Contains(id)).ToList())
            document.Footnotes.Remove(id);
        foreach (var id in document.Endnotes.Keys.Where(id => !usedEndnoteIds.Contains(id)).ToList())
            document.Endnotes.Remove(id);
        foreach (var id in document.Comments.Keys.Where(id => !usedCommentIds.Contains(id)).ToList())
            document.Comments.Remove(id);
    }

    /// <summary>
    /// Remove every tracked revision by <b>accepting</b> all tracked changes (reusing
    /// <see cref="TrackChanges.AcceptAll(TextDocument)"/>): insertions become ordinary text and deletions
    /// are dropped, so no revision marks remain. "Remove" here therefore means "accept" — the document's
    /// visible text becomes the all-changes-accepted text. Mutates <paramref name="document"/> in place.
    /// </summary>
    public static void RemoveRevisions(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TrackChanges.AcceptAll(document);
    }

    /// <summary>
    /// Remove every core document property: resets every field of
    /// <see cref="TextDocument.Properties"/> to null. Mutates <paramref name="document"/> in place.
    /// </summary>
    public static void RemoveProperties(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Properties.Clear();
    }

    /// <summary>
    /// Remove every bookmark: clears the <see cref="Paragraph.BookmarkName"/> on every body paragraph
    /// (including those nested in table cells) and clears the internal-link anchors
    /// (<see cref="Run.HyperlinkAnchor"/>) that pointed at those bookmarks, since those links would
    /// otherwise dangle. External hyperlinks (<see cref="Run.HyperlinkUrl"/>) are left untouched. Mutates
    /// <paramref name="document"/> in place.
    /// </summary>
    public static void RemoveBookmarks(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var paragraph in EnumerateParagraphs(document))
        {
            paragraph.BookmarkNames.Clear();
            foreach (var run in paragraph.Runs)
            {
                if (run.HyperlinkAnchor is not null)
                    run.HyperlinkAnchor = null;
            }
        }
    }

    // Count the populated (non-null, non-whitespace for strings) core document properties.
    private static int CountNonEmptyProperties(DocumentProperties properties) =>
        properties.CountNonEmptyCoreProperties();

    /// <summary>
    /// Counts every tracked insertion/deletion mark (<see cref="Run.Revision"/>,
    /// <see cref="Paragraph.MarkRevision"/>, <see cref="TableRow.RowRevision"/>) that
    /// <see cref="RemoveRevisions"/> (i.e. <see cref="TrackChanges.AcceptAll"/>) actually resolves —
    /// mirroring <see cref="TrackChanges.HasRevisions"/>'s reach: the body (including table rows, table
    /// cells, nested tables, and text-box shape content, via <see cref="EnumerateParagraphs"/>), every
    /// header/footer slot of every section (including a side-by-side header/footer layout table's own
    /// rows), and every footnote's/endnote's own content. Before this, <see cref="Inspect"/> only summed
    /// <see cref="Run.Revision"/> across the body — a document whose only tracked change was a table-row
    /// insertion/deletion, a tracked paragraph-mark (Enter-key) split, or lived in a header, footer,
    /// footnote, or endnote reported zero revisions and a permanently disabled "Revisions" checkbox even
    /// though <see cref="TrackChanges.HasRevisions"/> — and hence <see cref="RemoveRevisions"/> — still
    /// found and resolved it.
    /// </summary>
    private static int CountRevisions(TextDocument document)
    {
        var count = EnumerateParagraphs(document).Sum(CountParagraphRevisionMarks)
            + CountTableRowRevisionMarks(document.Blocks.OfType<Table>());

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            foreach (var headerFooter in new[]
                     {
                         headersFooters.Header,
                         headersFooters.Footer,
                         headersFooters.EvenHeader,
                         headersFooters.EvenFooter,
                         headersFooters.FirstHeader,
                         headersFooters.FirstFooter,
                     })
            {
                if (headerFooter is null)
                    continue;

                count += BodyParagraphWalk.Enumerate(headerFooter.Paragraphs).Sum(CountParagraphRevisionMarks);
                if (headerFooter.Table is { } headerFooterTable)
                    count += CountTableRowRevisionMarks([headerFooterTable]);
            }
        }

        foreach (var footnote in document.Footnotes.Values)
            count += BodyParagraphWalk.Enumerate(footnote.Content).Sum(CountParagraphRevisionMarks);
        foreach (var endnote in document.Endnotes.Values)
            count += BodyParagraphWalk.Enumerate(endnote.Content).Sum(CountParagraphRevisionMarks);

        return count;
    }

    // One paragraph's own tracked-change marks: its paragraph-mark revision, its tracked
    // paragraph-formatting change (Paragraph.ParagraphFormatRevision), plus each run's own
    // insertion/deletion revision and tracked run-formatting change (Run.FormatRevision) — mirroring
    // TrackChanges.ParagraphHasRevisions's reach exactly, so a document whose only tracked change is a
    // formatting-only edit (bold/italic/color toggled with Track Changes on, no text inserted/deleted)
    // is still counted instead of reporting zero while RemoveRevisions (TrackChanges.AcceptAll) still
    // clears it. Does not walk into a run's text-box (Run.Shape) content itself — callers needing that
    // reach route the paragraph list through BodyParagraphWalk.Enumerate first (EnumerateParagraphs for
    // the body; the IEnumerable<Paragraph> overload for a header/footer/footnote/endnote's own list), which
    // yields shape paragraphs as separate entries before this is applied to each.
    private static int CountParagraphRevisionMarks(Paragraph paragraph) =>
        (paragraph.MarkRevision != RevisionKind.None ? 1 : 0) +
        (paragraph.ParagraphFormatRevision is not null ? 1 : 0) +
        paragraph.Runs.Count(r => r.Revision != RevisionKind.None || r.FormatRevision is not null);

    // Table-row tracked-change marks (TableRow.RowRevision) for every row in every given table,
    // recursing into any table nested inside a cell to any depth — mirrors TrackChanges.ResolveTable's
    // reach.
    private static int CountTableRowRevisionMarks(IEnumerable<Table> tables)
    {
        var count = 0;
        foreach (var table in tables)
        {
            foreach (var row in table.Rows)
            {
                if (row.RowRevision != RevisionKind.None)
                    count++;

                foreach (var cell in row.Cells)
                    count += CountTableRowRevisionMarks(cell.NestedTables);
            }
        }
        return count;
    }

    // Every paragraph reachable in the document body — top-level paragraphs and those nested in table
    // cells, including tables nested inside table cells to any depth, plus the text-box content of any
    // Run.Shape a run carries (see BodyParagraphWalk) — the same walk TrackChanges uses, so inspection/
    // removal cover all body runs and bookmarks, including those living in a text box.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document) =>
        BodyParagraphWalk.Enumerate(document);

    /// <summary>
    /// Every paragraph that can carry a comment anchor (<see cref="Run.CommentId"/> /
    /// <see cref="Run.IsCommentReference"/>): the body/table paragraphs from <see cref="EnumerateParagraphs"/>,
    /// plus every header/footer of every document section (default, even, and first-page slots — mirroring
    /// <see cref="NoteCommands.EnumerateHeaderFooterParagraphs"/>'s fix for the identical footnote/endnote
    /// dangling-marker bug), plus every footnote's and endnote's own content paragraphs. Word allows anchoring
    /// a review comment in any of these; without walking them too, <see cref="RemoveComments"/> would clear
    /// <see cref="TextDocument.Comments"/> while leaving header/footer/footnote/endnote runs still carrying a
    /// <see cref="Run.CommentId"/> that no longer resolves to anything, which the docx writer would then still
    /// serialise as a dangling w:commentRangeStart/End/w:commentReference.
    /// </summary>
    private static IEnumerable<Paragraph> EnumerateCommentAnchorParagraphs(TextDocument document)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
            yield return paragraph;

        foreach (var section in document.Sections)
        {
            var headersFooters = section.HeadersFooters;
            foreach (var headerFooter in new[]
                     {
                         headersFooters.Header,
                         headersFooters.Footer,
                         headersFooters.EvenHeader,
                         headersFooters.EvenFooter,
                         headersFooters.FirstHeader,
                         headersFooters.FirstFooter,
                     })
            {
                if (headerFooter is null)
                    continue;

                foreach (var paragraph in headerFooter.Paragraphs)
                    yield return paragraph;
            }
        }

        foreach (var footnote in document.Footnotes.Values)
            foreach (var paragraph in footnote.Content)
                yield return paragraph;

        foreach (var endnote in document.Endnotes.Values)
            foreach (var paragraph in endnote.Content)
                yield return paragraph;
    }
}
