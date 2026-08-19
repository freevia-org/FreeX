using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Enforcement coverage for document protection (Restrict Editing) and Mark as Final on the live
/// <see cref="DocumentView"/> editing surface. These run on an STA thread (<c>[StaFact]</c>, via
/// Xunit.StaFact) because the RichTextBox needs STA + a Dispatcher.
/// </summary>
public sealed class ProtectionEnforcementTests
{
    private sealed class BodyHistoryCommand : IDocumentCommand
    {
        public string Label => "Insert Body Paragraph";

        public void Apply(IDocumentCommandContext context) =>
            context.Document.Blocks.Add(new Paragraph("Body history"));

        public void Revert(IDocumentCommandContext context) =>
            context.Document.Blocks.RemoveAt(context.Document.Blocks.Count - 1);
    }

    private static DocumentView Load()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body"));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView LoadWithComment()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body") { CommentId = 0 });
        paragraph.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(paragraph);
        doc.Comments[0] = new Comment(0, "note", "A", "A");

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.FirstBlock!.ContentStart;
        return view;
    }

    private static DocumentView LoadWithContentControl()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "Agree"));
        paragraph.Runs.Add(new Run("Body"));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static string PlainText(DocumentView view) =>
        view.Model.Paragraphs.First().PlainText;

    private static Paragraph FirstParagraph(DocumentView view) =>
        (Paragraph)view.Model.Blocks[0];

    [StaFact]
    public void NoChangesProtection_MakesEditorReadOnly_AndStopRestoresEditing()
    {
        var view = Load();
        view.IsReadOnly.Should().BeFalse();

        // Restrict Editing → No changes (Read only) locks the typing surface.
        view.SetProtection(ProtectionMode.ReadOnly);
        view.IsProtected.Should().BeTrue();
        view.IsReadOnly.Should().BeTrue();

        // Stop Protection (None) restores editing.
        view.SetProtection(ProtectionMode.None);
        view.IsProtected.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void HistoryCommands_FollowSharedProtectionPolicy()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.ReadOnly);
        view.GetRestrictEditingDecision(RestrictEditingOperationKind.HistoryUndo)
            .BlockReason.Should().Be(RestrictEditingBlockReason.ReadOnly);
        view.CanUndo.Should().BeFalse();
        view.CanRedo.Should().BeFalse();

        view.SetProtection(ProtectionMode.None);
        view.SetProtection(ProtectionMode.TrackChangesOnly);

        view.GetRestrictEditingDecision(RestrictEditingOperationKind.HistoryUndo)
            .RequiresTrackedChanges.Should().BeTrue();
        view.GetRestrictEditingDecision(RestrictEditingOperationKind.HistoryRedo)
            .IsAllowed.Should().BeTrue();
    }

    [StaFact]
    public void CommentsOnlyProtection_AllowsClassifiedCommentHistory_ButBlocksBodyHistory()
    {
        var view = Load();
        view.SetProtection(ProtectionMode.CommentsOnly);
        view.InsertComment("review", "Ann", "A");
        view.Model.Comments.Should().HaveCount(1);
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

        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments.Should().BeEmpty();

        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments.Should().HaveCount(1);

        var bodyView = Load();
        bodyView.Commands.Execute(new BodyHistoryCommand());
        bodyView.Model.Blocks.Should().HaveCount(2);
        bodyView.SetProtection(ProtectionMode.CommentsOnly);

        bodyView.CanUndo.Should().BeFalse();
        bodyView.Undo();
        bodyView.Model.Blocks.Should().HaveCount(2);
    }

    [StaFact]
    public void TrackChangesProtection_LeavesEditable_ButForcesTrackChangesOn()
    {
        var view = Load();
        view.TrackChangesEnabled.Should().BeFalse();

        view.SetProtection(ProtectionMode.TrackChangesOnly);

        // Tracked-changes protection keeps the surface editable but forces tracking on.
        view.IsReadOnly.Should().BeFalse();
        view.TrackChangesEnabled.Should().BeTrue();
        view.GetRestrictEditingDecision(RestrictEditingOperationKind.BodyTextEdit)
            .RequiresTrackedChanges.Should().BeTrue();

        view.InsertText("X");
        PlainText(view).Should().Contain("X");
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Should().Contain(run => run.Text == "X" && run.Revision == RevisionKind.Inserted);
    }

    [StaFact]
    public void CommentsOnlyProtection_AllowsCommentWorkflow_ButBlocksBodyTypingAndFormatting()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.CommentsOnly);
        view.IsReadOnly.Should().BeTrue();
        view.RestrictEditingPolicy.IsCommentWorkflowAllowed.Should().BeTrue();

        view.InsertText("X");
        PlainText(view).Should().Be("Body");

        view.ApplyFontFormatting(new RunFormatting { Bold = true });
        ((Paragraph)view.Model.Blocks[0]).Runs[0].Formatting.Bold.Should().BeFalse();

        view.InsertComment("review", "Ann", "A");
        view.Model.Comments.Should().HaveCount(1);
    }

    [StaFact]
    public void CommentsOnlyProtection_AllowsReplyResolveAndDelete()
    {
        var view = LoadWithComment();

        view.SetProtection(ProtectionMode.CommentsOnly);

        view.ReplyToCommentAtCaret("reply", "Bob", "B").Should().BeTrue();
        view.Model.Comments[0].Replies.Should().ContainSingle();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments[0].Replies.Should().BeEmpty();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments[0].Replies.Should().ContainSingle();

        view.ToggleResolveCommentAtCaret().Should().BeTrue();
        view.Model.Comments[0].Resolved.Should().BeTrue();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments[0].Resolved.Should().BeFalse();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments[0].Resolved.Should().BeTrue();

        view.DeleteCommentAtCaret().Should().BeTrue();
        view.Model.Comments.Should().BeEmpty();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments.Should().ContainKey(0);
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments.Should().BeEmpty();
    }

    [StaFact]
    public void CommentsOnlyProtection_AllowsEachClassifiedCommentHistoryEntry()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.CommentsOnly);
        view.InsertComment("note", "Ann", "A");
        var id = view.Model.Comments.Keys.Single();

        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments.Should().BeEmpty();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments.Should().ContainKey(id);

        view.ReplyToCommentAtCaret("reply", "Bob", "B").Should().BeTrue();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments[id].Replies.Should().BeEmpty();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments[id].Replies.Should().ContainSingle();

        view.ToggleResolveCommentAtCaret().Should().BeTrue();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments[id].Resolved.Should().BeFalse();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments[id].Resolved.Should().BeTrue();

        view.DeleteCommentAtCaret().Should().BeTrue();
        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments.Should().ContainKey(id);
        view.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments.Should().BeEmpty();
    }

    [StaFact]
    public void FillingFormsProtection_BlocksNormalBodyEdits_AndReportsFormOnlyPolicy()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.FillingForms);
        view.IsReadOnly.Should().BeTrue();
        view.RestrictEditingPolicy.IsFormFieldEditingOnly.Should().BeTrue();
        view.GetRestrictEditingDecision(RestrictEditingOperationKind.FormFieldEdit)
            .IsAllowed.Should().BeTrue();
        view.GetRestrictEditingDecision(RestrictEditingOperationKind.BodyTextEdit)
            .BlockReason.Should().Be(RestrictEditingBlockReason.FillingForms);

        view.InsertText("X");
        PlainText(view).Should().Be("Body");
    }

    [StaFact]
    public void FillingFormsProtection_AllowsExistingContentControlEdits_ButBlocksStricterProtection()
    {
        var view = LoadWithContentControl();

        view.SetProtection(ProtectionMode.FillingForms);
        view.InsertCheckBoxControl();

        FirstParagraph(view).Runs.Should().HaveCount(2, "Filling Forms can fill existing controls but not insert new body controls");
        view.ToggleContentControl(0, 0).Should().BeTrue();
        FirstParagraph(view).Runs[0].Control!.Checked.Should().BeTrue();

        view.CanUndo.Should().BeTrue();
        view.Undo();
        FirstParagraph(view).Runs[0].Control!.Checked.Should().BeFalse();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        FirstParagraph(view).Runs[0].Control!.Checked.Should().BeTrue();

        view.SetProtection(ProtectionMode.ReadOnly);
        view.ToggleContentControl(0, 0).Should().BeFalse();
        FirstParagraph(view).Runs[0].Control!.Checked.Should().BeTrue();

        view.SetProtection(ProtectionMode.None);
        view.SetMarkedAsFinal(true);
        view.ToggleContentControl(0, 0).Should().BeFalse();
        FirstParagraph(view).Runs[0].Control!.Checked.Should().BeTrue();
    }

    [StaFact]
    public void ReadOnlyProtection_BlocksCommentInsertion()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.ReadOnly);
        view.InsertComment("review", "Ann", "A");

        view.Model.Comments.Should().BeEmpty();
    }

    /// <summary>
    /// Covers the Insert-tab family of methods that previously bypassed Restrict Editing entirely
    /// (InsertTable/InsertHyperlink/InsertPageBreak and siblings all reach <c>_editingSession.InsertBlockAfter</c>
    /// or mutate the FlowDocument directly with no <c>AllowsRestrictEditingOperation</c> check, unlike
    /// InsertText/ApplyFontFormatting right above). "No changes (Read only)" must block all three
    /// representative shapes of insert: a new block (table/page break) and an inline edit (hyperlink).
    /// </summary>
    [StaFact]
    public void ReadOnlyProtection_BlocksInsertOperationsFromTheInsertRibbonFamily()
    {
        var view = Load();
        view.SetProtection(ProtectionMode.ReadOnly);

        view.InsertTable(2, 2).Should().Be(-1, "a read-only document must not gain a new table");
        view.Model.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();

        view.InsertHyperlink("Example", "https://example.com");
        PlainText(view).Should().Be("Body", "read-only protection must block hyperlink insertion");

        view.InsertPageBreak();
        view.Model.Blocks.Should().HaveCount(1, "read-only protection must block page-break insertion");
    }

    /// <summary>
    /// Sibling no-regression check for <see cref="ReadOnlyProtection_BlocksInsertOperationsFromTheInsertRibbonFamily"/>:
    /// the same three operations must still work normally once no protection is active, so the new guard
    /// only narrows the protected case and never breaks ordinary unprotected editing.
    /// </summary>
    [StaFact]
    public void Unprotected_StillAllowsInsertOperationsFromTheInsertRibbonFamily()
    {
        var view = Load();

        // Hyperlink first, while the caret still sits in the "Body" paragraph: InsertTable moves the
        // caret into the new table's first cell (see InsertTable's doc comment), which would otherwise
        // route the hyperlink into the table instead of the body paragraph PlainText(view) reads.
        view.InsertHyperlink("Example", "https://example.com");
        PlainText(view).Should().Contain("Example");

        var tableIndex = view.InsertTable(2, 2);
        tableIndex.Should().BeGreaterThanOrEqualTo(0);
        view.Model.Blocks[tableIndex].Should().BeOfType<Table>();

        var blocksBeforeBreak = view.Model.Blocks.Count;
        view.InsertPageBreak();
        view.Model.Blocks.Count.Should().Be(blocksBeforeBreak + 1, "an unprotected document must still accept a page break");
    }

    [StaFact]
    public void MarkAsFinal_LocksEditing_AndEditAnywayRestoresIt()
    {
        var view = Load();
        view.IsMarkedAsFinal.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();

        view.SetMarkedAsFinal(true);
        view.IsMarkedAsFinal.Should().BeTrue();
        view.IsReadOnly.Should().BeTrue();

        // "Edit Anyway" clears the flag and restores editing.
        view.SetMarkedAsFinal(false);
        view.IsMarkedAsFinal.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void ProtectionStateChanged_Fires_OnProtectionAndFinalChanges()
    {
        var view = Load();
        var fired = 0;
        view.ProtectionStateChanged += (_, _) => fired++;

        view.SetProtection(ProtectionMode.ReadOnly);
        view.SetMarkedAsFinal(true);

        fired.Should().BeGreaterThanOrEqualTo(2);
    }
}
