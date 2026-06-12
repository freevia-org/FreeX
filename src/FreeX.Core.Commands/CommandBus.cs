using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// In-memory implementation of the command bus with undo/redo stacks.
/// </summary>
public sealed class CommandBus : ICommandBus, ICommandStackChangeNotifier, ICommandHistoryProvider
{
    private const int MaxUndoDepth = 100;
    private const int MaxUndoByteBudget = 52_428_800; // 50 MB
    private const int DefaultCommandBytes = 200;

    private readonly Dictionary<WorkbookId, CommandStack> _stacks = [];
    private readonly Dictionary<WorkbookId, Func<IWorkbookCommand>> _repeatableCommandFactories = [];
    private readonly Func<WorkbookId, ICommandContext> _contextFactory;
    private readonly Action<WorkbookId, ICommandContext>? _beforeMutation;

    public CommandBus(
        Func<WorkbookId, ICommandContext> contextFactory,
        Action<WorkbookId, ICommandContext>? beforeMutation = null)
    {
        _contextFactory = contextFactory;
        _beforeMutation = beforeMutation;
    }

    public event EventHandler<CommandStackChangedEventArgs>? StackChanged;

    public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command)
    {
        var ctx = _contextFactory(workbookId);
        RunBeforeMutation(workbookId, ctx);
        var outcome = command.Apply(ctx);

        if (outcome.Success)
        {
            var stack = GetOrCreateStack(workbookId);
            stack.Push(command, EstimateBytes(command), GetAffectedCells(command, outcome));
            NotifyStackChanged(workbookId);
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
        var entry = stack.PopUndo();
        var command = entry.Command;
        try
        {
            RunBeforeMutation(workbookId, ctx);
            command.Revert(ctx);
        }
        catch (Exception ex)
        {
            stack.RollbackPopUndo(entry); // restore the command so the undo chain is intact
            return new CommandOutcome(false, $"Undo failed: {ex.Message}");
        }

        NotifyStackChanged(workbookId);
        return new CommandOutcome(true, AffectedCells: entry.AffectedCells ?? GetAffectedCells(command));
    }

    public CommandOutcome Redo(WorkbookId workbookId)
    {
        var stack = GetOrCreateStack(workbookId);
        if (!stack.CanRedo)
            return new CommandOutcome(false, "Nothing to redo");

        var ctx = _contextFactory(workbookId);
        var entry = stack.PopRedo();
        var command = entry.Command;
        CommandOutcome outcome;
        try
        {
            RunBeforeMutation(workbookId, ctx);
            outcome = command.Apply(ctx);
        }
        catch (Exception ex)
        {
            stack.PushRedo(entry); // restore so the user can retry
            return new CommandOutcome(false, $"Redo failed: {ex.Message}");
        }

        var affectedCells = outcome.Success
            ? GetAffectedCells(command, outcome) ?? entry.AffectedCells
            : null;

        if (outcome.Success)
            stack.PushWithoutClearingRedo(entry with { AffectedCells = affectedCells });
        else
            stack.PushRedo(entry); // restore so the user can retry

        if (outcome.Success)
            NotifyStackChanged(workbookId);

        return outcome with { AffectedCells = affectedCells };
    }

    public bool CanUndo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanUndo;

    public bool CanRedo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanRedo;

    public int GetUndoStackDepth(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) ? stack.UndoDepth : 0;

    public IReadOnlyList<CommandHistoryEntry> GetUndoHistory(WorkbookId workbookId, int maxCount) =>
        maxCount <= 0 || !_stacks.TryGetValue(workbookId, out var stack)
            ? []
            : stack.GetUndoHistory(maxCount);

    public IReadOnlyList<CommandHistoryEntry> GetRedoHistory(WorkbookId workbookId, int maxCount) =>
        maxCount <= 0 || !_stacks.TryGetValue(workbookId, out var stack)
            ? []
            : stack.GetRedoHistory(maxCount);

    public CommandOutcome RepeatLast(WorkbookId workbookId)
    {
        if (!_repeatableCommandFactories.TryGetValue(workbookId, out var commandFactory))
            return new CommandOutcome(false, "Nothing to repeat");

        return ExecuteRepeatable(workbookId, commandFactory);
    }

    public bool CanRepeat(WorkbookId workbookId) =>
        _repeatableCommandFactories.ContainsKey(workbookId);

    private void RunBeforeMutation(WorkbookId workbookId, ICommandContext context) =>
        _beforeMutation?.Invoke(workbookId, context);

    private void NotifyStackChanged(WorkbookId workbookId)
    {
        StackChanged?.Invoke(
            this,
            new CommandStackChangedEventArgs(
                workbookId,
                CanUndo(workbookId),
                CanRedo(workbookId)));
    }

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

    private static IReadOnlyList<CellAddress>? GetAffectedCells(IWorkbookCommand command, CommandOutcome outcome) =>
        outcome.AffectedCells ?? GetAffectedCells(command);

    private static int EstimateBytes(IWorkbookCommand command) =>
        command is IEstimatesMemory mem ? mem.EstimatedBytes : DefaultCommandBytes;

    private static string GetHistoryLabel(IWorkbookCommand command) =>
        string.IsNullOrWhiteSpace(command.Label)
            ? command.GetType().Name
            : command.Label.Trim();

    private readonly record struct CommandStackEntry(
        IWorkbookCommand Command,
        int Bytes,
        IReadOnlyList<CellAddress>? AffectedCells,
        string Label);

    private sealed class CommandStack
    {
        private readonly int _maxDepth;
        private readonly int _maxBytes;
        private readonly LinkedList<CommandStackEntry> _undoStack = new();
        private readonly Stack<CommandStackEntry> _redoStack = new();
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

        /// <summary>Number of commands currently on the undo stack.</summary>
        public int UndoDepth => _undoStack.Count;

        public void Push(
            IWorkbookCommand command,
            int bytes,
            IReadOnlyList<CellAddress>? affectedCells)
        {
            PushUndoEntry(new CommandStackEntry(command, bytes, affectedCells, GetHistoryLabel(command)));
            _redoStack.Clear(); // New action invalidates redo history

            TrimUndoStack();
        }

        public void PushWithoutClearingRedo(CommandStackEntry entry)
        {
            PushUndoEntry(entry);
            TrimUndoStack();
        }

        private void PushUndoEntry(CommandStackEntry entry)
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

        public CommandStackEntry PopUndo()
        {
            var entry = _undoStack.Last!.Value;
            _undoStack.RemoveLast();
            _undoStackBytes -= entry.Bytes;
            _redoStack.Push(entry);
            return entry;
        }

        public CommandStackEntry PopRedo()
        {
            return _redoStack.Pop();
        }

        public void PushRedo(CommandStackEntry entry)
        {
            _redoStack.Push(entry);
        }

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
        /// back on top of the undo stack.  Call this when <see cref="IWorkbookCommand.Revert"/>
        /// throws so the undo chain is not permanently broken.
        /// </summary>
        public void RollbackPopUndo(CommandStackEntry entry)
        {
            // PopUndo pushed the command onto the redo stack — reverse that first.
            if (_redoStack.Count > 0 && ReferenceEquals(_redoStack.Peek().Command, entry.Command))
                _redoStack.Pop();

            // Put the command back at the top of the undo stack.
            PushUndoEntry(entry);
        }
    }
}
