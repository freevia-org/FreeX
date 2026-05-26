using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets rows/columns repeated on each printed page.</summary>
public sealed class SetPrintTitlesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetRepeatRange? _rows;
    private readonly WorksheetRepeatRange? _columns;
    private WorksheetRepeatRange? _previousRows;
    private WorksheetRepeatRange? _previousColumns;

    public string Label => "Print Titles";

    public SetPrintTitlesCommand(SheetId sheetId, WorksheetRepeatRange? rows, WorksheetRepeatRange? columns)
    {
        _sheetId = sheetId;
        _rows = rows;
        _columns = columns;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_rows is { Start: 0 } or { End: 0 } || _columns is { Start: 0 } or { End: 0 })
            return new CommandOutcome(false, "Print title rows and columns must be 1-based.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousRows = sheet.PrintTitleRows;
        _previousColumns = sheet.PrintTitleColumns;
        sheet.PrintTitleRows = _rows;
        sheet.PrintTitleColumns = _columns;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PrintTitleRows = _previousRows;
        sheet.PrintTitleColumns = _previousColumns;
    }
}

/// <summary>Replaces worksheet manual page breaks with undo support.</summary>
public sealed class SetPageBreaksCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyCollection<uint> _rowBreaks;
    private readonly IReadOnlyCollection<uint> _columnBreaks;
    private List<uint>? _previousRowBreaks;
    private List<uint>? _previousColumnBreaks;

    public string Label => "Page Breaks";

    public SetPageBreaksCommand(SheetId sheetId, IReadOnlyCollection<uint> rowBreaks, IReadOnlyCollection<uint> columnBreaks)
    {
        _sheetId = sheetId;
        _rowBreaks = rowBreaks;
        _columnBreaks = columnBreaks;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_rowBreaks.Any(b => b < 2 || b > CellAddress.MaxRow) ||
            _columnBreaks.Any(b => b < 2 || b > CellAddress.MaxCol))
        {
            return new CommandOutcome(false, "Page breaks must be inside the worksheet and after the first row/column.");
        }

        var sheet = ctx.GetSheet(_sheetId);
        _previousRowBreaks = sheet.RowPageBreaks.ToList();
        _previousColumnBreaks = sheet.ColumnPageBreaks.ToList();
        sheet.RowPageBreaks.Clear();
        sheet.ColumnPageBreaks.Clear();
        foreach (var rowBreak in _rowBreaks.Order())
            sheet.RowPageBreaks.Add(rowBreak);
        foreach (var columnBreak in _columnBreaks.Order())
            sheet.ColumnPageBreaks.Add(columnBreak);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.RowPageBreaks.Clear();
        sheet.ColumnPageBreaks.Clear();
        if (_previousRowBreaks is not null)
            foreach (var rowBreak in _previousRowBreaks)
                sheet.RowPageBreaks.Add(rowBreak);
        if (_previousColumnBreaks is not null)
            foreach (var columnBreak in _previousColumnBreaks)
                sheet.ColumnPageBreaks.Add(columnBreak);
    }
}
