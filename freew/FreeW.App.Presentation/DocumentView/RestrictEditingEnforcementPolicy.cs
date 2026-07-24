using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Renderer-neutral operation buckets for Word-style Restrict Editing enforcement.
/// </summary>
public enum RestrictEditingOperationKind
{
    BodyTextEdit,
    BodyTextDelete,
    ProofingReplacement,
    BodyFormatting,
    CommentInsert,
    CommentReply,
    CommentResolve,
    CommentDelete,
    FormFieldEdit,
    HistoryUndo,
    HistoryRedo
}

public enum RestrictEditingBlockReason
{
    None,
    MarkedAsFinal,
    ReadOnly,
    CommentsOnly,
    TrackChangesOnly,
    FillingForms
}

public readonly record struct RestrictEditingEnforcementDecision(
    RestrictEditingOperationKind Operation,
    bool IsAllowed,
    bool RequiresTrackedChanges,
    RestrictEditingBlockReason BlockReason,
    ProtectionMode ProtectionMode)
{
    public bool IsBlocked => !IsAllowed;
}

/// <summary>
/// Shared policy for Word-style document protection and Mark as Final enforcement.
/// </summary>
public readonly record struct RestrictEditingEnforcementPolicy(
    ProtectionSettings Protection,
    bool IsMarkedAsFinal)
{
    public static RestrictEditingEnforcementPolicy From(ProtectionSettings? protection, bool isMarkedAsFinal) =>
        new(protection ?? ProtectionSettings.Unprotected, isMarkedAsFinal);

    public bool ShouldForceTrackChanges =>
        !IsMarkedAsFinal && Protection.Mode == ProtectionMode.TrackChangesOnly;

    public bool IsBodyEditingLocked => DecisionFor(RestrictEditingOperationKind.BodyTextEdit).IsBlocked;

    public bool IsBodyFormattingLocked => DecisionFor(RestrictEditingOperationKind.BodyFormatting).IsBlocked;

    public bool IsCommentWorkflowAllowed =>
        DecisionFor(RestrictEditingOperationKind.CommentInsert).IsAllowed
        && DecisionFor(RestrictEditingOperationKind.CommentReply).IsAllowed
        && DecisionFor(RestrictEditingOperationKind.CommentResolve).IsAllowed
        && DecisionFor(RestrictEditingOperationKind.CommentDelete).IsAllowed;

    public bool IsFormFieldEditingOnly =>
        !IsMarkedAsFinal
        && Protection.Mode == ProtectionMode.FillingForms
        && DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed
        && DecisionFor(RestrictEditingOperationKind.BodyTextEdit).IsBlocked
        && DecisionFor(RestrictEditingOperationKind.BodyFormatting).IsBlocked;

    public bool IsHistoryLocked =>
        DecisionFor(RestrictEditingOperationKind.HistoryUndo).IsBlocked
        || DecisionFor(RestrictEditingOperationKind.HistoryRedo).IsBlocked;

    /// <summary>
    /// Classifies the mutation at the top of an undo/redo stack into the
    /// protection operation it represents. Both document hosts use this same
    /// mapping so comments-only history remains a shared policy decision.
    /// </summary>
    public static RestrictEditingOperationKind ClassifyHistoryMutation(
        RestrictEditingOperationKind historyOperation,
        DocumentCommandMutationKind? mutationKind)
    {
        if (historyOperation is not RestrictEditingOperationKind.HistoryUndo
            and not RestrictEditingOperationKind.HistoryRedo)
            throw new ArgumentOutOfRangeException(nameof(historyOperation), historyOperation, "Expected an undo/redo history operation.");

        return mutationKind switch
        {
            DocumentCommandMutationKind.BodyText => RestrictEditingOperationKind.BodyTextEdit,
            DocumentCommandMutationKind.BodyFormatting => RestrictEditingOperationKind.BodyFormatting,
            DocumentCommandMutationKind.Comment => RestrictEditingOperationKind.CommentInsert,
            DocumentCommandMutationKind.FormField => RestrictEditingOperationKind.FormFieldEdit,
            _ => historyOperation
        };
    }

    public RestrictEditingEnforcementDecision DecisionFor(RestrictEditingOperationKind operation)
    {
        if (IsMarkedAsFinal)
            return Block(operation, RestrictEditingBlockReason.MarkedAsFinal);

        return Protection.Mode switch
        {
            ProtectionMode.None => Allow(operation),
            ProtectionMode.ReadOnly => Block(operation, RestrictEditingBlockReason.ReadOnly),
            ProtectionMode.TrackChangesOnly => TrackChangesDecision(operation),
            ProtectionMode.CommentsOnly => CommentDecision(operation),
            ProtectionMode.FillingForms => FormDecision(operation),
            _ => Block(operation, RestrictEditingBlockReason.ReadOnly)
        };
    }

    public bool Allows(RestrictEditingOperationKind operation) => DecisionFor(operation).IsAllowed;

    public RestrictEditingEnforcementDecision DecisionForHistory(
        RestrictEditingOperationKind historyOperation,
        DocumentCommandMutationKind? mutationKind)
    {
        if (historyOperation is not RestrictEditingOperationKind.HistoryUndo
            and not RestrictEditingOperationKind.HistoryRedo)
            throw new ArgumentOutOfRangeException(nameof(historyOperation), historyOperation, "Expected an undo/redo history operation.");

        if (mutationKind is null)
            return DecisionFor(historyOperation);

        var effectiveOperation = ClassifyHistoryMutation(historyOperation, mutationKind);

        var decision = DecisionFor(effectiveOperation);
        return decision with { Operation = historyOperation };
    }

    public bool AllowsHistory(
        RestrictEditingOperationKind historyOperation,
        DocumentCommandMutationKind? mutationKind) =>
        DecisionForHistory(historyOperation, mutationKind).IsAllowed;

    private RestrictEditingEnforcementDecision TrackChangesDecision(RestrictEditingOperationKind operation)
    {
        var requiresTracking = operation is RestrictEditingOperationKind.BodyTextEdit
            or RestrictEditingOperationKind.BodyTextDelete
            or RestrictEditingOperationKind.ProofingReplacement
            or RestrictEditingOperationKind.BodyFormatting
            or RestrictEditingOperationKind.HistoryUndo
            or RestrictEditingOperationKind.HistoryRedo;
        return Allow(operation, requiresTracking);
    }

    private RestrictEditingEnforcementDecision CommentDecision(RestrictEditingOperationKind operation) =>
        operation is RestrictEditingOperationKind.CommentInsert
            or RestrictEditingOperationKind.CommentReply
            or RestrictEditingOperationKind.CommentResolve
            or RestrictEditingOperationKind.CommentDelete
            ? Allow(operation)
            : Block(operation, RestrictEditingBlockReason.CommentsOnly);

    private RestrictEditingEnforcementDecision FormDecision(RestrictEditingOperationKind operation) =>
        operation == RestrictEditingOperationKind.FormFieldEdit
            ? Allow(operation)
            : Block(operation, RestrictEditingBlockReason.FillingForms);

    private RestrictEditingEnforcementDecision Allow(
        RestrictEditingOperationKind operation,
        bool requiresTrackedChanges = false) =>
        new(operation, IsAllowed: true, requiresTrackedChanges, RestrictEditingBlockReason.None, Protection.Mode);

    private RestrictEditingEnforcementDecision Block(
        RestrictEditingOperationKind operation,
        RestrictEditingBlockReason reason) =>
        new(operation, IsAllowed: false, RequiresTrackedChanges: false, reason, Protection.Mode);
}
