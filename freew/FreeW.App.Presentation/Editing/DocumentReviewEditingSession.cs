using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public enum CommentTextNormalization
{
    Preserve,
    Trim,
}

public enum RevisionResolutionAction
{
    Accept,
    Reject,
}

public readonly record struct InspectorRemovalDecision(
    bool Comments,
    bool Revisions,
    bool Properties,
    bool Bookmarks)
{
    public bool Any => Comments || Revisions || Properties || Bookmarks;

    public void Apply(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Comments)
            DocumentInspector.RemoveComments(document);
        if (Revisions)
            DocumentInspector.RemoveRevisions(document);
        if (Properties)
            DocumentInspector.RemoveProperties(document);
        if (Bookmarks)
            DocumentInspector.RemoveBookmarks(document);
    }
}

public sealed record RevisionTargetDecision(
    int RevisionIndex,
    int TopLevelBlockIndex,
    RevisionEntry Entry)
{
    public bool IsCurrent(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ResolveCurrent(document) is not null;
    }

    public bool TryApply(TextDocument document, RevisionResolutionAction action)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (ResolveCurrent(document) is not { } current)
            return false;

        return action == RevisionResolutionAction.Accept
            ? RevisionList.Accept(document, current)
            : RevisionList.Reject(document, current);
    }

    private RevisionEntry? ResolveCurrent(TextDocument document)
    {
        var entries = RevisionList.Enumerate(document);
        if (RevisionIndex < 0 || RevisionIndex >= entries.Count)
            return null;

        var current = entries[RevisionIndex];
        return ReferenceEquals(current.Paragraph, Entry.Paragraph)
               && ReferenceEquals(current.Run, Entry.Run)
               && current.Kind == Entry.Kind
            ? current
            : null;
    }
}

/// <summary>
/// Owns portable review-comment decisions and mutations for the active editing session. Renderers retain
/// native selection translation, focus, caret placement, layout, and redraw.
/// </summary>
public sealed class DocumentReviewEditingSession
{
    private readonly DocumentEditingSession _editingSession;
    private readonly Func<string?> _currentDateXml;

    internal DocumentReviewEditingSession(
        DocumentEditingSession editingSession,
        Func<string?> currentDateXml)
    {
        _editingSession = editingSession;
        _currentDateXml = currentDateXml;
    }

    public RestrictEditingEnforcementPolicy RestrictEditingPolicy =>
        RestrictEditingEnforcementPolicy.From(
            _editingSession.Document.Protection,
            _editingSession.Document.MarkedAsFinal);

