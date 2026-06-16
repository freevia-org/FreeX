using Free.Shared.Commands;

namespace FreeW.Core.Model;

/// <summary>Context a document command mutates — the document plus a redraw signal.</summary>
public interface IDocumentCommandContext
{
    TextDocument Document { get; }
}

/// <summary>A reversible edit to a <see cref="TextDocument"/>.</summary>
public interface IDocumentCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    void Apply(IDocumentCommandContext context);
    void Revert(IDocumentCommandContext context);
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

    /// <summary>Raised after any execute/undo/redo so the view can refresh.</summary>
    public event Action? Changed;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    public void Execute(IDocumentCommand command)
    {
        command.Apply(_context);
        _stack.Push(command, command.EstimatedBytes, null, command.Label);
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
