using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class RestrictEditingEnforcementPolicyTests
{
    [Theory]
    [InlineData(false, ProtectionMode.None, false, false)]
    [InlineData(true, ProtectionMode.None, true, false)]
    [InlineData(false, ProtectionMode.ReadOnly, false, true)]
    [InlineData(true, ProtectionMode.CommentsOnly, true, true)]
    public void Review_protection_state_plan_projects_shared_ribbon_toggle_state(
        bool isMarkedAsFinal,
        ProtectionMode mode,
        bool expectedFinalChecked,
        bool expectedRestrictEditingChecked)
    {
        var protection = new ProtectionSettings(mode);

        var plan = ReviewProtectionStatePlanner.Build(protection, isMarkedAsFinal);

        plan.MarkAsFinal.CommandId.Should().Be(ReviewProtectionStatePlanner.MarkAsFinalCommandId);
        plan.MarkAsFinal.IsChecked.Should().Be(expectedFinalChecked);
        plan.RestrictEditing.CommandId.Should().Be(ReviewProtectionStatePlanner.RestrictEditingCommandId);
        plan.RestrictEditing.IsChecked.Should().Be(expectedRestrictEditingChecked);
        plan.Commands.Select(state => state.CommandId)
            .Should().Equal(
                ReviewProtectionStatePlanner.MarkAsFinalCommandId,
                ReviewProtectionStatePlanner.RestrictEditingCommandId);
    }

    [Theory]
    [InlineData(RestrictEditingOperationKind.BodyTextEdit)]
    [InlineData(RestrictEditingOperationKind.BodyTextDelete)]
    [InlineData(RestrictEditingOperationKind.ProofingReplacement)]
    [InlineData(RestrictEditingOperationKind.BodyFormatting)]
    [InlineData(RestrictEditingOperationKind.CommentInsert)]
    [InlineData(RestrictEditingOperationKind.CommentReply)]
    [InlineData(RestrictEditingOperationKind.CommentResolve)]
    [InlineData(RestrictEditingOperationKind.CommentDelete)]
    [InlineData(RestrictEditingOperationKind.FormFieldEdit)]
    [InlineData(RestrictEditingOperationKind.HistoryUndo)]
    [InlineData(RestrictEditingOperationKind.HistoryRedo)]
    public void Unprotected_document_allows_normal_operations(RestrictEditingOperationKind operation)
    {
        var policy = RestrictEditingEnforcementPolicy.From(ProtectionSettings.Unprotected, isMarkedAsFinal: false);

        var decision = policy.DecisionFor(operation);

        decision.IsAllowed.Should().BeTrue();
        decision.RequiresTrackedChanges.Should().BeFalse();
        decision.BlockReason.Should().Be(RestrictEditingBlockReason.None);
    }

    [Theory]
    [InlineData(RestrictEditingOperationKind.BodyTextEdit)]
    [InlineData(RestrictEditingOperationKind.ProofingReplacement)]
    [InlineData(RestrictEditingOperationKind.BodyFormatting)]
    [InlineData(RestrictEditingOperationKind.CommentInsert)]
    [InlineData(RestrictEditingOperationKind.FormFieldEdit)]
    [InlineData(RestrictEditingOperationKind.HistoryUndo)]
    public void Mark_as_final_blocks_document_mutations_until_cleared(RestrictEditingOperationKind operation)
    {
        var policy = RestrictEditingEnforcementPolicy.From(ProtectionSettings.Unprotected, isMarkedAsFinal: true);

        var decision = policy.DecisionFor(operation);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockReason.Should().Be(RestrictEditingBlockReason.MarkedAsFinal);
        policy.IsBodyEditingLocked.Should().BeTrue();
    }

    [Fact]
    public void Read_only_protection_blocks_body_format_comments_and_forms()
    {
        var policy = Policy(ProtectionMode.ReadOnly);

        policy.DecisionFor(RestrictEditingOperationKind.BodyTextEdit).IsAllowed.Should().BeFalse();
        policy.DecisionFor(RestrictEditingOperationKind.BodyFormatting).IsAllowed.Should().BeFalse();
        policy.DecisionFor(RestrictEditingOperationKind.CommentInsert).IsAllowed.Should().BeFalse();
        policy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed.Should().BeFalse();
        policy.IsBodyEditingLocked.Should().BeTrue();
        policy.IsHistoryLocked.Should().BeTrue();
    }

    [Theory]
    [InlineData(RestrictEditingOperationKind.BodyTextEdit)]
    [InlineData(RestrictEditingOperationKind.BodyTextDelete)]
    [InlineData(RestrictEditingOperationKind.ProofingReplacement)]
    [InlineData(RestrictEditingOperationKind.BodyFormatting)]
    [InlineData(RestrictEditingOperationKind.HistoryUndo)]
    [InlineData(RestrictEditingOperationKind.HistoryRedo)]
    public void Track_changes_only_allows_body_edits_only_as_tracked_changes(RestrictEditingOperationKind operation)
    {
        var policy = Policy(ProtectionMode.TrackChangesOnly);

        var decision = policy.DecisionFor(operation);

        decision.IsAllowed.Should().BeTrue();
        decision.RequiresTrackedChanges.Should().BeTrue();
        policy.ShouldForceTrackChanges.Should().BeTrue();
        policy.IsBodyEditingLocked.Should().BeFalse();
    }

    [Fact]
    public void Comments_only_allows_comment_workflow_and_blocks_body_or_form_edits()
    {
        var policy = Policy(ProtectionMode.CommentsOnly);

        policy.IsCommentWorkflowAllowed.Should().BeTrue();
        policy.DecisionFor(RestrictEditingOperationKind.BodyTextEdit).BlockReason
            .Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.DecisionFor(RestrictEditingOperationKind.ProofingReplacement).BlockReason
            .Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.DecisionFor(RestrictEditingOperationKind.BodyFormatting).IsAllowed.Should().BeFalse();
        policy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed.Should().BeFalse();
        policy.DecisionFor(RestrictEditingOperationKind.HistoryUndo).BlockReason
            .Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.IsHistoryLocked.Should().BeTrue();
    }

    [Fact]
    public void Comments_only_allows_comment_history_entries_but_blocks_body_history_entries()
    {
        var policy = Policy(ProtectionMode.CommentsOnly);

        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.Comment)
            .IsAllowed.Should().BeTrue();
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.Comment)
            .IsAllowed.Should().BeTrue();
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.BodyText)
            .BlockReason.Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.BodyFormatting)
            .BlockReason.Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.Mixed)
            .BlockReason.Should().Be(RestrictEditingBlockReason.CommentsOnly);
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, mutationKind: null)
            .BlockReason.Should().Be(RestrictEditingBlockReason.CommentsOnly);
    }

    [Fact]
    public void Read_only_and_marked_final_still_block_comment_history_entries()
    {
        Policy(ProtectionMode.ReadOnly)
            .DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.Comment)
            .BlockReason.Should().Be(RestrictEditingBlockReason.ReadOnly);

        RestrictEditingEnforcementPolicy.From(ProtectionSettings.Unprotected, isMarkedAsFinal: true)
            .DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.Comment)
            .BlockReason.Should().Be(RestrictEditingBlockReason.MarkedAsFinal);
    }

    [Fact]
    public void Filling_forms_allows_only_form_field_edits()
    {
        var policy = Policy(ProtectionMode.FillingForms);

        policy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed.Should().BeTrue();
        policy.DecisionFor(RestrictEditingOperationKind.BodyTextEdit).BlockReason
            .Should().Be(RestrictEditingBlockReason.FillingForms);
        policy.DecisionFor(RestrictEditingOperationKind.CommentInsert).IsAllowed.Should().BeFalse();
        policy.IsFormFieldEditingOnly.Should().BeTrue();
        policy.IsHistoryLocked.Should().BeTrue();
    }

    [Fact]
    public void Filling_forms_allows_form_field_history_entries_but_blocks_body_history_entries()
    {
        var policy = Policy(ProtectionMode.FillingForms);

        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.FormField)
            .IsAllowed.Should().BeTrue();
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.FormField)
            .IsAllowed.Should().BeTrue();
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.BodyText)
            .BlockReason.Should().Be(RestrictEditingBlockReason.FillingForms);
        policy.DecisionForHistory(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.Mixed)
            .BlockReason.Should().Be(RestrictEditingBlockReason.FillingForms);
    }

    private static RestrictEditingEnforcementPolicy Policy(ProtectionMode mode) =>
        RestrictEditingEnforcementPolicy.From(new ProtectionSettings(mode), isMarkedAsFinal: false);
}
