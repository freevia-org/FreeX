using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Runs multiple workbook commands as one undoable operation.
/// </summary>
public sealed class CompositeWorkbookCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<IWorkbookCommand> _commands;
    private readonly List<IWorkbookCommand> _applied = [];

    public string Label { get; }

    public CompositeWorkbookCommand(string label, IReadOnlyList<IWorkbookCommand> commands)
    {
        Label = label;
        _commands = commands;
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
