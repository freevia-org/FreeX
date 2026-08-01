namespace FreeW.Core.Model;

/// <summary>Anchors a new review comment over a character range of a body paragraph.</summary>
public sealed class AddCommentCommand(
    int blockIndex,
    int startOffset,
    int endOffset,
    int commentId,
    Comment comment) : IDocumentCommand
{
    private List<Run>? _savedRuns;
    private List<BookmarkBoundary>? _savedBookmarkBoundaries;
    private bool _applied;

    public string Label => "Insert Comment";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Comment;

    public int CommentId => commentId;

    public void Apply(IDocumentCommandContext context)
    {
        if (context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
            return;

        _savedRuns = paragraph.Runs.Select(CloneRunMarks).ToList();
        _savedBookmarkBoundaries = [.. paragraph.BookmarkBoundaries];
        var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);

        if (!MarkCommentRange(paragraph, startOffset, endOffset, commentId))
        {
            _savedRuns = null;
            return;
        }
        BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);

        context.Document.Comments[commentId] = comment;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        context.Document.Comments.Remove(commentId);
        if (_savedRuns is not null && context.Document.Blocks.ElementAtOrDefault(blockIndex) is Paragraph paragraph)
        {
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(_savedRuns);
            paragraph.BookmarkBoundaries.Clear();
            if (_savedBookmarkBoundaries is not null)
                paragraph.BookmarkBoundaries.AddRange(_savedBookmarkBoundaries);
        }

        _applied = false;
    }

    public static bool HasCommentableRange(Paragraph paragraph, int startOffset, int endOffset)
    {
        var pos = 0;
        foreach (var run in paragraph.Runs)
        {
            var len = run.Text.Length;
            var runStart = pos;
            var runEnd = pos + len;
            pos = runEnd;
            if (len == 0)
                continue;

            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart < coverEnd)
                return true;
        }

        return false;
    }

    public static bool MarkCommentRange(Paragraph paragraph, int startOffset, int endOffset, int commentId)
    {
        var pos = 0;
        var lastCoveredIndex = -1;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var len = run.Text.Length;
            var runStart = pos;
            var runEnd = pos + len;
            pos = runEnd;
            if (len == 0)
                continue;

            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
                continue;

            if (coverStart > runStart)
            {
                var head = CloneRunMarks(run, run.Text[..(coverStart - runStart)]);
                run.Text = run.Text[(coverStart - runStart)..];
                paragraph.Runs.Insert(i, head);
                i++;
            }

            if (coverEnd < runEnd)
            {
                var tail = CloneRunMarks(run, run.Text[(coverEnd - coverStart)..]);
                run.Text = run.Text[..(coverEnd - coverStart)];
                paragraph.Runs.Insert(i + 1, tail);
            }

            run.CommentId = commentId;
            lastCoveredIndex = i;
        }

        if (lastCoveredIndex < 0)
            return false;

        paragraph.Runs.Insert(lastCoveredIndex + 1, Run.CommentReference(commentId));
        return true;
    }

    private static Run CloneRunMarks(Run source, string text) => new(text, source.Formatting)
    {
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        SubDocument = source.SubDocument,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
    };

    private static Run CloneRunMarks(Run source) => CloneRunMarks(source, source.Text);
}

/// <summary>Deletes a top-level comment thread and restores its anchors on undo.</summary>
public sealed class DeleteCommentCommand(int commentId) : IDocumentCommand
{
    private readonly Dictionary<int, List<Run>> _savedRuns = new();
    private readonly Dictionary<int, List<BookmarkBoundary>> _savedBookmarkBoundaries = new();
    private Comment? _savedComment;
    private int _savedOrdinal = -1;

    public string Label => "Delete Comment";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Comment;

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (!doc.Comments.TryGetValue(commentId, out var comment))
            return;

        _savedComment = comment;
        _savedOrdinal = doc.Comments.Keys.ToList().IndexOf(commentId);

        for (var bi = 0; bi < doc.Blocks.Count; bi++)
        {
            foreach (var paragraph in ParagraphsInBlock(doc.Blocks[bi]))
            {
                if (paragraph.Runs.Any(r => r.CommentId is { } cid && ResolveTopLevel(doc, cid) == commentId))
                {
                    var key = BlockParagraphKey(doc, bi, paragraph);
                    _savedRuns[key] = paragraph.Runs.Select(CloneRun).ToList();
                    _savedBookmarkBoundaries[key] = [.. paragraph.BookmarkBoundaries];
                }
            }
        }

