using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// In-memory implementation of the command bus with undo/redo stacks.
/// </summary>
public sealed class CommandBus : ICommandBus
{
    private const int MaxUndoDepth = 100;
    private const int MaxUndoByteBudget = 52_428_800; // 50 MB
    private const int DefaultCommandBytes = 200;

    private readonly Dictionary<WorkbookId, CommandStack> _stacks = [];
    private readonly Dictionary<WorkbookId, Func<IWorkbookCommand>> _repeatableCommandFactories = [];
    private readonly Func<WorkbookId, ICommandContext> _contextFactory;

    public CommandBus(Func<WorkbookId, ICommandContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command)
    {
        var ctx = _contextFactory(workbookId);
        var outcome = command.Apply(ctx);

        if (outcome.Success)
        {
            var stack = GetOrCreateStack(workbookId);
            stack.Push(command, EstimateBytes(command));
        }

        return outcome;
    }

    public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory)
    {
        var command = commandFactory();
        var outcome = Execute(workbookId, command);
        if (outcome.Success)
            _repeatableCommandFactories[workbookId] = commandFactory;

        return outcome;
    }

    public CommandOutcome Undo(WorkbookId workbookId)
    {
        var stack = GetOrCreateStack(workbookId);
        if (!stack.CanUndo)
            return new CommandOutcome(false, "Nothing to undo");

        var ctx = _contextFactory(workbookId);
        var command = stack.PopUndo();
        try
        {
            command.Revert(ctx);
        }
        catch (Exception ex)
        {
            stack.RollbackPopUndo(command); // restore the command so the undo chain is intact
            return new CommandOutcome(false, $"Undo failed: {ex.Message}");
        }

        return new CommandOutcome(true, AffectedCells: GetAffectedCells(command));
    }

    public CommandOutcome Redo(WorkbookId workbookId)
    {
        var stack = GetOrCreateStack(workbookId);
        if (!stack.CanRedo)
            return new CommandOutcome(false, "Nothing to redo");

        var ctx = _contextFactory(workbookId);
        var command = stack.PopRedo();
        CommandOutcome outcome;
        try
        {
            outcome = command.Apply(ctx);
        }
        catch (Exception ex)
        {
            stack.PushRedo(command); // restore so the user can retry
            return new CommandOutcome(false, $"Redo failed: {ex.Message}");
        }

        if (outcome.Success)
            stack.PushWithoutClearingRedo(command, EstimateBytes(command));
        else
            stack.PushRedo(command); // restore so the user can retry

        return outcome with { AffectedCells = outcome.AffectedCells ?? GetAffectedCells(command) };
    }

    public bool CanUndo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanUndo;

    public bool CanRedo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanRedo;

    public CommandOutcome RepeatLast(WorkbookId workbookId)
    {
        if (!_repeatableCommandFactories.TryGetValue(workbookId, out var commandFactory))
            return new CommandOutcome(false, "Nothing to repeat");

        return ExecuteRepeatable(workbookId, commandFactory);
    }

    public bool CanRepeat(WorkbookId workbookId) =>
        _repeatableCommandFactories.ContainsKey(workbookId);

    private CommandStack GetOrCreateStack(WorkbookId id)
    {
        if (!_stacks.TryGetValue(id, out var stack))
        {
            stack = new CommandStack(MaxUndoDepth, MaxUndoByteBudget);
            _stacks[id] = stack;
        }
        return stack;
    }

    private static IReadOnlyList<CellAddress>? GetAffectedCells(IWorkbookCommand command) =>
        command is IAffectedCellsCommand affectedCellsCommand
            ? affectedCellsCommand.AffectedCells
            : null;

    private static int EstimateBytes(IWorkbookCommand command) =>
        command is IEstimatesMemory mem ? mem.EstimatedBytes : DefaultCommandBytes;

    private sealed class CommandStack
    {
        private readonly int _maxDepth;
        private readonly int _maxBytes;
        private readonly LinkedList<(IWorkbookCommand Command, int Bytes)> _undoStack = new();
        private readonly Stack<IWorkbookCommand> _redoStack = new();
        private int _undoStackBytes;

        /// <summary>Running total of estimated bytes held in the undo stack.</summary>
        public int UndoStackBytes => _undoStackBytes;

        public CommandStack(int maxDepth, int maxBytes)
        {
            _maxDepth = maxDepth;
            _maxBytes = maxBytes;
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Push(IWorkbookCommand command, int bytes)
        {
            _undoStack.AddLast((command, bytes));
            _undoStackBytes += bytes;
            _redoStack.Clear(); // New action invalidates redo history

            TrimUndoStack();
        }

        public void PushWithoutClearingRedo(IWorkbookCommand command, int bytes)
        {
            _undoStack.AddLast((command, bytes));
            _undoStackBytes += bytes;
            TrimUndoStack();
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

        public IWorkbookCommand PopUndo()
        {
            var entry = _undoStack.Last!.Value;
            _undoStack.RemoveLast();
            _undoStackBytes -= entry.Bytes;
            _redoStack.Push(entry.Command);
            return entry.Command;
        }

        public IWorkbookCommand PopRedo()
        {
            return _redoStack.Pop();
        }

        public void PushRedo(IWorkbookCommand command)
        {
            _redoStack.Push(command);
        }

        /// <summary>
        /// Un-does a <see cref="PopUndo"/>: removes the command from the redo stack and puts it
        /// back on top of the undo stack.  Call this when <see cref="IWorkbookCommand.Revert"/>
        /// throws so the undo chain is not permanently broken.
        /// </summary>
        public void RollbackPopUndo(IWorkbookCommand command)
        {
            // PopUndo pushed the command onto the redo stack — reverse that first.
            if (_redoStack.Count > 0 && ReferenceEquals(_redoStack.Peek(), command))
                _redoStack.Pop();

            // Put the command back at the top of the undo stack.
            // Use the same byte estimate that was subtracted during PopUndo.
            var bytes = command is IEstimatesMemory mem ? mem.EstimatedBytes : DefaultCommandBytes;
            _undoStack.AddLast((command, bytes));
            _undoStackBytes += bytes;
        }
    }
}
