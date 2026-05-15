using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Undo-safe command that applies the result of a Goal Seek operation
/// by setting the changing cell to the found value.
/// </summary>
public sealed class GoalSeekCommand : IWorkbookCommand
{
    private readonly CellAddress _changingCell;
    private readonly double _newValue;
    private Cell? _originalCell;

    public string Label => "Goal Seek";

    public GoalSeekCommand(CellAddress changingCell, double newValue)
    {
        _changingCell = changingCell;
        _newValue = newValue;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_changingCell.Sheet);

        // Snapshot original value for undo
        _originalCell = sheet.GetCell(_changingCell)?.Clone();

        sheet.SetCell(_changingCell, new NumberValue(_newValue));

        return new CommandOutcome(true, AffectedCells: [_changingCell]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_changingCell.Sheet);

        if (_originalCell is not null)
            sheet.SetCell(_changingCell, _originalCell.Clone());
        else
            sheet.ClearCell(_changingCell);
    }
}
