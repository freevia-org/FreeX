using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum SortOn
{
    CellValues,
    CellColor,
    FontColor
}

public sealed record SortKey(uint ColumnOffset, bool Ascending, SortOn SortOn = SortOn.CellValues, CellColor? TargetColor = null);

public sealed record SortOptions(bool CaseSensitive = false, bool LeftToRight = false);

/// <summary>
/// Sorts the rows of a rectangular range by a specified column, ascending or descending.
/// Stores a snapshot of the original arrangement for undo via Revert.
/// </summary>
public sealed class SortCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly IReadOnlyList<SortKey> _sortKeys;
    private readonly SortOptions _options;

    // Snapshot for undo: list of rows, each row is a list of (address, cell?) pairs
    private List<List<(CellAddress Address, Cell? Cell)>>? _snapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    private HashSet<uint>? _hiddenRowsSnapshot;

    public string Label => _sortKeys.Count == 1
        ? $"Sort {(_sortKeys[0].Ascending ? "Ascending" : "Descending")}"
        : "Sort";

    public SortCommand(SheetId sheetId, GridRange range, uint sortByColOffset, bool ascending)
        : this(sheetId, range, [new SortKey(sortByColOffset, ascending)])
    {
    }

    public SortCommand(SheetId sheetId, GridRange range, IReadOnlyList<SortKey> sortKeys, SortOptions? options = null)
    {
        _sheetId = sheetId;
        _range = range;
        _sortKeys = sortKeys.Count == 0 ? [new SortKey(0, true)] : sortKeys;
        _options = options ?? new SortOptions();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        // Guard against inverted ranges — uint subtraction would wrap to ~4B
        if (_range.End.Row < _range.Start.Row || _range.End.Col < _range.Start.Col)
            return new CommandOutcome(true); // nothing to sort

        // Excel rejects sorts that contain merged cells: sorting would move cell content
        // out of sync with the merge region definitions.
        if (sheet.MergedRegions.Any(m => _range.Overlaps(m)))
            return new CommandOutcome(false, "Cannot sort a range that contains merged cells.");

        uint startRow = _range.Start.Row;
        uint endRow   = _range.End.Row;
        uint startCol = _range.Start.Col;
        uint endCol   = _range.End.Col;
        uint colCount32 = endCol - startCol + 1;
        var keyLimit = _options.LeftToRight ? endRow - startRow + 1 : colCount32;
        if (_sortKeys.Any(key => key.ColumnOffset >= keyLimit))
            return new CommandOutcome(false, "Sort key offset is outside the sort range.");
        var keyColIndexes = _sortKeys
            .Select(key => ((int)key.ColumnOffset, key.Ascending, key.SortOn, key.TargetColor))
            .ToList();

        int rowCount = (int)(endRow - startRow + 1);
        int colCount = (int)(endCol - startCol + 1);

        if (_options.LeftToRight)
            return ApplyLeftToRight(ctx.Workbook, sheet, startRow, endRow, startCol, endCol, keyColIndexes, rowCount, colCount);

        // Read current state and save snapshot. Redo replays Apply after Revert,
        // so the snapshot must describe the current pre-sort state each time.
        _snapshot = new List<List<(CellAddress, Cell?)>>(rowCount);
        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        _hiddenRowsSnapshot = new HashSet<uint>(sheet.HiddenRows);
        var rows = new List<(Cell?[] Cells, string?[] Comments, ThreadedComment?[] ThreadedComments, bool HasRowHeight, double RowHeight, bool IsHidden, int OriginalIndex)>(rowCount);

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var rowCells = new Cell?[colCount];
            var rowComments = new string?[colCount];
            var rowThreadedComments = new ThreadedComment?[colCount];
            var snapRow  = new List<(CellAddress, Cell?)>(colCount);
            var hasRowHeight = sheet.RowHeights.TryGetValue(row, out var rowHeight);
            var isHidden = sheet.HiddenRows.Contains(row);

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                var cell  = sheet.GetCell(addr);
                sheet.Comments.TryGetValue(addr, out rowComments[ci]);
                sheet.ThreadedComments.TryGetValue(addr, out rowThreadedComments[ci]);
                // Two independent clones are required: rowCells is sorted then written back;
                // snapRow is the undo snapshot. They must be independent copies.
                rowCells[ci] = cell?.Clone();
                snapRow.Add((addr, cell?.Clone()));
            }

            rows.Add((rowCells, rowComments, rowThreadedComments, hasRowHeight, rowHeight, isHidden, ri));
            _snapshot.Add(snapRow);
        }

        _commentSnapshot = sheet.Comments
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _threadedCommentSnapshot = sheet.ThreadedComments
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);

        rows.Sort((a, b) =>
        {
            foreach (var (index, ascending, sortOn, targetColor) in keyColIndexes)
            {
                var cmp = CompareKey(ctx.Workbook, a.Cells[index], b.Cells[index], sortOn, targetColor, _options.CaseSensitive);
                if (cmp != 0)
                    return ascending ? cmp : -cmp;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex); // stable tiebreaker
        });

        // Write sorted rows back
        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            sheet.RowHeights.Remove(row);
            if (rows[ri].HasRowHeight)
                sheet.RowHeights[row] = rows[ri].RowHeight;
            sheet.HiddenRows.Remove(row);
            if (rows[ri].IsHidden)
                sheet.HiddenRows.Add(row);

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                var cell  = rows[ri].Cells[ci];
                if (cell is null)
                    sheet.ClearCell(addr);
                else
                    sheet.SetCell(addr, cell.Clone());

                sheet.Comments.Remove(addr);
                var comment = rows[ri].Comments[ci];
                if (comment is not null)
                    sheet.Comments[addr] = comment;

                sheet.ThreadedComments.Remove(addr);
                var threadedComment = rows[ri].ThreadedComments[ci];
                if (threadedComment is not null)
                    sheet.ThreadedComments[addr] = threadedComment;

                affected.Add(addr);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    private CommandOutcome ApplyLeftToRight(
        Workbook workbook,
        Sheet sheet,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol,
        IReadOnlyList<(int RowIndex, bool Ascending, SortOn SortOn, CellColor? TargetColor)> keyRowIndexes,
        int rowCount,
        int colCount)
    {
        _snapshot = new List<List<(CellAddress, Cell?)>>(rowCount);
        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        _hiddenRowsSnapshot = new HashSet<uint>(sheet.HiddenRows);

        var columns = new List<(Cell?[] Cells, string?[] Comments, ThreadedComment?[] ThreadedComments, int OriginalIndex)>(colCount);

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var snapRow = new List<(CellAddress, Cell?)>(colCount);
            for (int ci = 0; ci < colCount; ci++)
            {
                uint col = startCol + (uint)ci;
                var addr = new CellAddress(_sheetId, row, col);
                snapRow.Add((addr, sheet.GetCell(addr)?.Clone()));
            }
            _snapshot.Add(snapRow);
        }

        for (int ci = 0; ci < colCount; ci++)
        {
            uint col = startCol + (uint)ci;
            var cells = new Cell?[rowCount];
            var comments = new string?[rowCount];
            var threadedComments = new ThreadedComment?[rowCount];

            for (int ri = 0; ri < rowCount; ri++)
            {
                uint row = startRow + (uint)ri;
                var addr = new CellAddress(_sheetId, row, col);
                cells[ri] = sheet.GetCell(addr)?.Clone();
                sheet.Comments.TryGetValue(addr, out comments[ri]);
                sheet.ThreadedComments.TryGetValue(addr, out threadedComments[ri]);
            }

            columns.Add((cells, comments, threadedComments, ci));
        }

        _commentSnapshot = sheet.Comments
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _threadedCommentSnapshot = sheet.ThreadedComments
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);

        columns.Sort((a, b) =>
        {
            foreach (var (index, ascending, sortOn, targetColor) in keyRowIndexes)
            {
                var cmp = CompareKey(workbook, a.Cells[index], b.Cells[index], sortOn, targetColor, _options.CaseSensitive);
                if (cmp != 0)
                    return ascending ? cmp : -cmp;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        });

        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ci = 0; ci < colCount; ci++)
        {
            uint col = startCol + (uint)ci;
            for (int ri = 0; ri < rowCount; ri++)
            {
                uint row = startRow + (uint)ri;
                var addr = new CellAddress(_sheetId, row, col);
                var cell = columns[ci].Cells[ri];
                if (cell is null)
                    sheet.ClearCell(addr);
                else
                    sheet.SetCell(addr, cell.Clone());

                sheet.Comments.Remove(addr);
                var comment = columns[ci].Comments[ri];
                if (comment is not null)
                    sheet.Comments[addr] = comment;

                sheet.ThreadedComments.Remove(addr);
                var threadedComment = columns[ci].ThreadedComments[ri];
                if (threadedComment is not null)
                    sheet.ThreadedComments[addr] = threadedComment;

                affected.Add(addr);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        foreach (var snapRow in _snapshot)
        {
            foreach (var (addr, cell) in snapRow)
            {
                if (cell is null)
                    sheet.ClearCell(addr);
                else
                    sheet.SetCell(addr, cell.Clone());
            }
        }

        foreach (var addr in _range.AllCells())
        {
            sheet.Comments.Remove(addr);
            sheet.ThreadedComments.Remove(addr);
        }

        if (_commentSnapshot is not null)
        {
            foreach (var (addr, comment) in _commentSnapshot)
                sheet.Comments[addr] = comment;
        }

        if (_threadedCommentSnapshot is not null)
        {
            foreach (var (addr, comment) in _threadedCommentSnapshot)
                sheet.ThreadedComments[addr] = comment;
        }

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.HiddenRows, _hiddenRowsSnapshot);
    }

    private static int CompareKey(Workbook workbook, Cell? a, Cell? b, SortOn sortOn, CellColor? targetColor, bool caseSensitive)
    {
        if (targetColor is not null && sortOn is SortOn.CellColor or SortOn.FontColor)
        {
            var aColor = sortOn == SortOn.CellColor ? GetStyle(workbook, a).FillColor : GetStyle(workbook, a).FontColor;
            var bColor = sortOn == SortOn.CellColor ? GetStyle(workbook, b).FillColor : GetStyle(workbook, b).FontColor;
            return CompareTargetColor(aColor, bColor, targetColor.Value);
        }

        return sortOn switch
        {
            SortOn.CellColor => CompareNullableColor(GetStyle(workbook, a).FillColor, GetStyle(workbook, b).FillColor),
            SortOn.FontColor => CompareNullableColor(GetStyle(workbook, a).FontColor, GetStyle(workbook, b).FontColor),
            _ => CompareScalar(a?.Value ?? BlankValue.Instance, b?.Value ?? BlankValue.Instance, caseSensitive)
        };
    }

    private static CellStyle GetStyle(Workbook workbook, Cell? cell) =>
        workbook.GetStyle(cell?.StyleId ?? StyleId.Default);

    private static int CompareNullableColor(CellColor? a, CellColor? b)
    {
        if (a is null && b is null)
            return 0;
        if (a is null)
            return 1;
        if (b is null)
            return -1;

        var red = a.Value.R.CompareTo(b.Value.R);
        if (red != 0)
            return red;
        var green = a.Value.G.CompareTo(b.Value.G);
        return green != 0 ? green : a.Value.B.CompareTo(b.Value.B);
    }

    private static int CompareTargetColor(CellColor? a, CellColor? b, CellColor targetColor)
    {
        var aMatches = a == targetColor;
        var bMatches = b == targetColor;
        if (aMatches == bMatches)
            return 0;

        return aMatches ? -1 : 1;
    }

    /// <summary>
    /// Sort comparison mirroring Excel's order: numbers/dates, text, booleans, blanks/errors last.
    /// </summary>
    private static int CompareScalar(ScalarValue a, ScalarValue b, bool caseSensitive)
    {
        bool aNum = a is NumberValue or DateTimeValue;
        bool bNum = b is NumberValue or DateTimeValue;
        if (aNum && bNum)
        {
            double av = a is DateTimeValue da ? da.Value : ((NumberValue)a).Value;
            double bv = b is DateTimeValue db ? db.Value : ((NumberValue)b).Value;
            return av.CompareTo(bv);
        }
        if (aNum) return -1;  // numbers/dates before text/bool/blank
        if (bNum) return  1;
        return (a, b) switch
        {
            (TextValue ta,   TextValue tb  ) => string.Compare(ta.Value, tb.Value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            (TextValue,      _             ) => -1,  // text before bool/blank
            (_,              TextValue     ) =>  1,
            (BoolValue ba,   BoolValue bb  ) => ba.Value.CompareTo(bb.Value),
            (BoolValue,      _             ) => -1,  // bools before blank/error
            (_,              BoolValue     ) =>  1,
            (BlankValue,     BlankValue    ) =>  0,
            (BlankValue,     _             ) =>  1,  // blanks last
            (_,              BlankValue    ) => -1,
            _                               =>  0,
        };
    }
}
