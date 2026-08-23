using FreeW.Core.Model;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CommentAnchorPosition(
    int BlockIndex,
    int Offset,
    int? TableRowIndex = null,
    int? TableGridColumnIndex = null,
    int? TableParagraphIndex = null,
    bool IsHeaderFooterOrNoteAnchor = false)
{
    public bool IsTableAnchor =>
        TableRowIndex is not null &&
        TableGridColumnIndex is not null &&
        TableParagraphIndex is not null;
}

public sealed record CommentListItem(
    int Id,
    CommentAnchorPosition Anchor,
    string Author,
    string Text,
    int ReplyCount,
    bool Resolved,
    string? DateXml = null)
{
    public int BlockIndex => Anchor.BlockIndex;
}

public static class CommentListPlanner
{
    public static IReadOnlyList<CommentListItem> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var items = new List<CommentListItem>();
        var seen = new HashSet<int>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            foreach (var paragraph in ParagraphsInBlock(document.Blocks[blockIndex]))
            {
                var capturedBlockIndex = blockIndex;
                AddCommentsInParagraph(
                    document,
                    paragraph.Paragraph,
                    offset => new CommentAnchorPosition(
                        capturedBlockIndex,
                        offset,
                        paragraph.TableRowIndex,
                        paragraph.TableGridColumnIndex,
                        paragraph.TableParagraphIndex),
                    items,
                    seen);
            }
        }

        // Comments legitimately anchor outside the body too: Word allows one in a header, footer,
        // footnote, or endnote (mirrors CommentCommands.EnumerateCommentAnchorParagraphs, which walks
        // this identical set for the same reason). None of these paragraphs has a body block index, so
        // they are appended after the body with a synthetic index just past its range -- harmless for
        // the balloon strip's existing ordinal-position approximation in ReviewBalloonLayoutPlanner --
        // and flagged via IsHeaderFooterOrNoteAnchor so SelectAdjacent below can keep Next/Previous
        // Comment cycling through only the body/table anchors the shells already know how to place a
        // caret in, exactly as it did before this method saw these comments at all.
        var outOfBodyBlockIndex = document.Blocks.Count;
        foreach (var paragraph in OutOfBodyParagraphs(document))
        {
            var capturedBlockIndex = outOfBodyBlockIndex;
            AddCommentsInParagraph(
                document,
                paragraph,
                offset => new CommentAnchorPosition(capturedBlockIndex, offset, IsHeaderFooterOrNoteAnchor: true),
                items,
                seen);
            outOfBodyBlockIndex++;
        }

        return items;
    }

    private static void AddCommentsInParagraph(
        TextDocument document,
        Paragraph paragraph,
        Func<int, CommentAnchorPosition> anchorAt,
        List<CommentListItem> items,
        HashSet<int> seen)
    {
        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            if (run.CommentId is not { } commentId)
            {
                offset += run.Text.Length;
                continue;
            }

            var topLevelId = TopLevelCommentId(document, commentId);
            if (!seen.Add(topLevelId) || !document.Comments.TryGetValue(topLevelId, out var comment))
            {
                offset += run.Text.Length;
                continue;
            }

            items.Add(new CommentListItem(
                topLevelId,
                anchorAt(offset),
                string.IsNullOrWhiteSpace(comment.Author) ? "Unknown" : comment.Author,
                comment.PlainText,
                comment.Replies.Count,
                comment.Resolved,
                comment.DateXml));

            offset += run.Text.Length;
        }
    }

    /// <summary>
    /// Every header/footer of every document section (default, even, and first-page slots), plus every
    /// footnote's and endnote's own content paragraphs, in that order.
    /// </summary>
    private static IEnumerable<Paragraph> OutOfBodyParagraphs(TextDocument document) =>
        TextDocumentStoryTraversal.EnumerateParagraphs(
            document,
            TextDocumentStorySubset.HeadersFooters
            | TextDocumentStorySubset.Footnotes
            | TextDocumentStorySubset.Endnotes,
            TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs);

    public static CommentListItem? SelectAdjacent(
        IReadOnlyList<CommentListItem> items,
        int? currentTopLevelCommentId,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Next/Previous Comment moves the on-screen caret, and the shells only know how to place a
        // caret in a body or table position -- a header/footer/footnote/endnote anchor has no caret
        // destination yet, so it is excluded from the cycle here (rather than in Build, which still
        // reports it for the Comments list and the markup-balloon strip). This keeps navigation
        // behaviour over body/table comments identical to before Build started reporting these too.
        var navigable = items.Where(item => !item.Anchor.IsHeaderFooterOrNoteAnchor).ToList();
        if (navigable.Count == 0)
            return null;

        var step = direction < 0 ? -1 : 1;
        var currentIndex = currentTopLevelCommentId is { } id
            ? IndexOf(navigable, id)
            : -1;
        var nextIndex = currentIndex < 0
            ? (step > 0 ? 0 : navigable.Count - 1)
            : (currentIndex + step + navigable.Count) % navigable.Count;

        return navigable[nextIndex];
    }

    private static int IndexOf(IReadOnlyList<CommentListItem> items, int commentId)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == commentId)
                return i;
        }

        return -1;
    }

    private static int TopLevelCommentId(TextDocument document, int commentId)
    {
        if (document.Comments.ContainsKey(commentId))
            return commentId;

        foreach (var comment in document.Comments.Values)
        {
            if (comment.Replies.Any(reply => reply.Id == commentId))
                return comment.Id;
        }

        return commentId;
    }

    private static IEnumerable<ParagraphAddress> ParagraphsInBlock(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return new ParagraphAddress(paragraph, null, null, null);
            yield break;
        }

        if (block is Table table)
        {
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                foreach (var projected in TableGridProjection.ProjectRow(row))
                {
                    for (var paragraphIndex = 0; paragraphIndex < projected.Cell.Paragraphs.Count; paragraphIndex++)
                    {
                        yield return new ParagraphAddress(
                            projected.Cell.Paragraphs[paragraphIndex],
                            rowIndex,
                            projected.StartColumn,
                            paragraphIndex);
                    }
                }
            }
        }
    }

    private sealed record ParagraphAddress(
        Paragraph Paragraph,
        int? TableRowIndex,
        int? TableGridColumnIndex,
        int? TableParagraphIndex)
    {
        public List<Run> Runs => Paragraph.Runs;
    }
}
