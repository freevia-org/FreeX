using Free.Shared.Commands;

namespace FreeW.Core.Model;

/// <summary>Context a document command mutates — the document plus a redraw signal.</summary>
public interface IDocumentCommandContext
{
    TextDocument Document { get; }

    /// <summary>The active review author for revisions created by document commands.</summary>
    string? RevisionAuthor => null;
}

/// <summary>A reversible edit to a <see cref="TextDocument"/>.</summary>
public interface IDocumentCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyText;
    void Apply(IDocumentCommandContext context);
    void Revert(IDocumentCommandContext context);

    /// <summary>
    /// Whether executing this command would actually change the document. When false, the bus
    /// skips it entirely (no Apply, no undo entry) so a no-op edit doesn't clear the redo history
    /// or pollute the undo stack. Defaults to true — commands that can be invoked on a target
    /// where they'd do nothing (e.g. "Bring Forward" on the already-topmost floating object)
    /// override this. Mirrors FreeP's IPresentationCommand.HasEffect.
    /// </summary>
    bool HasEffect(IDocumentCommandContext context) => true;
}

public enum DocumentCommandMutationKind
{
    BodyText,
    BodyFormatting,
    Comment,
    FormField,
    Mixed
}

/// <summary>
/// Groups several <see cref="IDocumentCommand"/> instances into a single undoable action.
/// Applying executes the inner commands in order; reverting executes them in reverse order.
/// This makes multi-property dialog applications (e.g. the Font dialog setting family +
/// size + bold in one OK) appear as a single undo step.
/// </summary>
public sealed class CompositeDocumentCommand : IDocumentCommand
{
    private readonly string _label;
    private readonly IReadOnlyList<IDocumentCommand> _commands;

    // Tracks which children are currently applied and therefore must be reverted.
    // Seeded to EVERY child at construction time (not left empty) because
    // DocumentCommandBus.CommitUndoGroup builds a composite from children that were
    // already applied individually as Execute() collected them into the batch -- it
    // never calls this composite's own Apply(). Without seeding, Revert() on such a
    // composite would find an empty list and silently do nothing (a no-op "undo" that
    // leaves the document exactly as edited). Apply() below resets this list itself
    // when it actually runs, so the direct-construct-then-Execute path (e.g.
    // DocumentObjectEditingCoordinator's Resize composites) is unaffected.
    private readonly List<IDocumentCommand> _applied;

    public CompositeDocumentCommand(string label, IReadOnlyList<IDocumentCommand> commands)
    {
        _label = label;
        _commands = commands;
        _applied = [.. commands];
    }

    public string Label => _label;

    public DocumentCommandMutationKind MutationKind => Classify(_commands);

    public int EstimatedBytes =>
        _commands.Count == 0 ? 0 : _commands.Sum(c => c.EstimatedBytes);

    public void Apply(IDocumentCommandContext context)
    {
        _applied.Clear();
        foreach (var cmd in _commands)
        {
            try
            {
                cmd.Apply(context);
            }
            catch
            {
                // A child threw mid-apply: roll back the children that already
                // succeeded so the composite stays atomic (no user-unauthored partial
                // state), then let the caller see the failure -- DocumentCommandBus.Execute
                // never pushes an undo entry for a command whose Apply throws.
                RevertApplied(context);
                throw;
            }

            _applied.Add(cmd);
        }
    }

    public void Revert(IDocumentCommandContext context) => RevertApplied(context);

    private void RevertApplied(IDocumentCommandContext context)
    {
        try
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                try
                {
                    _applied[i].Revert(context);
                }
                catch
                {
                    // Best-effort rollback: a failing child revert must not abort the
                    // rest of the rollback, nor leave _applied populated for a second
                    // (double) revert pass. Mirrors FreeX's CompositeWorkbookCommand.
                }
            }
        }
        finally
        {
            _applied.Clear();
        }
    }

    private static DocumentCommandMutationKind Classify(IReadOnlyList<IDocumentCommand> commands)
    {
        if (commands.Count == 0)
            return DocumentCommandMutationKind.Mixed;

        var first = commands[0].MutationKind;
        for (var i = 1; i < commands.Count; i++)
        {
            if (commands[i].MutationKind != first)
                return DocumentCommandMutationKind.Mixed;
        }

        return first;
    }
}

/// <summary>
/// FreeW's undo/redo command bus. The mechanics — paired stacks, depth/byte budget, redo
/// invalidation — are the shared <see cref="UndoRedoStack{TCommand,TPayload}"/>; this bus only
/// adds the document-command apply/revert and a change notification for the view to redraw.
/// </summary>
public sealed class DocumentCommandBus(IDocumentCommandContext context)
{
    private const int MaxDepth = 200;
    private const int MaxBytes = 50 * 1024 * 1024;

    private readonly UndoRedoStack<IDocumentCommand, object?> _stack = new(MaxDepth, MaxBytes);
    private readonly IDocumentCommandContext _context = context;

    // Batch / undo-group support: when non-null, Execute() collects into this list
    // instead of pushing directly onto the undo stack.
    private List<IDocumentCommand>? _batch;

    // Set by BeginUndoGroup(notifyOnEachExecute: true). Most callers batch several commands that were
    // each computed against the model directly (e.g. MultilevelListMutationCoordinator), so they neither
    // need nor want a redraw between commands. Find & Replace's Replace All is different: each replacement
    // is found by walking the *rendered* surface via TextPointer, and the WPF host's per-edit pipeline
    // (TryApplyBodyTextInput -> CommitToModel/PlaceCaretAtModelTextOffset) only stays correct if Changed's
    // Render() runs between edits -- otherwise the next edit's CommitToModel() re-reads the stale rendered
    // surface and silently discards every replacement but the last (see FindReplaceDialog.cs ReplaceAll).
    private bool _notifyOnEachBatchedExecute;

