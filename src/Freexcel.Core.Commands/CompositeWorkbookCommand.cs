using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

        foreach (var command in _commands)
        {
            var outcome = command.Apply(ctx);
            if (!outcome.Success)
            {
                RevertApplied(ctx);
                return outcome;
            }

            _applied.Add(command);
            if (outcome.AffectedCells is not null)
                affectedCells.AddRange(outcome.AffectedCells);
        }

        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        RevertApplied(ctx);
    }

    private void RevertApplied(ICommandContext ctx)
    {
        for (var i = _applied.Count - 1; i >= 0; i--)
            _applied[i].Revert(ctx);
        _applied.Clear();
    }
}
