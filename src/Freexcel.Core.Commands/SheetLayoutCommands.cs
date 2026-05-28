using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets or clears explicit row heights with undo support.</summary>
public sealed class SetRowHeightCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly double? _height;
    private Dictionary<uint, double>? _previousHeights;

    public string Label => _height.HasValue ? "Set Row Height" : "AutoFit Row Height";

    public SetRowHeightCommand(SheetId sheetId, uint startRow, uint endRow, double? height)
    {
        _sheetId = sheetId;
        _startRow = Math.Min(startRow, endRow);
        _endRow = Math.Max(startRow, endRow);
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!IsValidRowRange(_startRow, _endRow))
            return new CommandOutcome(false, "Row range is outside the worksheet bounds.");
        if (_height is { } height && (!double.IsFinite(height) || height <= 0))
            return new CommandOutcome(false, "Row height must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _previousHeights = RangeSnapshot.Capture(sheet.RowHeights, _startRow, _endRow);
        for (uint row = _startRow; row <= _endRow; row++)
        {
            if (_height.HasValue)
                sheet.RowHeights[row] = _height.Value;
            else
                sheet.RowHeights.Remove(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHeights is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.RowHeights, _startRow, _endRow, _previousHeights);
    }

    private static bool IsValidRowRange(uint startRow, uint endRow) =>
        startRow >= 1 && endRow <= CellAddress.MaxRow;

}

/// <summary>Sets or clears explicit column widths with undo support.</summary>
public sealed class SetColumnWidthCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _endCol;
    private readonly double? _width;
    private Dictionary<uint, double>? _previousWidths;

    public string Label => _width.HasValue ? "Set Column Width" : "AutoFit Column Width";

    public SetColumnWidthCommand(SheetId sheetId, uint startCol, uint endCol, double? width)
    {
        _sheetId = sheetId;
        _startCol = Math.Min(startCol, endCol);
        _endCol = Math.Max(startCol, endCol);
        _width = width;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!IsValidColumnRange(_startCol, _endCol))
            return new CommandOutcome(false, "Column range is outside the worksheet bounds.");
        if (_width is { } width && (!double.IsFinite(width) || width <= 0))
            return new CommandOutcome(false, "Column width must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _previousWidths = RangeSnapshot.Capture(sheet.ColumnWidths, _startCol, _endCol);
        for (uint col = _startCol; col <= _endCol; col++)
        {
            if (_width.HasValue)
                sheet.ColumnWidths[col] = _width.Value;
            else
                sheet.ColumnWidths.Remove(col);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousWidths is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.ColumnWidths, _startCol, _endCol, _previousWidths);
    }

    private static bool IsValidColumnRange(uint startCol, uint endCol) =>
        startCol >= 1 && endCol <= CellAddress.MaxCol;

}
