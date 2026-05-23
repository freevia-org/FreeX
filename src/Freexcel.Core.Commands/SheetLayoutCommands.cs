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

        _previousHeights = Capture(sheet.RowHeights, _startRow, _endRow);
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
        Restore(sheet.RowHeights, _startRow, _endRow, _previousHeights);
    }

    private static bool IsValidRowRange(uint startRow, uint endRow) =>
        startRow >= 1 && endRow <= CellAddress.MaxRow;

    private static Dictionary<uint, double> Capture(Dictionary<uint, double> source, uint start, uint end)
    {
        var snapshot = new Dictionary<uint, double>();
        for (uint i = start; i <= end; i++)
        {
            if (source.TryGetValue(i, out var value))
                snapshot[i] = value;
        }

        return snapshot;
    }

    private static void Restore(Dictionary<uint, double> target, uint start, uint end, Dictionary<uint, double> snapshot)
    {
        for (uint i = start; i <= end; i++)
            target.Remove(i);

        foreach (var (key, value) in snapshot)
            target[key] = value;
    }
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

        _previousWidths = Capture(sheet.ColumnWidths, _startCol, _endCol);
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
        Restore(sheet.ColumnWidths, _startCol, _endCol, _previousWidths);
    }

    private static bool IsValidColumnRange(uint startCol, uint endCol) =>
        startCol >= 1 && endCol <= CellAddress.MaxCol;

    private static Dictionary<uint, double> Capture(Dictionary<uint, double> source, uint start, uint end)
    {
        var snapshot = new Dictionary<uint, double>();
        for (uint i = start; i <= end; i++)
        {
            if (source.TryGetValue(i, out var value))
                snapshot[i] = value;
        }

        return snapshot;
    }

    private static void Restore(Dictionary<uint, double> target, uint start, uint end, Dictionary<uint, double> snapshot)
    {
        for (uint i = start; i <= end; i++)
            target.Remove(i);

        foreach (var (key, value) in snapshot)
            target[key] = value;
    }
}

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

/// <summary>Sets the worksheet view mode with undo support.</summary>
public sealed class SetWorksheetViewModeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetViewMode _viewMode;
    private WorksheetViewMode? _previousViewMode;

    public string Label => _viewMode switch
    {
        WorksheetViewMode.Normal => "Normal View",
        WorksheetViewMode.PageBreakPreview => "Page Break Preview",
        WorksheetViewMode.PageLayout => "Page Layout View",
        _ => "Set Worksheet View"
    };

    public SetWorksheetViewModeCommand(SheetId sheetId, WorksheetViewMode viewMode)
    {
        _sheetId = sheetId;
        _viewMode = viewMode;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_viewMode))
            return new CommandOutcome(false, "Worksheet view mode is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousViewMode = sheet.ViewMode;
        sheet.ViewMode = _viewMode;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousViewMode is null) return;
        ctx.GetSheet(_sheetId).ViewMode = _previousViewMode.Value;
    }
}

/// <summary>Sets worksheet display options with undo support.</summary>
public sealed class SetWorksheetViewOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _showGridlines;
    private readonly bool _showHeadings;
    private readonly bool _showRulers;
    private bool _previousShowGridlines;
    private bool _previousShowHeadings;
    private bool _previousShowRulers;

    public string Label => "Worksheet View Options";

    public SetWorksheetViewOptionsCommand(SheetId sheetId, bool showGridlines, bool showHeadings, bool showRulers = true)
    {
        _sheetId = sheetId;
        _showGridlines = showGridlines;
        _showHeadings = showHeadings;
        _showRulers = showRulers;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousShowGridlines = sheet.ShowGridlines;
        _previousShowHeadings = sheet.ShowHeadings;
        _previousShowRulers = sheet.ShowRulers;
        sheet.ShowGridlines = _showGridlines;
        sheet.ShowHeadings = _showHeadings;
        sheet.ShowRulers = _showRulers;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ShowGridlines = _previousShowGridlines;
        sheet.ShowHeadings = _previousShowHeadings;
        sheet.ShowRulers = _previousShowRulers;
    }
}

/// <summary>Sets worksheet zoom with undo support.</summary>
public sealed class SetWorksheetZoomCommand : IWorkbookCommand
{
    public const int MinZoomPercent = 10;
    public const int MaxZoomPercent = 400;

    private readonly SheetId _sheetId;
    private readonly int _zoomPercent;
    private int _previousZoomPercent;

    public string Label => "Zoom";

    public SetWorksheetZoomCommand(SheetId sheetId, int zoomPercent)
    {
        _sheetId = sheetId;
        _zoomPercent = zoomPercent;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_zoomPercent is < MinZoomPercent or > MaxZoomPercent)
            return new CommandOutcome(false, "Zoom must be between 10% and 400%.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousZoomPercent = sheet.ZoomPercent;
        sheet.ZoomPercent = _zoomPercent;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ZoomPercent = _previousZoomPercent;
    }
}

/// <summary>Sets whether formulas are displayed in cells instead of calculated values.</summary>
public sealed class SetWorksheetShowFormulasCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _showFormulas;
    private bool _previousShowFormulas;

    public string Label => "Show Formulas";

    public SetWorksheetShowFormulasCommand(SheetId sheetId, bool showFormulas)
    {
        _sheetId = sheetId;
        _showFormulas = showFormulas;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousShowFormulas = sheet.ShowFormulas;
        sheet.ShowFormulas = _showFormulas;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ShowFormulas = _previousShowFormulas;
    }
}
