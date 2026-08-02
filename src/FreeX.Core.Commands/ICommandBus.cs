using Free.Shared.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Every mutation to the workbook goes through this bus as a command.
/// This enables undo/redo and future collaboration/AI action replay.
/// </summary>
public interface ICommandBus
{
    /// <summary>Execute a command and push it onto the undo stack.</summary>
    CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command);

    /// <summary>Execute a command that can be repeated with F4-style semantics.</summary>
    CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory);

    /// <summary>Undo the last command.</summary>
    CommandOutcome Undo(WorkbookId workbookId);

    /// <summary>Redo a previously undone command.</summary>
    CommandOutcome Redo(WorkbookId workbookId);

    /// <summary>Check if undo is available.</summary>
    bool CanUndo(WorkbookId workbookId);

    /// <summary>Check if redo is available.</summary>
    bool CanRedo(WorkbookId workbookId);

    /// <summary>Repeat the last repeatable command.</summary>
    CommandOutcome RepeatLast(WorkbookId workbookId);

    /// <summary>Check if a repeatable command is available.</summary>
    bool CanRepeat(WorkbookId workbookId);

    /// <summary>
    /// Returns the current depth of the undo stack (number of commands that can be undone).
    /// Used by <c>WorkbookDocumentState</c> to track the save-point depth so that undo/redo
    /// can restore the clean state when the stack returns to the saved depth.
    /// Returns 0 when there is nothing to undo or the workbook has no stack yet.
    /// </summary>
    int GetUndoStackDepth(WorkbookId workbookId);

    /// <summary>
    /// Returns the current monotonic version token of the undo stack (see
    /// <see cref="Free.Shared.Commands.UndoRedoStack{TCommand,TPayload}.Version"/>): it advances on
    /// every push AND every silent eviction caused by the depth/byte cap, so unlike
    /// <see cref="GetUndoStackDepth"/> it can never alias across a trim. Used by
    /// <c>WorkbookDocumentState</c> as the robust save-point identity check — two observations
    /// with equal depth but a different version are guaranteed to no longer represent the same
    /// undo-stack contents. Returns 0 when the workbook has no stack yet.
    /// </summary>
    /// <remarks>
    /// Has a default implementation returning 0 so pre-existing test fakes that implement
    /// <see cref="ICommandBus"/> without a real backing stack continue to compile unchanged.
    /// <see cref="CommandBus"/> (the only production implementation) overrides it with the real
    /// value from its underlying <c>UndoRedoStack</c>.
    /// </remarks>
    long GetUndoStackVersion(WorkbookId workbookId) => 0;

    /// <summary>
    /// Releases <paramref name="workbookId"/>'s undo/redo stack and any pending repeatable-command
    /// factory. A bus that keys its stacks by <see cref="WorkbookId"/> forever (e.g.
    /// <see cref="CommandBus"/>'s app-lifetime instance in a host that reuses one bus across
    /// File &gt; Open / File &gt; New) would otherwise leak up to the full undo byte budget per
    /// workbook that was ever loaded into the window, for the life of the process. Call this right
    /// before dropping the last reference to a replaced/closed workbook -- mirroring
    /// <c>RecalcEngine.RetireWorkbook</c> -- and only when no other window still shares that
    /// workbook instance.
    /// </summary>
    /// <remarks>
    /// Has a default no-op implementation so pre-existing test fakes that implement
    /// <see cref="ICommandBus"/> without a real backing stack continue to compile unchanged.
    /// <see cref="CommandBus"/> (the only production implementation) overrides it to actually
    /// evict the workbook's entries.
    /// </remarks>
    void Retire(WorkbookId workbookId) { }
}

public interface ICommandStackChangeNotifier
{
    event EventHandler<CommandStackChangedEventArgs>? StackChanged;
}

public interface ICommandHistoryProvider
{
    IReadOnlyList<CommandHistoryEntry> GetUndoHistory(WorkbookId workbookId, int maxCount);

    IReadOnlyList<CommandHistoryEntry> GetRedoHistory(WorkbookId workbookId, int maxCount);
}

public sealed class CommandStackChangedEventArgs : EventArgs
{
    public CommandStackChangedEventArgs(WorkbookId workbookId, bool canUndo, bool canRedo)
    {
        WorkbookId = workbookId;
        CanUndo = canUndo;
        CanRedo = canRedo;
    }

    public WorkbookId WorkbookId { get; }

    public bool CanUndo { get; }

    public bool CanRedo { get; }
}

/// <summary>A command that can be applied and reverted on a workbook.</summary>
public interface IWorkbookCommand
{
    /// <summary>Human-readable label for undo/redo UI.</summary>
    string Label { get; }

    /// <summary>Apply the command, returning a snapshot for undo.</summary>
    CommandOutcome Apply(ICommandContext ctx);

    /// <summary>Revert the command using the saved snapshot.</summary>
    void Revert(ICommandContext ctx);
}

public interface IAffectedCellsCommand
{
    IReadOnlyList<CellAddress> AffectedCells { get; }
}

/// <summary>
/// Marker for commands whose Apply/Revert can change which sheets fall inside a 3-D span
/// reference (e.g. <c>=SUM(Sheet1:Sheet3!A1)</c>) purely by adding, removing, moving, or
/// duplicating a sheet -- not by editing any cell of their own, so they report no
/// <see cref="IAffectedCellsCommand.AffectedCells"/>. On the forward Execute path this gap is
/// covered by explicit <c>RecalculateWorkbook()</c> calls in <c>WorkbookSession</c>
/// (Add/Delete/Move/Duplicate/Rename Sheet), but <see cref="ICommandBus.Undo"/>/
/// <see cref="ICommandBus.Redo"/> call straight into the command bus and never reach those
/// wrapper methods. <see cref="CommandBus"/> uses this marker to flag
/// <see cref="CommandOutcome.RequiresFullRecalc"/> on Undo/Redo so
/// <c>WorkbookCellEditService.ApplyHistoryOutcome</c> can force a full recalculation instead of
/// relying on the (empty) affected-cells list, which would otherwise leave 3-D span aggregates
/// stale until the next F9.
/// </summary>
public interface IWholeWorkbookRecalcCommand
{
}

/// <summary>
/// Optional interface a command can implement to report its estimated
/// in-memory snapshot size.  Used by <see cref="CommandBus"/> to enforce
/// a byte-budget cap on the undo stack in addition to the count cap.
/// Commands that do <em>not</em> implement this interface are assigned a
/// default estimate of 200 bytes.
/// </summary>
public interface IEstimatesMemory
{
    /// <summary>Estimated snapshot size in bytes.</summary>
    int EstimatedBytes { get; }
}

/// <summary>Context provided to commands for accessing workbook state.</summary>
public interface ICommandContext
{
    Workbook Workbook { get; }
    Sheet GetSheet(SheetId sheetId);
}

/// <summary>Result of executing a command.</summary>
/// <param name="RequiresFullRecalc">
/// Set by <see cref="CommandBus"/>'s Undo/Redo (never by a command's own Apply/Revert) when the
/// undone/redone command implements <see cref="IWholeWorkbookRecalcCommand"/> -- see that
/// interface for why Undo/Redo, unlike the forward Execute path, has no other way to trigger the
/// full recalculation these structural sheet operations require.
/// </param>
public sealed record CommandOutcome(
    bool Success,
    string? ErrorMessage = null,
    IReadOnlyList<CellAddress>? AffectedCells = null,
    bool IsNoOp = false,
    bool RequiresFullRecalc = false);
