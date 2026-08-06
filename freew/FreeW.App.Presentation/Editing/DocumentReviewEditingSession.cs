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
    public bool TryApply(TextDocument document, RevisionResolutionAction action)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = RevisionList.Enumerate(document);
        if (RevisionIndex < 0 || RevisionIndex >= entries.Count)
            return false;

        var current = entries[RevisionIndex];
        if (!ReferenceEquals(current.Paragraph, Entry.Paragraph)
            || !ReferenceEquals(current.Run, Entry.Run)
            || current.Kind != Entry.Kind)
        {
            return false;
        }

        return action == RevisionResolutionAction.Accept
            ? RevisionList.Accept(document, current)
            : RevisionList.Reject(document, current);
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
