using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Excel's Insert Options choices for freshly inserted rows or columns.</summary>
public enum InsertedBandFormat
{
    /// <summary>Format Same As Above (rows) / Left (columns): the insert's own default.</summary>
    SameAsBefore,

    /// <summary>Format Same As Below (rows) / Right (columns).</summary>
    SameAsAfter,

    /// <summary>Clear Formatting.</summary>
    Clear,
}

/// <summary>
/// Excel's Insert Options button: re-formats a band of just-inserted rows (or columns) like the row
/// above, like the row below, or with no formatting. Cell values are untouched; undo restores the
/// band's previous formats exactly.
/// </summary>
public sealed class InsertedBandFormatCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _rows;
    private readonly uint _start;
    private readonly uint _count;
    private readonly InsertedBandFormat _format;
    private List<(uint Row, uint Col, StyleId? CellStyle, StyleId? StyleOnly)>? _snapshot;

    public InsertedBandFormatCommand(SheetId sheetId, bool rows, uint start, uint count, InsertedBandFormat format)
    {
        _sheetId = sheetId;
        _rows = rows;
        _start = start;
        _count = Math.Max(1u, count);
        _format = format;
    }

    public SheetId SheetId => _sheetId;

    public bool Rows => _rows;

    public uint Start => _start;

    public uint Count => _count;

    public string Label => _format switch
    {
        InsertedBandFormat.Clear => "Clear Formatting",
        InsertedBandFormat.SameAsAfter => _rows ? "Format Same As Below" : "Format Same As Right",
        _ => _rows ? "Format Same As Above" : "Format Same As Left",
    };

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
            return CommandGuards.RejectSheetProtected();

        var end = _start + _count - 1;
        bool InBand(uint row, uint col) => _rows ? row >= _start && row <= end : col >= _start && col <= end;

        // Snapshot every formatted position in the band (cells and style-only entries).
        _snapshot = [];
        var touched = new HashSet<(uint Row, uint Col)>();
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (InBand(row, col) && touched.Add((row, col)))
                _snapshot.Add((row, col, cell.StyleId, sheet.GetStyleOnly(row, col)));
        }

        foreach (var ((row, col), styleOnly) in sheet.GetStyleOnlyEntries().ToList())
        {
            if (InBand(row, col) && touched.Add((row, col)))
                _snapshot.Add((row, col, sheet.GetCell(row, col)?.StyleId, styleOnly));
        }

        // Clear the band first; then copy from the chosen neighbour line.
        foreach (var (row, col) in touched)
        {
            if (sheet.GetCell(row, col) is { } cell)
                cell.StyleId = StyleId.Default;
            sheet.ClearStyleOnly(row, col);
        }

        if (_format != InsertedBandFormat.Clear)
        {
            var neighbour = _format == InsertedBandFormat.SameAsBefore ? _start - 1 : end + 1;
            if (neighbour >= 1)
                CopyNeighbourFormats(sheet, neighbour, end, touched);
        }

        // Asking for the format the band already has changes nothing: report a no-op so the bus does
        // not push an undo step (and discard the user's pending redo) for it.
        var changed = false;
        foreach (var (row, col, cellStyle, styleOnly) in _snapshot)
        {
            var nowCellStyle = sheet.GetCell(row, col)?.StyleId;
            if (nowCellStyle != cellStyle || sheet.GetStyleOnly(row, col) != styleOnly)
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            _snapshot = null;
            return new CommandOutcome(true, IsNoOp: true);
        }

        sheet.NotifyContentRecalculated();
        return new CommandOutcome(true);
    }

    private void CopyNeighbourFormats(Sheet sheet, uint neighbour, uint end, HashSet<(uint Row, uint Col)> touched)
    {
        var lines = new HashSet<uint>();
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if ((_rows ? row : col) == neighbour && cell.StyleId != StyleId.Default)
                lines.Add(_rows ? col : row);
        }

        foreach (var ((row, col), _) in sheet.GetStyleOnlyEntries())
        {
            if ((_rows ? row : col) == neighbour)
                lines.Add(_rows ? col : row);
        }

        foreach (var line in lines)
        {
            var (sourceRow, sourceCol) = _rows ? (neighbour, line) : (line, neighbour);
            var style = sheet.GetCell(sourceRow, sourceCol) is { } sourceCell
                ? (sourceCell.StyleId == StyleId.Default ? (StyleId?)null : sourceCell.StyleId)
                : sheet.GetStyleOnly(sourceRow, sourceCol);
            if (style is not { } styleId)
                continue;

            for (var index = _start; index <= end; index++)
            {
                var (row, col) = _rows ? (index, line) : (line, index);
                if (touched.Add((row, col)))
                    _snapshot!.Add((row, col, sheet.GetCell(row, col)?.StyleId, sheet.GetStyleOnly(row, col)));

                if (sheet.GetCell(row, col) is { } cell)
                    cell.StyleId = styleId;
                else
                    sheet.SetStyleOnly(row, col, styleId);
            }
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (row, col, cellStyle, styleOnly) in _snapshot)
        {
            if (sheet.GetCell(row, col) is { } cell)
                cell.StyleId = cellStyle ?? StyleId.Default;

            if (styleOnly is { } style)
                sheet.SetStyleOnly(row, col, style);
            else
                sheet.ClearStyleOnly(row, col);
        }

        sheet.NotifyContentRecalculated();
    }
}
