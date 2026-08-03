namespace Free.Shared.Commands;

/// <summary>One entry in undo/redo history (label only).</summary>
public sealed record CommandHistoryEntry(string Label);

/// <summary>One entry held on the undo or redo stack.</summary>
/// <typeparam name="TCommand">The application command type.</typeparam>
/// <typeparam name="TPayload">
/// Opaque per-entry payload carried alongside the command (e.g. the spreadsheet
/// app stores the affected-cell list here). The stack never inspects it.
/// </typeparam>
/// <param name="Stamp">
/// A never-reused identity token assigned once, when the entry is first created by
/// <see cref="UndoRedoStack{TCommand,TPayload}.Push"/>. Preserved unchanged across
/// undo/redo round-trips (<see cref="UndoRedoStack{TCommand,TPayload}.PopUndo"/>,
/// <see cref="UndoRedoStack{TCommand,TPayload}.PushWithoutClearingRedo"/>,
/// <see cref="UndoRedoStack{TCommand,TPayload}.RollbackPopUndo"/>) — only a fresh
/// <see cref="UndoRedoStack{TCommand,TPayload}.Push"/> ever mints a new one. See
/// <see cref="UndoRedoStack{TCommand,TPayload}.Version"/> for why this makes the top-of-stack
/// stamp a robust save-point identity check.
/// </param>
public readonly record struct UndoRedoStackEntry<TCommand, TPayload>(
    TCommand Command,
    int Bytes,
    TPayload Payload,
    string Label,
    long Stamp = 0);

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
    private long _nextStamp;

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

    /// <summary>
    /// Content-identity token for the undo stack: the <see cref="UndoRedoStackEntry{TCommand,TPayload}.Stamp"/>
    /// of the entry currently on top of the undo stack, or <c>0</c> when the stack is empty. Every
    /// stamp is minted once, by a fresh <see cref="Push"/>, from a counter that only ever increases and
    /// is never reused — undo/redo round-trips (<see cref="PopUndo"/>, <see cref="PushWithoutClearingRedo"/>,
    /// <see cref="RollbackPopUndo"/>) carry the same entry (and therefore the same stamp) back and forth
    /// without minting a new one.
    /// <para>
    /// R14-undo-redo-depth-1: a save-point identity check needs more than "the stack is the same
    /// <em>size</em> it was at save time" — <see cref="UndoDepth"/> alone (and, for the same reason, a
    /// running push/pop +1/-1 counter, which is exactly self-inverse and therefore re-derivable by any
    /// push/pop sequence of equal net length) cannot tell "the live stack's top entry is literally the
    /// one that was on top at save time" from "a different entry was substituted at the same depth"
    /// (undo past the save point, make a new edit, undo it, then redo it: depth and a net push/pop
    /// counter both return to their saved values even though the entry at that depth is a different
    /// command). Comparing the top entry's own never-reused stamp closes that gap: reaching a given
    /// stamp again requires the literal entry object with that stamp to still be reachable via undo/redo,
    /// and any fresh <see cref="Push"/> at or below that entry's position clears the entire redo stack
    /// (<see cref="Push"/>), permanently discarding it — so a live top-of-stack stamp equal to the saved
    /// one, together with equal <see cref="UndoDepth"/>, proves the whole stack (every entry beneath the
    /// top, not only the top itself) is identical to what it was at save time, including immunity to the
    /// depth-cap trim/refill aliasing <see cref="TrimUndoStack"/> can otherwise cause. Callers that need
    /// to know "is the live undo stack identical to the one recorded at some earlier point" must compare
    /// this token together with <see cref="UndoDepth"/>, not <see cref="UndoDepth"/> alone.
    /// </para>
    /// </summary>
    public long Version => _undoStack.Count == 0 ? 0L : _undoStack.Last!.Value.Stamp;

    /// <summary>Push a freshly applied command, invalidating the redo history.</summary>
    public void Push(TCommand command, int bytes, TPayload payload, string label)
    {
        PushUndoEntry(new UndoRedoStackEntry<TCommand, TPayload>(command, bytes, payload, label, ++_nextStamp));
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
        // The byte-budget check (`_undoStackBytes > _maxBytes`) only evicts an OLDER entry to make
        // room -- it must never evict the sole remaining (most-recently-pushed) entry on its own,
        // or a single command whose own estimate already exceeds the whole budget (e.g. a large
        // Sort/Paste/RemoveSheet -- see IEstimatesMemory implementers) would be silently discarded
        // the instant it is pushed, leaving CanUndo() false right after the user's own action with
        // no warning. `Count > 1` gates that branch so it only ever removes an entry that is NOT the
        // newest. The depth cap (`Count > _maxDepth`) is a separate, unrelated limit and is left free
        // to trim down to the newest entry as before.
        while (_undoStack.Count > _maxDepth || (_undoStack.Count > 1 && _undoStackBytes > _maxBytes))
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