    public RestrictEditingEnforcementDecision DecisionFor(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.DecisionFor(operation);

    public RestrictEditingEnforcementDecision DecisionForHistory(
        RestrictEditingOperationKind operation,
        DocumentCommandMutationKind? mutationKind) =>
        RestrictEditingPolicy.DecisionForHistory(operation, mutationKind);

    public bool Allows(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.Allows(operation);

    public bool AllowsHistory(
        RestrictEditingOperationKind operation,
        DocumentCommandMutationKind? mutationKind) =>
        RestrictEditingPolicy.AllowsHistory(operation, mutationKind);

    public int? TryAddComment(
        int blockIndex,
        int startOffset,
        int endOffset,
        string text,
        string author,
        string initials)
    {
        if (!Allows(RestrictEditingOperationKind.CommentInsert)
            || string.IsNullOrWhiteSpace(text)
            || _editingSession.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph
            || !AddCommentCommand.HasCommentableRange(paragraph, startOffset, endOffset))
        {
            return null;
        }

        var commentId = _editingSession.Document.NextCommentId();
        var comment = new Comment(commentId, text, author, initials)
        {
            DateXml = _currentDateXml(),
        };

        _editingSession.Commands.Execute(
            new AddCommentCommand(blockIndex, startOffset, endOffset, commentId, comment));
        return _editingSession.Document.Comments.ContainsKey(commentId) ? commentId : null;
    }

    public bool TryReplyToComment(
        int commentId,
        string text,
        string author,
        string initials,
        CommentTextNormalization textNormalization = CommentTextNormalization.Preserve)
    {
        if (!Allows(RestrictEditingOperationKind.CommentReply)
            || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var topLevelId = ResolveTopLevelCommentId(commentId);
        if (!_editingSession.Document.Comments.TryGetValue(topLevelId, out var comment))
            return false;

        var replyId = _editingSession.Document.NextCommentId();
        var replyText = textNormalization == CommentTextNormalization.Trim ? text.Trim() : text;
        var reply = new Comment(replyId, replyText, author, initials)
        {
            DateXml = _currentDateXml(),
        };

        _editingSession.Commands.Execute(new AddCommentReplyCommand(topLevelId, reply));
        return comment.Replies.Any(candidate => candidate.Id == replyId);
    }

    public bool TrySetCommentResolved(int commentId, bool resolved)
    {
        if (!Allows(RestrictEditingOperationKind.CommentResolve))
            return false;

        var topLevelId = ResolveTopLevelCommentId(commentId);
        if (!_editingSession.Document.Comments.ContainsKey(topLevelId))
            return false;

        _editingSession.Commands.Execute(new SetCommentResolvedCommand(topLevelId, resolved));
        return true;
    }

    public bool? TryToggleCommentResolved(int commentId)
    {
        if (!Allows(RestrictEditingOperationKind.CommentResolve))
            return null;

        var topLevelId = ResolveTopLevelCommentId(commentId);
        if (!_editingSession.Document.Comments.TryGetValue(topLevelId, out var comment))
            return null;

        var resolved = !comment.Resolved;
        return TrySetCommentResolved(topLevelId, resolved) ? resolved : null;
    }

    public bool TryDeleteComment(int commentId)
    {
        if (!Allows(RestrictEditingOperationKind.CommentDelete))
            return false;

        var topLevelId = ResolveTopLevelCommentId(commentId);
        if (!_editingSession.Document.Comments.ContainsKey(topLevelId))
            return false;

        _editingSession.Commands.Execute(new DeleteCommentCommand(topLevelId));
        return !_editingSession.Document.Comments.ContainsKey(topLevelId);
    }

    public int ResolveTopLevelCommentId(int commentId) =>
        DeleteCommentCommand.ResolveTopLevel(_editingSession.Document, commentId);

    public IReadOnlyList<Comment> AllComments() =>
        _editingSession.Document.Comments.Values.OrderBy(comment => comment.Id).ToList();

    public IReadOnlyList<CommentListItem> BuildCommentList() =>
        CommentListPlanner.Build(_editingSession.Document);

    public CommentListItem? SelectAdjacentComment(int? currentCommentId, int direction) =>
        CommentListPlanner.SelectAdjacent(
            BuildCommentList(),
            currentCommentId is { } id ? ResolveTopLevelCommentId(id) : null,
            direction);

    public InspectorRemovalDecision PlanInspectorRemovals(InspectorRemovalChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return new InspectorRemovalDecision(
            choice.Comments,
            choice.Revisions,
            choice.Properties,
            choice.Bookmarks);
    }

    public IReadOnlyList<RevisionEntry> ListRevisions() =>
        RevisionList.Enumerate(_editingSession.Document);

    public bool TryMarkRevisionRange(
        int blockIndex,
        int startOffset,
        int endOffset,
        RevisionKind kind,
        string author,
        string? dateXml,
        bool recordUndo = true) =>
        TryMarkRevisionRange(
            new DocumentTextRange(
                new DocumentTextPosition(blockIndex, startOffset),
                new DocumentTextPosition(blockIndex, endOffset)),
            kind,
            author,
            dateXml,
            recordUndo);

    public bool TryMarkRevisionRange(
        DocumentTextRange range,
        RevisionKind kind,
        string author,
        string? dateXml,
        bool recordUndo = true)
    {
        var normalized = range.Normalize();
        if (kind == RevisionKind.None
            || normalized.Start.BlockIndex < 0
            || normalized.End.BlockIndex >= _editingSession.Document.Blocks.Count
            || normalized.Start.BlockIndex > normalized.End.BlockIndex)
        {
            return false;
        }

        var ranges = new List<(int BlockIndex, Paragraph Paragraph, int StartOffset, int EndOffset)>();
        for (var blockIndex = normalized.Start.BlockIndex; blockIndex <= normalized.End.BlockIndex; blockIndex++)
        {
            if (_editingSession.Document.Blocks[blockIndex] is not Paragraph paragraph)
                return false;

            var startOffset = blockIndex == normalized.Start.BlockIndex
                ? Math.Clamp(normalized.Start.Offset, 0, paragraph.PlainText.Length)
                : 0;
            var endOffset = blockIndex == normalized.End.BlockIndex
                ? Math.Clamp(normalized.End.Offset, 0, paragraph.PlainText.Length)
                : paragraph.PlainText.Length;
            ranges.Add((blockIndex, paragraph, startOffset, endOffset));
        }

        var coversBoundary = normalized.Start.BlockIndex < normalized.End.BlockIndex;
        if (!coversBoundary && ranges[0].StartOffset == ranges[0].EndOffset)
            return false;

        if (recordUndo)
        {
            var commands = new List<IDocumentCommand>();
            foreach (var target in ranges)
            {
                if (target.StartOffset != target.EndOffset)
                {
                    commands.Add(new MarkRevisionRangeCommand(
                        target.BlockIndex,
                        target.StartOffset,
                        target.EndOffset,
                        kind,
                        author,
                        dateXml));
                }

                if (target.BlockIndex < normalized.End.BlockIndex)
                {
                    commands.Add(new SetParagraphMarkRevisionCommand(
                        target.BlockIndex,
                        kind,
                        author,
                        dateXml));
                }
            }

            _editingSession.ExecuteCommands(
                commands,
                kind == RevisionKind.Deleted ? "Mark Deletion" : "Mark Insertion");
        }
        else
        {
            var changed = false;
            foreach (var target in ranges)
            {
                if (target.StartOffset != target.EndOffset)
                {
                    changed |= RevisionEditPlanner.MarkRevisionRange(
                        target.Paragraph,
                        target.StartOffset,
                        target.EndOffset,
                        kind,
                        author,
                        dateXml);
                }

                if (target.BlockIndex < normalized.End.BlockIndex)
                {
                    target.Paragraph.MarkRevision = kind;
                    target.Paragraph.MarkRevisionAuthor = author;
                    target.Paragraph.MarkRevisionDateXml = dateXml;
                    changed = true;
                }
            }

            if (!changed)
                return false;
            _editingSession.NotifyChanged();
        }
        return true;
    }

    public bool TryResolveRevision(
        RevisionTargetDecision target,
        RevisionResolutionAction action)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsCurrent(_editingSession.Document))
            return false;

        _editingSession.Commands.Execute(new ResolveOneRevisionCommand(target, action));
        return true;
    }

    public bool TryResolveAllRevisions(RevisionResolutionAction action)
    {
        if (!TrackChanges.HasRevisions(_editingSession.Document))
            return false;

        _editingSession.Commands.Execute(action == RevisionResolutionAction.Accept
            ? new AcceptAllRevisionsCommand()
            : new RejectAllRevisionsCommand());
        return true;
    }

    public RevisionTargetDecision? ResolveRevisionTarget(RevisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entries = ListRevisions();
        for (var index = 0; index < entries.Count; index++)
        {
            var current = entries[index];
            if (ReferenceEquals(current.Paragraph, entry.Paragraph)
                && ReferenceEquals(current.Run, entry.Run)
                && current.Kind == entry.Kind)
            {
                return BuildRevisionTarget(index, current);
            }
        }

        return null;
    }

    public RevisionTargetDecision? ResolveRevisionTargetAtOrAfterTopLevelBlock(int topLevelBlockIndex)
    {
        var entries = ListRevisions();
        RevisionTargetDecision? first = null;
        for (var index = 0; index < entries.Count; index++)
        {
            var target = BuildRevisionTarget(index, entries[index]);
            if (target is null)
                continue;

            first ??= target;
            if (target.TopLevelBlockIndex >= topLevelBlockIndex)
                return target;
        }

        return first;
    }

    private RevisionTargetDecision? BuildRevisionTarget(int revisionIndex, RevisionEntry entry)
    {
        var topLevelBlockIndex = TopLevelBlockIndexOf(entry.Paragraph);
        return topLevelBlockIndex >= 0
            ? new RevisionTargetDecision(revisionIndex, topLevelBlockIndex, entry)
            : null;
    }

    private int TopLevelBlockIndexOf(Paragraph target)
    {
        var blocks = _editingSession.Document.Blocks;
        for (var index = 0; index < blocks.Count; index++)
        {
            if (ReferenceEquals(blocks[index], target))
                return index;

            if (blocks[index] is Table table
                && table.Rows.Any(row => row.Cells.Any(cell =>
                    cell.Paragraphs.Any(paragraph => ReferenceEquals(paragraph, target)))))
            {
                return index;
            }
        }

        return -1;
    }
}