    /// <summary>Raised after any execute/undo/redo so the view can refresh.</summary>
    public event Action? Changed;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    /// <summary>True while an outer caller is collecting commands into one undoable edit.</summary>
    public bool IsUndoGroupOpen => _batch is not null;

    public DocumentCommandMutationKind? NextUndoMutationKind =>
        _stack.TryPeekUndo(out var entry) ? entry.Command.MutationKind : null;

    public DocumentCommandMutationKind? NextRedoMutationKind =>
        _stack.TryPeekRedo(out var entry) ? entry.Command.MutationKind : null;

    public void Execute(IDocumentCommand command)
    {
        // Skip no-op commands entirely so they don't create an empty undo entry or clear
        // a pending redo (Push() below always invalidates redo — see UndoRedoStack.Push).
        if (!command.HasEffect(_context))
            return;

        command.Apply(_context);
        if (_batch is not null)
        {
            _batch.Add(command);
            if (_notifyOnEachBatchedExecute)
                Changed?.Invoke();
        }
        else
        {
            _stack.Push(command, command.EstimatedBytes, null, command.Label);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Begins collecting subsequent <see cref="Execute"/> calls into a single undo group.
    /// Each call still applies its command immediately; the group is committed as a single
    /// <see cref="CompositeDocumentCommand"/> when <see cref="CommitUndoGroup"/> is called.
    /// Not reentrant — only one group may be open at a time.
    /// </summary>
    /// <param name="notifyOnEachExecute">
    /// When true, <see cref="Changed"/> still fires after every batched <see cref="Execute"/> (only the
    /// undo-stack push is deferred). Most batches compute every command against the model up front and
    /// don't need this, but a caller whose commands are found by re-searching the *rendered* surface
    /// between edits (e.g. Find &amp; Replace's Replace All) needs the redraw <see cref="Changed"/> would
    /// normally trigger to happen before the next command is computed.
    /// </param>
    public void BeginUndoGroup(bool notifyOnEachExecute = false)
    {
        if (_batch is not null)
            throw new InvalidOperationException("An undo group is already open.");
        _batch = new List<IDocumentCommand>();
        _notifyOnEachBatchedExecute = notifyOnEachExecute;
    }

    /// <summary>
    /// Closes the current undo group started by <see cref="BeginUndoGroup"/> and pushes the
    /// collected commands as a single <see cref="CompositeDocumentCommand"/> onto the undo stack.
    /// If no commands were collected, nothing is pushed.
    /// </summary>
    public void CommitUndoGroup(string label)
    {
        var batch = _batch ?? throw new InvalidOperationException("No undo group is open.");
        _batch = null;
        if (batch.Count == 0)
            return;
        var composite = new CompositeDocumentCommand(label, batch);
        _stack.Push(composite, composite.EstimatedBytes, null, label);
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes and discards any open undo group without pushing anything onto the undo stack.
    /// Any commands already applied are NOT reverted — use this only on error paths where
    /// the caller will handle cleanup.
    /// </summary>
    public void AbortUndoGroup()
    {
        _batch = null;
    }

    /// <summary>
    /// Reverts every command already applied in the current undo group, closes the group, and leaves no
    /// history entry. Every revert is attempted even when an earlier one fails. A complete restoration is
    /// silent; an incomplete restoration raises <see cref="Changed"/> so renderers can show the surviving
    /// state. Returned exceptions are ordered by rollback attempt, followed by notification failures.
    /// </summary>
    public IReadOnlyList<Exception> RollbackUndoGroup()
    {
        var batch = _batch ?? throw new InvalidOperationException("No undo group is open.");
        _batch = null;
        var failures = new List<Exception>();
        for (var i = batch.Count - 1; i >= 0; i--)
        {
            try
            {
                batch[i].Revert(_context);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            NotifyChangedBestEffort(failures);

        return failures;
    }

    private void NotifyChangedBestEffort(List<Exception> failures)
    {
        if (Changed is not { } changed)
            return;

        foreach (var subscriber in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                subscriber();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    public bool Undo()
    {
        if (!_stack.CanUndo)
            return false;
        var entry = _stack.PopUndo();
        try
        {
            entry.Command.Revert(_context);
        }
        catch
        {
            // Revert threw mid-undo: PopUndo already moved the entry onto the redo
            // stack, so without this the entry would be stranded there while the
            // document may be left half-reverted -- a later Undo() call would see a
            // shorter/wrong undo stack. RollbackPopUndo restores the entry to the top
            // of the undo stack (and removes it from the redo stack), matching
            // FreeX's CommandBus.Undo safety net so the stacks stay consistent even
            // though this attempt failed.
            _stack.RollbackPopUndo(entry);
            throw;
        }

        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!_stack.CanRedo)
            return false;
        var entry = _stack.PopRedo();
        try
        {
            entry.Command.Apply(_context);
        }
        catch
        {
            // Apply threw mid-redo: PopRedo already removed the entry from the redo
            // stack, so without this the entry would be lost entirely. PushRedo
            // restores it so the user can retry, matching FreeX's CommandBus.Redo
            // safety net.
            _stack.PushRedo(entry);
            throw;
        }

        _stack.PushWithoutClearingRedo(entry);
        Changed?.Invoke();
        return true;
    }
}
