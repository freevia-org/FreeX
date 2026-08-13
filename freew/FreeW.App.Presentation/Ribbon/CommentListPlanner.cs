using FreeW.Core.Model;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CommentAnchorPosition(
    int BlockIndex,
    int Offset,
    int? TableRowIndex = null,
    int? TableGridColumnIndex = null,
    int? TableParagraphIndex = null)
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
                        new CommentAnchorPosition(
                            blockIndex,
                            offset,
                            paragraph.TableRowIndex,
                            paragraph.TableGridColumnIndex,
                            paragraph.TableParagraphIndex),
                        string.IsNullOrWhiteSpace(comment.Author) ? "Unknown" : comment.Author,
                        comment.PlainText,
                        comment.Replies.Count,
                        comment.Resolved,
                        comment.DateXml));

                    offset += run.Text.Length;
                }
            }
        }

        return items;
    }

    public static CommentListItem? SelectAdjacent(
        IReadOnlyList<CommentListItem> items,
        int? currentTopLevelCommentId,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return null;

        var step = direction < 0 ? -1 : 1;
        var currentIndex = currentTopLevelCommentId is { } id
            ? IndexOf(items, id)
            : -1;
        var nextIndex = currentIndex < 0
            ? (step > 0 ? 0 : items.Count - 1)
            : (currentIndex + step + items.Count) % items.Count;

        return items[nextIndex];
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
