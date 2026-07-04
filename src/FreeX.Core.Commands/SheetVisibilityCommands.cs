using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Hides or unhides rows with undo support.</summary>
public sealed class SetRowsHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly bool _hidden;
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousGroupHiddenRows;

    public string Label => _hidden ? "Hide Rows" : "Unhide Rows";

    public SetRowsHiddenCommand(SheetId sheetId, uint startRow, uint endRow, bool hidden)
    {
        _sheetId = sheetId;
        _startRow = Math.Min(startRow, endRow);
        _endRow = Math.Max(startRow, endRow);
        _hidden = hidden;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_startRow < 1 || _endRow > CellAddress.MaxRow)
            return CommandGuards.RejectRowRangeOutsideWorksheetBounds();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = RangeSnapshot.Capture(sheet.HiddenRows, _startRow, _endRow);
        for (uint row = _startRow; row <= _endRow; row++)
        {
            if (_hidden)
                sheet.HiddenRows.Add(row);
            else
                sheet.HiddenRows.Remove(row);
        }

        // Excel's Unhide Rows reveals rows hidden by any mechanism, including a collapsed outline
        // group — it doesn't just undo the plain Hide Rows command. Clear GroupHiddenRows for the
        // selection too so IsRowEffectivelyHidden actually flips to visible (see GroupRowsCommand /
        // ClearWorksheetOutlineCommand, which own the same flag for the group-collapse mechanism).
        if (!_hidden)
        {
            _previousGroupHiddenRows = RangeSnapshot.Capture(sheet.GroupHiddenRows, _startRow, _endRow);
            sheet.GroupHiddenRows.RemoveWhere(row => row >= _startRow && row <= _endRow);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.HiddenRows, _startRow, _endRow, _previousHiddenRows);
        if (_previousGroupHiddenRows is not null)
            RangeSnapshot.Restore(sheet.GroupHiddenRows, _startRow, _endRow, _previousGroupHiddenRows);
    }

}

/// <summary>Hides or unhides columns with undo support.</summary>
public sealed class SetColumnsHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _endCol;
    private readonly bool _hidden;
    private HashSet<uint>? _previousHiddenCols;
    private HashSet<uint>? _previousGroupHiddenCols;

    public string Label => _hidden ? "Hide Columns" : "Unhide Columns";

    public SetColumnsHiddenCommand(SheetId sheetId, uint startCol, uint endCol, bool hidden)
    {
        _sheetId = sheetId;
        _startCol = Math.Min(startCol, endCol);
        _endCol = Math.Max(startCol, endCol);
        _hidden = hidden;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_startCol < 1 || _endCol > CellAddress.MaxCol)
            return CommandGuards.RejectColumnRangeOutsideWorksheetBounds();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenCols = RangeSnapshot.Capture(sheet.HiddenCols, _startCol, _endCol);
        for (uint col = _startCol; col <= _endCol; col++)
        {
            if (_hidden)
                sheet.HiddenCols.Add(col);
            else
                sheet.HiddenCols.Remove(col);
        }

        // Mirrors SetRowsHiddenCommand: Excel's Unhide Columns also reveals columns hidden by a
        // collapsed outline group, not just ones hidden by the plain Hide Columns command.
        if (!_hidden)
        {
            _previousGroupHiddenCols = RangeSnapshot.Capture(sheet.GroupHiddenCols, _startCol, _endCol);
            sheet.GroupHiddenCols.RemoveWhere(col => col >= _startCol && col <= _endCol);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenCols is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.HiddenCols, _startCol, _endCol, _previousHiddenCols);
        if (_previousGroupHiddenCols is not null)
            RangeSnapshot.Restore(sheet.GroupHiddenCols, _startCol, _endCol, _previousGroupHiddenCols);
    }

}
