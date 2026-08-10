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
public sealed class CompositeDocumentCommand(string label, IReadOnlyList<IDocumentCommand> commands)
    : IDocumentCommand
{
    public string Label => label;

    public DocumentCommandMutationKind MutationKind => Classify(commands);

    public int EstimatedBytes =>
        commands.Count == 0 ? 0 : commands.Sum(c => c.EstimatedBytes);

    public void Apply(IDocumentCommandContext context)
    {
        foreach (var cmd in commands)
            cmd.Apply(context);
    }

    public void Revert(IDocumentCommandContext context)
    {
        for (var i = commands.Count - 1; i >= 0; i--)
            commands[i].Revert(context);
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
        command.Apply(_context);
        if (_batch is not null)
        {
            _batch.Add(command);
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
    public void BeginUndoGroup()
    {
        if (_batch is not null)
            throw new InvalidOperationException("An undo group is already open.");
        _batch = new List<IDocumentCommand>();
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
    /// history entry. Use this when a multi-step operation fails after partially mutating the document.
    /// </summary>
    public void RollbackUndoGroup()
    {
        var batch = _batch ?? throw new InvalidOperationException("No undo group is open.");
        _batch = null;
        for (var i = batch.Count - 1; i >= 0; i--)
            batch[i].Revert(_context);
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (!_stack.CanUndo)
            return false;
        var entry = _stack.PopUndo();
        entry.Command.Revert(_context);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!_stack.CanRedo)
            return false;
        var entry = _stack.PopRedo();
        entry.Command.Apply(_context);
        _stack.PushWithoutClearingRedo(entry);
        Changed?.Invoke();
        return true;
    }
}
