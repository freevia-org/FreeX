namespace FreeW.Core.Model;

/// <summary>
/// Pure, unit-testable operations over tracked changes (revisions) carried on <see cref="Run.Revision"/>.
/// Accept turns insertions into ordinary text and drops deletions; Reject does the inverse (drops
/// insertions, restores deletions to ordinary text). Both walk every paragraph in the document body
/// (including paragraphs nested in table cells) and clear the revision marks they resolve. The document
/// is mutated in place; nothing here touches the editor or docx layers.
/// </summary>
public static class TrackChanges
{
    /// <summary>True when any run anywhere in the document body carries a tracked-change mark.</summary>
    public static bool HasRevisions(TextDocument document) =>
        EnumerateParagraphs(document).Any(p => p.Runs.Any(r => r.Revision != RevisionKind.None));

    /// <summary>
    /// Accept every tracked change: inserted runs become ordinary text (their revision mark cleared) and
    /// deleted runs are removed entirely. The document is mutated in place.
    /// </summary>
    public static void AcceptAll(TextDocument document)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
            Resolve(paragraph, accept: true);
    }

    /// <summary>
    /// Reject every tracked change: inserted runs are removed entirely and deleted runs are restored to
    /// ordinary text (their revision mark cleared). The document is mutated in place.
    /// </summary>
    public static void RejectAll(TextDocument document)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
            Resolve(paragraph, accept: false);
    }

    // Resolve all revision marks in one paragraph. On accept, deletions are dropped and insertions kept;
    // on reject, insertions are dropped and deletions kept. Kept runs have their revision metadata cleared.
    private static void Resolve(Paragraph paragraph, bool accept)
    {
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
        {
            var run = paragraph.Runs[i];
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

    // Every paragraph reachable in the document body — top-level paragraphs and those nested in table
    // cells (the same walk DocxWriter uses), so accept/reject cover all body runs that can carry marks.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is Table table)
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
        }
    }
}
