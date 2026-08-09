using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Runs multiple workbook commands as one undoable operation.
/// </summary>
public sealed class CompositeWorkbookCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly IReadOnlyList<IWorkbookCommand> _commands;
    private readonly List<IWorkbookCommand> _applied = [];

    public string Label { get; }

    /// <summary>
    /// The child commands this composite runs as one undoable operation. Exposed (read-only) so
    /// callers that need to reason about what kind of edit a composite represents -- e.g.
    /// WorkbookSession.ExecuteReviewCommand's structural-edit clipboard cancellation
    /// (R127B-services-clipboard-structural-cancel-1), which must recognise a multi-area
    /// Insert/Delete Rows/Columns composite the same way it recognises a single-area command --
    /// can inspect the members without this class needing to know about every such caller.
    /// </summary>
    public IReadOnlyList<IWorkbookCommand> Commands => _commands;

    public CompositeWorkbookCommand(string label, IReadOnlyList<IWorkbookCommand> commands)
    {
        Label = label;
        _commands = commands;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// R125-commands-undo-byte-budget: a composite (Text to Columns' per-row EditCellsCommand
    /// list, grouped multi-sheet operations, etc.) retains every one of its successfully-applied
    /// child commands for undo, so its own footprint is the SUM of its children's estimates
    /// rather than a flat per-command constant -- otherwise a composite wrapping hundreds of
    /// large per-row/per-sheet child commands was billed at the flat 200-byte default no matter
    /// how much the children actually retained. Children that don't implement IEstimatesMemory
    /// still fall back to CommandBus's own default via their own missing-interface path, so
    /// mirror that same default (200 bytes) here per non-estimating child rather than counting
    /// them as zero.
    /// </remarks>
    public int EstimatedBytes
    {
        get
        {
            long bytes = 0;
            foreach (var command in _applied)
                bytes += command is IEstimatesMemory mem ? mem.EstimatedBytes : 200;
            return (int)Math.Min(bytes, int.MaxValue);
        }
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _applied.Clear();
        var affectedCells = new List<CellAddress>();
        // Starts true so a composite wrapping zero child commands (e.g. an AutoFit Row
        // Height/Column Width whose sizing planner found nothing to size, R112-composite-
        // empty-noop-1) reports IsNoOp itself, matching "nothing happened". Also stays true
        // when every child that DID run was itself a no-op, so a grouped-sheet composite whose
        // members are each an empty/no-op sub-composite correctly bubbles IsNoOp up instead of
        // reporting a real edit. A single non-no-op child flips this false for the whole
        // composite, same as today.
        var allNoOp = true;

        foreach (var command in _commands)
        {
            CommandOutcome outcome;
            try
            {
                outcome = command.Apply(ctx);
            }
            catch (Exception ex)
            {
                // An inner command threw mid-apply: roll back the sub-commands that
                // already succeeded so the composite stays atomic, then surface a
                // failure outcome rather than leaving the operation half-applied.
                RevertApplied(ctx);
                return new CommandOutcome(false, $"{Label}: {ex.Message}");
            }

            if (!outcome.Success)
            {
                RevertApplied(ctx);
                return outcome;
            }

            _applied.Add(command);
            if (outcome.AffectedCells is not null)
                affectedCells.AddRange(outcome.AffectedCells);
            if (!outcome.IsNoOp)
                allNoOp = false;
        }

        return new CommandOutcome(true, AffectedCells: affectedCells, IsNoOp: allNoOp);
    }

    public void Revert(ICommandContext ctx)
    {
        RevertApplied(ctx);
    }

    private void RevertApplied(ICommandContext ctx)
    {
        try
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                try
                {
                    _applied[i].Revert(ctx);
                }
                catch
                {
                    // Best-effort rollback: a failing sub-command revert must not abort the rest of
                    // the rollback, nor leave _applied populated for a second (double) revert pass.
                }
            }
        }
        finally
        {
            _applied.Clear();
        }
    }
}
