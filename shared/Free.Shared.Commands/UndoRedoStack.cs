namespace Free.Shared.Commands;

/// <summary>One entry in undo/redo history (label only).</summary>
public sealed record CommandHistoryEntry(string Label);

/// <summary>One entry held on the undo or redo stack.</summary>
/// <typeparam name="TCommand">The application command type.</typeparam>
/// <typeparam name="TPayload">
/// Opaque per-entry payload carried alongside the command (e.g. the spreadsheet
/// app stores the affected-cell list here). The stack never inspects it.
/// </typeparam>
public readonly record struct UndoRedoStackEntry<TCommand, TPayload>(
    TCommand Command,
    int Bytes,
    TPayload Payload,
    string Label);

/// <summary>
/// Document-agnostic undo/redo stack engine: paired undo/redo stacks with a
/// depth cap and an estimated-byte budget, redo invalidation on new actions,
/// history labels, and rollback when an apply/revert throws.
/// </summary>
/// <remarks>
/// Extracted from the spreadsheet command bus so any document app (e.g. FreeW)
/// can reuse the same undo/redo mechanics with its own command and payload
/// types. The engine is intentionally unaware of how commands mutate state —
/// the owning bus applies/reverts commands and uses this only for bookkeeping.
/// </remarks>
/// <typeparam name="TCommand">The application command type (a reference type).</typeparam>
/// <typeparam name="TPayload">Opaque per-entry payload; see <see cref="UndoRedoStackEntry{TCommand,TPayload}"/>.</typeparam>
public class UndoRedoStack<TCommand, TPayload>
    where TCommand : class
{
    private readonly int _maxDepth;
    private readonly int _maxBytes;
    private readonly LinkedList<UndoRedoStackEntry<TCommand, TPayload>> _undoStack = new();
    private readonly Stack<UndoRedoStackEntry<TCommand, TPayload>> _redoStack = new();
    private int _undoStackBytes;

    public UndoRedoStack(int maxDepth, int maxBytes)
    {
        _maxDepth = maxDepth;
        _maxBytes = maxBytes;
    }

    /// <summary>Running total of estimated bytes held in the undo stack.</summary>
    public int UndoStackBytes => _undoStackBytes;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Number of commands currently on the undo stack.</summary>
    public int UndoDepth => _undoStack.Count;

    /// <summary>Push a freshly applied command, invalidating the redo history.</summary>
    public void Push(TCommand command, int bytes, TPayload payload, string label)
    {
        PushUndoEntry(new UndoRedoStackEntry<TCommand, TPayload>(command, bytes, payload, label));
        _redoStack.Clear(); // New action invalidates redo history
        TrimUndoStack();
    }

    /// <summary>Re-push an entry (e.g. after a redo) without clearing the redo history.</summary>
    public void PushWithoutClearingRedo(UndoRedoStackEntry<TCommand, TPayload> entry)
    {
        PushUndoEntry(entry);
        TrimUndoStack();
    }

    private void PushUndoEntry(UndoRedoStackEntry<TCommand, TPayload> entry)
    {
        _undoStack.AddLast(entry);
        _undoStackBytes += entry.Bytes;
    }

    private void TrimUndoStack()
    {
        while (_undoStack.Count > _maxDepth || (_undoStack.Count > 0 && _undoStackBytes > _maxBytes))
        {
            var first = _undoStack.First!.Value;
            _undoStack.RemoveFirst();
            _undoStackBytes -= first.Bytes;
        }
    }

    public UndoRedoStackEntry<TCommand, TPayload> PopUndo()
    {
        var entry = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        _undoStackBytes -= entry.Bytes;
        _redoStack.Push(entry);
        return entry;
    }

    public bool TryPeekUndo(out UndoRedoStackEntry<TCommand, TPayload> entry)
    {
        if (_undoStack.Last is { } last)
        {
            entry = last.Value;
            return true;
        }

        entry = default;
        return false;
    }

    public UndoRedoStackEntry<TCommand, TPayload> PopRedo() => _redoStack.Pop();

    public bool TryPeekRedo(out UndoRedoStackEntry<TCommand, TPayload> entry)
    {
        if (_redoStack.Count > 0)
        {
            entry = _redoStack.Peek();
            return true;
        }

        entry = default;
        return false;
    }

    public void PushRedo(UndoRedoStackEntry<TCommand, TPayload> entry) => _redoStack.Push(entry);

    public IReadOnlyList<CommandHistoryEntry> GetUndoHistory(int maxCount)
    {
        var history = new List<CommandHistoryEntry>(Math.Min(maxCount, _undoStack.Count));
        for (var node = _undoStack.Last; node is not null && history.Count < maxCount; node = node.Previous)
            history.Add(new CommandHistoryEntry(node.Value.Label));

        return history;
    }

    public IReadOnlyList<CommandHistoryEntry> GetRedoHistory(int maxCount)
    {
        var history = new List<CommandHistoryEntry>(Math.Min(maxCount, _redoStack.Count));
        foreach (var entry in _redoStack)
        {
            if (history.Count >= maxCount)
                break;

            history.Add(new CommandHistoryEntry(entry.Label));
        }

        return history;
    }

    /// <summary>
    /// Un-does a <see cref="PopUndo"/>: removes the command from the redo stack and puts it
    /// back on top of the undo stack. Call this when reverting a command throws so the undo
    /// chain is not permanently broken.
    /// </summary>
    public void RollbackPopUndo(UndoRedoStackEntry<TCommand, TPayload> entry)
    {
        // PopUndo pushed the command onto the redo stack — reverse that first.
        if (_redoStack.Count > 0 && ReferenceEquals(_redoStack.Peek().Command, entry.Command))
            _redoStack.Pop();

        // Put the command back at the top of the undo stack.
        PushUndoEntry(entry);
    }
}
