using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// In-memory implementation of the command bus with undo/redo stacks.
/// </summary>
public sealed class CommandBus : ICommandBus
{
    private const int MaxUndoDepth = 100;

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
            stack.Push(command);
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
        command.Revert(ctx);

        return new CommandOutcome(true, AffectedCells: GetAffectedCells(command));
    }

    public CommandOutcome Redo(WorkbookId workbookId)
    {
        var stack = GetOrCreateStack(workbookId);
        if (!stack.CanRedo)
            return new CommandOutcome(false, "Nothing to redo");

        var ctx = _contextFactory(workbookId);
        var command = stack.PopRedo();
        var outcome = command.Apply(ctx);

        if (outcome.Success)
            stack.PushWithoutClearingRedo(command);
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
            stack = new CommandStack(MaxUndoDepth);
            _stacks[id] = stack;
        }
        return stack;
    }

    private static IReadOnlyList<CellAddress>? GetAffectedCells(IWorkbookCommand command) =>
        command is IAffectedCellsCommand affectedCellsCommand
            ? affectedCellsCommand.AffectedCells
            : null;

    private sealed class CommandStack
    {
        private readonly int _maxDepth;
        private readonly LinkedList<IWorkbookCommand> _undoStack = new();
        private readonly Stack<IWorkbookCommand> _redoStack = new();

        public CommandStack(int maxDepth) => _maxDepth = maxDepth;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Push(IWorkbookCommand command)
        {
            _undoStack.AddLast(command);
            _redoStack.Clear(); // New action invalidates redo history

            while (_undoStack.Count > _maxDepth)
                _undoStack.RemoveFirst();
        }

        public void PushWithoutClearingRedo(IWorkbookCommand command)
        {
            _undoStack.AddLast(command);
            while (_undoStack.Count > _maxDepth)
                _undoStack.RemoveFirst();
        }

        public IWorkbookCommand PopUndo()
        {
            var command = _undoStack.Last!.Value;
            _undoStack.RemoveLast();
            _redoStack.Push(command);
            return command;
        }

        public IWorkbookCommand PopRedo()
        {
            return _redoStack.Pop();
        }

        public void PushRedo(IWorkbookCommand command)
        {
            _redoStack.Push(command);
        }
    }
}
