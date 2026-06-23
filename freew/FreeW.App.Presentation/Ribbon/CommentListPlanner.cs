using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CommentListItem(
    int Id,
    int BlockIndex,
    string Author,
    string Text,
    int ReplyCount,
    bool Resolved);

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
                foreach (var run in paragraph.Runs)
                {
                    if (run.CommentId is not { } commentId)
                        continue;

                    var topLevelId = TopLevelCommentId(document, commentId);
                    if (!seen.Add(topLevelId) || !document.Comments.TryGetValue(topLevelId, out var comment))
                        continue;

                    items.Add(new CommentListItem(
                        topLevelId,
                        blockIndex,
                        string.IsNullOrWhiteSpace(comment.Author) ? "Unknown" : comment.Author,
                        comment.PlainText,
                        comment.Replies.Count,
                        comment.Resolved));
                }
            }
        }

        return items;
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

    private static IEnumerable<Paragraph> ParagraphsInBlock(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is Table table)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var cellParagraph in cell.Paragraphs)
                        yield return cellParagraph;
                }
            }
        }
    }
}
