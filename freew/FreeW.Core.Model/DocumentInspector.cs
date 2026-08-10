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

        var revisions = EnumerateParagraphs(document)
            .Sum(p => p.Runs.Count(r => r.Revision != RevisionKind.None));

        var properties = CountNonEmptyProperties(document.Properties);

        var bookmarks = EnumerateParagraphs(document)
            .Sum(p => p.BookmarkNames.Count(n => !string.IsNullOrEmpty(n)));

        return new InspectionResult(comments, revisions, properties, bookmarks);
    }

    /// <summary>
    /// Remove every review comment: clears the document's <see cref="TextDocument.Comments"/> store and
    /// strips the comment marks from every body run — both the <see cref="Run.CommentId"/> on covered
    /// runs and the textless comment-reference anchor runs (see <see cref="Run.IsCommentReference"/>),
    /// which are removed entirely. Mutates <paramref name="document"/> in place.
    /// </summary>
    public static void RemoveComments(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Comments.Clear();

        foreach (var paragraph in EnumerateParagraphs(document))
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

    // Every paragraph reachable in the document body — top-level paragraphs and those nested in table
    // cells, including tables nested inside table cells to any depth (the same walk TrackChanges uses),
    // so inspection/removal cover all body runs and bookmarks.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document) =>
        document.Blocks.SelectMany(ParagraphsInBlock);

    private static IEnumerable<Paragraph> ParagraphsInBlock(Block block)
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
                    foreach (var nestedParagraph in ParagraphsInBlock(nestedTable))
                        yield return nestedParagraph;
            }
        }
    }
}
