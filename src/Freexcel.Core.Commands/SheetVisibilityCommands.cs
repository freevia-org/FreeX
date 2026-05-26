using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Hides or unhides rows with undo support.</summary>
public sealed class SetRowsHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly bool _hidden;
    private HashSet<uint>? _previousHiddenRows;

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
            return new CommandOutcome(false, "Row range is outside the worksheet bounds.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = Capture(sheet.HiddenRows, _startRow, _endRow);
        for (uint row = _startRow; row <= _endRow; row++)
        {
            if (_hidden)
                sheet.HiddenRows.Add(row);
            else
                sheet.HiddenRows.Remove(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        Restore(sheet.HiddenRows, _startRow, _endRow, _previousHiddenRows);
    }

    private static HashSet<uint> Capture(HashSet<uint> source, uint start, uint end) =>
        source.Where(i => i >= start && i <= end).ToHashSet();

    private static void Restore(HashSet<uint> target, uint start, uint end, HashSet<uint> snapshot)
    {
        target.RemoveWhere(i => i >= start && i <= end);
        target.UnionWith(snapshot);
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
            return new CommandOutcome(false, "Column range is outside the worksheet bounds.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenCols = Capture(sheet.HiddenCols, _startCol, _endCol);
        for (uint col = _startCol; col <= _endCol; col++)
        {
            if (_hidden)
                sheet.HiddenCols.Add(col);
            else
                sheet.HiddenCols.Remove(col);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenCols is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        Restore(sheet.HiddenCols, _startCol, _endCol, _previousHiddenCols);
    }

    private static HashSet<uint> Capture(HashSet<uint> source, uint start, uint end) =>
        source.Where(i => i >= start && i <= end).ToHashSet();

    private static void Restore(HashSet<uint> target, uint start, uint end, HashSet<uint> snapshot)
    {
        target.RemoveWhere(i => i >= start && i <= end);
        target.UnionWith(snapshot);
    }
}
