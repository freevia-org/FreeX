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
                // An inner command threw mid-apply. Roll back in two stages so the composite
                // stays atomic, then surface a failure outcome rather than leaving the
                // operation half-applied.
                //
                // Stage 1 -- the THROWING command itself. It is deliberately absent from
                // _applied (it never returned an outcome), but an IWorkbookCommand that mutates
                // in several steps can already have changed the workbook before it threw:
                // EditCellsCommand, for instance, captures each cell's CellEditCompanionSnapshot
                // into _snapshot and THEN writes that cell, one at a time inside a loop, so a
                // throw on cell K leaves cells 0..K-1 written. Its Revert() replays the
                // snapshots it managed to capture, so it CAN undo a partial apply -- we just
                // have to ask it. Best-effort and wrapped in its own try/catch: a command whose
                // Revert is unhappy about half-applied state must not abort the sibling
                // rollback below, and `ex` (not the revert's own failure) is what the caller
                // needs to see in the returned outcome.
                try
                {
                    command.Revert(ctx);
                }
                catch
                {
                    // Best-effort: nothing more can be done for this command, and the original
                    // failure is the one worth reporting.
                }

                // Stage 2 -- the siblings that fully succeeded before it.
                RevertApplied(ctx);
                return new CommandOutcome(false, $"{Label}: {ex.Message}");
            }

            // R175-commands-composite-failure-outcome-audit-1: note the deliberate asymmetry with
            // the catch above -- the failing command is NOT reverted here, and must not be.
            //
            // A command that THREW is mid-flight by definition: it entered Apply and never
            // returned, so asking it to roll back is always the right call (CommandBus.Execute
            // makes the same call via TryRevert on its own throw path). A command that RETURNED a
            // failure outcome is a different animal: the overwhelmingly common case is a clean
            // up-front rejection (protection guards, "target no longer exists", invalid input)
            // that never touched the workbook, and a Revert on that path is actively destructive
            // for any command that snapshots INSIDE Apply after its guards. SetCalculationMode-
            // Command is the worked example: it returns failure on an undefined mode BEFORE
            // assigning _previousMode, so _previousMode still holds default(WorkbookCalculation-
            // Mode) == Automatic -- reverting it would silently flip a Manual workbook to
            // Automatic, corrupting a setting the command never wrote. 74 of the 236 Revert
            // implementations audited have no never-applied guard and would misbehave the same way.
            //
            // Nor is there any signal here to tell "failed after mutating" from "rejected up
            // front". An audit of the failure returns in FreeX.Core.Commands found no command that
            // mutates the workbook and then returns a failure outcome -- the convention is
            // validate-then-mutate -- so the correct place to hold this line is that convention,
            // not a blanket rollback that would break the well-behaved majority. If a
            // mutate-then-fail command is ever introduced, give it the guard its Revert needs and
            // have it throw (or revert itself) rather than reverting from here.
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