        foreach (var paragraph in doc.Blocks.SelectMany(ParagraphsInBlock))
        {
            var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);
            for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
            {
                var run = paragraph.Runs[i];
                if (run.CommentId is not { } cid || ResolveTopLevel(doc, cid) != commentId)
                    continue;

                if (run.IsCommentReference)
                    paragraph.Runs.RemoveAt(i);
                else
                    run.CommentId = null;
            }
            BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);
        }

        doc.Comments.Remove(commentId);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_savedComment is null)
            return;

        var doc = context.Document;
        var entries = doc.Comments.ToList();
        doc.Comments.Clear();
        var inserted = false;
        for (var i = 0; i < entries.Count; i++)
        {
            if (i == _savedOrdinal)
            {
                doc.Comments[commentId] = _savedComment;
                inserted = true;
            }

            doc.Comments[entries[i].Key] = entries[i].Value;
        }

        if (!inserted)
            doc.Comments[commentId] = _savedComment;

        foreach (var (key, runs) in _savedRuns)
        {
            if (ParagraphForKey(doc, key) is not { } paragraph)
                continue;

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(runs);
            paragraph.BookmarkBoundaries.Clear();
            if (_savedBookmarkBoundaries.TryGetValue(key, out var boundaries))
                paragraph.BookmarkBoundaries.AddRange(boundaries);
        }
    }

    private static int BlockParagraphKey(TextDocument doc, int blockIndex, Paragraph target)
    {
        var ordinal = 0;
        foreach (var paragraph in doc.Blocks.SelectMany(ParagraphsInBlock))
        {
            if (ReferenceEquals(paragraph, target))
                return ordinal;

            ordinal++;
        }

        return -1;
    }

    private static Paragraph? ParagraphForKey(TextDocument doc, int key) =>
        doc.Blocks.SelectMany(ParagraphsInBlock).ElementAtOrDefault(key);

    private static Run CloneRun(Run source) => new(source.Text, source.Formatting)
    {
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        SubDocument = source.SubDocument,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
    };

    public static IEnumerable<Paragraph> ParagraphsInBlock(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                yield return paragraph;
                break;
            case Table table:
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
                break;
        }
    }

    public static int ResolveTopLevel(TextDocument doc, int commentId)
    {
        if (doc.Comments.ContainsKey(commentId))
            return commentId;

        foreach (var top in doc.Comments.Values)
            if (top.Replies.Any(r => r.Id == commentId))
                return top.Id;

        return commentId;
    }
}

/// <summary>Appends a reply to an existing top-level comment thread.</summary>
public sealed class AddCommentReplyCommand(int topLevelCommentId, Comment reply) : IDocumentCommand
{
    private int _insertedIndex = -1;
    private bool _applied;

    public string Label => "Reply to Comment";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Comment;

    public void Apply(IDocumentCommandContext context)
    {
        if (!context.Document.Comments.TryGetValue(topLevelCommentId, out var comment))
            return;

        if (comment.ThreadInOrder().Any(existing => existing.Id == reply.Id))
            return;

        _insertedIndex = comment.Replies.Count;
        comment.Replies.Add(reply);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        if (context.Document.Comments.TryGetValue(topLevelCommentId, out var comment))
        {
            var index = comment.Replies.FindIndex(candidate => candidate.Id == reply.Id);
            if (index >= 0)
                comment.Replies.RemoveAt(index);
            else if (_insertedIndex >= 0
                && _insertedIndex < comment.Replies.Count
                && ReferenceEquals(comment.Replies[_insertedIndex], reply))
            {
                comment.Replies.RemoveAt(_insertedIndex);
            }
        }

        _applied = false;
    }
}

/// <summary>Sets the resolved/done flag on a top-level comment thread.</summary>
public sealed class SetCommentResolvedCommand(int commentId, bool resolved) : IDocumentCommand
{
    private bool _previous;
    private bool _applied;

    public string Label => resolved ? "Resolve Comment" : "Reopen Comment";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Comment;

    public void Apply(IDocumentCommandContext context)
    {
        if (!context.Document.Comments.TryGetValue(commentId, out var comment))
            return;

        _previous = comment.Resolved;
        comment.Resolved = resolved;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        if (context.Document.Comments.TryGetValue(commentId, out var comment))
            comment.Resolved = _previous;

        _applied = false;
    }
}
