using Avalonia;
using Avalonia.Headless;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewProtectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    [Fact]
    public async Task MarkAsFinal_blocks_text_and_insert_mutations_until_cleared()
    {
        var textWhileFinal = "";
        var textAfterCleared = "";
        var blocksWhileFinal = 0;
        var protectionEvents = 0;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");
            var startingBlocks = view.Document.Blocks.Count;
            view.ProtectionStateChanged += (_, _) => protectionEvents++;

            view.SetMarkedAsFinal(true);
            view.InsertText("X");
            view.InsertTable(2, 2);
            view.InsertPageBreak();

            textWhileFinal = view.PlainText;
            blocksWhileFinal = view.Document.Blocks.Count - startingBlocks;

            view.SetMarkedAsFinal(false);
            view.InsertText("X");
            textAfterCleared = view.PlainText;
        });

        if (!ran)
            return;

        textWhileFinal.Should().Be("Hello");
        blocksWhileFinal.Should().Be(0);
        textAfterCleared.Should().Be("XHello");
        protectionEvents.Should().Be(2);
    }

    [Fact]
    public async Task ReadOnlyProtection_blocks_text_but_trackChangesOnly_records_tracked_edits()
    {
        var textWhileReadOnly = "";
        var textAfterTrackChangesOnly = "";
        var trackChangesEnabled = false;
        var hasRevisions = false;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");

            view.SetProtection(ProtectionMode.ReadOnly);
            view.InsertText("X");
            textWhileReadOnly = view.PlainText;

            view.SetProtection(ProtectionMode.None);
            view.SetProtection(ProtectionMode.TrackChangesOnly);
            trackChangesEnabled = view.TrackChangesEnabled;
            view.MoveCaretToBlock(0, 5);
            view.InsertText("X");
            view.MoveCaretToBlock(0, 0);
            view.DeleteForwardPublic();
            textAfterTrackChangesOnly = view.PlainText;
            hasRevisions = view.HasRevisions;
        });

        if (!ran)
            return;

        textWhileReadOnly.Should().Be("Hello");
        trackChangesEnabled.Should().BeTrue();
        textAfterTrackChangesOnly.Should().Be("HelloX");
        hasRevisions.Should().BeTrue();
    }

    [Fact]
    public async Task ReadOnlyProtection_blocks_undo_redo_history_until_protection_is_cleared()
    {
        var canUndoWhileReadOnly = true;
        var textAfterBlockedUndo = "";
        var canUndoAfterCleared = false;
        var textAfterAllowedUndo = "";
        var canRedoWhileFinal = true;
        var textAfterBlockedRedo = "";
        var canRedoAfterCleared = false;
        var textAfterAllowedRedo = "";

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");

            view.InsertText("X");
            view.SetProtection(ProtectionMode.ReadOnly);

            canUndoWhileReadOnly = view.CanUndo;
            view.Undo();
            textAfterBlockedUndo = view.PlainText;

            view.SetProtection(ProtectionMode.None);
            canUndoAfterCleared = view.CanUndo;
            view.Undo();
            textAfterAllowedUndo = view.PlainText;

            view.SetMarkedAsFinal(true);
            canRedoWhileFinal = view.CanRedo;
            view.Redo();
            textAfterBlockedRedo = view.PlainText;

            view.SetMarkedAsFinal(false);
            canRedoAfterCleared = view.CanRedo;
            view.Redo();
            textAfterAllowedRedo = view.PlainText;
        });

        if (!ran)
            return;

        canUndoWhileReadOnly.Should().BeFalse();
        textAfterBlockedUndo.Should().Be("XHello");
        canUndoAfterCleared.Should().BeTrue();
        textAfterAllowedUndo.Should().Be("Hello");
        canRedoWhileFinal.Should().BeFalse();
        textAfterBlockedRedo.Should().Be("Hello");
        canRedoAfterCleared.Should().BeTrue();
        textAfterAllowedRedo.Should().Be("XHello");
    }

    [Fact]
    public async Task TrackChangesOnly_keeps_text_and_formatting_unlocked_under_tracking_policy()
    {
        var editingLocked = true;
        var trackChangesEnabled = false;
        var textEditRequiresTracking = false;
        var formattingRequiresTracking = false;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");

            view.SetProtection(ProtectionMode.TrackChangesOnly);

            editingLocked = view.IsEditingLocked;
            trackChangesEnabled = view.TrackChangesEnabled;
            textEditRequiresTracking = view.GetRestrictEditingDecision(RestrictEditingOperationKind.BodyTextEdit)
                .RequiresTrackedChanges;
            formattingRequiresTracking = view.GetRestrictEditingDecision(RestrictEditingOperationKind.BodyFormatting)
                .RequiresTrackedChanges;
        });

        if (!ran)
            return;

        editingLocked.Should().BeFalse();
        trackChangesEnabled.Should().BeTrue();
        textEditRequiresTracking.Should().BeTrue();
        formattingRequiresTracking.Should().BeTrue();
    }

    [Fact]
    public async Task CommentsOnly_allows_comment_workflow_but_blocks_body_typing_and_formatting()
    {
        var textAfterBlockedTyping = "";
        var boldAfterBlockedFormatting = true;
        var commentsAfterInsert = -1;
        var replyAdded = false;
        bool? resolved = null;
        var commentsAfterDelete = -1;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello world");

            view.SetProtection(ProtectionMode.CommentsOnly);
            view.InsertText("X");
            textAfterBlockedTyping = view.PlainText;

            view.SelectAll();
            view.ToggleBold();
            boldAfterBlockedFormatting = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.Bold;

            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.NewComment("note");
            commentsAfterInsert = view.Document.Comments.Count;

            view.MoveCaretToBlock(0, 2);
            replyAdded = view.ReplyToCommentAtCaret("reply");
            resolved = view.ToggleResolveCommentAtCaret();
            view.DeleteCommentAtCaret();
            commentsAfterDelete = view.Document.Comments.Count;
        });

        if (!ran)
            return;

        textAfterBlockedTyping.Should().Be("Hello world");
        boldAfterBlockedFormatting.Should().BeFalse();
        commentsAfterInsert.Should().Be(1);
        replyAdded.Should().BeTrue();
        resolved.Should().BeTrue();
        commentsAfterDelete.Should().Be(0);
    }

    [Fact]
    public async Task CommentsOnly_allows_comment_history_undo_redo_but_blocks_body_history()
    {
        var canUndoCommentHistory = false;
        var commentsAfterCommentUndo = -1;
        var canRedoCommentHistory = false;
        var commentsAfterCommentRedo = -1;
        var canUndoBodyHistory = true;
        var textAfterBlockedBodyUndo = "";

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello world");

            view.SetSelectionRangePublic(0, 0, 0, 5);
            view.SetProtection(ProtectionMode.CommentsOnly);
            view.NewComment("note");
            view.GetRestrictEditingHistoryDecision(
                    RestrictEditingOperationKind.HistoryUndo,
                    DocumentCommandMutationKind.Comment)
                .Should().Match<RestrictEditingEnforcementDecision>(decision =>
                    decision.Operation == RestrictEditingOperationKind.HistoryUndo
                    && decision.IsAllowed
                    && decision.BlockReason == RestrictEditingBlockReason.None);
            view.GetRestrictEditingHistoryDecision(
                    RestrictEditingOperationKind.HistoryUndo,
                    DocumentCommandMutationKind.BodyText)
                .BlockReason.Should().Be(RestrictEditingBlockReason.CommentsOnly);

            canUndoCommentHistory = view.CanUndo;
            view.Undo();
            commentsAfterCommentUndo = view.Document.Comments.Count;

            canRedoCommentHistory = view.CanRedo;
            view.Redo();
            commentsAfterCommentRedo = view.Document.Comments.Count;

            var bodyView = BuildView("Hello");
            bodyView.InsertText("X");
            bodyView.SetProtection(ProtectionMode.CommentsOnly);

            canUndoBodyHistory = bodyView.CanUndo;
            bodyView.Undo();
            textAfterBlockedBodyUndo = bodyView.PlainText;
        });

        if (!ran)
            return;

        canUndoCommentHistory.Should().BeTrue();
        commentsAfterCommentUndo.Should().Be(0);
        canRedoCommentHistory.Should().BeTrue();
        commentsAfterCommentRedo.Should().Be(1);
        canUndoBodyHistory.Should().BeFalse();
        textAfterBlockedBodyUndo.Should().Be("XHello");
    }

    [Fact]
    public async Task CommentsOnly_allows_each_classified_comment_history_entry()
    {
        var canUndoInsert = false;
        var commentsAfterInsertUndo = -1;
        var canRedoInsert = false;
        var commentsAfterInsertRedo = -1;
        var canUndoReply = false;
        var repliesAfterReplyUndo = -1;
        var canRedoReply = false;
        var repliesAfterReplyRedo = -1;
        var canUndoResolve = false;
        bool? resolvedAfterResolveUndo = null;
        var canRedoResolve = false;
        bool? resolvedAfterResolveRedo = null;
        var canUndoDelete = false;
        var commentsAfterDeleteUndo = -1;
        var canRedoDelete = false;
        var commentsAfterDeleteRedo = -1;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello world");

            view.SetSelectionRangePublic(0, 0, 0, 5);
            view.SetProtection(ProtectionMode.CommentsOnly);
            var id = view.NewComment("note")!.Value;

            canUndoInsert = view.CanUndo;
            view.Undo();
            commentsAfterInsertUndo = view.Document.Comments.Count;
            canRedoInsert = view.CanRedo;
            view.Redo();
            commentsAfterInsertRedo = view.Document.Comments.Count;

            view.ReplyToComment(id, "reply").Should().BeTrue();
            canUndoReply = view.CanUndo;
            view.Undo();
            repliesAfterReplyUndo = view.Document.Comments[id].Replies.Count;
            canRedoReply = view.CanRedo;
            view.Redo();
            repliesAfterReplyRedo = view.Document.Comments[id].Replies.Count;

            view.SetCommentResolved(id, resolved: true).Should().BeTrue();
            canUndoResolve = view.CanUndo;
            view.Undo();
            resolvedAfterResolveUndo = view.Document.Comments[id].Resolved;
            canRedoResolve = view.CanRedo;
            view.Redo();
            resolvedAfterResolveRedo = view.Document.Comments[id].Resolved;

            view.DeleteComment(id).Should().BeTrue();
            canUndoDelete = view.CanUndo;
            view.Undo();
            commentsAfterDeleteUndo = view.Document.Comments.Count;
            canRedoDelete = view.CanRedo;
            view.Redo();
            commentsAfterDeleteRedo = view.Document.Comments.Count;
        });

        if (!ran)
            return;

        canUndoInsert.Should().BeTrue();
        commentsAfterInsertUndo.Should().Be(0);
        canRedoInsert.Should().BeTrue();
        commentsAfterInsertRedo.Should().Be(1);
        canUndoReply.Should().BeTrue();
        repliesAfterReplyUndo.Should().Be(0);
        canRedoReply.Should().BeTrue();
        repliesAfterReplyRedo.Should().Be(1);
        canUndoResolve.Should().BeTrue();
        resolvedAfterResolveUndo.Should().BeFalse();
        canRedoResolve.Should().BeTrue();
        resolvedAfterResolveRedo.Should().BeTrue();
        canUndoDelete.Should().BeTrue();
        commentsAfterDeleteUndo.Should().Be(1);
        canRedoDelete.Should().BeTrue();
        commentsAfterDeleteRedo.Should().Be(0);
    }

    [Fact]
    public async Task FillingForms_blocks_body_edits_and_reports_form_only_policy()
    {
        var textAfterBlockedTyping = "";
        var formEditAllowed = false;
        var bodyBlockReason = RestrictEditingBlockReason.None;
        var formOnlyPolicy = false;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");

            view.SetProtection(ProtectionMode.FillingForms);
            view.InsertText("X");

            textAfterBlockedTyping = view.PlainText;
            formEditAllowed = view.GetRestrictEditingDecision(RestrictEditingOperationKind.FormFieldEdit).IsAllowed;
            bodyBlockReason = view.GetRestrictEditingDecision(RestrictEditingOperationKind.BodyTextEdit).BlockReason;
            formOnlyPolicy = view.RestrictEditingPolicy.IsFormFieldEditingOnly;
        });

        if (!ran)
            return;

        textAfterBlockedTyping.Should().Be("Hello");
        formEditAllowed.Should().BeTrue();
        bodyBlockReason.Should().Be(RestrictEditingBlockReason.FillingForms);
        formOnlyPolicy.Should().BeTrue();
    }

    private static DocumentView BuildView(string firstParagraphText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(firstParagraphText));

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }
}
