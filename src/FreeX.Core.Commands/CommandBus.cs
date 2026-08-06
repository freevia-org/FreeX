using Free.Shared.Commands;
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

    // The undo/redo stack mechanics live in the shared engine; this bus owns the
    // spreadsheet-specific concerns (context creation, apply/revert, affected cells).
    private readonly Dictionary<WorkbookId, WorkbookCommandStack> _stacks = [];
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

        CommandOutcome outcome;
        try
        {
            outcome = command.Apply(ctx);
        }
        catch (Exception ex)
        {
            // Apply threw mid-mutation: attempt a best-effort rollback so the
            // workbook is not left half-edited, and report failure rather than
            // propagating with a dirty model and nothing on the undo stack.
            TryRevert(command, ctx);
            return new CommandOutcome(false, CommandFailureMessages.FormatExceptionFailure("Command failed", ex));
        }

        if (outcome.Success && !outcome.IsNoOp)
        {
            var stack = GetOrCreateStack(workbookId);
            stack.Push(command, EstimateBytes(command), GetAffectedCells(command, outcome), GetHistoryLabel(command));

            // R14-undo-redo-depth-2: a plain Execute is never itself repeatable (only
            // ExecuteRepeatable registers a factory for F4/Repeat Last Action). If an earlier
            // ExecuteRepeatable call left a factory registered for this workbook, this new
            // command is now "the last thing the user did" and that stale factory no longer
            // describes it — leaving it in place would let RepeatLast silently replay the old,
            // unrelated command against whatever is selected now. Clear it so CanRepeat
            // correctly reports nothing pending instead of resurrecting stale state.
            _repeatableCommandFactories.Remove(workbookId);
            NotifyStackChanged(workbookId);
        }

        return outcome;
    }

    public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory)
    {
        var command = commandFactory();
        var outcome = Execute(workbookId, command);
        if (outcome.Success && !outcome.IsNoOp)
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
            return new CommandOutcome(false, CommandFailureMessages.FormatExceptionFailure("Undo failed", ex));
        }

        // R71-services-undo-redo-4-1: once an Undo has run, the primary defense against RepeatLast
        // resurrecting a stale factory is the CanRedo gate in the shells' F4/Repeat-Last entry
        // points (Redo takes priority over Repeat). Clearing the factory here too means that if a
        // caller ever bypasses that gate, or the pending redo is later consumed by a fresh Execute,
        // CanRepeat/RepeatLast correctly report "nothing to repeat" instead of silently replaying a
        // command that no longer describes "the last thing the user did".
        _repeatableCommandFactories.Remove(workbookId);

        NotifyStackChanged(workbookId);
        return new CommandOutcome(
            true,
            // R96-commands-undo-affected-cells-1: prefer the command's LIVE AffectedCells (queried
            // now, after Revert just ran) over the frozen entry.Payload captured at the original
            // forward Apply. For most IAffectedCellsCommand implementations Revert never mutates
            // the backing AffectedCells field, so this is equivalent to the old entry.Payload-first
            // order -- but a command whose Revert relocates cells to a DIFFERENT address than Apply
            // reported (InsertRowsCommand/DeleteRowsCommand/InsertColumnsCommand/DeleteColumnsCommand)
            // updates that field in Revert to the true post-Revert address set, and this order is
            // the only way Undo can observe it (Revert returns void, so there is no fresh outcome to
            // read the way Redo reads Apply's outcome).
            AffectedCells: GetAffectedCells(command) ?? entry.Payload,
            RequiresFullRecalc: command is IWholeWorkbookRecalcCommand,
            // R124-app-drawing-undo-selection-1: Revert just ran, so a deleted drawing object is
            // back in the model -- tell the shells to re-select it (Exists: true) instead of only
            // landing a plain cell-range selection on its anchor. See IDrawingObjectDeletionCommand.
            DrawingObjectSelection: command is IDrawingObjectDeletionCommand deletion
                ? new DrawingObjectSelectionHint(deletion.DrawingObjectKind, deletion.DrawingObjectId, Exists: true)
                : null);
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
            // Apply threw mid-mutation: roll back any partial edit, then restore
            // the entry so the user can retry.
            TryRevert(command, ctx);
            stack.PushRedo(entry); // restore so the user can retry
            return new CommandOutcome(false, CommandFailureMessages.FormatExceptionFailure("Redo failed", ex));
        }

        var affectedCells = outcome.Success
            ? GetAffectedCells(command, outcome) ?? entry.Payload
            : null;

        if (outcome.Success && !outcome.IsNoOp)
            stack.PushWithoutClearingRedo(entry with { Payload = affectedCells });
        else
            stack.PushRedo(entry); // restore so the user can retry

        if (outcome.Success && !outcome.IsNoOp)
            NotifyStackChanged(workbookId);

        return outcome with
        {
            AffectedCells = affectedCells,
            RequiresFullRecalc = command is IWholeWorkbookRecalcCommand,
            // R124-app-drawing-undo-selection-1: Apply just re-ran, so a re-deleted drawing object is
            // gone from the model again -- tell the shells to clear a stale selection still pointing
            // at it (Exists: false), the mirror of the Undo case above. Only set on success: a
            // rejected redo (e.g. protection) leaves the object exactly as it was, so any existing
            // selection referencing it is still valid and must not be cleared.
            DrawingObjectSelection = outcome.Success && command is IDrawingObjectDeletionCommand deletion
                ? new DrawingObjectSelectionHint(deletion.DrawingObjectKind, deletion.DrawingObjectId, Exists: false)
                : null,
        };
    }

    public bool CanUndo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanUndo;

    public bool CanRedo(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) && stack.CanRedo;

    public int GetUndoStackDepth(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) ? stack.UndoDepth : 0;

    public long GetUndoStackVersion(WorkbookId workbookId) =>
        _stacks.TryGetValue(workbookId, out var stack) ? stack.Version : 0;

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

    /// <summary>
    /// R114-commands-workbook-retire-1: drop <paramref name="workbookId"/>'s undo/redo stack and
    /// any pending repeatable-command factory. Without this, a host that keeps one CommandBus
    /// instance alive across File &gt; Open / File &gt; New (rather than replacing the bus itself,
    /// as the "New Window" detach path does) leaks up to <see cref="MaxUndoByteBudget"/> (50 MB)
    /// of undo history per workbook the window ever displayed, for the remaining lifetime of the
    /// process -- <see cref="_stacks"/>/<see cref="_repeatableCommandFactories"/> have no other
    /// eviction path.
    /// </summary>
    public void Retire(WorkbookId workbookId)
    {
        _stacks.Remove(workbookId);
        _repeatableCommandFactories.Remove(workbookId);
    }

    private void RunBeforeMutation(WorkbookId workbookId, ICommandContext context) =>
        _beforeMutation?.Invoke(workbookId, context);

    private static void TryRevert(IWorkbookCommand command, ICommandContext ctx)
    {
        try
        {
            command.Revert(ctx);
        }
        catch
        {
            // Best-effort rollback only; the original failure is already being
            // reported, and a secondary revert failure must not mask it.
        }
    }

    private void NotifyStackChanged(WorkbookId workbookId)
    {
        StackChanged?.Invoke(
            this,
            new CommandStackChangedEventArgs(
                workbookId,
                CanUndo(workbookId),
                CanRedo(workbookId)));
    }

    private WorkbookCommandStack GetOrCreateStack(WorkbookId id)
    {
        if (!_stacks.TryGetValue(id, out var stack))
        {
            stack = new WorkbookCommandStack(MaxUndoDepth, MaxUndoByteBudget);
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

    /// <summary>
    /// The spreadsheet-specialised undo/redo stack: the shared engine keyed to
    /// <see cref="IWorkbookCommand"/> with the affected-cell list as its payload.
    /// </summary>
    private sealed class WorkbookCommandStack(int maxDepth, int maxBytes)
        : UndoRedoStack<IWorkbookCommand, IReadOnlyList<CellAddress>?>(maxDepth, maxBytes);
}
