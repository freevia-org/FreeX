using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Undo-safe command that applies the result of a Goal Seek operation
/// by setting the changing cell to the found value.
/// </summary>
public sealed class GoalSeekCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly CellAddress _changingCell;
    private readonly double _newValue;
    private Cell? _originalCell;
    private TableHeaderRenameTransaction? _headerRename;
    public int EstimatedBytes => (int)Math.Min(int.MaxValue, 300L + (_headerRename?.EstimatedBytes ?? 0));

    public string Label => "Goal Seek";

    public GoalSeekCommand(CellAddress changingCell, double newValue)
    {
        _changingCell = changingCell;
        _newValue = newValue;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_newValue))
            return new CommandOutcome(false, ErrorMessage: "Goal Seek result must be a finite number.");

        var sheet = ctx.GetSheet(_changingCell.Sheet);
        if (sheet is null)
            return new CommandOutcome(false, ErrorMessage: "Sheet no longer exists.");

        if (sheet.IsProtected && !CommandGuards.CanEditCell(ctx.Workbook, sheet, _changingCell))
            return CommandGuards.RejectSheetProtected();

        // r232: Goal Seek writes the value its solve arrived at, and a solve that starts from a
        // cell already holding the answer arrives back at it -- re-running Goal Seek with the same
        // target, or seeking a goal the sheet already satisfies. Compared against the cell's current
        // NUMBER rather than its cell object, because that is the whole of what Apply writes.
        if (sheet.GetValue(_changingCell.Row, _changingCell.Col) is NumberValue current
            && current.Value.Equals(_newValue))
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

        _originalCell = sheet.GetCell(_changingCell)?.Clone();
        _headerRename = TableHeaderRenameTransaction.Capture(ctx, [_changingCell]);
        sheet.SetCell(_changingCell, new NumberValue(_newValue));
        try { _headerRename.Apply(ctx); }
        catch (InvalidOperationException ex) { Revert(ctx); return new CommandOutcome(false, ex.Message); }
        return new CommandOutcome(true, AffectedCells: new[] { _changingCell }.Concat(_headerRename.Affected).Distinct().ToArray());
    }

    public void Revert(ICommandContext ctx)
    {
        _headerRename?.Revert();
        var sheet = ctx.GetSheet(_changingCell.Sheet);
        if (sheet is null) return;

        if (_originalCell is not null)
            sheet.SetCell(_changingCell, _originalCell.Clone());
        else
            sheet.ClearCell(_changingCell);
    }
}
