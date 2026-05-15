using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Set worksheet frozen rows/columns with undo support.</summary>
public sealed class SetFreezePanesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _frozenRows;
    private readonly uint _frozenCols;
    private uint _previousFrozenRows;
    private uint _previousFrozenCols;
    private uint? _previousSplitRow;
    private uint? _previousSplitColumn;

    public string Label => "Freeze Panes";

    public SetFreezePanesCommand(SheetId sheetId, uint frozenRows, uint frozenCols)
    {
        _sheetId = sheetId;
        _frozenRows = frozenRows;
        _frozenCols = frozenCols;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_frozenRows >= CellAddress.MaxRow || _frozenCols >= CellAddress.MaxCol)
            return new CommandOutcome(false, "Freeze pane position is outside the worksheet bounds.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousFrozenRows = sheet.FrozenRows;
        _previousFrozenCols = sheet.FrozenCols;
        _previousSplitRow = sheet.SplitRow;
        _previousSplitColumn = sheet.SplitColumn;
        sheet.FrozenRows = _frozenRows;
        sheet.FrozenCols = _frozenCols;
        if (_frozenRows > 0 || _frozenCols > 0)
        {
            sheet.SplitRow = null;
            sheet.SplitColumn = null;
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.FrozenRows = _previousFrozenRows;
        sheet.FrozenCols = _previousFrozenCols;
        sheet.SplitRow = _previousSplitRow;
        sheet.SplitColumn = _previousSplitColumn;
    }
}

/// <summary>Set worksheet split panes with undo support.</summary>
public sealed class SetSplitPanesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint? _splitRow;
    private readonly uint? _splitColumn;
    private uint? _previousSplitRow;
    private uint? _previousSplitColumn;
    private uint _previousFrozenRows;
    private uint _previousFrozenCols;

    public string Label => "Split";

    public SetSplitPanesCommand(SheetId sheetId, uint? splitRow, uint? splitColumn)
    {
        _sheetId = sheetId;
        _splitRow = splitRow;
        _splitColumn = splitColumn;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_splitRow is <= 1 or > CellAddress.MaxRow ||
            _splitColumn is <= 1 or > CellAddress.MaxCol)
            return new CommandOutcome(false, "Split pane position is outside the worksheet bounds.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousSplitRow = sheet.SplitRow;
        _previousSplitColumn = sheet.SplitColumn;
        _previousFrozenRows = sheet.FrozenRows;
        _previousFrozenCols = sheet.FrozenCols;

        sheet.SplitRow = _splitRow;
        sheet.SplitColumn = _splitColumn;
        sheet.FrozenRows = 0;
        sheet.FrozenCols = 0;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.SplitRow = _previousSplitRow;
        sheet.SplitColumn = _previousSplitColumn;
        sheet.FrozenRows = _previousFrozenRows;
        sheet.FrozenCols = _previousFrozenCols;
    }
}
