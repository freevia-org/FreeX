using System.Collections.Generic;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Anchors a new review comment over a character range of a body paragraph: splits the covered runs,
/// stamps their <see cref="Run.CommentId"/>, appends a textless comment-reference run after the range,
/// and stores the <see cref="Comment"/> (author/initials/text/date) in <see cref="TextDocument.Comments"/>.
/// Both the run mutation and the dictionary insert happen in <see cref="Apply"/> and are unwound together
/// in <see cref="Revert"/>, so a single Undo removes the whole comment atomically.
///
/// Implemented in the app (not shared FreeW.Core) against the public <see cref="IDocumentCommand"/>
/// interface so it rides the same undo/redo bus as the rest of the Avalonia editor. Mirrors the WPF host's
/// MarkCommentRange + InsertComment behaviour, reusing the shared model (<see cref="Run.CommentReference"/>,
/// <see cref="Comment"/>) verbatim.
/// </summary>
internal sealed class AddCommentCommand(
    int blockIndex,
    int startOffset,
    int endOffset,
    int commentId,
    Comment comment) : IDocumentCommand
{
    private List<Run>? _savedRuns;
    private bool _applied;

    public string Label => "Insert Comment";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Comment;

    /// <summary>The id allocated for this comment (echoed so callers can navigate to it).</summary>
    public int CommentId => commentId;

    public void Apply(IDocumentCommandContext context)
    {
        if (context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
            return;

        // Snapshot deep clones: MarkCommentRange mutates the run objects in place (splits text, stamps
        // CommentId), so a shallow [.. Runs] copy would share — and thus also see — those mutations,
        // breaking Revert. Cloning the marks this command touches keeps the undo faithful.
        _savedRuns = paragraph.Runs.Select(CloneRunMarks).ToList();

        if (!MarkCommentRange(paragraph, startOffset, endOffset, commentId))
        {
            // Nothing textual to anchor to: roll the snapshot back so Revert is a no-op.
            _savedRuns = null;
            return;
        }

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
        }

        _applied = false;
    }

    /// <summary>
    /// Stamps <paramref name="commentId"/> onto every run (slice) covering [<paramref name="startOffset"/>,
    /// <paramref name="endOffset"/>) of the paragraph's plain text, splitting partially-covered runs so the
    /// mark is exact, then inserts a textless <see cref="Run.CommentReference"/> after the last covered run.
    /// Returns false when the range covers no text. Mirrors the WPF host's private MarkCommentRange.
    /// </summary>
    internal static bool MarkCommentRange(Paragraph paragraph, int startOffset, int endOffset, int commentId)
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

            var coverStart = System.Math.Max(runStart, startOffset);
            var coverEnd = System.Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
                continue;

            // Split off the leading uncovered part, if any.
            if (coverStart > runStart)
            {
                var head = CloneRunMarks(run, run.Text[..(coverStart - runStart)]);
                run.Text = run.Text[(coverStart - runStart)..];
                paragraph.Runs.Insert(i, head);
                i++;
            }
            // Split off the trailing uncovered part, if any.
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

    /// <summary>
    /// Clones the formatting + hyperlink marks of <paramref name="source"/> onto a new run carrying
    /// <paramref name="text"/>. Used when a run is split so the uncovered slice keeps its formatting/links
    /// (the covered slice keeps the original run, which gets the new comment id stamped).
    /// </summary>
    private static Run CloneRunMarks(Run source, string text) => new(text, source.Formatting)
    {
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
    };

    /// <summary>Deep-clones a run carrying its full text plus the marks this command can touch.</summary>
    private static Run CloneRunMarks(Run source) => CloneRunMarks(source, source.Text);
}

/// <summary>
/// Removes the comment thread with a given id: deletes its <see cref="Comment"/> entry (and therefore its
/// replies) from <see cref="TextDocument.Comments"/>, clears the <see cref="Run.CommentId"/> mark from every
/// covered run, and drops the textless reference run(s). Captured before-state is restored verbatim on
/// <see cref="Revert"/>, so a single Undo brings the whole thread (and its anchored marks) back.
/// </summary>
internal sealed class DeleteCommentCommand(int commentId) : IDocumentCommand
{
    private readonly Dictionary<int, List<Run>> _savedRuns = new();
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
                // Deep-clone the snapshot: Apply nulls CommentId on the live run objects, so a shallow
                // copy would share that mutation and Revert could not restore the anchor.
                if (paragraph.Runs.Any(r => r.CommentId is { } cid && ResolveTopLevel(doc, cid) == commentId))
                    _savedRuns[BlockParagraphKey(doc, bi, paragraph)] = paragraph.Runs.Select(CloneRun).ToList();
            }
        }

        // Re-walk to mutate (kept separate from the snapshot walk for clarity).
        foreach (var paragraph in doc.Blocks.SelectMany(ParagraphsInBlock))
        {
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
        }

        doc.Comments.Remove(commentId);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_savedComment is null)
            return;

        var doc = context.Document;

        // Restore the comment entry, preserving its original key ordering where possible.
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

        // Restore the snapshotted paragraph runs.
        foreach (var (key, runs) in _savedRuns)
        {
            if (ParagraphForKey(doc, key) is not { } paragraph)
                continue;
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(runs);
        }
    }

    // ── Paragraph addressing ──────────────────────────────────────────────────
    // Body paragraphs and table-cell paragraphs are addressed by a flat ordinal computed by walking the
    // document in the same order on Apply and Revert, so snapshots line up even across table cells.

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

    /// <summary>Deep-clones a run carrying the text + marks this command mutates (CommentId / reference).</summary>
    private static Run CloneRun(Run source) => new(source.Text, source.Formatting)
    {
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
    };

    internal static IEnumerable<Paragraph> ParagraphsInBlock(Block block)
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

    internal static int ResolveTopLevel(TextDocument doc, int commentId)
    {
        if (doc.Comments.ContainsKey(commentId))
            return commentId;
        foreach (var top in doc.Comments.Values)
            if (top.Replies.Any(r => r.Id == commentId))
                return top.Id;
        return commentId;
    }
}

/// <summary>
/// Appends a reply to an existing top-level comment. Captures the inserted reply id and list ordinal so
/// Undo removes exactly this reply while preserving any other thread edits.
/// </summary>
internal sealed class AddCommentReplyCommand(int topLevelCommentId, Comment reply) : IDocumentCommand
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
            else if (_insertedIndex >= 0 && _insertedIndex < comment.Replies.Count && ReferenceEquals(comment.Replies[_insertedIndex], reply))
                comment.Replies.RemoveAt(_insertedIndex);
        }

        _applied = false;
    }
}

/// <summary>
/// Toggles (sets) the resolved/done flag on the comment thread with a given id. Captures the previous flag
/// so Undo restores it. The flag lives on the model <see cref="Comment.Resolved"/> property (Word's
/// w15:done) and already round-trips through Core.IO.
/// </summary>
internal sealed class SetCommentResolvedCommand(int commentId, bool resolved) : IDocumentCommand
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
